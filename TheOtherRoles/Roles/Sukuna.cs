using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TheOtherRoles.MetaContext;
using TheOtherRoles.Modules;
using TheOtherRoles.Objects;
using TheOtherRoles.Patches;
using UnityEngine;
using static TheOtherRoles.TheOtherRoles;

namespace TheOtherRoles.Roles
{
    [TORRPCHolder]
    public class Sukuna : RoleBase<Sukuna>
    {
        public static Color color = Palette.ImpostorRed;

        public static float cooldown = 30f;
        public static float chantDuration = 8f;

        public static bool hasDomain = true;
        public static int domainMaxUses = 1;
        public static float domainRadius = 5f;
        public static float domainDuration = 20f;
        public static int simpleDomainClicks = 5;
        public static float simpleDomainTime = 6f;
        public static int burnoutMeetings = 2;
        public static float domainCastTime = 2f;

        public bool isChanting;
        public float chantTimer;
        public bool facingLeft;

        public bool isDomainActive;
        public bool isDomainCasting;
        public float domainCastTimer;
        public Vector2 domainCenter;
        public float domainTimer;
        public int domainUses;
        public int burnout;

        private static AudioSource domainMusicSource;
        private static AudioClip[] domainSounds = new AudioClip[4];

        public static readonly HashSet<byte> domainProtected = [];
        public static int localSimpleClicks;
        public static float localSimpleTimer;
        private static float nextDomainSlash;
        private static float nextDomainShake;
        private static float lastWallLog;
        private static readonly Dictionary<byte, bool> hiddenByDomain = [];
        private static GameObject domainRingObject;
        private static SpriteRenderer domainRing;
        private static GameObject domainMaskObject;
        private static SpriteRenderer domainMask;
        private static Sprite domainMaskSprite;

        private GameObject verseObject;
        private TMPro.TextMeshPro verseText;
        private Vector3 verseScale = Vector3.one;
        private int lastVerse = -1;

        private static Sprite slashSprite;
        private static Sprite buttonSprite;
        private static Sprite domainButtonSprite;
        private static Sprite simpleDomainSprite;

        public Sukuna()
        {
            RoleId = roleId = RoleId.Sukuna;
            isChanting = false;
            chantTimer = 0f;
            facingLeft = false;
            isDomainActive = false;
            domainTimer = 0f;
            domainUses = Mathf.RoundToInt(CustomOptionHolder.sukunaDomainUses.getFloat());
            burnout = 0;
        }

        public static Sprite getButtonSprite()
        {
            if (buttonSprite) return buttonSprite;
            buttonSprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.CurseKillButton.png", 115f);
            return buttonSprite;
        }

        public static Sprite getSlashSprite()
        {
            if (slashSprite) return slashSprite;
            slashSprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.Sukuna.png", 115f);
            return slashSprite;
        }

        public static Sprite getDomainButtonSprite()
        {
            if (domainButtonSprite) return domainButtonSprite;
            domainButtonSprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.BrainwashButton.png", 115f);
            return domainButtonSprite;
        }

        public static Sprite getSimpleDomainButtonSprite()
        {
            if (simpleDomainSprite) return simpleDomainSprite;
            simpleDomainSprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.EnergyFieldButton.png", 115f);
            return simpleDomainSprite;
        }

        private static Sprite ringSprite;

        public static Sprite GetRingSprite()
        {
            if (ringSprite) return ringSprite;

            int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
            Color clear = new Color(0, 0, 0, 0);
            Vector2 center = new Vector2(size / 2f, size / 2f);
            float outerRadius = size / 2f - 2f;
            float innerRadius = outerRadius - 6f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), center);
                    tex.SetPixel(x, y, dist <= outerRadius && dist >= innerRadius ? Color.white : clear);
                }
            }
            tex.Apply();
            ringSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            return ringSprite;
        }

        public static RemoteProcess<(byte sukunaId, float duration, bool facingLeft)> StartChant = new("SukunaStartChant", (message, _) =>
        {
            var sukuna = getRole(Helpers.playerById(message.sukunaId));
            if (sukuna == null || sukuna.player == null) return;

            sukuna.isChanting = true;
            sukuna.chantTimer = message.duration;
            sukuna.facingLeft = message.facingLeft;
            sukuna.lastVerse = -1;
            sukuna.ShowVerse();
            SoundEffectsManager.playAtPosition("warlockCurse", sukuna.player.GetTruePosition(), 1f, 10f);
        });

        private static AudioClip getDomainClip(int index)
        {
            if (index < 0 || index > 3) return null;
            if (domainSounds[index] != null) return domainSounds[index];

            string file = index == 0 ? "domain1" : index == 1 ? "domain2" : index == 2 ? "domainmusic" : "domainbroken";
            domainSounds[index] = Helpers.loadWavFromResources($"TheOtherRoles.Resources.domain.{file}.wav", "TORV_Domain" + file);
            return domainSounds[index];
        }

        public static void playDomainSound(int index)
        {
            if (ClientOption.GetValue(ClientOption.ClientOptionType.EnableSoundEffects) == 0) return;
            if (!Constants.ShouldPlaySfx()) return;

            var clip = getDomainClip(index);
            if (clip != null) SoundManager.Instance.PlaySound(clip, false, 0.8f);
        }

        public static void playDomainMusic()
        {
            if (ClientOption.GetValue(ClientOption.ClientOptionType.EnableSoundEffects) == 0) return;
            if (!Constants.ShouldPlaySfx()) return;

            var clip = getDomainClip(2);
            if (clip == null) return;

            if (domainMusicSource == null)
            {
                var holder = new GameObject("SukunaDomainMusic");
                UnityEngine.Object.DontDestroyOnLoad(holder);
                domainMusicSource = holder.AddComponent<AudioSource>();
                domainMusicSource.playOnAwake = false;
                domainMusicSource.loop = true;
                domainMusicSource.spatialBlend = 0f;
                if (SoundManager.Instance != null && SoundManager.Instance.SfxChannel != null)
                    domainMusicSource.outputAudioMixerGroup = SoundManager.Instance.SfxChannel;
            }

            domainMusicSource.Stop();
            domainMusicSource.clip = clip;
            domainMusicSource.Play();
        }

        public static void stopDomainMusic()
        {
            if (domainMusicSource != null && domainMusicSource.isPlaying) domainMusicSource.Stop();
        }

        public static RemoteProcess<(byte sukunaId, float castTime, byte soundIndex)> StartDomainCast = new("SukunaDomainCast", (message, _) =>
        {
            var sukuna = getRole(Helpers.playerById(message.sukunaId));
            if (sukuna == null || sukuna.player == null) return;

            sukuna.isDomainCasting = true;
            sukuna.domainCastTimer = message.castTime;
            playDomainSound(message.soundIndex);
        });

        public static RemoteProcess<(byte sukunaId, Vector2 center)> StartDomain = new("SukunaStartDomain", (message, _) =>
        {
            var sukuna = getRole(Helpers.playerById(message.sukunaId));
            if (sukuna == null || sukuna.player == null) return;

            sukuna.isDomainActive = true;
            sukuna.domainCenter = message.center;
            sukuna.domainTimer = domainDuration;
            domainProtected.Clear();
            localSimpleClicks = 0;
            localSimpleTimer = 0f;
            nextDomainSlash = 0f;

            playDomainMusic();
            if (PlayerControl.LocalPlayer == sukuna.player)
                new CustomMessage(ModTranslation.getString("sukunaDomainStart"), 3f);
        });

        public static RemoteProcess<byte> EndDomain = RemotePrimitiveProcess.OfByte("SukunaEndDomain", (message, _) =>
        {
            var sukuna = getRole(Helpers.playerById(message));
            if (sukuna == null || !sukuna.isDomainActive) return;

            playDomainSound(3);
            if (sukuna.player == PlayerControl.LocalPlayer) sukuna.startBurnout();
            sukuna.isDomainActive = false;
            sukuna.domainTimer = 0f;
            cleanupDomain();
        });

        public static RemoteProcess<byte> SimpleDomainSuccess = RemotePrimitiveProcess.OfByte("SukunaSimpleDomain", (message, _) =>
        {
            domainProtected.Add(message);
            if (PlayerControl.LocalPlayer != null && PlayerControl.LocalPlayer.PlayerId == message)
            {
                SoundEffectsManager.play("medicShield");
                new CustomMessage(ModTranslation.getString("sukunaSimpleDomainOk"), 3f);
            }
        });

        public static RemoteProcess<(byte sukunaId, byte playerId)> DomainExecute = new("SukunaDomainExecute", (message, _) =>
        {
            var sukuna = getRole(Helpers.playerById(message.sukunaId));
            var victim = Helpers.playerById(message.playerId);
            if (sukuna == null || sukuna.player == null || victim == null || victim.Data == null) return;
            if (!sukuna.isDomainActive || domainProtected.Contains(victim.PlayerId)) return;

            Helpers.forceMurderPlayer(sukuna.player, victim, false);
        });

        public bool canUseDomain => hasDomain && domainUses > 0 && !isDomainActive && !isDomainCasting
            && player != null && player.Data != null && !player.Data.IsDead;

        public bool canCloseDomain => isDomainActive && player == PlayerControl.LocalPlayer
            && player.Data != null && !player.Data.IsDead;

        public void CloseDomain()
        {
            if (!canCloseDomain) return;
            EndDomain.Invoke(player.PlayerId);
        }

        public void TryStartDomain()
        {
            if (player != PlayerControl.LocalPlayer || !canUseDomain) return;
            if (MeetingHud.Instance || ExileController.Instance) return;

            byte soundIndex = (byte)UnityEngine.Random.Range(0, 2);
            StartDomainCast.Invoke((player.PlayerId, domainCastTime, soundIndex));
        }

        public void startBurnout()
        {
            burnout = burnoutMeetings;
            if (burnout > 0) new CustomMessage(string.Format(ModTranslation.getString("sukunaBurnout"), burnout), 4f);
        }

        public void registerSimpleDomainClick()
        {
            if (!isDomainActive) return;
            var local = PlayerControl.LocalPlayer;
            if (local == null || local == player || local.Data == null || local.Data.IsDead) return;
            if (!isInsideDomain(local) || domainProtected.Contains(local.PlayerId)) return;

            localSimpleClicks++;
            if (localSimpleClicks < simpleDomainClicks) return;

            SimpleDomainSuccess.Invoke(local.PlayerId);
        }

        public static void registerLocalSimpleDomainClick()
        {
            for (int i = 0; i < players.Count; i++)
            {
                var sukuna = players[i];
                if (!sukuna.isDomainActive) continue;
                if (sukuna.player == PlayerControl.LocalPlayer) continue;
                sukuna.registerSimpleDomainClick();
                return;
            }
        }

        private bool isInsideDomain(PlayerControl target)
        {
            if (target == null || target.Data == null || target.Data.IsDead) return false;
            return Vector2.Distance(target.transform.position, domainCenter) <= domainRadius;
        }

        public static bool localPlayerNeedsSimpleDomain()
        {
            var local = PlayerControl.LocalPlayer;
            if (local == null || local.Data == null || local.Data.IsDead) return false;

            for (int i = 0; i < players.Count; i++)
            {
                var sukuna = players[i];
                if (!sukuna.isDomainActive || sukuna.player == null || sukuna.player == local) continue;
                if (sukuna.isInsideDomain(local) && !domainProtected.Contains(local.PlayerId)) return true;
            }
            return false;
        }

        private void updateDomain()
        {
            if (player == PlayerControl.LocalPlayer && HudManagerStartPatch.sukunaDomainUsesText != null)
                HudManagerStartPatch.sukunaDomainUsesText.text = domainUses.ToString();

            if (isDomainCasting)
            {
                if (player.Data.IsDead || MeetingHud.Instance || ExileController.Instance)
                {
                    isDomainCasting = false;
                    domainCastTimer = 0f;
                }
                else if (player == PlayerControl.LocalPlayer)
                {
                    domainCastTimer -= Time.fixedDeltaTime;
                    if (domainCastTimer <= 0f)
                    {
                        isDomainCasting = false;
                        domainCastTimer = 0f;
                        domainUses--;
                        StartDomain.Invoke((player.PlayerId, player.GetTruePosition()));
                    }
                }
            }

            if (!isDomainActive)
            {
                cleanupDomain();
                return;
            }

            if (player == PlayerControl.LocalPlayer)
            {
                if (player.Data != null && !player.Data.IsDead
                    && Vector2.Distance(player.transform.position, domainCenter) > domainRadius)
                {
                    EndDomain.Invoke(player.PlayerId);
                    return;
                }

                domainTimer -= Time.fixedDeltaTime;
                if (domainTimer <= 0f)
                {
                    EndDomain.Invoke(player.PlayerId);
                    return;
                }
            }

            var local = PlayerControl.LocalPlayer;
            if (local == null || local.Data == null) return;

            if (Time.time >= nextDomainSlash)
            {
                nextDomainSlash = Time.time + 0.12f;
                foreach (PlayerControl p in PlayerControl.AllPlayerControls)
                {
                    if (p == null || p.Data == null || p.Data.IsDead || p == player) continue;
                    if (!isInsideDomain(p) || domainProtected.Contains(p.PlayerId)) continue;
                    spawnDomainSlash(p);
                }
            }

            updateDomainVision();

            if (isInsideDomain(local) && Time.time >= nextDomainShake)
            {
                nextDomainShake = Time.time + 0.3f;
                var follower = Camera.main != null ? Camera.main.GetComponent<FollowerCamera>() : null;
                if (follower != null) follower.ShakeScreen(0.4f, 1.5f);
            }

            if (local != player && isInsideDomain(local))
            {
                if (domainProtected.Contains(local.PlayerId))
                {
                    localSimpleTimer = 0f;
                }
                else
                {
                    localSimpleTimer += Time.fixedDeltaTime;
                    if (localSimpleTimer >= simpleDomainTime)
                    {
                        localSimpleTimer = 0f;
                        DomainExecute.Invoke((player.PlayerId, local.PlayerId));
                    }
                }
            }
            else
            {
                localSimpleTimer = 0f;
            }

            if (HudManagerStartPatch.simpleDomainClicksText != null)
                HudManagerStartPatch.simpleDomainClicksText.text = $"{localSimpleClicks}/{simpleDomainClicks}";

            if (HudManagerStartPatch.simpleDomainButton != null)
                HudManagerStartPatch.simpleDomainButton.buttonText = localSimpleTimer > 0f
                    ? $"{ModTranslation.getString("simpleDomain")} {Mathf.Max(0f, simpleDomainTime - localSimpleTimer):F1}s"
                    : ModTranslation.getString("simpleDomain");
        }

        private void spawnDomainSlash(PlayerControl target)
        {
            var sprite = getSlashSprite();
            float elementSize = getPlayerBodySize(target) * 0.6f;
            float spriteSize = sprite.rect.width / sprite.pixelsPerUnit;
            float scale = elementSize / Mathf.Max(0.01f, spriteSize);

            for (int i = 0; i < 2; i++)
            {
                var slashObject = new GameObject("SukunaDomainSlash") { layer = 5 };
                slashObject.transform.position = (Vector2)target.transform.position + UnityEngine.Random.insideUnitCircle * 0.45f;
                slashObject.transform.rotation = Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(0f, 180f));
                slashObject.transform.localScale = Vector3.one * scale;

                var renderer = slashObject.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.color = new Color(1f, 1f, 1f, 0.9f);

                HudManager.Instance.StartCoroutine(Effects.Lerp(0.3f, new Action<float>((p) =>
                {
                    if (renderer != null) renderer.color = new Color(1f, 1f, 1f, 0.9f * (1f - p));
                    if (p == 1f && slashObject != null) UnityEngine.Object.Destroy(slashObject);
                })));
            }
        }

        private void updateDomainVision()
        {
            var local = PlayerControl.LocalPlayer;
            if (local == null || local.Data == null) return;

            if (local.Data.IsDead)
            {
                restoreDomainVisibility();
                hideDomainMask();
                showDomainRing(true);
                return;
            }

            bool localInside = isInsideDomain(local);

            foreach (PlayerControl p in PlayerControl.AllPlayerControls)
            {
                if (p == null || p.Data == null || p == local) continue;
                setPlayerVisibleByDomain(p, isInsideDomain(p) == localInside);
            }

            if (localInside)
            {
                showDomainRing(false);
                showDomainMask(true);
            }
            else
            {
                showDomainRing(true);
                hideDomainMask();
            }
        }

        private static void setPlayerVisibleByDomain(PlayerControl target, bool visible)
        {
            if (hiddenByDomain.TryGetValue(target.PlayerId, out bool hidden) && hidden == !visible) return;
            hiddenByDomain[target.PlayerId] = !visible;

            if (visible && (Camouflager.camouflageTimer > 0f || Helpers.MushroomSabotageActive())) return;

            target.cosmetics.SetBodyCosmeticsVisible(visible);
            if (target.cosmetics.nameText != null) target.cosmetics.nameText.gameObject.SetActive(visible);
        }

        private static void restoreDomainVisibility()
        {
            foreach (var pair in hiddenByDomain.ToList())
            {
                if (!pair.Value) continue;
                var target = Helpers.playerById(pair.Key);
                if (target != null && target.cosmetics != null)
                {
                    target.cosmetics.SetBodyCosmeticsVisible(true);
                    if (target.cosmetics.nameText != null) target.cosmetics.nameText.gameObject.SetActive(true);
                }
            }
            hiddenByDomain.Clear();
        }

        private static Sprite getDomainMaskSprite()
        {
            if (domainMaskSprite) return domainMaskSprite;

            int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
            Vector2 center = new Vector2(size / 2f, size / 2f);
            float holeRadius = size / 8f;
            Color black = new Color(0f, 0f, 0f, 0.99f);
            Color clear = new Color(0f, 0f, 0f, 0f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    tex.SetPixel(x, y, Vector2.Distance(new Vector2(x, y), center) <= holeRadius ? clear : black);
                }
            }
            tex.Apply();
            domainMaskSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            return domainMaskSprite;
        }

        private void showDomainMask(bool show)
        {
            if (show)
            {
                if (domainMaskObject == null)
                {
                    domainMaskObject = new GameObject("SukunaDomainMask") { layer = 5 };
                    domainMask = domainMaskObject.AddComponent<SpriteRenderer>();
                    domainMask.sprite = getDomainMaskSprite();
                    domainMask.color = Color.white;
                }

                float holeSize = (getDomainMaskSprite().rect.width / 8f) / getDomainMaskSprite().pixelsPerUnit;
                domainMaskObject.transform.position = new Vector3(domainCenter.x, domainCenter.y, 0f);
                domainMaskObject.transform.localScale = Vector3.one * (domainRadius / holeSize);
                domainMaskObject.SetActive(true);
            }
            else
            {
                hideDomainMask();
            }
        }

        private void hideDomainMask()
        {
            if (domainMaskObject != null) domainMaskObject.SetActive(false);
        }

        private void showDomainRing(bool show)
        {
            if (show)
            {
                if (domainRingObject == null)
                {
                    domainRingObject = new GameObject("SukunaDomainRing") { layer = 5 };
                    domainRing = domainRingObject.AddComponent<SpriteRenderer>();
                    domainRing.sprite = GetRingSprite();
                    domainRing.color = new Color(color.r, color.g, color.b, 0.55f);
                }

                float ringSize = GetRingSprite().rect.width / GetRingSprite().pixelsPerUnit;
                domainRingObject.transform.position = new Vector3(domainCenter.x, domainCenter.y, 0f);
                domainRingObject.transform.localScale = Vector3.one * (domainRadius * 2f / ringSize);
                domainRingObject.SetActive(true);
            }
            else
            {
                if (domainRingObject != null) domainRingObject.SetActive(false);
            }
        }

        private static void cleanupDomain()
        {
            stopDomainMusic();
            restoreDomainVisibility();
            domainProtected.Clear();
            localSimpleClicks = 0;
            localSimpleTimer = 0f;
            if (domainRingObject != null) domainRingObject.SetActive(false);
            if (domainMaskObject != null) domainMaskObject.SetActive(false);
        }

        public static RemoteProcess<(byte sukunaId, Vector2 origin, Vector2 direction)> ReleaseSlash = new("SukunaReleaseSlash", (message, _) =>
        {
            SpawnSlash(Helpers.playerById(message.sukunaId), message.origin, message.direction);
        });

        public static float getPlayerBodySize(PlayerControl target)
        {
            var body = target != null && target.cosmetics != null ? target.cosmetics.currentBodySprite : null;
            if (body == null || body.BodySprite == null) return 1.1f;
            return Mathf.Max(0.6f, body.BodySprite.bounds.size.y);
        }

        private static void SpawnSlash(PlayerControl sukuna, Vector2 origin, Vector2 direction)
        {
            float elementSize = getPlayerBodySize(sukuna);
            bool facingLeft = direction.x < 0f;

            var sprite = getSlashSprite();
            float spriteSize = sprite.rect.width / sprite.pixelsPerUnit;
            float scale = elementSize / Mathf.Max(0.01f, spriteSize);

            int count = Mathf.CeilToInt(50f / Mathf.Max(0.4f, elementSize));
            for (int i = 1; i <= count; i++)
            {
                Vector2 position = origin + direction * (i * elementSize * 0.8f);
                var slashObject = new GameObject("SukunaSlash") { layer = 5 };
                slashObject.transform.position = position;
                slashObject.transform.localScale = Vector3.one * scale;

                var renderer = slashObject.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.flipX = facingLeft;
                renderer.color = Color.white;

                HudManager.Instance.StartCoroutine(Effects.Lerp(0.6f, new Action<float>((p) =>
                {
                    if (renderer != null) renderer.color = new Color(1f, 1f, 1f, 1f - p);
                    if (p == 1f && slashObject != null) UnityEngine.Object.Destroy(slashObject);
                })));
            }
        }

        private void ShowVerse()
        {
            if (player == null || player.cosmetics == null || player.cosmetics.nameText == null) return;

            if (verseObject == null)
            {
                verseText = UnityEngine.Object.Instantiate(player.cosmetics.nameText, player.cosmetics.nameText.transform.parent);
                verseText.transform.localPosition = new Vector3(0f, 0.6f, -0.01f);
                verseText.fontSize = 1.5f;
                verseText.enableWordWrapping = false;
                verseText.color = color;
                verseScale = verseText.transform.localScale;
                verseObject = verseText.gameObject;
            }

            verseObject.SetActive(true);
            verseText.text = "";
        }

        private void HideVerse()
        {
            if (verseObject != null) UnityEngine.Object.Destroy(verseObject);
            verseObject = null;
            verseText = null;
            lastVerse = -1;
        }

        private void UpdateVerse()
        {
            if (verseText == null) return;

            float interval = Mathf.Max(0.5f, chantDuration / 3f);
            float elapsed = chantDuration - chantTimer;
            int verse = Mathf.Clamp((int)(elapsed / interval), 0, 2);

            if (verse != lastVerse)
            {
                lastVerse = verse;
                verseText.text = string.Join("\n", Enumerable.Range(0, verse + 1).Select(i => ModTranslation.getString("sukunaVerse" + (i + 1))));
            }

            float pulse = 1f + 0.15f * Mathf.Sin(Time.time * 6f);
            verseText.transform.localScale = verseScale * pulse;
        }

        public void TryStartChant()
        {
            if (player != PlayerControl.LocalPlayer || player.Data == null || player.Data.IsDead) return;
            if (isChanting || MeetingHud.Instance || ExileController.Instance) return;
            if (burnout > 0 || isDomainActive) return;

            facingLeft = player.cosmetics != null && player.cosmetics.currentBodySprite != null && player.cosmetics.currentBodySprite.BodySprite != null
                && player.cosmetics.currentBodySprite.BodySprite.flipX;

            StartChant.Invoke((player.PlayerId, chantDuration, facingLeft));
        }

        private void ReleaseSlashAndKill()
        {
            player.moveable = true;

            Vector2 origin = player.GetTruePosition();
            Vector2 direction = facingLeft ? Vector2.left : Vector2.right;

            var victims = new List<PlayerControl>();
            foreach (PlayerControl target in PlayerControl.AllPlayerControls)
            {
                if (target == null || target.Data == null || target == player) continue;
                if (target.Data.IsDead || target.Data.Disconnected) continue;

                Vector2 relative = target.GetTruePosition() - origin;
                float along = Vector2.Dot(relative, direction);
                if (along < 0f) continue;

                float perpendicular = Mathf.Abs(Vector2.Dot(relative, new Vector2(-direction.y, direction.x)));
                if (perpendicular > getPlayerBodySize(player) * 0.5f) continue;

                victims.Add(target);
            }

            ReleaseSlash.Invoke((player.PlayerId, origin, direction));

            foreach (var victim in victims)
                Helpers.forceMurderPlayer(player, victim, false);
        }

        public override void FixedUpdate()
        {
            if (player == null || player.Data == null) return;
            updateDomain();
            if (!isChanting) return;

            if (player.Data.IsDead || MeetingHud.Instance || ExileController.Instance)
            {
                isChanting = false;
                HideVerse();
                if (player == PlayerControl.LocalPlayer) player.moveable = true;
                return;
            }

            chantTimer -= Time.fixedDeltaTime;
            UpdateVerse();

            if (player.cosmetics != null && player.cosmetics.currentBodySprite != null && player.cosmetics.currentBodySprite.BodySprite != null)
                player.cosmetics.currentBodySprite.BodySprite.flipX = facingLeft;

            if (player == PlayerControl.LocalPlayer)
            {
                player.moveable = false;
                if (player.MyPhysics != null && player.MyPhysics.body != null)
                    player.MyPhysics.body.velocity = Vector2.zero;

                if (chantTimer <= 0f)
                {
                    isChanting = false;
                    HideVerse();
                    ReleaseSlashAndKill();
                }
            }
            else if (chantTimer <= 0f)
            {
                isChanting = false;
                HideVerse();
            }
        }

        public override void OnMeetingStart()
        {
            isChanting = false;
            HideVerse();
            if (player == PlayerControl.LocalPlayer) player.moveable = true;

            if (isDomainActive)
            {
                playDomainSound(3);
                isDomainActive = false;
                domainTimer = 0f;
                if (player == PlayerControl.LocalPlayer) startBurnout();
                cleanupDomain();
            }
        }

        public override void OnMeetingEnd(PlayerControl exiled = null)
        {
            if (player != PlayerControl.LocalPlayer || burnout <= 0) return;

            burnout--;
            if (burnout <= 0) new CustomMessage(ModTranslation.getString("sukunaBurnoutOver"), 4f);
        }

        public override void OnDeath(PlayerControl killer = null)
        {
            isChanting = false;
            HideVerse();
            if (player == PlayerControl.LocalPlayer) player.moveable = true;

            if (isDomainActive)
            {
                playDomainSound(3);
                isDomainActive = false;
                domainTimer = 0f;
                cleanupDomain();
            }
        }

        static public IEnumerable<DocumentReplacement> GetReplacementPart()
        {
            yield return new("%CD%", Mathf.RoundToInt(cooldown).ToString());
            yield return new("%CHANT%", chantDuration.ToString("0.#"));
            yield return new("%DUSES%", domainMaxUses.ToString());
            yield return new("%DR%", domainRadius.ToString("0.##"));
            yield return new("%DDUR%", domainDuration.ToString("0.#"));
            yield return new("%SCLICKS%", simpleDomainClicks.ToString());
            yield return new("%STIME%", simpleDomainTime.ToString("0.#"));
            yield return new("%BURNOUT%", burnoutMeetings.ToString());
        }

        public static void clearAndReload()
        {
            cooldown = CustomOptionHolder.sukunaCooldown.getFloat();
            chantDuration = CustomOptionHolder.sukunaChantDuration.getFloat();
            hasDomain = CustomOptionHolder.sukunaHasDomain.getBool();
            domainMaxUses = Mathf.RoundToInt(CustomOptionHolder.sukunaDomainUses.getFloat());
            domainRadius = CustomOptionHolder.sukunaDomainRadius.getFloat();
            domainDuration = CustomOptionHolder.sukunaDomainDuration.getFloat();
            simpleDomainClicks = Mathf.RoundToInt(CustomOptionHolder.sukunaDomainSimpleClicks.getFloat());
            simpleDomainTime = CustomOptionHolder.sukunaDomainSimpleTime.getFloat();
            burnoutMeetings = Mathf.RoundToInt(CustomOptionHolder.sukunaDomainBurnoutMeetings.getFloat());
            domainCastTime = 2f;
            TheOtherRolesPlugin.Logger.LogMessage($"[DOMAIN] options: has={hasDomain} uses={domainMaxUses} radius={domainRadius} duration={domainDuration} cast={domainCastTime} clicks={simpleDomainClicks} stime={simpleDomainTime} burnout={burnoutMeetings}");
            stopDomainMusic();
            cleanupDomain();
            players = [];
        }

        [HarmonyPatch(typeof(PlayerPhysics), nameof(PlayerPhysics.FixedUpdate))]
        public static class SukunaDomainPhysicsPatch
        {
            public static void Postfix(PlayerPhysics __instance)
            {
                var target = __instance.myPlayer;
                if (target == null || target.Data == null || target.Data.IsDead) return;
                if (!__instance.AmOwner || !target.CanMove) return;
                if (MeetingHud.Instance || ExileController.Instance || target.inVent) return;

                for (int i = 0; i < players.Count; i++)
                {
                    var sukuna = players[i];
                    if (!sukuna.isDomainActive || sukuna.player == null || sukuna.player == target) continue;

                    Vector2 selfPos = __instance.body.position;
                    Vector2 delta = selfPos - sukuna.domainCenter;
                    float distance = delta.magnitude;
                    if (distance <= 0.001f) continue;

                    float clamped = distance < domainRadius
                        ? Mathf.Min(distance, domainRadius - 0.1f)
                        : Mathf.Max(distance, domainRadius + 0.1f);
                    if (Mathf.Abs(clamped - distance) < 0.001f) continue;

                    Vector2 safePosition = sukuna.domainCenter + delta / distance * clamped;
                    __instance.body.velocity = Vector2.zero;
                    __instance.body.position = safePosition;
                    __instance.transform.position = safePosition;

                    if (Time.time - lastWallLog > 2f)
                    {
                        lastWallLog = Time.time;
                        TheOtherRolesPlugin.Logger.LogMessage($"[DOMAIN] wall clamped player {target.PlayerId} dist {distance:0.##} -> {clamped:0.##} (radius {domainRadius:0.##})");
                    }
                }
            }
        }
    }
}
