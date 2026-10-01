using System.Collections.Generic;
using HarmonyLib;
using TheOtherRoles.Objects;
using TheOtherRoles.Utilities;
using UnityEngine;
using static TheOtherRoles.TheOtherRoles;

namespace TheOtherRoles.Roles
{
    public static class HiromiHigurumaPatches
    {
        private static readonly HashSet<CustomButton> wrappedButtons = new();
        private static int lastButtonCount = -1;

        public static void ApplyForfeit(PlayerControl defendant)
        {
            if (defendant == null || defendant.Data == null) return;

            TheOtherRolesPlugin.Logger.LogInfo($"[Higuruma] forfeit applied to {defendant.PlayerId} for {HiromiHiguruma.forfeitRounds} meeting(s)");

            if (defendant == PlayerControl.LocalPlayer)
                new CustomMessage(string.Format(ModTranslation.getString("hiromiForfeitNotice"), HiromiHiguruma.forfeitRounds), 4f);
        }

        public static void EnsureForfeitWrappers()
        {
            if (CustomButton.buttons.Count == lastButtonCount) return;
            lastButtonCount = CustomButton.buttons.Count;

            foreach (var button in CustomButton.buttons)
            {
                if (button == null || button.CouldUse == null) continue;
                if (!wrappedButtons.Add(button)) continue;

                var original = button.CouldUse;
                button.CouldUse = () => original() && !HiromiHiguruma.isForfeited(PlayerControl.LocalPlayer);
            }
        }

        [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Select), new[] { typeof(int) })]
        static class HigurumaBlockVotePatch
        {
            static bool Prefix(ref bool __result)
            {
                if (!HiromiHiguruma.trialActive) return true;
                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(Helpers), nameof(Helpers.checkSuspendAction))]
        static class HigurumaSuspendOnDefendantPatch
        {
            static bool Prefix([HarmonyArgument(1)] PlayerControl target, ref bool __result)
            {
                if (!HiromiHiguruma.trialActive || target == null) return true;
                if (target.PlayerId != HiromiHiguruma.defendantId) return true;

                __result = true;
                return false;
            }
        }

        [HarmonyPatch(typeof(Helpers), nameof(Helpers.checkMuderAttempt))]
        static class HigurumaProtectDefendantPatch
        {
            static bool Prefix([HarmonyArgument(0)] PlayerControl killer, [HarmonyArgument(1)] PlayerControl target, ref MurderAttemptResult __result)
            {
                if (!HiromiHiguruma.trialActive || target == null || killer == null) return true;
                if (target.PlayerId != HiromiHiguruma.defendantId) return true;
                if (killer.PlayerId == target.PlayerId) return true;

                __result = MurderAttemptResult.SuppressKill;
                return false;
            }
        }

        [HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
        static class HigurumaForfeitWrapperPatch
        {
            static void Postfix() => EnsureForfeitWrappers();
        }

        [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.MurderPlayer))]
        static class HigurumaMurderPatch
        {
            static void Postfix(PlayerControl __instance)
            {
                if (__instance == null) return;
                HiromiHiguruma.Record(__instance.PlayerId, HigurumaCrime.Kill);
            }
        }

        [HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.UpdateSystem), new[] { typeof(SystemTypes), typeof(PlayerControl), typeof(byte) })]
        static class HigurumaSabotagePatch
        {
            static void Prefix([HarmonyArgument(0)] SystemTypes systemType, [HarmonyArgument(1)] PlayerControl player)
            {
                if (systemType != SystemTypes.Sabotage || player == null) return;
                HiromiHiguruma.Record(player.PlayerId, HigurumaCrime.Sabotage);
            }
        }

        [HarmonyPatch(typeof(PlayerPhysics), nameof(PlayerPhysics.RpcEnterVent))]
        static class HigurumaVentPatch
        {
            static void Postfix(PlayerPhysics __instance)
            {
                var player = __instance != null ? __instance.myPlayer : null;
                if (player == null) return;
                HiromiHiguruma.Record(player.PlayerId, HigurumaCrime.Vent);
            }
        }

        [HarmonyPatch(typeof(CustomButton), nameof(CustomButton.onClickEvent))]
        static class HigurumaAbilityPatch
        {
            static void Postfix()
            {
                if (PlayerControl.LocalPlayer == null) return;
                HiromiHiguruma.Record(PlayerControl.LocalPlayer.PlayerId, HigurumaCrime.Ability);
            }
        }

        [HarmonyPatch(typeof(GameData), nameof(GameData.CompleteTask))]
        static class HigurumaTaskPatch
        {
            static void Postfix([HarmonyArgument(0)] PlayerControl pc)
            {
                if (pc == null) return;
                HiromiHiguruma.Record(pc.PlayerId, HigurumaCrime.Task);
            }
        }

        public static void ExecuteSword()
        {
            var killer = PlayerControl.LocalPlayer;
            if (killer == null || !HiromiHiguruma.hasExecutionerSword) return;

            var target = Helpers.playerById(HiromiHiguruma.executionTarget);
            if (target == null || target.Data == null || target.Data.IsDead) return;

            var result = Helpers.checkMuderAttempt(killer, target);
            if (result != MurderAttemptResult.PerformKill) return;

            killer.MurderPlayer(target, MurderResultFlags.Succeeded);

            HiromiHiguruma.executionTarget = byte.MaxValue;
            HiromiHiguruma.hasExecutionerSword = false;
        }

        public static bool SwordUsable()
        {
            if (!HiromiHiguruma.hasExecutionerSword) return false;
            if (PlayerControl.LocalPlayer == null || PlayerControl.LocalPlayer.Data == null) return false;
            if (PlayerControl.LocalPlayer.Data.IsDead) return false;

            var target = Helpers.playerById(HiromiHiguruma.executionTarget);
            if (target == null || target.Data == null || target.Data.IsDead) return false;

            float distance = Vector2.Distance(PlayerControl.LocalPlayer.GetTruePosition(), target.GetTruePosition());
            return distance <= 1.5f;
        }
    }
}
