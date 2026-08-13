using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
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
    public class Werewolf : RoleBase<Werewolf>
    {
        public static Color color = Palette.ImpostorRed;

        public Werewolf()
        {
            RoleId = roleId = RoleId.Werewolf;
            markedTarget = null;
            markTimer = 0f;
            markCooldownTimer = 0f;
            isMarking = false;
            channelingTimer = 0f;
            channelingTarget = null;
        }

        static public IEnumerable<DocumentReplacement> GetReplacementPart()
        {
            yield return new("%MARKDURATION%", markDuration.ToString());
            yield return new("%MARKCOOLDOWN%", markCooldown.ToString());
            yield return new("%MARKRANGE%", markRange.ToString());
            yield return new("%MARKCHANNEL%", markChannelTime.ToString());
            yield return new("%SPEEDBOOST%", rampageSpeedBoost.ToString());
            yield return new("%KILLCDREDUCTION%", rampageKillCooldownReduction.ToString());
        }

        public static float markCooldown = 15f;
        public static float markDuration = 25f;
        public static float markRange = 3f;
        public static float markChannelTime = 5f;
        public static float rampageSpeedBoost = 1f;
        public static float rampageKillCooldownReduction = 20f;

        public PlayerControl markedTarget;
        public float markTimer;
        public float markCooldownTimer;
        public bool isMarking;
        public float channelingTimer;
        public PlayerControl channelingTarget;

        private static Sprite markButtonSprite;
        public static Sprite getMarkButtonSprite()
        {
            if (markButtonSprite) return markButtonSprite;
            markButtonSprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.WerewolfMarkButton.png", 115f);
            return markButtonSprite;
        }

        public static bool IsLightsOff()
        {
            var switchSystem = MapUtilities.Systems[SystemTypes.Electrical].CastFast<SwitchSystem>();
            return switchSystem.ActualSwitches != switchSystem.ExpectedSwitches;
        }

        public static PlayerControl GetNearestMarkableTarget(PlayerControl from, float maxRange)
        {
            PlayerControl best = null;
            float bestDist = maxRange;

            foreach (PlayerControl p in PlayerControl.AllPlayerControls)
            {
                if (p == null || p == from || p.Data == null || p.Data.IsDead) continue;
                float dist = Vector2.Distance(from.transform.position, p.transform.position);
                if (dist <= bestDist)
                {
                    bestDist = dist;
                    best = p;
                }
            }
            return best;
        }

        public static RemoteProcess<(byte werewolfId, byte targetId)> MarkTarget = new("WerewolfMark", (message, _) =>
        {
            var role = getRole(Helpers.playerById(message.werewolfId));
            if (role == null) return;
            role.markedTarget = Helpers.playerById(message.targetId);
            role.markTimer = markDuration;

            if (role.markedTarget == PlayerControl.LocalPlayer)
                SoundEffectsManager.play("werewolfMarked");
        });

        public static RemoteProcess<(byte werewolfId, byte unused)> ClearMark = new("WerewolfClearMark", (message, _) =>
        {
            var role = getRole(Helpers.playerById(message.werewolfId));
            if (role == null) return;
            role.markedTarget = null;
            role.markTimer = 0f;
        });

        public void TryStartMarking()
        {
            if (player != PlayerControl.LocalPlayer) return;
            if (markCooldownTimer > 0f) return;
            if (isMarking) return;

            PlayerControl nearest = GetNearestMarkableTarget(player, markRange);
            if (nearest == null) return;

            isMarking = true;
            channelingTarget = nearest;
            channelingTimer = 0f;
        }

        public override void FixedUpdate()
        {
            if (player != PlayerControl.LocalPlayer) return;

            if (markCooldownTimer > 0f)
                markCooldownTimer = Mathf.Max(0f, markCooldownTimer - Time.fixedDeltaTime);

            if (markedTarget != null)
            {
                markTimer -= Time.fixedDeltaTime;
                if (markTimer <= 0f || markedTarget.Data.IsDead)
                {
                    ClearMark.Invoke((player.PlayerId, (byte)0));
                }
            }

            if (isMarking && channelingTarget != null)
            {
                if (channelingTarget.Data.IsDead || Vector2.Distance(player.transform.position, channelingTarget.transform.position) > markRange)
                {
                    isMarking = false;
                    channelingTarget = null;
                    channelingTimer = 0f;
                    Helpers.CreateAndShowNotification(ModTranslation.getString("WerewolfChannelingInterrupted"), color, new Vector3(0f, 1f, -20f));
                }
                else
                {
                    channelingTimer += Time.fixedDeltaTime;
                    float remaining = Mathf.Max(0f, markChannelTime - channelingTimer);
                    Helpers.CreateAndShowNotification(string.Format(ModTranslation.getString("WerewolfChannelingHint"), remaining.ToString("F1")), color, new Vector3(0f, 1f, -20f));

                    if (channelingTimer >= markChannelTime)
                    {
                        isMarking = false;
                        var target = channelingTarget;
                        channelingTarget = null;
                        channelingTimer = 0f;
                        markCooldownTimer = markCooldown;
                        MarkTarget.Invoke((player.PlayerId, target.PlayerId));
                    }
                }
            }
        }

        public override void OnMeetingStart()
        {
            if (player != PlayerControl.LocalPlayer) return;
            if (markedTarget != null)
                ClearMark.Invoke((player.PlayerId, (byte)0));
            isMarking = false;
            channelingTarget = null;
            channelingTimer = 0f;
        }

        public static void clearAndReload()
        {
            markCooldown = CustomOptionHolder.werewolfMarkCooldown.getFloat();
            markDuration = CustomOptionHolder.werewolfMarkDuration.getFloat();
            markRange = CustomOptionHolder.werewolfMarkRange.getFloat();
            markChannelTime = CustomOptionHolder.werewolfMarkChannelTime.getFloat();
            rampageSpeedBoost = CustomOptionHolder.werewolfRampageSpeedBoost.getFloat();
            rampageKillCooldownReduction = CustomOptionHolder.werewolfRampageKillCooldownReduction.getFloat();
            players = [];
        }

        [HarmonyPatch(typeof(PlayerPhysics), nameof(PlayerPhysics.FixedUpdate))]
        public static class WerewolfRampageSpeedPatch
        {
            public static void Postfix(PlayerPhysics __instance)
            {
                var target = __instance.myPlayer;
                if (target == null || target.Data == null || target.Data.IsDead) return;
                if (!__instance.AmOwner || !target.CanMove) return;
                if (!target.isRole(RoleId.Werewolf)) return;
                if (!IsLightsOff()) return;

                __instance.body.velocity *= 1 + rampageSpeedBoost;
            }
        }

        [HarmonyPatch(typeof(PlayerControlSetCoolDownPatch), nameof(PlayerControlSetCoolDownPatch.SetKillTimerUnchecked))]
        public static class WerewolfRampageKillCooldownPatch
        {
            public static void Postfix(PlayerControl player, float time)
            {
                if (player == null || player.Data == null || player.Data.IsDead) return;
                if (!player.isRole(RoleId.Werewolf)) return;
                if (!IsLightsOff()) return;

                player.killTimer = Mathf.Min(player.killTimer, time * (1f - rampageKillCooldownReduction / 100f));
            }
        }
    }
}
