using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using TheOtherRoles.MetaContext;
using TheOtherRoles.Modules;
using TheOtherRoles.Objects;
using TheOtherRoles.Patches;
using TheOtherRoles.Utilities;
using UnityEngine;
using static TheOtherRoles.TheOtherRoles;

namespace TheOtherRoles.Roles
{
    [TORRPCHolder]
    public class Sniper : RoleBase<Sniper>
    {
        public static Color color = Palette.ImpostorRed;

        public static float cooldown => CustomOptionHolder.sniperCooldown.getFloat();
        public static float shotSize => CustomOptionHolder.sniperShotSize.getFloat();
        public static float effectiveRange => CustomOptionHolder.sniperRange.getFloat();
        public static float noticeRange => CustomOptionHolder.sniperNoticeRange.getFloat();
        public static bool storeRifleOnFire => CustomOptionHolder.sniperStoreOnFire.getBool();
        public static bool canKillImpostor => CustomOptionHolder.sniperCanKillImpostor.getBool();
        public static bool canKillHiding => CustomOptionHolder.sniperCanKillHiding.getBool();
        public static bool aimAssist => CustomOptionHolder.sniperAimAssist.getBool();
        public static bool canNormalKill => CustomOptionHolder.sniperCanNormalKill.getBool();
        public static float aimAssistDelay => CustomOptionHolder.sniperAimAssistDelay.getFloat();

        public bool hasRifle;
        public float rifleAngle;
        private SniperRifle rifle;

        private static Sprite buttonSprite;
        private static AudioClip shotClip;
        private static AudioClip equipClip;
        private static bool loggedMissingAudio;

        public Sniper()
        {
            RoleId = roleId = RoleId.Sniper;
            hasRifle = false;
            rifleAngle = 0f;
        }

        public static Sprite getButtonSprite()
        {
            if (buttonSprite) return buttonSprite;
            buttonSprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.SnipeButton.png", 115f);
            return buttonSprite;
        }

        public static bool canEquip => local != null
            && local.player == PlayerControl.LocalPlayer
            && PlayerControl.LocalPlayer.Data != null
            && !PlayerControl.LocalPlayer.Data.IsDead
            && !MeetingHud.Instance
            && !ExileController.Instance;

        public static bool showKillButton => local != null
            && local.player == PlayerControl.LocalPlayer
            && PlayerControl.LocalPlayer.Data != null
            && !PlayerControl.LocalPlayer.Data.IsDead
            && (local.hasRifle || canNormalKill);

        public static RemoteProcess<(byte playerId, bool equip)> RpcEquip = new("SniperEquip", (message, _) =>
        {
            var sniper = getRole(Helpers.playerById(message.playerId));
            if (sniper == null) return;

            if (message.equip) sniper.Equip();
            else sniper.Unequip();
        });

        public static RemoteProcess<Vector2> RpcShowNotice = RemotePrimitiveProcess.OfVector2("SniperNotice", (message, _) =>
        {
            var localPlayer = PlayerControl.LocalPlayer;
            if (localPlayer == null || localPlayer.Data == null || localPlayer.Data.IsDead) return;

            if (Vector2.Distance(message, localPlayer.GetTruePosition()) > noticeRange) return;

            var arrow = new Arrow(color);
            var sprite = SniperRifle.getArrowSprite();
            if (arrow.image != null && sprite != null) arrow.image.sprite = sprite;
            arrow.Update(message, new Color(1f, 0.35f, 0.35f, 0.9f));

            var hud = FastDestroyableSingleton<HudManager>.Instance;
            if (hud != null) hud.StartCoroutine(CoRemoveArrow(arrow).WrapToIl2Cpp());
        });

        private static IEnumerator CoRemoveArrow(Arrow arrow)
        {
            yield return Effects.Wait(3f);
            if (arrow?.arrow != null) UnityEngine.Object.Destroy(arrow.arrow);
        }

        public void Equip()
        {
            if (hasRifle) return;

            hasRifle = true;
            if (player == PlayerControl.LocalPlayer) _ = new StaticAchievementToken("sniper.common1");
            rifle = new SniperRifle(player);
            rifle.StartAimAssist();
            PlaySound(equip: true);
        }

        public void Unequip()
        {
            if (!hasRifle) return;

            hasRifle = false;
            rifle?.Destroy();
            rifle = null;
        }

        public void ToggleRifle()
        {
            RpcEquip.Invoke((player.PlayerId, !hasRifle));
        }

        public void TrySnipe()
        {
            if (player != PlayerControl.LocalPlayer || !hasRifle) return;

            PlaySound(equip: false);

            var target = FindTarget();

            _ = new StaticAchievementToken("sniper.common2");

            if (target != null)
            {
                float distance = Vector2.Distance(player.GetTruePosition(), target.GetTruePosition());
                if (distance >= 20f) _ = new StaticAchievementToken("sniper.another1");
                if (target.inVent) _ = new StaticAchievementToken("sniper.challenge");

                Helpers.checkMurderAttemptAndKill(player, target, showAnimation: false);
            }

            RpcShowNotice.Invoke(player.GetTruePosition());

            if (storeRifleOnFire) Unequip();

            TheOtherRolesPlugin.Logger.LogInfo($"[Sniper] {player.PlayerId} fired, target={(target != null ? target.PlayerId.ToString() : "none")}");
        }

        private PlayerControl FindTarget()
        {
            float minDistance = effectiveRange;
            PlayerControl result = null;
            var origin = player.GetTruePosition();

            foreach (var candidate in PlayerControl.AllPlayerControls)
            {
                if (candidate == null || candidate.Data == null) continue;
                if (candidate == player || candidate.Data.IsDead || candidate.Data.Disconnected) continue;
                if (!canKillHiding && candidate.inVent) continue;
                if (!canKillImpostor && candidate.Data.Role != null && candidate.Data.Role.IsImpostor) continue;

                var offset = candidate.GetTruePosition() - origin;
                var local = Rotate(offset, -rifleAngle);

                if (local.x > 0f && local.x < minDistance && Mathf.Abs(local.y) < shotSize * 0.5f)
                {
                    result = candidate;
                    minDistance = local.x;
                }
            }

            return result;
        }

        private static Vector2 Rotate(Vector2 value, float degrees)
        {
            float rad = degrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad);
            float sin = Mathf.Sin(rad);
            return new Vector2(value.x * cos - value.y * sin, value.x * sin + value.y * cos);
        }

        private float ComputeAngle()
        {
            if (player == null) return rifleAngle;

            if (player == PlayerControl.LocalPlayer)
            {
                var camera = Camera.main;
                if (camera != null)
                {
                    var mouse = camera.ScreenToWorldPoint(Input.mousePosition);
                    var origin = player.GetTruePosition();
                    var direction = new Vector2(mouse.x - origin.x, mouse.y - origin.y);

                    if (direction.sqrMagnitude > 0.0001f)
                        return Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
                }
            }
            else
            {
                var body = player.MyPhysics != null ? player.MyPhysics.body : null;
                if (body != null && body.velocity.sqrMagnitude > 0.01f)
                    return Mathf.Atan2(body.velocity.y, body.velocity.x) * Mathf.Rad2Deg;
            }

            return rifleAngle;
        }

        public override void FixedUpdate()
        {
            if (player == null || player.Data == null) return;

            if (!hasRifle || player.Data.IsDead || MeetingHud.Instance || ExileController.Instance)
            {
                if (hasRifle && (player.Data.IsDead || MeetingHud.Instance)) Unequip();
                return;
            }

            rifleAngle = ComputeAngle();
            rifle?.Update(rifleAngle);
        }

        public override void OnMeetingStart()
        {
            Unequip();
        }

        public override void OnDeath(PlayerControl killer = null)
        {
            Unequip();
        }

        public override void ResetRole(bool isShifted)
        {
            Unequip();
        }

        private static void PlaySound(bool equip)
        {
            if (ClientOption.GetValue(ClientOption.ClientOptionType.EnableSoundEffects) == 0) return;
            if (!Constants.ShouldPlaySfx()) return;

            if (equip)
                equipClip ??= Helpers.loadWavFromResources("TheOtherRoles.Resources.SniperEquip.wav", "TORV_SniperEquip");
            else
                shotClip ??= Helpers.loadWavFromResources("TheOtherRoles.Resources.SniperShot.wav", "TORV_SniperShot");

            var clip = equip ? equipClip : shotClip;

            if (clip == null)
            {
                if (!loggedMissingAudio)
                {
                    loggedMissingAudio = true;
                    TheOtherRolesPlugin.Logger.LogWarning("[Sniper] SniperShot.wav / SniperEquip.wav 未找到，音效已跳过");
                }
                return;
            }

            if (SoundManager.Instance != null) SoundManager.Instance.PlaySoundImmediate(clip, false, 0.8f, 1f, null);
        }

        public static void clearAndReload()
        {
            foreach (var sniper in players) sniper?.Unequip();
            players = [];
        }

        static public IEnumerable<DocumentReplacement> GetReplacementPart()
        {
            yield return new("%CD%", cooldown.ToString());
            yield return new("%RANGE%", effectiveRange.ToString());
            yield return new("%SIZE%", shotSize.ToString());
            yield return new("%SOUND%", noticeRange.ToString());
        }
    }
}
