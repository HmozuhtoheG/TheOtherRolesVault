using System.Collections.Generic;
using TheOtherRoles.MetaContext;
using TheOtherRoles.Modules.Emotes;
using TheOtherRoles.Utilities;
using UnityEngine;
using Image = UnityEngine.UI.Image;

namespace TheOtherRoles.Modules
{
    public static class EmoteWheel
    {
        private const int Slots = EmoteCatalog.SlotsPerPage;
        private const float SliceAngle = 360f / Slots;

        private const float OuterRadius = 205f;
        private const float DeadZoneRadius = 52f;
        private const float IconRadius = 150f;
        private const float IconSize = 96f;
        private const float IconOutlineThickness = 7f;
        private const float BubbleIconSize = 84f;
        private const float BubbleLifetime = 2.6f;
        private const float BubbleFadeOut = 0.5f;

        private const float PagerY = -OuterRadius - 34f;
        private const float ArrowOffsetX = OuterRadius - 34f;
        private const float ArrowSize = 26f;
        private const float DotSpacing = 16f;
        private const float DotSize = 9f;

        internal const float ConfirmGraceSeconds = 0.18f;

        public static void OpenFromButton()
        {
            if (IsOpen || !CanOpen()) return;
            heldByKey = false;
            Open();
        }

        public static void OpenFromKey()
        {
            if (!IsOpen && !CanOpen()) return;
            heldByKey = true;
            if (!IsOpen) Open();
        }

        public static void CloseFromButton()
        {
            if (IsOpen) Close();
        }

        public static bool CanOpen()
        {
            if (PlayerControl.LocalPlayer == null) return false;
            if (PlayerControl.LocalPlayer.Data == null || PlayerControl.LocalPlayer.Data.IsDead) return false;
            if (EmoteCatalog.PageCount == 0) return false;
            if (MeetingHud.Instance || ExileController.Instance) return false;
            if (TORGUIManager.Instance != null && TORGUIManager.Instance.HasSomeUI) return false;
            if (TextField.AnyoneValid) return false;

            var hud = FastDestroyableSingleton<HudManager>.Instance;
            if (hud != null && hud.Chat != null && hud.Chat.IsOpenOrOpening) return false;

            return true;
        }

        internal static void Emit(int index)
        {
            var page = CurrentPage();
            if (page == null || index < 0 || index >= page.Count) return;
            EmoteNet.Send(page[index]);
        }

        internal static void ChangePage(int delta)
        {
            int count = Mathf.Max(1, EmoteCatalog.PageCount);
            if (count <= 1) return;
            currentPage = ((currentPage + delta) % count + count) % count;
            hoveredIndex = -1;
            RenderSlots();
            RefreshPager();
        }

        private static List<EmoteDefinition> CurrentPage()
        {
            var pages = EmoteCatalog.Pages;
            if (pages.Count == 0) return null;
            return pages[Mathf.Clamp(currentPage, 0, pages.Count - 1)];
        }

        private static GameObject wheelRoot;
        private static CanvasGroup wheelCanvasGroup;
        private static RectTransform highlightRect;
        private static Image highlightImage;
        private static RectTransform[] slotRects;
        private static Image[] slotIcons;
        private static float[] iconScaleCurrent;
        private static RectTransform dotsRoot;
        private static RectTransform arrowLeftRect;
        private static RectTransform arrowRightRect;
        private static Image arrowLeftImage;
        private static Image arrowRightImage;

        internal static int hoveredIndex = -1;
        internal static int hoveredArrow;
        internal static bool heldByKey;
        internal static int currentPage;
        private static float highlightAngleCurrent;
        private static float highlightAngleVelocity;
        private static bool highlightAngleInit;
        internal static float openTimeStamp;

        public static bool IsOpen => wheelRoot != null;

        private static Sprite discSprite;
        private static Sprite ringSprite;
        private static Sprite wedgeSprite;
        private static Sprite triangleRightSprite;
        private static Sprite triangleLeftSprite;

        private static void EnsureAssets()
        {
            if (discSprite != null) return;
            float innerRatio = DeadZoneRadius / OuterRadius;
            discSprite = ToSprite(GenerateRadialTexture(128, 0f, null));
            ringSprite = ToSprite(GenerateRadialTexture(512, innerRatio, null));
            wedgeSprite = ToSprite(GenerateRadialTexture(512, innerRatio, SliceAngle));
            triangleRightSprite = ToSprite(GenerateTriangleTexture(64, true));
            triangleLeftSprite = ToSprite(GenerateTriangleTexture(64, false));
        }

        internal static void Open()
        {
            EnsureAssets();
            currentPage = 0;

            wheelRoot = Helpers.CreateOverlayCanvas("EmoteWheel", 10500);
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

            var ring = Helpers.CreateCenteredImage("Ring", wheelRoot.transform, ringSprite, new Vector2(OuterRadius * 2f, OuterRadius * 2f));
            ring.color = new Color(0.05f, 0.05f, 0.09f, 0.72f);

            var hl = Helpers.CreateCenteredImage("Highlight", wheelRoot.transform, wedgeSprite, new Vector2(OuterRadius * 2f, OuterRadius * 2f));
            hl.color = new Color(1f, 1f, 1f, 0f);
            highlightImage = hl;
            highlightRect = hl.rectTransform;

            var center = Helpers.CreateCenteredImage("Center", wheelRoot.transform, discSprite, new Vector2(DeadZoneRadius * 1.8f, DeadZoneRadius * 1.8f));
            center.color = new Color(0f, 0f, 0f, 0.55f);

            slotRects = new RectTransform[Slots];
            slotIcons = new Image[Slots];
            iconScaleCurrent = new float[Slots];

            for (int i = 0; i < Slots; i++)
            {
                float angle = i * SliceAngle;
                float rad = angle * Mathf.Deg2Rad;
                Vector2 pos = new Vector2(Mathf.Sin(rad) * IconRadius, Mathf.Cos(rad) * IconRadius);

                var slot = Helpers.CreateCenteredRect("EmoteSlot" + i, wheelRoot.transform, new Vector2(IconSize, IconSize));
                slot.anchoredPosition = pos;
                slotRects[i] = slot;
                iconScaleCurrent[i] = 1f;

                var outline = Helpers.CreateCenteredImage("Outline", slot, discSprite, new Vector2(IconSize + IconOutlineThickness * 2f, IconSize + IconOutlineThickness * 2f));
                outline.color = new Color(0.05f, 0.05f, 0.08f, 0.85f);

                var icon = Helpers.CreateCenteredImage("Icon", slot, null, new Vector2(IconSize, IconSize));
                icon.color = Color.white;
                slotIcons[i] = icon;
            }

            BuildPager();
            RenderSlots();
            RefreshPager();

            hoveredIndex = -1;
            hoveredArrow = 0;
            highlightAngleInit = false;
            highlightAngleVelocity = 0f;
            openTimeStamp = Time.unscaledTime;
        }

        private static void BuildPager()
        {
            arrowLeftRect = Helpers.CreateCenteredRect("PagePrev", wheelRoot.transform, new Vector2(ArrowSize, ArrowSize));
            arrowLeftRect.anchoredPosition = new Vector2(-ArrowOffsetX, PagerY);
            arrowLeftImage = arrowLeftRect.gameObject.AddComponent<Image>();
            arrowLeftImage.sprite = triangleLeftSprite;
            arrowLeftImage.raycastTarget = false;

            arrowRightRect = Helpers.CreateCenteredRect("PageNext", wheelRoot.transform, new Vector2(ArrowSize, ArrowSize));
            arrowRightRect.anchoredPosition = new Vector2(ArrowOffsetX, PagerY);
            arrowRightImage = arrowRightRect.gameObject.AddComponent<Image>();
            arrowRightImage.sprite = triangleRightSprite;
            arrowRightImage.raycastTarget = false;

            dotsRoot = Helpers.CreateCenteredRect("PageDots", wheelRoot.transform, Vector2.zero);
            dotsRoot.anchoredPosition = new Vector2(0f, PagerY);
        }

        private static void RenderSlots()
        {
            var page = CurrentPage();
            for (int i = 0; i < Slots; i++)
            {
                bool has = page != null && i < page.Count && page[i] != null && page[i].Icon != null;
                slotRects[i].gameObject.SetActive(has);
                if (has) slotIcons[i].sprite = page[i].Icon;
            }
        }

        private static void RefreshPager()
        {
            if (dotsRoot == null) return;

            for (int i = dotsRoot.childCount - 1; i >= 0; i--)
            {
                Object.Destroy(dotsRoot.GetChild(i).gameObject);
            }

            int count = Mathf.Max(1, EmoteCatalog.PageCount);
            float start = -(count - 1) * DotSpacing * 0.5f;
            for (int i = 0; i < count; i++)
            {
                var dot = Helpers.CreateCenteredImage("Dot" + i, dotsRoot, discSprite, new Vector2(DotSize, DotSize));
                dot.rectTransform.anchoredPosition = new Vector2(start + i * DotSpacing, 0f);
                dot.color = i == currentPage ? Color.white : new Color(1f, 1f, 1f, 0.32f);
            }

            bool many = count > 1;
            arrowLeftRect.gameObject.SetActive(many);
            arrowRightRect.gameObject.SetActive(many);
            arrowLeftImage.color = new Color(1f, 1f, 1f, 0.55f);
            arrowRightImage.color = new Color(1f, 1f, 1f, 0.55f);
        }

        internal static void Close()
        {
            if (wheelRoot != null) Object.Destroy(wheelRoot);
            wheelRoot = null;
            wheelCanvasGroup = null;
            highlightRect = null;
            highlightImage = null;
            slotRects = null;
            slotIcons = null;
            iconScaleCurrent = null;
            dotsRoot = null;
            arrowLeftRect = null;
            arrowRightRect = null;
            arrowLeftImage = null;
            arrowRightImage = null;
            hoveredIndex = -1;
            hoveredArrow = 0;
            heldByKey = false;
        }

        internal static void TickWheel()
        {
            if (wheelRoot == null) return;

            float fadeT = Mathf.Clamp01((Time.unscaledTime - openTimeStamp) / 0.12f);
            wheelCanvasGroup.alpha = fadeT;
            wheelRoot.transform.localScale = Vector3.one * Mathf.Lerp(0.85f, 1f, fadeT);

            int n = Slots;

            Vector2 mouseFromCenter = (Vector2)Input.mousePosition - new Vector2(Screen.width / 2f, Screen.height / 2f);
            float dist = mouseFromCenter.magnitude;

            hoveredArrow = 0;
            if (EmoteCatalog.PageCount > 1)
            {
                if ((mouseFromCenter - new Vector2(-ArrowOffsetX, PagerY)).magnitude <= ArrowSize * 1.1f) hoveredArrow = -1;
                else if ((mouseFromCenter - new Vector2(ArrowOffsetX, PagerY)).magnitude <= ArrowSize * 1.1f) hoveredArrow = 1;

                arrowLeftImage.color = new Color(1f, 1f, 1f, hoveredArrow == -1 ? 1f : 0.55f);
                arrowRightImage.color = new Color(1f, 1f, 1f, hoveredArrow == 1 ? 1f : 0.55f);
            }

            hoveredIndex = -1;
            if (hoveredArrow == 0 && dist >= DeadZoneRadius)
            {
                float angle = Mathf.Atan2(mouseFromCenter.x, mouseFromCenter.y) * Mathf.Rad2Deg;
                if (angle < 0f) angle += 360f;
                int index = Mathf.RoundToInt(angle / SliceAngle) % n;
                if (index >= 0 && index < n && slotRects[index].gameObject.activeSelf) hoveredIndex = index;
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
                slotRects[i].localScale = Vector3.one * iconScaleCurrent[i];
            }
        }

        private static Sprite ToSprite(Texture2D tex) => Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);

        private static Texture2D NewTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            return tex;
        }

        private static Texture2D GenerateTriangleTexture(int size, bool pointRight)
        {
            var tex = NewTexture(size);
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = (x + 0.5f) / size;
                    float ny = (y + 0.5f) / size;
                    float halfWidth = 0.5f * (pointRight ? nx : 1f - nx);
                    float distance = halfWidth - Mathf.Abs(ny - 0.5f);
                    float a = Mathf.Clamp01(distance * size * 0.35f) * Mathf.Clamp01(0.92f - ny * 0.04f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

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
            public Image Icon;
            public Sprite[] Frames;
            public float[] FrameDelays;
            public float FrameSeconds;
            public float FrameTimer;
            public int FrameIndex;
            public float Lifetime;
            public float Timer;
        }

        private static readonly List<ActiveBubble> bubbles = new();
        private static GameObject bubbleLayerRoot;

        private static void EnsureBubbleLayer()
        {
            if (bubbleLayerRoot != null) return;
            bubbleLayerRoot = Helpers.CreateOverlayCanvas("EmoteBubbleLayer", 9000);
        }

        internal static void ShowBubble(PlayerControl target, EmoteDefinition emote)
        {
            if (target == null || emote == null) return;
            var frames = emote.Frames;
            if (frames == null || frames.Length == 0 || frames[0] == null) return;

            EnsureAssets();
            EnsureBubbleLayer();

            var obj = new GameObject("EmoteBubble");
            obj.transform.SetParent(bubbleLayerRoot.transform, false);
            var rect = obj.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(BubbleIconSize, BubbleIconSize);
            rect.pivot = new Vector2(0.5f, 0.5f);
            var group = obj.AddComponent<CanvasGroup>();

            var icon = Helpers.CreateCenteredImage("Icon", obj.transform, frames[0], new Vector2(BubbleIconSize, BubbleIconSize));
            icon.color = Color.white;

            var delays = emote.FrameDelays;

            float lifetime = BubbleLifetime;
            if (frames.Length > 1)
            {
                float cycle = 0f;
                for (int i = 0; i < frames.Length; i++)
                {
                    cycle += delays != null && i < delays.Length ? delays[i] : emote.FrameSeconds;
                }
                lifetime = Mathf.Clamp(cycle * 2f, BubbleLifetime, 6f);
            }

            bubbles.Add(new ActiveBubble
            {
                Target = target,
                Rect = rect,
                Group = group,
                Icon = icon,
                Frames = frames,
                FrameDelays = delays,
                FrameSeconds = Mathf.Max(0.02f, emote.FrameSeconds),
                Lifetime = lifetime
            });
        }

        private static float FrameStep(ActiveBubble bubble)
        {
            var delays = bubble.FrameDelays;
            if (delays != null && delays.Length > 0) return delays[bubble.FrameIndex % delays.Length];
            return bubble.FrameSeconds;
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
                if (b.Timer >= b.Lifetime)
                {
                    Object.Destroy(b.Rect.gameObject);
                    bubbles.RemoveAt(i);
                    continue;
                }

                if (b.Frames != null && b.Frames.Length > 1)
                {
                    b.FrameTimer += Time.unscaledDeltaTime;
                    float step = FrameStep(b);
                    while (b.FrameTimer >= step)
                    {
                        b.FrameTimer -= step;
                        b.FrameIndex = (b.FrameIndex + 1) % b.Frames.Length;
                        if (b.Icon != null) b.Icon.sprite = b.Frames[b.FrameIndex];
                        step = FrameStep(b);
                    }
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
                float fadeOut = Mathf.Clamp01((b.Lifetime - b.Timer) / BubbleFadeOut);
                b.Group.alpha = visible ? popIn * fadeOut : 0f;
                b.Rect.localScale = Vector3.one * Mathf.Lerp(0.4f, 1f, popIn);
            }
        }
    }
}
