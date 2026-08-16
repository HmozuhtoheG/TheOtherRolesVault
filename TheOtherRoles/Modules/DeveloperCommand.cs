using System;
using System.Text.RegularExpressions;
using HarmonyLib;
using TMPro;
using UnityEngine;
using BepInEx.Unity.IL2CPP;

namespace TheOtherRoles.Modules
{
    [HarmonyPatch]
    public static class DeveloperCommand
    {
        //正则命令解析（感谢DS）
        private static readonly Regex commandRegex = new(@"^/(\w+)\s*(.*)$", RegexOptions.IgnoreCase);

        static DeveloperCommand() => ClassInjector.RegisterTypeInIl2Cpp<AnnouncementTimer>();

        [HarmonyPatch(typeof(ChatController), nameof(ChatController.SendChat))]
        private static class SendChatPatch
        {
            private static bool Prefix(ChatController __instance)
            {
                string text = __instance.freeChatField.Text;
                if (string.IsNullOrWhiteSpace(text) || !text.Trim().StartsWith("/")) return true;

                var match = commandRegex.Match(text.Trim());
                if (!match.Success) return true;

                string cmd = match.Groups[1].Value.ToLower();
                string arg = match.Groups[2].Value.Trim();

                //命令权限管理
                bool isDevCommand = cmd switch
                {
                    "s" => true,
                    _ => false
                };
                if (!isDevCommand) return true;//非命令直接发送

                //拦截发送
                __instance.freeChatField.Clear();
                __instance.quickChatMenu.Clear();

                if (!DeveloperManager.IsDev(PlayerControl.LocalPlayer)) return false;//非开发者拦截

                switch (cmd)
                {
                    case "s"://全图公告
                        if (arg.Length > 0)
                            RPCProcedure.DevAnnouncement.Invoke((PlayerControl.LocalPlayer.PlayerId, arg));
                        break;
                }
                return false;
            }
        }

        //全图公告
        private static GameObject bannerCanvas;

        public static void Show(string message)
        {
            if (bannerCanvas != null) GameObject.Destroy(bannerCanvas);
            //置顶居中
            bannerCanvas = new GameObject("DevAnnouncementCanvas");
            bannerCanvas.layer = LayerMask.NameToLayer("UI");
            var canvas = bannerCanvas.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 9999;

            var panel = new GameObject("Panel");
            panel.transform.SetParent(bannerCanvas.transform, false);
            var panelRect = panel.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 1f);
            panelRect.anchorMax = new Vector2(1f, 1f);
            panelRect.pivot = new Vector2(0.5f, 1f);
            panelRect.sizeDelta = new Vector2(0f, 110f);
            var panelImage = panel.AddComponent<UnityEngine.UI.Image>();
            panelImage.color = new Color(0f, 0f, 0f, 0.7f);

            var textObj = new GameObject("BannerText");
            textObj.transform.SetParent(panel.transform, false);
            var textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
            var text = textObj.AddComponent<TextMeshProUGUI>();
            text.text = "DevAnnouncement:";
            text.text += message;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.fontSize = 72;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;

            bannerCanvas.AddComponent<AnnouncementTimer>();
        }
        //销毁字幕
        private class AnnouncementTimer : MonoBehaviour
        {
            public float life = 4f;
            private void Update()
            {
                life -= Time.deltaTime;
                if (life <= 0f)//销毁
                {
                    if (gameObject == DeveloperCommand.bannerCanvas)
                        DeveloperCommand.bannerCanvas = null;
                    GameObject.Destroy(gameObject);
                }
            }
        }
    }
}
