using System.Collections.Generic;
using TheOtherRoles.MetaContext;
using TheOtherRoles.Utilities;
using UnityEngine;
using Image = UnityEngine.UI.Image;

namespace TheOtherRoles.Modules
{
    // input handling lives in Patches/EmoteWheelPatch.cs, this is just the wheel itself
    [TORRPCHolder]
    public static class EmoteWheel
    {
        private const int EmoteCount = 8;
        private const float SliceAngle = 360f / EmoteCount;

        private static Sprite[] emoteSprites;
        private static Sprite[] EmoteSprites
        {
            get
            {
                if (emoteSprites == null)
                {
                    emoteSprites = new Sprite[EmoteCount];
                    for (int i = 0; i < EmoteCount; i++)
                        emoteSprites[i] = Helpers.loadSpriteFromResources($"TheOtherRoles.Resources.expression{i + 1}.png", 100f);
                }
                return emoteSprites;
            }
        }

        private const float OuterRadius = 205f;
        private const float DeadZoneRadius = 52f;
        private const float IconRadius = 150f;
        private const float IconSize = 96f;
        private const float IconOutlineThickness = 7f;
        private const float BubbleIconSize = 84f;
        private const float SendCooldownSeconds = 1.2f;
        private const float BubbleLifetime = 2.6f;
        private const float BubbleFadeOut = 0.5f;

        internal const float ConfirmGraceSeconds = 0.18f;

        public static RemoteProcess<(byte senderId, byte emoteIndex)> SendEmote = new("EmoteWheelSend", (message, _) =>
        {
            var sender = Helpers.playerById(message.senderId);
            if (sender == null || message.emoteIndex >= EmoteCount) return;
            ShowBubble(sender, message.emoteIndex);
        });

        public static void OpenFromButton()
        {
            if (IsOpen || !CanOpen()) return;
            Open();
        }

        public static void CloseFromButton()
        {
            if (IsOpen) Close();
        }

        public static bool CanOpen()
        {
            if (PlayerControl.LocalPlayer == null) return false;
            if (sendCooldownTimer > 0f) return false;
            if (MeetingHud.Instance || ExileController.Instance) return false;
            if (TORGUIManager.Instance != null && TORGUIManager.Instance.HasSomeUI) return false;
            if (TextField.AnyoneValid) return false;

            var hud = FastDestroyableSingleton<HudManager>.Instance;
            if (hud != null && hud.Chat != null && hud.Chat.IsOpenOrOpening) return false;

            return true;
        }

        internal static void Emit(int index)
        {
            sendCooldownTimer = SendCooldownSeconds;
            SendEmote.Invoke((PlayerControl.LocalPlayer.PlayerId, (byte)index));
        }

        internal static float sendCooldownTimer;

        private static GameObject wheelRoot;
        private static CanvasGroup wheelCanvasGroup;
        private static RectTransform highlightRect;
        private static Image highlightImage;
        private static RectTransform[] iconRects;
        private static float[] iconScaleCurrent;

        internal static int hoveredIndex = -1;
        private static float highlightAngleCurrent;
        private static float highlightAngleVelocity;
        private static bool highlightAngleInit;
        internal static float openTimeStamp;

        public static bool IsOpen => wheelRoot != null;

        private static Sprite discSprite;
        private static Sprite ringSprite;
        private static Sprite wedgeSprite;

        private static void EnsureAssets()
        {
            if (discSprite != null) return;
            float innerRatio = DeadZoneRadius / OuterRadius;
            discSprite = ToSprite(GenerateRadialTexture(128, 0f, null));
            ringSprite = ToSprite(GenerateRadialTexture(512, innerRatio, null));
            wedgeSprite = ToSprite(GenerateRadialTexture(512, innerRatio, SliceAngle));
        }

        internal static void Open()
        {
            EnsureAssets();
            int n = EmoteCount;

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
                float angle = i * SliceAngle;
                float rad = angle * Mathf.Deg2Rad;
                Vector2 pos = new Vector2(Mathf.Sin(rad) * IconRadius, Mathf.Cos(rad) * IconRadius);

                var slot = CreateCenteredRect("EmoteSlot" + i, wheelRoot.transform, new Vector2(IconSize, IconSize));
                slot.anchoredPosition = pos;
                iconRects[i] = slot;
                iconScaleCurrent[i] = 1f;

                var outline = CreateCenteredImage("Outline", slot, discSprite, new Vector2(IconSize + IconOutlineThickness * 2f, IconSize + IconOutlineThickness * 2f));
                outline.color = new Color(0.05f, 0.05f, 0.08f, 0.85f);

                var icon = CreateCenteredImage("Icon", slot, EmoteSprites[i], new Vector2(IconSize, IconSize));
                icon.color = Color.white;
            }

            hoveredIndex = -1;
            highlightAngleInit = false;
            highlightAngleVelocity = 0f;
            openTimeStamp = Time.unscaledTime;
        }

        internal static void Close()
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

        internal static void TickWheel()
        {
            if (wheelRoot == null) return;

            float fadeT = Mathf.Clamp01((Time.unscaledTime - openTimeStamp) / 0.12f);
            wheelCanvasGroup.alpha = fadeT;
            wheelRoot.transform.localScale = Vector3.one * Mathf.Lerp(0.85f, 1f, fadeT);

            int n = EmoteCount;

            Vector2 mouseFromCenter = (Vector2)Input.mousePosition - new Vector2(Screen.width / 2f, Screen.height / 2f);
            float dist = mouseFromCenter.magnitude;

            hoveredIndex = -1;
            if (dist >= DeadZoneRadius)
            {
                float angle = Mathf.Atan2(mouseFromCenter.x, mouseFromCenter.y) * Mathf.Rad2Deg;
                if (angle < 0f) angle += 360f;
                hoveredIndex = Mathf.RoundToInt(angle / SliceAngle) % n;
            }

            if (!highlightAngleInit)
            {
                highlightAngleCurrent = hoveredIndex >= 0 ? hoveredIndex * SliceAngle : 0f;
                highlightAngleInit = true;
            }
            else if (hoveredIndex >= 0)
            {
                float targetAngle = hoveredIndex * SliceAngle;
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

        private static Sprite ToSprite(Texture2D tex) => Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);

        private static Texture2D NewTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            return tex;
        }

        // disc/ring/wedge are all the same filled-circle-with-feathered-edges math, just with an
        // optional inner cutout (innerRatio > 0) and an optional angular slice (sliceAngleDeg)
        private static Texture2D GenerateRadialTexture(int size, float innerRatio, float? sliceAngleDeg)
        {
            var tex = NewTexture(size);
            float r = size / 2f;
            float innerR = r * innerRatio;
            float half = sliceAngleDeg.HasValue ? sliceAngleDeg.Value * 0.5f : 0f;
            const float feather = 2.5f;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - r;
                    float dy = y + 0.5f - r;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);

                    bool inSlice = true;
                    if (sliceAngleDeg.HasValue)
                    {
                        float angle = Mathf.Atan2(dx, dy) * Mathf.Rad2Deg;
                        inSlice = Mathf.Abs(Mathf.DeltaAngle(0f, angle)) <= half;
                    }

                    float a = 0f;
                    if (d <= r && inSlice)
                    {
                        a = 1f;
                        if (d > r - feather) a *= Mathf.Clamp01((r - d) / feather);
                        if (innerRatio > 0f && d < innerR + feather) a *= Mathf.Clamp01((d - innerR) / feather);
                    }
                    pixels[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

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
            rect.sizeDelta = new Vector2(BubbleIconSize, BubbleIconSize);
            rect.pivot = new Vector2(0.5f, 0.5f);
            var group = obj.AddComponent<CanvasGroup>();

            var icon = CreateCenteredImage("Icon", obj.transform, EmoteSprites[emoteIndex], new Vector2(BubbleIconSize, BubbleIconSize));
            icon.color = Color.white;

            bubbles.Add(new ActiveBubble { Target = target, Rect = rect, Group = group, Timer = 0f });
        }

        internal static void UpdateBubbles()
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
