using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TheOtherRoles.Patches
{
    public static class MainMenuBackground
    {
        public static Color TopColor = new Color32(0x0A, 0x0C, 0x1A, 0xFF);
        public static Color BottomColor = new Color32(0x1B, 0x14, 0x33, 0xFF);
        public static float Alpha = 0.94f;
        public static Vector2 CoverSize = new Vector2(40f, 24f);
        public static float Z = 8f;

        private static Sprite _gradient;
        private static Color _cachedTop;
        private static Color _cachedBottom;

        public static void Apply(MainMenuManager instance)
        {
            try
            {
                if (!instance || !instance.mainMenuUI) return;

                var previous = GameObject.Find("TORMainMenuBackground");
                if (previous) Object.Destroy(previous);

                var sprite = BuildGradient(TopColor, BottomColor);
                if (!sprite) return;

                var renderer = Helpers.CreateObject<SpriteRenderer>(
                    "TORMainMenuBackground", instance.mainMenuUI.transform, new Vector3(0f, 0f, Z));

                renderer.sprite = sprite;
                renderer.drawMode = SpriteDrawMode.Simple;
                renderer.color = new Color(1f, 1f, 1f, Mathf.Clamp01(Alpha));

                var size = sprite.bounds.size;
                renderer.transform.localScale = new Vector3(
                    CoverSize.x / Mathf.Max(size.x, 0.0001f),
                    CoverSize.y / Mathf.Max(size.y, 0.0001f),
                    1f);
            }
            catch (Exception ex)
            {
                Log("Apply 失败: " + ex.Message);
            }
        }

        private static Sprite BuildGradient(Color top, Color bottom)
        {
            if (_gradient && _cachedTop == top && _cachedBottom == bottom) return _gradient;

            const int Width = 4;
            const int Height = 128;

            var texture = new Texture2D(Width, Height, TextureFormat.ARGB32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            for (int y = 0; y < Height; y++)
            {
                var color = Color.Lerp(bottom, top, y / (float)(Height - 1));
                for (int x = 0; x < Width; x++) texture.SetPixel(x, y, color);
            }

            texture.Apply();
            texture.hideFlags |= HideFlags.HideAndDontSave;

            if (_gradient) Object.Destroy(_gradient);
            _gradient = Sprite.Create(texture, new Rect(0f, 0f, Width, Height), new Vector2(0.5f, 0.5f), 100f);
            _gradient.hideFlags |= HideFlags.HideAndDontSave;

            _cachedTop = top;
            _cachedBottom = bottom;
            return _gradient;
        }

        private static void Log(string message)
        {
            try { TheOtherRolesPlugin.Logger.LogWarning("[MainMenuBackground] " + message); }
            catch { }
        }
    }
}
