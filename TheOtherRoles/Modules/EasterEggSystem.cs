using HarmonyLib;
using TheOtherRoles.MetaContext;
using TMPro;
using UnityEngine;
using static TheOtherRoles.TheOtherRoles;

namespace TheOtherRoles.Modules
{
    [HarmonyPatch]
    public static class EasterEggSystem
    {
        public static bool isActive => eggCanvas != null;

        private static GameObject eggCanvas;

        private static readonly string eggTrigger = "/minecraft";

        [HarmonyPatch(typeof(ChatController), nameof(ChatController.SendChat))]
        private static class SendChatPatch
        {
            private static bool Prefix(ChatController __instance)
            {
                string text = __instance.freeChatField.Text;
                if (text.ToLower().Trim() == eggTrigger)
                {
                    PlayEasterEgg();
                    __instance.freeChatField.Clear();
                    __instance.quickChatMenu.Clear();
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(TORGUIManager), nameof(TORGUIManager.Update))]
        private static class ESCClosePatch
        {
            private static void Postfix()
            {
                if (isActive && Input.GetKeyDown(KeyCode.Escape))
                {
                    CloseEasterEgg();
                }
            }
        }

        private static void PlayEasterEgg()
        {
            if (isActive) CloseEasterEgg();
            CreateHintCanvas();
        }

        private static void CreateHintCanvas()
        {
            eggCanvas = new GameObject("EasterEggCanvas");
            eggCanvas.layer = LayerMask.NameToLayer("UI");
            var canvas = eggCanvas.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 9999;

            var panel = new GameObject("Panel");
            panel.transform.SetParent(eggCanvas.transform, false);
            var panelRect = panel.AddComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.sizeDelta = Vector2.zero;

            var panelImage = panel.AddComponent<UnityEngine.UI.Image>();
            panelImage.color = new Color(0f, 0f, 0f, 0.8f);

            var textObj = new GameObject("HintText");
            textObj.transform.SetParent(eggCanvas.transform, false);
            var textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0.5f, 0.5f);
            textRect.anchorMax = new Vector2(0.5f, 0.5f);
            textRect.pivot = new Vector2(0.5f, 0.5f);
            textRect.sizeDelta = new Vector2(500, 100);

            var text = textObj.AddComponent<TextMeshProUGUI>();
            text.text = "你发现了彩蛋!\n按 ESC 关闭此提示";
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.fontSize = 28;
        }

        private static void CloseEasterEgg()
        {
            if (eggCanvas != null)
            {
                GameObject.Destroy(eggCanvas);
                eggCanvas = null;
            }
        }
    }
}
