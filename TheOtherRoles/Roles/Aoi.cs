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
    public class Aoi : RoleBase<Aoi>
    {
        public static Color color = Palette.ImpostorRed;

        public static float cooldown = 30f;
        public static int maxUses = 3;

        private static byte lastSwapTargetId = byte.MaxValue;
        private static float lastSwapTime = -99f;

        public int usesLeft;

        public static CustomButton unjustGameButton;
        private static Sprite buttonSprite;
        private static AudioClip clapClip;
        private static AudioSource clapSource;

        public Aoi()
        {
            RoleId = roleId = RoleId.Aoi;
            usesLeft = maxUses;
        }

        public static Sprite getButtonSprite()
        {
            if (buttonSprite) return buttonSprite;
            buttonSprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.SwapperCheck.png", 115f);
            return buttonSprite;
        }

        private static AudioClip getClapClip()
        {
            if (clapClip != null) return clapClip;
            clapClip = Helpers.loadWavFromResources("TheOtherRoles.Resources.Clap.wav", "TORV_AoiClap");
            if (clapClip != null) clapClip.MarkDontUnload();
            return clapClip;
        }

        private static void playClap()
        {
            if (!Constants.ShouldPlaySfx()) return;

            var clip = getClapClip();
            if (clip == null) return;

            if (clapSource == null)
            {
                var hud = FastDestroyableSingleton<HudManager>.Instance;
                if (hud == null) return;
                clapSource = Helpers.CreateObject<AudioSource>("AoiClapSource", hud.transform, Vector3.zero);
                clapSource.playOnAwake = false;
                clapSource.spatialBlend = 0f;
                if (SoundManager.Instance != null && SoundManager.Instance.SfxChannel != null)
                    clapSource.outputAudioMixerGroup = SoundManager.Instance.SfxChannel;
            }

            clapSource.PlayOneShot(clip, 0.8f);
        }

        public static RemoteProcess<(byte actorId, byte targetId, float actorX, float actorY, float targetX, float targetY)> UnjustGame = new("AoiUnjustGame", (message, __) =>
        {
            playClap();

            var actor = Helpers.playerById(message.actorId);
            var target = Helpers.playerById(message.targetId);
            if (actor == null || target == null || actor == target) return;

            actor.MyPhysics.ResetMoveState();
            target.MyPhysics.ResetMoveState();

            actor.NetTransform.SnapTo(new Vector2(message.targetX, message.targetY));
            target.NetTransform.SnapTo(new Vector2(message.actorX, message.actorY));

            if (actor == PlayerControl.LocalPlayer)
            {
                lastSwapTargetId = message.targetId;
                lastSwapTime = Time.time;
            }
        });

        public override void OnKill(PlayerControl target)
        {
            if (player != PlayerControl.LocalPlayer || target == null) return;
            if (Time.time - lastSwapTime > 5f) return;

            _ = new StaticAchievementToken("aoi.challenge");
            if (target.PlayerId == lastSwapTargetId) _ = new StaticAchievementToken("aoi.another1");
        }

        private void use()
        {
            if (usesLeft <= 0) return;

            var target = pickTarget();
            if (target == null) return;

            usesLeft--;
            var actorPos = player.transform.position;
            var targetPos = target.transform.position;
            UnjustGame.Invoke((player.PlayerId, target.PlayerId, actorPos.x, actorPos.y, targetPos.x, targetPos.y));

            _ = new StaticAchievementToken("aoi.common1");

            if (unjustGameButton != null)
            {
                unjustGameButton.MaxTimer = cooldown;
                unjustGameButton.Timer = unjustGameButton.MaxTimer;
            }
        }

        private PlayerControl pickTarget()
        {
            var candidates = new List<PlayerControl>();
            foreach (PlayerControl candidate in PlayerControl.AllPlayerControls)
            {
                if (candidate == null || candidate.Data == null) continue;
                if (candidate == player || candidate.Data.IsDead || candidate.Data.Disconnected) continue;
                candidates.Add(candidate);
            }

            return candidates.Count == 0 ? null : candidates[rnd.Next(candidates.Count)];
        }

        public override void PostInit()
        {
            if (PlayerControl.LocalPlayer != player) return;

            unjustGameButton = new CustomButton(
                use,
                () => PlayerControl.LocalPlayer.isRole(RoleId.Aoi) && usesLeft > 0,
                () => player.CanMove && !MeetingHud.Instance && !Minigame.Instance && !ExileController.Instance,
                () => { unjustGameButton.MaxTimer = cooldown; unjustGameButton.Timer = unjustGameButton.MaxTimer; },
                getButtonSprite(),
                CustomButton.ButtonPositions.upperRowLeft,
                HudManager.Instance,
                KeyCode.F,
                buttonText: ModTranslation.getString("aoiUnjustGameText"),
                abilityTexture: CustomButton.ButtonLabelType.UseButton
            );

            unjustGameButton.MaxTimer = cooldown;
            unjustGameButton.Timer = cooldown;
        }

        public override void ResetRole(bool isShifted)
        {
            if (player != PlayerControl.LocalPlayer) return;
            destroyUi();
        }

        private static void destroyUi()
        {
            if (unjustGameButton != null)
            {
                UnityEngine.Object.Destroy(unjustGameButton.actionButtonGameObject);
                unjustGameButton = null;
            }
        }

        public static void clearAndReload()
        {
            cooldown = CustomOptionHolder.aoiCooldown.getFloat();
            maxUses = Mathf.RoundToInt(CustomOptionHolder.aoiUses.getFloat());
            destroyUi();
            players = [];
        }

        static public IEnumerable<HelpSprite> GetHelpSprites()
        {
            yield return new(getButtonSprite(), "aoiUnjustGameText");
        }

        static public IEnumerable<DocumentReplacement> GetReplacementPart()
        {
            yield return new("%CD%", cooldown.ToString());
            yield return new("%USES%", maxUses.ToString());
        }
    }
}
