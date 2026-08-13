using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
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
        public static bool isActive => tempVideoPath != null && File.Exists(tempVideoPath);

        private static string tempVideoPath;
        private static GameObject eggCanvas;
        private static Process videoProcess;
        private static float checkInterval = 0.5f;
        private static float checkTimer = 0f;

        private static readonly string eggTrigger = "/minecraft";
        private static readonly string eggResourcePath = "TheOtherRoles.Video.ColorfulEgg.mp4";

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private static readonly uint SWP_NOMOVE = 0x0002;
        private static readonly uint SWP_NOSIZE = 0x0001;
        private static readonly int SW_SHOWMAXIMIZED = 3;

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

                if (isActive)
                {
                    checkTimer += Time.deltaTime;
                    if (checkTimer >= checkInterval)
                    {
                        checkTimer = 0f;
                        CheckVideoProcess();
                    }
                }
            }
        }

        private static void PlayEasterEgg()
        {
            if (isActive)
            {
                CloseEasterEgg();
            }

            try
            {
                Assembly assembly = Assembly.GetExecutingAssembly();
                using Stream stream = assembly.GetManifestResourceStream(eggResourcePath);
                if (stream == null)
                {
                    return;
                }

                byte[] videoBytes = new byte[stream.Length];
                stream.Read(videoBytes, 0, videoBytes.Length);

                string timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
                tempVideoPath = Path.Combine(Path.GetTempPath(), "TORV_egg_" + timestamp + ".mp4");
                File.WriteAllBytes(tempVideoPath, videoBytes);

                videoProcess = new Process();
                videoProcess.StartInfo.FileName = tempVideoPath;
                videoProcess.StartInfo.UseShellExecute = true;
                videoProcess.StartInfo.Verb = "open";
                videoProcess.Start();

                System.Threading.Thread.Sleep(500);
                BringWindowToFront();
                CreateHintCanvas();
                checkTimer = 0f;
            }
            catch (Exception ex)
            {
                CloseEasterEgg();
            }
        }

        private static void BringWindowToFront()
        {
            if (videoProcess == null || videoProcess.HasExited) return;

            try
            {
                IntPtr hWnd = videoProcess.MainWindowHandle;
                if (hWnd != IntPtr.Zero)
                {
                    SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
                    ShowWindow(hWnd, SW_SHOWMAXIMIZED);
                }
            }
            catch (Exception ex)
            {
            }
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
            text.text = "视频已全屏播放\n按 ESC 关闭此提示";
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.fontSize = 28;
        }

        private static void CheckVideoProcess()
        {
            if (videoProcess == null) return;

            try
            {
                videoProcess.Refresh();
                if (videoProcess.HasExited)
                {
                    CloseEasterEgg();
                }
            }
            catch (Exception ex)
            {
            }
        }

        private static void CloseEasterEgg()
        {
            try
            {
                if (videoProcess != null)
                {
                    if (!videoProcess.HasExited)
                    {
                        try { videoProcess.Kill(); } catch { }
                    }
                    try { videoProcess.Dispose(); } catch { }
                    videoProcess = null;
                }

                if (eggCanvas != null)
                {
                    GameObject.Destroy(eggCanvas);
                    eggCanvas = null;
                }

                if (!string.IsNullOrEmpty(tempVideoPath) && File.Exists(tempVideoPath))
                {
                    try { File.Delete(tempVideoPath); } catch { }
                    tempVideoPath = null;
                }
            }
            catch (Exception ex)
            {
            }
        }
    }
}
