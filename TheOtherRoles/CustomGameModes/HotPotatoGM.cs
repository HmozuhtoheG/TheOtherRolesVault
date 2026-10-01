using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using TheOtherRoles.MetaContext;
using TheOtherRoles.Objects;
using TheOtherRoles.Patches;
using TheOtherRoles.Utilities;
using UnityEngine;
using static TheOtherRoles.TheOtherRoles;

namespace TheOtherRoles.CustomGameModes
{
    [TORRPCHolder]
    public static class HotPotato
    {
        public static Color color = new Color32(255, 140, 40, byte.MaxValue);
        public static bool isHotPotatoGM = false;
        public static float timer = 30f;
        public static bool isWaitingTimer = true;
        public static bool isDropping = false;
        public static byte holderId = byte.MaxValue;
        public static readonly List<byte> deathOrder = new();
        public static HideAndSeekTimerBar timerBar;

        public const int FrameCount = 17;
        private const float BgmVolume = 0.25f;
        private const float FrameDelay = 0.12f;
        private static readonly Sprite[] boomFrames = new Sprite[FrameCount];
        private static readonly List<PlayerControl> alives = new();
        private static AudioClip boomClip;
        private static AudioSource boomSource;
        private static AudioClip bgmClip;
        private static AudioSource bgmSource;
        private static bool bgmPlaying;
        private static bool loggedPositions;
        private static GameObject marker;

        public static readonly Vector3 PassButtonOffset = CustomButton.ButtonPositions.upperRowRight;

        public static float settingTimer => CustomOptionHolder.hotPotatoTimer.getFloat();
        public static float passRange => CustomOptionHolder.hotPotatoPassRange.getFloat();
        public static bool keepBody => CustomOptionHolder.hotPotatoKeepBody.getBool();
        public static float speedBoost => CustomOptionHolder.hotPotatoSpeedBoost.getFloat();

        private static bool adminMapOpened;

        public static bool isHolder(PlayerControl player)
        {
            return player != null && player.Data != null && !player.Data.IsDead
                   && holderId != byte.MaxValue && player.PlayerId == holderId;
        }

        public static void toggleAdminMap()
        {
            if (adminMapOpened)
            {
                closeAdminMap();
                return;
            }
            if (MapBehaviour.Instance && MapBehaviour.Instance.isActiveAndEnabled) return;

            var hud = FastDestroyableSingleton<HudManager>.Instance;
            if (hud == null) return;

            hud.InitMap();
            MapBehaviour.Instance.ShowCountOverlay(allowedToMove: true, showLivePlayerPosition: true, includeDeadBodies: false);
            PlayerControl.LocalPlayer.NetTransform.Halt();
            adminMapOpened = true;
        }

        public static void closeAdminMap()
        {
            if (!adminMapOpened) return;
            adminMapOpened = false;
            if (MapBehaviour.Instance && MapBehaviour.Instance.isActiveAndEnabled) MapBehaviour.Instance.Close();
        }

        public static RemoteProcess<(byte holderId, float seconds)> AssignPotato = new("HotPotatoAssignPotato", (message, _) =>
        {
            if (!isHotPotatoGM) return;
            holderId = message.holderId;
            timer = message.seconds;
            isWaitingTimer = false;
            refreshMarker();
        });

        public static RemoteProcess<(byte fromId, byte toId)> PassPotato = new("HotPotatoPassPotato", (message, _) =>
        {
            if (!isHotPotatoGM) return;
            if (holderId != message.fromId) return;
            holderId = message.toId;
            refreshMarker();
        });

        public static RemoteProcess<(byte victimId, byte nextHolderId)> ExplodePotato = new("HotPotatoExplodePotato", (message, _) =>
        {
            if (!isHotPotatoGM) return;

            holderId = message.nextHolderId;
            deathOrder.Add(message.victimId);

            var victim = Helpers.playerById(message.victimId);
            if (victim != null && victim.Data != null)
            {
                playBoom(victim);
                if (!victim.Data.IsDead)
                {
                    victim.Die(DeathReason.Kill, false);
                    GameHistory.overrideDeathReasonAndKiller(victim, DeadPlayer.CustomDeathReason.Bomb, victim);
                }
                if (!keepBody) TORGUIManager.Instance.StartCoroutine(CoHideBody(victim.PlayerId).WrapToIl2Cpp());
            }

            timer = settingTimer;
            refreshMarker();
        });

        private static Sprite potatoSprite;
        public static Sprite getPotatoSprite()
        {
            if (potatoSprite) return potatoSprite;
            potatoSprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.Potato.png", 115f);
            return potatoSprite;
        }

        public static Sprite getBoomFrame(int index)
        {
            index = Mathf.Clamp(index, 0, FrameCount - 1);
            if (boomFrames[index] == null)
                boomFrames[index] = Helpers.loadSpriteFromResources($"TheOtherRoles.Resources.BoomAnimation.boom_{index:000}.png", 115f);
            return boomFrames[index];
        }

        public static AudioClip getBoomClip()
        {
            if (boomClip != null) return boomClip;
            boomClip = Helpers.loadWavFromResources("TheOtherRoles.Resources.Boom!.wav", "TORV_Boom");
            if (boomClip != null) boomClip.MarkDontUnload();
            return boomClip;
        }

        private static void playBoomSound()
        {
            if (!Constants.ShouldPlaySfx()) return;

            var clip = getBoomClip();
            if (clip == null) return;

            if (boomSource == null)
            {
                var hud = FastDestroyableSingleton<HudManager>.Instance;
                if (hud == null) return;
                boomSource = Helpers.CreateObject<AudioSource>("HotPotatoBoomSource", hud.transform, Vector3.zero);
                boomSource.playOnAwake = false;
                boomSource.spatialBlend = 0f;
                if (SoundManager.Instance != null && SoundManager.Instance.SfxChannel != null) boomSource.outputAudioMixerGroup = SoundManager.Instance.SfxChannel;
            }
            boomSource.PlayOneShot(clip, 0.8f);
        }

        public static AudioClip getBgmClip()
        {
            if (bgmClip != null) return bgmClip;
            bgmClip = Helpers.loadWavFromResources("TheOtherRoles.Resources.PotatoBgm.wav", "TORV_PotatoBgm");
            if (bgmClip != null) bgmClip.MarkDontUnload();
            return bgmClip;
        }

        private static void ensureBgm()
        {
            if (bgmPlaying) return;
            if (ClientOption.GetValue(ClientOption.ClientOptionType.EnableSoundEffects) == 0) return;

            var hud = FastDestroyableSingleton<HudManager>.Instance;
            if (hud == null) return;

            var clip = getBgmClip();
            if (clip == null) return;

            bgmPlaying = true;
            bgmSource = Helpers.CreateObject<AudioSource>("HotPotatoBgmSource", hud.transform, Vector3.zero);
            bgmSource.playOnAwake = false;
            bgmSource.loop = true;
            bgmSource.spatialBlend = 0f;
            bgmSource.volume = BgmVolume;
            if (SoundManager.Instance != null && SoundManager.Instance.MusicChannel != null) bgmSource.outputAudioMixerGroup = SoundManager.Instance.MusicChannel;
            bgmSource.clip = clip;
            bgmSource.Play();
        }

        public static void stopBgm()
        {
            bgmPlaying = false;
            if (bgmSource != null)
            {
                bgmSource.Stop();
                UnityEngine.Object.Destroy(bgmSource.gameObject);
                bgmSource = null;
            }
        }

        public static void playBoom(PlayerControl player)
        {
            Vector3 position = new(player.transform.localPosition.x, player.transform.localPosition.y, player.transform.localPosition.z - 0.001f);
            TORGUIManager.Instance.StartCoroutine(CoBoom(position).WrapToIl2Cpp());
            playBoomSound();
        }

        private static IEnumerator CoBoom(Vector3 position)
        {
            var renderer = Helpers.CreateObject<SpriteRenderer>("HotPotatoBoom", null, new Vector3(position.x, position.y, -10f));
            renderer.transform.localScale = Vector3.one * 1.2f;
            for (int i = 0; i < FrameCount; i++)
            {
                renderer.sprite = getBoomFrame(i);
                yield return Effects.Wait(FrameDelay);
            }
            UnityEngine.Object.Destroy(renderer.gameObject);
        }

        private static IEnumerator CoHideBody(byte playerId)
        {
            for (int i = 0; i < 30; i++)
            {
                foreach (DeadBody body in UnityEngine.Object.FindObjectsOfType<DeadBody>())
                {
                    if (body.ParentId != playerId) continue;
                    UnityEngine.Object.Destroy(body.gameObject);
                    yield break;
                }
                yield return Effects.Wait(0.1f);
            }
        }

        public static void refreshMarker()
        {
            if (marker != null)
            {
                UnityEngine.Object.Destroy(marker);
                marker = null;
            }
            if (!isHotPotatoGM || holderId == byte.MaxValue) return;

            var holder = Helpers.playerById(holderId);
            if (holder == null) return;

            marker = new GameObject("HotPotatoMarker") { layer = 11 };
            marker.transform.SetParent(holder.transform, false);
            marker.transform.localPosition = new Vector3(0f, 0.55f, -1f);
            marker.transform.localScale = Vector3.one * 0.3f;
            var renderer = marker.AddComponent<SpriteRenderer>();
            renderer.sprite = getPotatoSprite();
        }

        public static PlayerControl findPassTarget(PlayerControl holder)
        {
            if (holder == null || holder.Data == null || holder.Data.IsDead) return null;

            PlayerControl nearest = null;
            float bestDistance = passRange;
            foreach (PlayerControl candidate in PlayerControl.AllPlayerControls)
            {
                if (candidate == null || candidate == holder || candidate.Data == null || candidate.Data.IsDead || candidate.Data.Disconnected) continue;

                float distance = Vector2.Distance(holder.transform.position, candidate.transform.position);
                if (distance > bestDistance) continue;
                bestDistance = distance;
                nearest = candidate;
            }
            return nearest;
        }

        public static void tryPass(PlayerControl holder)
        {
            if (!isHotPotatoGM || holder != PlayerControl.LocalPlayer) return;
            if (isWaitingTimer || holderId != holder.PlayerId) return;
            if (holder.Data == null || holder.Data.IsDead) return;

            var target = findPassTarget(holder);
            if (target == null) return;
            PassPotato.Invoke((holder.PlayerId, target.PlayerId));
        }

        private static PlayerControl pickRandomAlive(byte exclude)
        {
            alives.Clear();
            foreach (PlayerControl player in PlayerControl.AllPlayerControls)
            {
                if (player == null || player.Data == null || player.Data.IsDead || player.Data.Disconnected) continue;
                if (player.PlayerId == exclude) continue;
                alives.Add(player);
            }
            return alives.Count == 0 ? null : alives[rnd.Next(alives.Count)];
        }

        private static int countAlive()
        {
            int count = 0;
            foreach (PlayerControl player in PlayerControl.AllPlayerControls)
                if (player != null && player.Data != null && !player.Data.IsDead && !player.Data.Disconnected) count++;
            return count;
        }

        public static PlayerControl getSurvivor()
        {
            foreach (PlayerControl player in PlayerControl.AllPlayerControls)
                if (player != null && player.Data != null && !player.Data.IsDead && !player.Data.Disconnected) return player;
            return null;
        }

        public static List<string> rankSummary()
        {
            List<string> lines = new();
            List<byte> ranked = new();

            var survivor = getSurvivor();
            if (survivor != null) ranked.Add(survivor.PlayerId);
            for (int i = deathOrder.Count - 1; i >= 0 && ranked.Count < 3; i--) ranked.Add(deathOrder[i]);

            for (int i = 0; i < ranked.Count; i++)
            {
                var player = Helpers.playerById(ranked[i]);
                string name = player != null && player.Data != null ? player.Data.PlayerName : "?";
                lines.Add($"{Helpers.cs(color, $"#{i + 1}")} {Helpers.cs(Color.white, name)}");
            }
            return lines;
        }

        public static RemoteProcess<byte> ScatterSpawn = RemotePrimitiveProcess.OfByte("HotPotatoScatterSpawn", (message, _) =>
        {
            if (!isHotPotatoGM) return;

            var vents = MapUtilities.CachedShipStatus?.AllVents;
            if (vents == null || vents.Length == 0) return;

            var random = new System.Random(message);
            foreach (var player in PlayerControl.AllPlayerControls.ToArray())
            {
                var vent = vents[random.Next(vents.Length)];
                if (player == null || player.Data == null || player.Data.IsDead || player.Data.Disconnected) continue;
                if (player.MyPhysics == null || player.NetTransform == null || vent == null) continue;

                player.MyPhysics.ResetMoveState();
                player.NetTransform.SnapTo(vent.transform.position);
            }
        });

        public static void startRound()
        {
            if (!AmongUsClient.Instance.AmHost) return;

            ScatterSpawn.Invoke((byte)UnityEngine.Random.Range(0, 256));

            var first = pickRandomAlive(byte.MaxValue);
            if (first == null) return;
            AssignPotato.Invoke((first.PlayerId, settingTimer));
        }

        private static void explode()
        {
            var holder = holderId != byte.MaxValue ? Helpers.playerById(holderId) : null;
            if (holder == null || holder.Data == null || holder.Data.IsDead) holder = pickRandomAlive(byte.MaxValue);
            if (holder == null) return;

            var next = pickRandomAlive(holder.PlayerId);
            isDropping = true;
            ExplodePotato.Invoke((holder.PlayerId, next != null ? next.PlayerId : byte.MaxValue));
            isDropping = false;

            if (countAlive() <= 1)
                GameManager.Instance.RpcEndGame((GameOverReason)CustomGameOverReason.HotPotatoWin, false);
        }

        private static void ensureTimerBar()
        {
            var hud = FastDestroyableSingleton<HudManager>.Instance;
            if (hud == null) return;

            if (timerBar == null)
            {
                var creator = GameManagerCreator.Instance;
                var managerPrefab = creator != null ? creator.HideAndSeekManagerPrefab : null;
                var barPrefab = managerPrefab != null ? managerPrefab.TimerBarPrefab : null;
                if (barPrefab == null) return;

                timerBar = UnityEngine.Object.Instantiate(barPrefab, hud.transform);
                timerBar.gameObject.SetActive(true);
                TheOtherRolesPlugin.Logger.LogMessage($"[HotPotato] timer bar created at {timerBar.transform.localPosition}");
            }

            if (hud.TaskStuff != null && hud.TaskStuff.activeSelf) hud.TaskStuff.SetActive(false);
            if (hud.TaskPanel != null && hud.TaskPanel.gameObject.activeSelf) hud.TaskPanel.gameObject.SetActive(false);
        }

        public static void hotPotatoUpdate()
        {
            if (!isHotPotatoGM) return;

            if (!loggedPositions)
            {
                var hud = FastDestroyableSingleton<HudManager>.Instance;
                if (hud != null && hud.UseButton != null && hud.KillButton != null)
                {
                    loggedPositions = true;
                    var pass = HudManagerStartPatch.hotPotatoPassButton;
                    string passPos = pass != null && pass.actionButton != null ? pass.actionButton.transform.localPosition.ToString() : "null";
                    TheOtherRolesPlugin.Logger.LogMessage($"[HotPotato] use={hud.UseButton.transform.localPosition} kill={hud.KillButton.transform.localPosition} killActive={hud.KillButton.gameObject.activeSelf} pass={passPos}");
                }
            }

            if (adminMapOpened && !isHolder(PlayerControl.LocalPlayer)) closeAdminMap();

            ensureTimerBar();
            ensureBgm();

            if (!isWaitingTimer) timer = Mathf.Max(0f, timer - Time.deltaTime);

            if (timerBar != null)
            {
                try { timerBar.UpdateTimer(Mathf.Max(0f, timer), settingTimer); }
                catch
                {
                    if (timerBar.timeText != null)
                    {
                        int fallbackSeconds = Mathf.Max(0, Mathf.CeilToInt(timer));
                        timerBar.timeText.text = $"{fallbackSeconds / 60:00}:{fallbackSeconds % 60:00}";
                    }
                }
            }

            if (marker != null)
                marker.transform.localScale = Vector3.one * (0.28f + Mathf.Sin(Time.time * 5f) * 0.03f);

            if (!AmongUsClient.Instance.AmHost || isDropping) return;
            if (deathOrder.Count > 0 && countAlive() <= 1)
            {
                GameManager.Instance.RpcEndGame((GameOverReason)CustomGameOverReason.HotPotatoWin, false);
                return;
            }
            if (isWaitingTimer) return;
            if (timer > 0f) return;
            explode();
        }

        public static void clearAndReload()
        {
            isHotPotatoGM = TORMapOptions.gameMode == CustomGamemodes.HotPotato;
            TheOtherRolesPlugin.Logger.LogMessage($"[HotPotato] gameMode={TORMapOptions.gameMode} active={isHotPotatoGM}");
            timer = settingTimer;
            isWaitingTimer = true;
            isDropping = false;
            holderId = byte.MaxValue;
            deathOrder.Clear();
            closeAdminMap();
            refreshMarker();
            stopBgm();

            if (timerBar != null)
            {
                UnityEngine.Object.Destroy(timerBar.gameObject);
                timerBar = null;
            }

            if (!isHotPotatoGM) return;
            for (int i = 0; i < FrameCount; i++) getBoomFrame(i);
            getBoomClip();
        }
    }

    [HarmonyPatch(typeof(PlayerPhysics), nameof(PlayerPhysics.FixedUpdate))]
    public static class HotPotatoSpeedPatch
    {
        public static void Postfix(PlayerPhysics __instance)
        {
            if (!HotPotato.isHotPotatoGM) return;
            if (!__instance.AmOwner || __instance.body == null || __instance.myPlayer == null) return;
            if (AmongUsClient.Instance == null || AmongUsClient.Instance.GameState != InnerNet.InnerNetClient.GameStates.Started) return;
            if (!GameData.Instance || !__instance.myPlayer.CanMove) return;
            if (!HotPotato.isHolder(__instance.myPlayer)) return;

            float boost = HotPotato.speedBoost;
            if (boost != 1f) __instance.body.velocity *= boost;
        }
    }
}
