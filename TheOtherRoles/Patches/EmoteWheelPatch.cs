using HarmonyLib;
using TheOtherRoles.MetaContext;
using TheOtherRoles.Modules;
using TheOtherRoles.Modules.Emotes;
using UnityEngine;

namespace TheOtherRoles.Patches
{
    [HarmonyPatch]
    public static class EmoteSessionResetPatch
    {
        [HarmonyPatch(typeof(LobbyBehaviour), nameof(LobbyBehaviour.Start))]
        [HarmonyPostfix]
        public static void Postfix()
        {
            EmoteNet.ResetBroadcast();
            EmoteCatalog.ClearReceived();
            EmoteDance.ResetAll();
        }
    }

    [HarmonyPatch]
    public static class EmoteWheelPatch
    {
        private static bool emoteTickFailed;

        [HarmonyPatch(typeof(TORGUIManager), nameof(TORGUIManager.Update))]
        [HarmonyPostfix]
        public static void Postfix()
        {
            EmoteWheel.UpdateBubbles();

            try
            {
                EmoteNet.Tick();
            }
            catch (System.Exception ex)
            {
                if (!emoteTickFailed)
                {
                    emoteTickFailed = true;
                    TheOtherRolesPlugin.Logger.LogError($"[EmoteNet] Tick failed, transfers disabled: {ex}");
                }
            }

            if (EmoteFileDialog.TryConsume(out var imported) && imported.Count > 0)
            {
                if (EmoteImport.ImportFiles(imported) > 0)
                {
                    EmoteCatalog.Reload();
                    EmoteHelpPage.Refresh();
                }
            }

            if (Input.GetKeyDown(KeyCode.F7))
            {
                EmoteCatalog.Reload();
                EmoteHelpPage.Refresh();
                TheOtherRolesPlugin.Logger.LogInfo("[EmoteCatalog] reloaded");
            }

            bool keyDown = Input.GetKeyDown(KeyCode.R);
            bool keyUp = Input.GetKeyUp(KeyCode.R);

            if (keyDown && EmoteWheel.CanOpen()) EmoteWheel.OpenFromKey();

            if (!EmoteWheel.IsOpen) return;

            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
            {
                EmoteWheel.Close();
                return;
            }

            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.01f) EmoteWheel.ChangePage(scroll > 0f ? -1 : 1);

            EmoteWheel.TickWheel();

            bool pastOpenGrace = Time.unscaledTime - EmoteWheel.openTimeStamp >= EmoteWheel.ConfirmGraceSeconds;

            if (EmoteWheel.heldByKey)
            {
                if (keyUp)
                {
                    if (pastOpenGrace && EmoteWheel.hoveredIndex >= 0) EmoteWheel.Emit(EmoteWheel.hoveredIndex);
                    EmoteWheel.Close();
                }
                return;
            }

            if (pastOpenGrace && Input.GetMouseButtonDown(0))
            {
                if (EmoteWheel.hoveredArrow != 0)
                {
                    EmoteWheel.ChangePage(EmoteWheel.hoveredArrow);
                }
                else if (EmoteWheel.hoveredIndex >= 0)
                {
                    EmoteWheel.Emit(EmoteWheel.hoveredIndex);
                    EmoteWheel.Close();
                }
                else if (System.OperatingSystem.IsAndroid())
                {
                    EmoteWheel.Close();
                }
            }
        }
    }
}
