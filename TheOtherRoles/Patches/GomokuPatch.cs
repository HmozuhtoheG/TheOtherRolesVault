using HarmonyLib;
using TheOtherRoles.Objects;
using UnityEngine;

namespace TheOtherRoles.Patches
{
    public class GomokuPatch
    {
        private static float connectionCheckTimer = 0f;

        [HarmonyPatch(typeof(GameStartManager), nameof(GameStartManager.Start))]
        public class GomokuGameStartManagerStartPatch
        {
            public static void Postfix()
            {
                connectionCheckTimer = 0f;
                GomokuGame.OnEnterLobby();
            }
        }

        [HarmonyPatch(typeof(GameStartManager), nameof(GameStartManager.Update))]
        public class GomokuGameStartManagerUpdatePatch
        {
            public static void Postfix()
            {
                connectionCheckTimer += Time.deltaTime;
                if (connectionCheckTimer < 2f) return;
                connectionCheckTimer = 0f;
                GomokuGame.ValidatePlayersConnected();
            }
        }

        [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.Start))]
        public class GomokuMainMenuManagerStartPatch
        {
            public static void Postfix()
            {
                GomokuGame.OnLeaveLobby();
            }
        }

        [HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.Awake))]
        public class GomokuShipStatusAwakePatch
        {
            public static void Postfix()
            {
                GomokuGame.OnLeaveLobby();
            }
        }

        // Chat and the settings menus are meant to sit above the Gomoku board; rather than
        // relying on exact sorting-order values matching, close the board whenever either opens.
        [HarmonyPatch(typeof(ChatController), nameof(ChatController.Toggle))]
        public class GomokuChatControllerTogglePatch
        {
            public static void Postfix(ChatController __instance)
            {
                if (__instance.IsOpenOrOpening) GomokuGame.CloseForExternalUI();
            }
        }

        [HarmonyPatch(typeof(OptionsMenuBehaviour), nameof(OptionsMenuBehaviour.Start))]
        public class GomokuOptionsMenuBehaviourStartPatch
        {
            public static void Postfix()
            {
                GomokuGame.CloseForExternalUI();
            }
        }

        [HarmonyPatch(typeof(GameSettingMenu), nameof(GameSettingMenu.Start))]
        public class GomokuGameSettingMenuStartPatch
        {
            public static void Postfix()
            {
                GomokuGame.CloseForExternalUI();
            }
        }
    }
}
