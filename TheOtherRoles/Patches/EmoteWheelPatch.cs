using HarmonyLib;
using TheOtherRoles.MetaContext;
using TheOtherRoles.Modules;
using UnityEngine;

namespace TheOtherRoles.Patches
{
    // Drives EmoteWheel: press ` to open the radial emote wheel, move the mouse to pick a
    // slice, click to send it. Hooks TORGUIManager.Update since that runs in every scene,
    // including the pre-game lobby room.
    [HarmonyPatch]
    public static class EmoteWheelPatch
    {
        [HarmonyPatch(typeof(TORGUIManager), nameof(TORGUIManager.Update))]
        [HarmonyPostfix]
        public static void Postfix()
        {
            if (EmoteWheel.sendCooldownTimer > 0f) EmoteWheel.sendCooldownTimer -= Time.unscaledDeltaTime;

            EmoteWheel.UpdateBubbles();

            if (Input.GetKeyDown(KeyCode.BackQuote))
            {
                if (EmoteWheel.IsOpen) EmoteWheel.Close();
                else if (EmoteWheel.CanOpen()) EmoteWheel.Open();
                return;
            }

            if (!EmoteWheel.IsOpen) return;

            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
            {
                EmoteWheel.Close();
                return;
            }

            EmoteWheel.TickWheel();

            bool pastOpenGrace = Time.unscaledTime - EmoteWheel.openTimeStamp >= EmoteWheel.ConfirmGraceSeconds;
            if (pastOpenGrace && Input.GetMouseButtonDown(0))
            {
                if (EmoteWheel.hoveredIndex >= 0)
                {
                    if (EmoteWheel.sendCooldownTimer <= 0f) EmoteWheel.Emit(EmoteWheel.hoveredIndex);
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
