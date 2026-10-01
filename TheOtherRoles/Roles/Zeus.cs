using System;
using System.Collections.Generic;
using System.Linq;
using TheOtherRoles.MetaContext;
using TheOtherRoles.Modules;
using TheOtherRoles.Objects;
using TheOtherRoles.Utilities;
using UnityEngine;
using static TheOtherRoles.TheOtherRoles;

namespace TheOtherRoles.Roles
{
    [TORRPCHolder]
    public class Zeus : RoleBase<Zeus>
    {
        public static Color color = Palette.ImpostorRed;

        public static float strikeCooldown = 30f;
        public static float cancelCooldown = 5f;
        public static float killRadius = 1.2f;

        private const float LightningSize = 2.5f;
        private const float LightningDuration = 0.55f;
        private const float MaxStrikeDistance = 60f;

        public bool mapOpenedBySkill;

        public static CustomButton strikeButton;
        private static Sprite lightningSprite;
        private static Sprite buttonSprite;

        public static Sprite GetButtonSprite()
        {
            if (buttonSprite) return buttonSprite;

            var texture = Helpers.loadTextureFromResources("TheOtherRoles.Resources.Zeus.png");
            if (texture == null)
            {
                buttonSprite = GetLightningSprite();
                return buttonSprite;
            }

            try
            {
                var pixels = texture.GetPixels32();
                int minX = texture.width, minY = texture.height, maxX = -1, maxY = -1;

                for (int y = 0; y < texture.height; y++)
                {
                    int row = y * texture.width;
                    for (int x = 0; x < texture.width; x++)
                    {
                        if (pixels[row + x].a <= 16) continue;
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }

                if (maxX < minX || maxY < minY)
                {
                    buttonSprite = GetLightningSprite();
                    return buttonSprite;
                }

                int size = Math.Min(texture.width, texture.height);
                float centerX = (minX + maxX) / 2f;
                float centerY = (minY + maxY) / 2f;

                var rect = new Rect(
                    Mathf.Clamp(centerX - size / 2f, 0f, texture.width - size),
                    Mathf.Clamp(centerY - size / 2f, 0f, texture.height - size),
                    size, size);

                buttonSprite = Sprite.Create(texture, rect, new Vector2(0.5f, 0.5f), size);
                buttonSprite.hideFlags |= HideFlags.HideAndDontSave | HideFlags.DontSaveInEditor;
                return buttonSprite;
            }
            catch (Exception ex)
            {
                TheOtherRolesPlugin.Logger.LogWarning($"[Zeus] button sprite crop failed: {ex.Message}");
                buttonSprite = GetLightningSprite();
                return buttonSprite;
            }
        }
        private static AudioClip thunderClip;
        private static AudioSource thunderSource;

        private static AudioClip GetThunderClip()
        {
            if (thunderClip != null) return thunderClip;
            thunderClip = Helpers.loadWavFromResources("TheOtherRoles.Resources.Zues.wav", "TORV_ZeusThunder");
            if (thunderClip != null) thunderClip.MarkDontUnload();
            return thunderClip;
        }

        private static void PlayThunder()
        {
            if (!Constants.ShouldPlaySfx()) return;

            var clip = GetThunderClip();
            if (clip == null) return;

            if (thunderSource == null)
            {
                var hud = FastDestroyableSingleton<HudManager>.Instance;
                if (hud == null) return;
                thunderSource = Helpers.CreateObject<AudioSource>("ZeusThunderSource", hud.transform, Vector3.zero);
                thunderSource.playOnAwake = false;
                thunderSource.spatialBlend = 0f;
                if (SoundManager.Instance != null && SoundManager.Instance.SfxChannel != null)
                    thunderSource.outputAudioMixerGroup = SoundManager.Instance.SfxChannel;
            }

            thunderSource.PlayOneShot(clip, 0.8f);
        }

        public Zeus()
        {
            RoleId = roleId = RoleId.Zeus;
            mapOpenedBySkill = false;
        }

        public static Sprite GetLightningSprite()
        {
            if (lightningSprite) return lightningSprite;
            lightningSprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.Zeus.png", 115f);
            return lightningSprite;
        }

        public static RemoteProcess<(byte zeusId, Vector2 position)> StrikeLightning = new("ZeusStrike", (message, __) =>
        {
            SpawnLightning(message.position);

            var zeus = getRole(Helpers.playerById(message.zeusId));
            if (zeus == null || zeus.player == null) return;
            if (PlayerControl.LocalPlayer != zeus.player) return;

            var victims = new List<PlayerControl>();
            foreach (PlayerControl target in PlayerControl.AllPlayerControls)
            {
                if (target == null || target.Data == null) continue;
                if (target.Data.IsDead || target.Data.Disconnected) continue;
                if (Vector2.Distance(target.transform.position, message.position) > killRadius) continue;
                victims.Add(target);
            }

            _ = new StaticAchievementToken("zeus.common1");
            if (victims.Count >= 2) _ = new StaticAchievementToken("zeus.another1");
            if (victims.Count >= 3) _ = new StaticAchievementToken("zeus.challenge");

            foreach (var victim in victims)
            {
                Helpers.forceMurderPlayer(zeus.player, victim, false);
                GameHistory.overrideDeathReasonAndKiller(victim, DeadPlayer.CustomDeathReason.ZeusStrike, zeus.player);
            }
        });

        private static void SpawnLightning(Vector2 position)
        {
            var sprite = GetLightningSprite();
            if (sprite == null) return;

            PlayThunder();

            var effect = new GameObject("ZeusLightning") { layer = 5 };
            effect.transform.position = new Vector3(position.x, position.y, position.y / 1000f + 0.001f);
            effect.AddSubmergedComponent(SubmergedCompatibility.Classes.ElevatorMover);

            var renderer = effect.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = Color.white;

            float spriteSize = sprite.rect.width / sprite.pixelsPerUnit;
            if (spriteSize > 0.01f) effect.transform.localScale = Vector3.one * (LightningSize / spriteSize);

            var host = FastDestroyableSingleton<HudManager>.Instance;
            if (host == null)
            {
                UnityEngine.Object.Destroy(effect);
                return;
            }

            host.StartCoroutine(Effects.Lerp(LightningDuration, new Action<float>(p =>
            {
                if (renderer != null) renderer.color = new Color(1f, 1f, 1f, 1f - p);
                if (p == 1f && effect != null) UnityEngine.Object.Destroy(effect);
            })));
        }

        private void OpenMap()
        {
            var hud = FastDestroyableSingleton<HudManager>.Instance;
            if (hud == null) return;
            if (MapBehaviour.Instance && MapBehaviour.Instance.isActiveAndEnabled) return;

            hud.InitMap();
            if (MapBehaviour.Instance == null) return;

            MapBehaviour.Instance.ShowCountOverlay(allowedToMove: true, showLivePlayerPosition: true, includeDeadBodies: false);
            if (player.NetTransform != null) player.NetTransform.Halt();

            mapOpenedBySkill = true;
            if (strikeButton != null) strikeButton.Timer = -1f;
        }

        private void CloseMap(bool struck)
        {
            mapOpenedBySkill = false;

            if (MapBehaviour.Instance && MapBehaviour.Instance.isActiveAndEnabled) MapBehaviour.Instance.Close();

            if (strikeButton != null)
            {
                strikeButton.MaxTimer = struck ? strikeCooldown : cancelCooldown;
                strikeButton.Timer = strikeButton.MaxTimer;
            }
        }

        private void onUse()
        {
            if (mapOpenedBySkill) CloseMap(false);
            else OpenMap();
        }

        private void Strike(Vector2 world)
        {
            StrikeLightning.Invoke((player.PlayerId, world));
            CloseMap(true);
        }

        public override void PostInit()
        {
            if (PlayerControl.LocalPlayer != player) return;

            strikeButton = new CustomButton(
                onUse,
                () => PlayerControl.LocalPlayer.isRole(RoleId.Zeus),
                () => player.CanMove && !MeetingHud.Instance && !Minigame.Instance && !ExileController.Instance && !player.Data.IsDead,
                () => { strikeButton.MaxTimer = strikeCooldown; strikeButton.Timer = strikeButton.MaxTimer; },
                GetButtonSprite(),
                CustomButton.ButtonPositions.upperRowLeft,
                HudManager.Instance,
                KeyCode.F,
                buttonText: ModTranslation.getString("zeusStrike"),
                abilityTexture: CustomButton.ButtonLabelType.UseButton
            );

            strikeButton.MaxTimer = strikeCooldown;
            strikeButton.Timer = strikeCooldown;
        }

        public override void FixedUpdate()
        {
            if (player != PlayerControl.LocalPlayer) return;
            if (player.Data == null || player.Data.IsDead)
            {
                if (mapOpenedBySkill) CloseMap(false);
                return;
            }

            if (!mapOpenedBySkill) return;

            if (MeetingHud.Instance || ExileController.Instance)
            {
                CloseMap(false);
                return;
            }

            if (MapBehaviour.Instance == null || !MapBehaviour.Instance.IsOpen)
            {
                CloseMap(false);
                return;
            }

            if (!Input.GetMouseButtonDown(0)) return;

            var ship = MapUtilities.CachedShipStatus;
            var herePoint = MapBehaviour.Instance.HerePoint;
            if (ship == null || herePoint == null || herePoint.transform.parent == null) return;

            Vector3 uiWorld = Helpers.ScreenToWorldPoint(Input.mousePosition, LayerMask.NameToLayer("UI"));
            if (uiWorld == Vector3.zero) return;

            Vector3 local = herePoint.transform.parent.InverseTransformPoint(uiWorld);

            float sign = Mathf.Sign(ship.transform.localScale.x);
            if (sign == 0f) sign = 1f;

            Vector2 world = new(local.x * sign * ship.MapScale, local.y * ship.MapScale);

            TheOtherRolesPlugin.Logger.LogInfo($"[Zeus] click local=({local.x:0.###},{local.y:0.###}) scale={ship.MapScale:0.###} sign={sign:0} -> world=({world.x:0.###},{world.y:0.###})");

            if (world.magnitude > MaxStrikeDistance) return;

            Strike(world);
        }

        public override void OnDeath(PlayerControl killer = null)
        {
            if (player == PlayerControl.LocalPlayer && mapOpenedBySkill) CloseMap(false);
        }

        public override void ResetRole(bool isShifted)
        {
            if (player == PlayerControl.LocalPlayer) destroyUi();
        }

        private static void destroyUi()
        {
            if (strikeButton != null)
            {
                UnityEngine.Object.Destroy(strikeButton.actionButtonGameObject);
                strikeButton = null;
            }
        }

        static public IEnumerable<HelpSprite> GetHelpSprites()
        {
            yield return new(GetLightningSprite(), "zeusStrikeHint");
        }

        static public IEnumerable<DocumentReplacement> GetReplacementPart()
        {
            yield return new("%CD%", strikeCooldown.ToString());
            yield return new("%CCD%", cancelCooldown.ToString());
            yield return new("%RADIUS%", killRadius.ToString());
        }

        public static void clearAndReload()
        {
            strikeCooldown = CustomOptionHolder.zeusStrikeCooldown.getFloat();
            cancelCooldown = CustomOptionHolder.zeusCancelCooldown.getFloat();
            killRadius = CustomOptionHolder.zeusKillRadius.getFloat();
            players = [];
            destroyUi();
        }
    }
}
