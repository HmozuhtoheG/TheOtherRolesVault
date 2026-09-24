using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TheOtherRoles.Patches
{
    public static class MainMenuBackground
    {
        public const string BackgroundResource = "TheOtherRoles.Resources.ReBuildUi.BackGround.png";
        public const float PixelsPerUnit = 100f;
        private const float DesignAspect = 16f / 9f;
        public static float Alpha = 1f;
        public static Vector2 CoverSize = new Vector2(40f, 24f);
        public static float Z = 8f;

        public static void Apply(MainMenuManager instance)
        {
            try
            {
                if (!instance || !instance.mainMenuUI) return;

                var previous = GameObject.Find("TORMainMenuBackground");
                if (previous) Object.Destroy(previous);

                var sprite = Helpers.loadSpriteFromResources(BackgroundResource, PixelsPerUnit);
                if (!sprite)
                {
                    Log("背景图加载失败: " + BackgroundResource);
                    return;
                }

                var renderer = Helpers.CreateObject<SpriteRenderer>(
                    "TORMainMenuBackground", instance.mainMenuUI.transform, new Vector3(0f, 0f, Z));

                renderer.sprite = sprite;
                renderer.drawMode = SpriteDrawMode.Simple;
                renderer.color = new Color(1f, 1f, 1f, Mathf.Clamp01(Alpha));

                var cam = Helpers.FindCamera(renderer.gameObject.layer);
                if (!cam) cam = Camera.main;

                float screenHeight = cam ? 2f * cam.orthographicSize : CoverSize.y;
                float aspect = Mathf.Max(DesignAspect, Screen.height > 0 ? Screen.width / (float)Screen.height : DesignAspect);
                float screenWidth = screenHeight * aspect;

                var size = sprite.bounds.size;
                float scale = Mathf.Max(
                    screenWidth / Mathf.Max(size.x, 0.0001f),
                    screenHeight / Mathf.Max(size.y, 0.0001f));

                renderer.transform.localScale = new Vector3(scale, scale, 1f);

                Log($"cam={(cam ? cam.name : "null")} ortho={(cam ? cam.orthographicSize.ToString("F2") : "-")} " +
                    $"camAspect={(cam ? cam.aspect.ToString("F2") : "-")} aspect={aspect:F3} " +
                    $"screen={screenWidth:F2}x{screenHeight:F2} sprite={size.x:F2}x{size.y:F2} scale={scale:F3}");
            }
            catch (Exception ex)
            {
                Log("Apply 失败: " + ex.Message);
            }
        }

        private static void Log(string message)
        {
            try { TheOtherRolesPlugin.Logger.LogWarning("[MainMenuBackground] " + message); }
            catch { }
        }
    }
}
