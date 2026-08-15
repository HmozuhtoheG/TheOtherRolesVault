using System.Collections.Generic;
using HarmonyLib;
using TheOtherRoles.MetaContext;
using TheOtherRoles.Utilities;
using TMPro;
using UnityEngine;
using Image = UnityEngine.UI.Image;

namespace TheOtherRoles.Modules
{
    // Press ` (backquote/tilde key) to open a radial emote wheel. Move the mouse to pick a
    // slice, click to send it. Works both in the pre-game lobby room and in an active game,
    // since it hooks TORGUIManager.Update which runs in every scene.
    [HarmonyPatch]
    [TORRPCHolder]
    public static class EmoteWheel
    {
        private static readonly (string Glyph, Color32 Color)[] Emotes =
        {
            ("☺", new Color32(255, 214, 64, 255)),  // ☺ happy
            ("♥", new Color32(255, 90, 140, 255)),  // ♥ love
            ("★", new Color32(255, 193, 40, 255)),  // ★ nice
            ("✓", new Color32(90, 217, 110, 255)),  // ✓ agree
            ("✗", new Color32(230, 80, 80, 255)),   // ✗ disagree
            ("!", new Color32(255, 150, 40, 255)),       // wow
            ("?", new Color32(150, 140, 255, 255)),      // confused
            ("☹", new Color32(100, 165, 255, 255)), // ☹ sad
        };

        private const float OuterRadius = 170f;
        private const float DeadZoneRadius = 46f;
        private const float IconRadius = 122f;
        private const float IconSize = 64f;
        private const float IconOutlineThickness = 6f;
        private const float SendCooldownSeconds = 1.2f;
        private const float BubbleLifetime = 2.6f;
        private const float BubbleFadeOut = 0.5f;

        public static RemoteProcess<(byte senderId, byte emoteIndex)> SendEmote = new("EmoteWheelSend", (message, _) =>
        {
            var sender = Helpers.playerById(message.senderId);
            if (sender == null || message.emoteIndex >= Emotes.Length) return;
            ShowBubble(sender, message.emoteIndex);
        });

        [HarmonyPatch(typeof(TORGUIManager), nameof(TORGUIManager.Update))]
        private static class WheelUpdatePatch
        {
            private static void Postfix()
            {
                if (sendCooldownTimer > 0f) sendCooldownTimer -= Time.unscaledDeltaTime;

                UpdateBubbles();

                if (Input.GetKeyDown(KeyCode.BackQuote))
                {
                    if (IsOpen) Close();
                    else if (CanOpen()) Open();
                    return;
                }

                if (!IsOpen) return;

                if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
                {
                    Close();
                    return;
                }

                TickWheel();

                if (Input.GetMouseButtonDown(0) && hoveredIndex >= 0 && sendCooldownTimer <= 0f)
                {
                    Emit(hoveredIndex);
                    Close();
                }
            }
        }

        private static bool CanOpen()
        {
            if (PlayerControl.LocalPlayer == null) return false;
            if (MeetingHud.Instance || ExileController.Instance) return false;
            if (TORGUIManager.Instance != null && TORGUIManager.Instance.HasSomeUI) return false;
            if (TextField.AnyoneValid) return false;

            var hud = FastDestroyableSingleton<HudManager>.Instance;
            if (hud != null && hud.Chat != null && hud.Chat.IsOpenOrOpening) return false;

            return true;
        }

        private static void Emit(int index)
        {
            sendCooldownTimer = SendCooldownSeconds;
            SendEmote.Invoke((PlayerControl.LocalPlayer.PlayerId, (byte)index));
        }

        // ---------------- Wheel UI ----------------

        private static float sendCooldownTimer;

        private static GameObject wheelRoot;
        private static CanvasGroup wheelCanvasGroup;
        private static RectTransform highlightRect;
        private static Image highlightImage;
        private static RectTransform[] iconRects;
        private static float[] iconScaleCurrent;

        private static int hoveredIndex = -1;
        private static float highlightAngleCurrent;
        private static float highlightAngleVelocity;
        private static bool highlightAngleInit;
        private static float openTimeStamp;

        public static bool IsOpen => wheelRoot != null;

        private static Sprite discSprite;
        private static Sprite ringSprite;
        private static Sprite wedgeSprite;

        private static void EnsureAssets()
        {
            if (discSprite != null) return;
            float sliceAngle = 360f / Emotes.Length;
            discSprite = ToSprite(GenerateDiscTexture(128));
            ringSprite = ToSprite(GenerateRingTexture(512, DeadZoneRadius / OuterRadius));
            wedgeSprite = ToSprite(GenerateWedgeTexture(512, sliceAngle, DeadZoneRadius / OuterRadius));
        }

        private static void Open()
        {
            EnsureAssets();
            int n = Emotes.Length;
            float sliceAngle = 360f / n;

            wheelRoot = new GameObject("EmoteWheel");
            wheelRoot.layer = LayerMask.NameToLayer("UI");
            var canvas = wheelRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 10500;
            wheelCanvasGroup = wheelRoot.AddComponent<CanvasGroup>();
            wheelCanvasGroup.alpha = 0f;

            var blocker = new GameObject("Blocker");
            blocker.transform.SetParent(wheelRoot.transform, false);
            var blockerRect = blocker.AddComponent<RectTransform>();
            blockerRect.anchorMin = Vector2.zero;
            blockerRect.anchorMax = Vector2.one;
            blockerRect.sizeDelta = Vector2.zero;
            var blockerImage = blocker.AddComponent<Image>();
            blockerImage.color = new Color(0f, 0f, 0f, 0.35f);
            blockerImage.raycastTarget = true;

            var ring = CreateCenteredImage("Ring", wheelRoot.transform, ringSprite, new Vector2(OuterRadius * 2f, OuterRadius * 2f));
            ring.color = new Color(0.05f, 0.05f, 0.09f, 0.72f);

            var hl = CreateCenteredImage("Highlight", wheelRoot.transform, wedgeSprite, new Vector2(OuterRadius * 2f, OuterRadius * 2f));
            hl.color = new Color(1f, 1f, 1f, 0f);
            highlightImage = hl;
            highlightRect = hl.rectTransform;

            var center = CreateCenteredImage("Center", wheelRoot.transform, discSprite, new Vector2(DeadZoneRadius * 1.8f, DeadZoneRadius * 1.8f));
            center.color = new Color(0f, 0f, 0f, 0.55f);

            iconRects = new RectTransform[n];
            iconScaleCurrent = new float[n];

            for (int i = 0; i < n; i++)
            {
                float angle = i * sliceAngle;
                float rad = angle * Mathf.Deg2Rad;
                Vector2 pos = new Vector2(Mathf.Sin(rad) * IconRadius, Mathf.Cos(rad) * IconRadius);

                var slot = CreateCenteredRect("EmoteSlot" + i, wheelRoot.transform, new Vector2(IconSize, IconSize));
                slot.anchoredPosition = pos;
                iconRects[i] = slot;
                iconScaleCurrent[i] = 1f;

                var outline = CreateCenteredImage("Outline", slot, discSprite, new Vector2(IconSize + IconOutlineThickness * 2f, IconSize + IconOutlineThickness * 2f));
                outline.color = new Color(0.05f, 0.05f, 0.08f, 0.85f);

                var bg = CreateCenteredImage("Fill", slot, discSprite, new Vector2(IconSize, IconSize));
                bg.color = new Color(Emotes[i].Color.r / 255f, Emotes[i].Color.g / 255f, Emotes[i].Color.b / 255f, 0.85f);

                var glyph = CreateCenteredText("Glyph", slot, Vector2.zero, new Vector2(IconSize, IconSize));
                glyph.text = Emotes[i].Glyph;
                glyph.fontSize = 34f;
                glyph.color = Color.white;
                glyph.fontStyle = FontStyles.Bold;
                glyph.outlineWidth = 0.2f;
                glyph.outlineColor = new Color32(20, 20, 20, 255);
            }

            hoveredIndex = -1;
            highlightAngleInit = false;
            highlightAngleVelocity = 0f;
            openTimeStamp = Time.unscaledTime;
        }

        private static void Close()
        {
            if (wheelRoot != null) Object.Destroy(wheelRoot);
            wheelRoot = null;
            wheelCanvasGroup = null;
            highlightRect = null;
            highlightImage = null;
            iconRects = null;
            iconScaleCurrent = null;
            hoveredIndex = -1;
        }

        private static void TickWheel()
        {
            if (wheelRoot == null) return;

            float fadeT = Mathf.Clamp01((Time.unscaledTime - openTimeStamp) / 0.12f);
            wheelCanvasGroup.alpha = fadeT;
            wheelRoot.transform.localScale = Vector3.one * Mathf.Lerp(0.85f, 1f, fadeT);

            int n = Emotes.Length;
            float sliceAngle = 360f / n;

            Vector2 mouseFromCenter = (Vector2)Input.mousePosition - new Vector2(Screen.width / 2f, Screen.height / 2f);
            float dist = mouseFromCenter.magnitude;

            hoveredIndex = -1;
            if (dist >= DeadZoneRadius)
            {
                float angle = Mathf.Atan2(mouseFromCenter.x, mouseFromCenter.y) * Mathf.Rad2Deg;
                if (angle < 0f) angle += 360f;
                hoveredIndex = Mathf.RoundToInt(angle / sliceAngle) % n;
            }

            if (!highlightAngleInit)
            {
                highlightAngleCurrent = hoveredIndex >= 0 ? hoveredIndex * sliceAngle : 0f;
                highlightAngleInit = true;
            }
            else if (hoveredIndex >= 0)
            {
                float targetAngle = hoveredIndex * sliceAngle;
                highlightAngleCurrent = Mathf.SmoothDampAngle(highlightAngleCurrent, targetAngle, ref highlightAngleVelocity, 0.09f, Mathf.Infinity, Time.unscaledDeltaTime);
            }

            highlightRect.localEulerAngles = new Vector3(0f, 0f, -highlightAngleCurrent);

            var hc = highlightImage.color;
            hc.a = Mathf.MoveTowards(hc.a, hoveredIndex >= 0 ? 0.55f : 0f, Time.unscaledDeltaTime * 4f);
            highlightImage.color = hc;

            for (int i = 0; i < n; i++)
            {
                float targetScale = i == hoveredIndex ? 1.22f : 1f;
                iconScaleCurrent[i] = Mathf.MoveTowards(iconScaleCurrent[i], targetScale, Time.unscaledDeltaTime * 6f);
                iconRects[i].localScale = Vector3.one * iconScaleCurrent[i];
            }
        }

        private static RectTransform CreateCenteredRect(string name, Transform parent, Vector2 size)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            var rect = obj.AddComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = size;
            return rect;
        }

        private static Image CreateCenteredImage(string name, Transform parent, Sprite sprite, Vector2 size)
        {
            var rect = CreateCenteredRect(name, parent, size);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
            return image;
        }

        private static TextMeshProUGUI CreateCenteredText(string name, Transform parent, Vector2 pos, Vector2 size)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            var rect = obj.AddComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;
            var text = obj.AddComponent<TextMeshProUGUI>();
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            return text;
        }

        // ---------------- Procedural textures ----------------

        private static Sprite ToSprite(Texture2D tex) => Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);

        private static Texture2D NewTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            return tex;
        }

        private static Texture2D GenerateDiscTexture(int size)
        {
            var tex = NewTexture(size);
            float r = size / 2f;
            const float feather = 2.5f;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - r;
                    float dy = y + 0.5f - r;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01((r - d) / feather);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        private static Texture2D GenerateRingTexture(int size, float innerRatio)
        {
            var tex = NewTexture(size);
            float r = size / 2f;
            float innerR = r * innerRatio;
            const float feather = 2.5f;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - r;
                    float dy = y + 0.5f - r;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = 0f;
                    if (d <= r)
                    {
                        a = 1f;
                        if (d > r - feather) a *= Mathf.Clamp01((r - d) / feather);
                        if (d < innerR + feather) a *= Mathf.Clamp01((d - innerR) / feather);
                    }
                    pixels[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        private static Texture2D GenerateWedgeTexture(int size, float sliceAngleDeg, float innerRatio)
        {
            var tex = NewTexture(size);
            float r = size / 2f;
            float innerR = r * innerRatio;
            float half = sliceAngleDeg * 0.5f;
            const float feather = 2.5f;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - r;
                    float dy = y + 0.5f - r;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float angle = Mathf.Atan2(dx, dy) * Mathf.Rad2Deg;
                    float da = Mathf.Abs(Mathf.DeltaAngle(0f, angle));
                    float a = 0f;
                    if (da <= half && d <= r)
                    {
                        a = 1f;
                        if (d > r - feather) a *= Mathf.Clamp01((r - d) / feather);
                        if (d < innerR + feather) a *= Mathf.Clamp01((d - innerR) / feather);
                    }
                    pixels[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        // ---------------- Received emote bubbles ----------------

        private class ActiveBubble
        {
            public PlayerControl Target;
            public RectTransform Rect;
            public CanvasGroup Group;
            public float Timer;
        }

        private static readonly List<ActiveBubble> bubbles = new();
        private static GameObject bubbleLayerRoot;

        private static void EnsureBubbleLayer()
        {
            if (bubbleLayerRoot != null) return;
            bubbleLayerRoot = new GameObject("EmoteBubbleLayer");
            bubbleLayerRoot.layer = LayerMask.NameToLayer("UI");
            var canvas = bubbleLayerRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 9000;
        }

        private static void ShowBubble(PlayerControl target, int emoteIndex)
        {
            if (target == null) return;
            EnsureAssets();
            EnsureBubbleLayer();

            var obj = new GameObject("EmoteBubble");
            obj.transform.SetParent(bubbleLayerRoot.transform, false);
            var rect = obj.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(60f, 60f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            var group = obj.AddComponent<CanvasGroup>();

            var outline = CreateCenteredImage("Outline", obj.transform, discSprite, new Vector2(60f + IconOutlineThickness * 2f, 60f + IconOutlineThickness * 2f));
            outline.color = new Color(0.05f, 0.05f, 0.08f, 0.85f);

            var bg = CreateCenteredImage("Bg", obj.transform, discSprite, new Vector2(60f, 60f));
            var c = Emotes[emoteIndex].Color;
            bg.color = new Color(c.r / 255f, c.g / 255f, c.b / 255f, 0.9f);

            var glyph = CreateCenteredText("Glyph", obj.transform, Vector2.zero, new Vector2(60f, 60f));
            glyph.text = Emotes[emoteIndex].Glyph;
            glyph.fontSize = 30f;
            glyph.color = Color.white;
            glyph.fontStyle = FontStyles.Bold;
            glyph.outlineWidth = 0.2f;
            glyph.outlineColor = new Color32(20, 20, 20, 255);

            bubbles.Add(new ActiveBubble { Target = target, Rect = rect, Group = group, Timer = 0f });
        }

        private static void UpdateBubbles()
        {
            if (bubbles.Count == 0) return;
            var cam = Camera.main;

            for (int i = bubbles.Count - 1; i >= 0; i--)
            {
                var b = bubbles[i];
                if (b.Target == null || b.Rect == null)
                {
                    if (b.Rect != null) Object.Destroy(b.Rect.gameObject);
                    bubbles.RemoveAt(i);
                    continue;
                }

                b.Timer += Time.unscaledDeltaTime;
                if (b.Timer >= BubbleLifetime)
                {
                    Object.Destroy(b.Rect.gameObject);
                    bubbles.RemoveAt(i);
                    continue;
                }

                bool visible = cam != null && b.Target.Visible;
                if (visible)
                {
                    Vector3 worldPos = b.Target.transform.position + new Vector3(0f, 0.85f, 0f);
                    Vector3 screenPos = cam.WorldToScreenPoint(worldPos);
                    visible = screenPos.z > 0f;
                    if (visible) b.Rect.position = screenPos;
                }

                float popIn = Mathf.Clamp01(b.Timer / 0.15f);
                float fadeOut = Mathf.Clamp01((BubbleLifetime - b.Timer) / BubbleFadeOut);
                b.Group.alpha = visible ? popIn * fadeOut : 0f;
                b.Rect.localScale = Vector3.one * Mathf.Lerp(0.4f, 1f, popIn);
            }
        }
    }
}
