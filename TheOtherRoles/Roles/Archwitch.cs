using System;
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
    public class Archwitch : RoleBase<Archwitch>
    {
        public static Color color = new Color32(150, 96, 196, byte.MaxValue);

        public static float killCooldown => CustomOptionHolder.archwitchKillCooldown.getFloat();
        public static int requiredPoints => Mathf.RoundToInt(CustomOptionHolder.archwitchRequiredPoints.getFloat());
        public static int witnessPoints => Mathf.RoundToInt(CustomOptionHolder.archwitchWitnessPoints.getFloat());
        public static int sabotagePoints => Mathf.RoundToInt(CustomOptionHolder.archwitchSabotagePoints.getFloat());
        public static int abilityPoints => Mathf.RoundToInt(CustomOptionHolder.archwitchAbilityPoints.getFloat());
        public static int deathPoints => Mathf.RoundToInt(CustomOptionHolder.archwitchDeathPoints.getFloat());
        public static int taskReduce => Mathf.RoundToInt(CustomOptionHolder.archwitchTaskReduce.getFloat());
        public static float visionMultiplier => CustomOptionHolder.archwitchVisionMultiplier.getFloat();

        public int points;
        public bool isWitched;
        public PlayerControl currentTarget;
        public bool redeemedByTask;
        private bool sabotaged;

        public static bool transformed;
        public static bool redeemed;

        private static Sprite killButtonSprite;

        public Archwitch()
        {
            RoleId = roleId = RoleId.Archwitch;
            points = 0;
            isWitched = false;
            currentTarget = null;
            redeemedByTask = false;
            sabotaged = false;
        }

        public static Sprite getKillButtonSprite()
        {
            if (killButtonSprite) return killButtonSprite;
            killButtonSprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.CurseKillButton.png", 115f);
            return killButtonSprite;
        }

        public static bool isWitchedPlayer(PlayerControl player)
        {
            if (player == null) return false;
            var role = getRole(player);
            return role != null && role.isWitched;
        }

        public static bool anyWitched => transformed;

        public static int witchedAliveCount => players.Count(x => x != null && x.player != null && x.player.Data != null
            && !x.player.Data.IsDead && x.isWitched);

        public static RemoteProcess<byte> RpcTransform = RemotePrimitiveProcess.OfByte("ArchwitchTransform", (message, _) =>
        {
            var witch = getRole(Helpers.playerById(message));
            if (witch == null || witch.isWitched) return;

            witch.isWitched = true;
            witch.currentTarget = null;
            transformed = true;

            if (witch.player != null && witch.player == PlayerControl.LocalPlayer)
                new CustomMessage(ModTranslation.getString("archwitchTransformed"), 4f);

            TheOtherRolesPlugin.Logger.LogInfo($"[Archwitch] {message} fully transformed");
        });

        public void AddPoints(int amount, string reason)
        {
            if (amount <= 0 || isWitched) return;
            if (player == null || player.Data == null || player.Data.IsDead) return;
            if (player != PlayerControl.LocalPlayer) return;

            points += amount;
            TheOtherRolesPlugin.Logger.LogInfo($"[Archwitch] +{amount} ({reason}) -> {points}/{requiredPoints}");

            if (points >= requiredPoints) RpcTransform.Invoke(player.PlayerId);
        }

        public void ReducePoints(int amount)
        {
            if (amount <= 0 || isWitched) return;
            if (player != PlayerControl.LocalPlayer) return;

            points = Mathf.Max(0, points - amount);
            redeemedByTask = true;
            TheOtherRolesPlugin.Logger.LogInfo($"[Archwitch] -{amount} (task) -> {points}/{requiredPoints}");
        }

        public override void PostInit()
        {
            if (player != PlayerControl.LocalPlayer) return;
            points = 0;
            isWitched = false;
            redeemedByTask = false;
            sabotaged = false;
        }

        public override void FixedUpdate()
        {
            if (player != PlayerControl.LocalPlayer) return;
            if (player.Data == null || player.Data.IsDead || !isWitched) return;

            if (MeetingHud.Instance || ExileController.Instance)
            {
                currentTarget = null;
                return;
            }

            currentTarget = PlayerControlFixedUpdatePatch.setTarget(untargetablePlayers: new List<PlayerControl>());
            if (currentTarget != null && currentTarget.isRole(RoleId.Archwitch)) currentTarget = null;
            if (currentTarget != null && currentTarget.Data != null && currentTarget.Data.Role != null && currentTarget.Data.Role.IsImpostor) currentTarget = null;
        }

        public override void OnKill(PlayerControl target)
        {
            if (!isWitched || target == null || target == player) return;

            TheOtherRolesPlugin.Logger.LogInfo($"[Archwitch] {player.PlayerId} killed {target.PlayerId} (witched)");
        }

        public override void OnDeath(PlayerControl killer = null)
        {
            currentTarget = null;
        }

        public static void onWitnessDeath(PlayerControl victim)
        {
            var witch = local;
            if (witch == null || witch.isWitched || witch.player == null) return;
            if (witch.player.Data == null || witch.player.Data.IsDead) return;
            if (victim == null || victim == witch.player) return;

            if (!Helpers.isVisible(witch.player, victim)) return;

            witch.AddPoints(witnessPoints, "witness");
        }

        public static void onSabotage()
        {
            var witch = local;
            if (witch == null || witch.isWitched || witch.player == null) return;
            if (witch.player.Data == null || witch.player.Data.IsDead) return;
            if (witch.sabotaged) return;

            witch.sabotaged = true;
            witch.AddPoints(sabotagePoints, "sabotage");
        }

        public static void onAffectedByAbility(PlayerControl caster, PlayerControl target)
        {
            if (caster == null || target == null || caster == target) return;

            var witch = getRole(target);
            if (witch == null || witch.isWitched) return;
            if (target != PlayerControl.LocalPlayer) return;

            witch.AddPoints(abilityPoints, "ability");
        }

        public static void onTaskComplete(PlayerControl pc)
        {
            var witch = getRole(pc);
            if (witch == null || witch.isWitched) return;

            witch.ReducePoints(taskReduce);
        }

        public override void OnMeetingStart()
        {
            sabotaged = false;
            currentTarget = null;
        }

        public override void ResetRole(bool isShifted)
        {
            currentTarget = null;
        }

        public static void clearAndReload()
        {
            transformed = false;
            redeemed = false;
            players = [];
        }

        static public IEnumerable<DocumentReplacement> GetReplacementPart()
        {
            yield return new("%POINT%", requiredPoints.ToString());
            yield return new("%VISION%", visionMultiplier.ToString());
        }
    }

    public static class ArchwitchPatches
    {
        [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.MurderPlayer))]
        static class ArchwitchWitnessPatch
        {
            static void Prefix([HarmonyArgument(0)] PlayerControl target)
            {
                if (target == null) return;

                var victim = Archwitch.getRole(target);
                if (victim != null && !victim.isWitched)
                    victim.AddPoints(Archwitch.deathPoints, "own death");

                Archwitch.onWitnessDeath(target);
            }
        }

        [HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.UpdateSystem), new[] { typeof(SystemTypes), typeof(PlayerControl), typeof(byte) })]
        static class ArchwitchSabotagePatch
        {
            static void Prefix([HarmonyArgument(0)] SystemTypes systemType)
            {
                if (systemType != SystemTypes.Sabotage) return;
                Archwitch.onSabotage();
            }
        }

        [HarmonyPatch(typeof(Helpers), nameof(Helpers.checkSuspendAction))]
        static class ArchwitchAbilityPatch
        {
            static void Postfix([HarmonyArgument(0)] PlayerControl player, [HarmonyArgument(1)] PlayerControl target)
            {
                Archwitch.onAffectedByAbility(player, target);
            }
        }

        [HarmonyPatch(typeof(GameData), nameof(GameData.CompleteTask))]
        static class ArchwitchTaskPatch
        {
            static void Postfix([HarmonyArgument(0)] PlayerControl pc)
            {
                if (pc == null) return;
                Archwitch.onTaskComplete(pc);
            }
        }

        [HarmonyPatch(typeof(Helpers), nameof(Helpers.checkMuderAttempt))]
        static class ArchwitchUnkillablePatch
        {
            static bool Prefix([HarmonyArgument(1)] PlayerControl target, ref MurderAttemptResult __result)
            {
                if (target == null) return true;
                if (!Archwitch.isWitchedPlayer(target)) return true;

                __result = MurderAttemptResult.SuppressKill;
                return false;
            }
        }
    }
}
