using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TheOtherRoles.Objects;
using UnityEngine;

namespace TheOtherRoles.Roles
{
    public static class HeavenlyRestriction
    {
        public static Color color = new Color32(158, 158, 158, byte.MaxValue);

        public static float speedMultiplier = 1.45f;
        public static float killCooldownMultiplier = 0.35f;

        public static List<PlayerControl> players = [];

        public static bool isRestricted(PlayerControl player)
        {
            if (player == null) return false;
            return players.Any(x => x != null && x.PlayerId == player.PlayerId);
        }

        public static void clearAndReload()
        {
            speedMultiplier = CustomOptionHolder.modifierHeavenlyRestrictionSpeed.getFloat();
            killCooldownMultiplier = CustomOptionHolder.modifierHeavenlyRestrictionKillCooldown.getFloat();
            players = [];
        }

        [HarmonyPatch(typeof(PlayerPhysics), nameof(PlayerPhysics.FixedUpdate))]
        static class HeavenlySpeedPatch
        {
            static void Postfix(PlayerPhysics __instance)
            {
                var player = __instance.myPlayer;
                if (player == null || player.Data == null || player.Data.IsDead) return;
                if (!__instance.AmOwner || !player.CanMove) return;
                if (!isRestricted(player)) return;

                __instance.body.velocity *= speedMultiplier;
            }
        }

        [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.FixedUpdate))]
        static class HeavenlyKillCooldownPatch
        {
            static void Postfix(PlayerControl __instance)
            {
                var player = __instance;
                if (player == null || player.Data == null || player.Data.IsDead) return;
                if (player != PlayerControl.LocalPlayer) return;
                if (!isRestricted(player)) return;
                if (!Helpers.isKiller(player)) return;

                float baseCooldown = GameOptionsManager.Instance.currentNormalGameOptions.KillCooldown;
                float reduced = baseCooldown * killCooldownMultiplier;
                if (player.killTimer > reduced) player.killTimer = reduced;
            }
        }

        static readonly HashSet<CustomButton> wrappedButtons = [];
        static int lastButtonCount = -1;

        public static void EnsureWrappers()
        {
            if (CustomButton.buttons.Count == lastButtonCount) return;
            lastButtonCount = CustomButton.buttons.Count;

            foreach (var button in CustomButton.buttons)
            {
                if (button == null || button.HasButton == null) continue;
                if (!wrappedButtons.Add(button)) continue;

                var originalHas = button.HasButton;
                var originalCan = button.CouldUse;

                button.HasButton = () => originalHas() && !isRestricted(PlayerControl.LocalPlayer);
                if (originalCan != null)
                    button.CouldUse = () => originalCan() && !isRestricted(PlayerControl.LocalPlayer);
            }
        }

        [HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
        static class HeavenlyButtonWrapperPatch
        {
            static void Postfix() => EnsureWrappers();
        }
    }
}
