using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using TheOtherRoles.MetaContext;
using static TheOtherRoles.TheOtherRoles;
using TMPro;
using UnityEngine;
using Image = UnityEngine.UI.Image;

namespace TheOtherRoles.Modules
{
    internal static class RoleDraftWheel
    {
        private const float PanelRadius = 268f;
        private const float IconRingRadius = 196f;
        private const float IconSize = 100f;
        private const float IconPad = 8f;
        private const float HubRadius = 66f;
        private const float DeadZoneRadius = 62f;
        private const float HoverScale = 1.22f;
        private const float InputGrace = 0.12f;
        private const float OpenFade = 0.15f;
        private const float OutroDuration = 0.4f;
        private const float ViewfinderSize = 138f;
        private const float BracketThickness = 5f;
        private const float BracketLength = 30f;
        private const int SortingOrder = 10400;

        private static readonly RoleId[] ButtonIllustrationRoles = [RoleId.Agnosia, RoleId.Illusionist];
        private static readonly Vector2 CenterOffset = new(0f, -30f);
        private static readonly Color FrameIdle = new(0.06f, 0.07f, 0.10f, 0.9f);
        private static readonly Color RandomColor = new(0.30f, 0.85f, 0.35f, 1f);

        private static GameObject wheelRoot;
        private static GameObject outroRoot;
        private static CanvasGroup canvasGroup;
        private static RectTransform container;
        private static RectTransform viewfinder;
        private static Image flashImage;
        private static TextMeshProUGUI hubLabel;
        private static TextMeshProUGUI countdownLabel;
        private static Image countdownRing;

        private static RectTransform[] slotRects;
        private static Image[] slotFrames;
        private static Image[] slotIcons;
        private static float[] slotScales;
        private static RoleInfo[] slotRoles;

        private static Sprite discSprite;
        private static Sprite ringSprite;
        private static Sprite hubRingSprite;

        private static Action<RoleInfo> pickAction;
        private static Action randomAction;
        private static int slotCount;
        private static float sliceAngle;
        private static float maxTimer;
        private static float uiScale = 1f;
        private static float openTime;
        private static float viewfinderAlpha;
        private static string randomLabel;
        private static string countdownFormat;
        private static int lastSecondsLeft = -1;
        private static int hoveredIndex = -1;
        private static bool accepting;

        internal static bool IsOpen => wheelRoot != null;

        internal static void Build(IReadOnlyList<RoleInfo> roles, IReadOnlyList<RoleInfo> randomPool, float timerLimit,
            Action<RoleInfo> onPick, Action onRandom)
        {
            if (roles == null || roles.Count == 0) return;

            EnsureAssets();

            pickAction = onPick;
            randomAction = onRandom;
            maxTimer = timerLimit;
            hoveredIndex = -1;
            accepting = true;
            openTime = Time.unscaledTime;
            uiScale = Mathf.Clamp(Mathf.Min(Screen.height / 1080f, Screen.width / 1920f), 0.6f, 1.35f);
            randomLabel = ModTranslation.getString("roleDraftRandom");
            countdownFormat = ModTranslation.getString("roleDraftRandomSelection");
            lastSecondsLeft = -1;

            bool withRandom = randomPool != null;
            slotCount = roles.Count + (withRandom ? 1 : 0);
            sliceAngle = 360f / slotCount;
            slotRoles = new RoleInfo[slotCount];
            for (int i = 0; i < roles.Count; i++) slotRoles[i] = roles[i];

            Cursor.visible = true;

            wheelRoot = Helpers.CreateOverlayCanvas("RoleDraftWheel", SortingOrder);
            canvasGroup = wheelRoot.AddComponent<CanvasGroup>();
            canvasGroup.alpha = 0f;

            container = Helpers.CreateCenteredRect("Container", wheelRoot.transform, Vector2.zero);
            container.anchoredPosition = CenterOffset;
            container.localScale = Vector3.one * uiScale;

            var panel = Helpers.CreateCenteredImage("Panel", container, discSprite, new Vector2(PanelRadius * 2f, PanelRadius * 2f));
            panel.color = new Color(0.043f, 0.051f, 0.075f, 0.86f);

            var rim = Helpers.CreateCenteredImage("PanelRim", container, ringSprite, new Vector2(PanelRadius * 2f, PanelRadius * 2f));
            rim.color = new Color(0.28f, 0.30f, 0.38f, 0.85f);

            countdownRing = Helpers.CreateCenteredImage("CountdownRing", container, ringSprite, new Vector2(IconRingRadius * 2.3f, IconRingRadius * 2.3f));
            countdownRing.color = new Color(1f, 1f, 1f, 0.35f);
            countdownRing.type = Image.Type.Filled;
            countdownRing.fillMethod = Image.FillMethod.Radial360;
            countdownRing.fillOrigin = (int)Image.Origin360.Top;
            countdownRing.fillClockwise = false;
            countdownRing.fillAmount = 1f;

            BuildHub();
            BuildSlots(withRandom);
            BuildViewfinder();
            BuildCountdownLabel();

            flashImage = Helpers.CreateCenteredRect("Flash", wheelRoot.transform, Vector2.zero).gameObject.AddComponent<Image>();
            var flashRect = flashImage.rectTransform;
            flashRect.anchorMin = Vector2.zero;
            flashRect.anchorMax = Vector2.one;
            flashRect.sizeDelta = Vector2.zero;
            flashImage.color = new Color(1f, 1f, 1f, 0f);
            flashImage.raycastTarget = false;

            SoundEffectsManager.play("timemasterShield");
        }

        private static void EnsureAssets()
        {
            if (discSprite != null) return;
            discSprite = Helpers.ToSprite(Helpers.GenerateRadialTexture(256, 0f, null));
            ringSprite = Helpers.ToSprite(Helpers.GenerateRadialTexture(512, 0.93f, null));
            hubRingSprite = Helpers.ToSprite(Helpers.GenerateRadialTexture(512, DeadZoneRadius / PanelRadius, null));
        }

        private static void BuildHub()
        {
            var hub = Helpers.CreateCenteredImage("Hub", container, discSprite, new Vector2(HubRadius * 2f, HubRadius * 2f));
            hub.color = new Color(0.02f, 0.025f, 0.04f, 0.95f);

            var aperture = Helpers.CreateCenteredImage("HubAperture", container, hubRingSprite, new Vector2(HubRadius * 2f, HubRadius * 2f));
            aperture.color = new Color(1f, 1f, 1f, 0.18f);

            var labelObj = new GameObject("HubLabel");
            labelObj.transform.SetParent(container, false);
            var labelRect = labelObj.AddComponent<RectTransform>();
            labelRect.anchorMin = labelRect.anchorMax = new Vector2(0.5f, 0.5f);
            labelRect.pivot = new Vector2(0.5f, 0.5f);
            labelRect.anchoredPosition = Vector2.zero;
            labelRect.sizeDelta = new Vector2(HubRadius * 2.4f, HubRadius * 2f);
            hubLabel = labelObj.AddComponent<TextMeshProUGUI>();
            hubLabel.alignment = TextAlignmentOptions.Center;
            hubLabel.fontSize = 24f * uiScale;
            hubLabel.color = Color.white;
            hubLabel.text = "";
            hubLabel.raycastTarget = false;
        }

        private static void BuildSlots(bool withRandom)
        {
            slotRects = new RectTransform[slotCount];
            slotFrames = new Image[slotCount];
            slotIcons = new Image[slotCount];
            slotScales = new float[slotCount];

            for (int i = 0; i < slotCount; i++)
            {
                float rad = i * sliceAngle * Mathf.Deg2Rad;
                var slot = Helpers.CreateCenteredRect("RoleSlot" + i, container, new Vector2(IconSize, IconSize));
                slot.anchoredPosition = new Vector2(Mathf.Sin(rad) * IconRingRadius, Mathf.Cos(rad) * IconRingRadius);
                slotRects[i] = slot;
                slotScales[i] = 1f;

                bool isRandom = withRandom && i == slotCount - 1;
                RoleInfo roleInfo = slotRoles[i];

                slotFrames[i] = Helpers.CreateCenteredImage("Frame", slot, discSprite, new Vector2(IconSize + IconPad * 2f, IconSize + IconPad * 2f));
                slotFrames[i].color = isRandom ? RandomColor : FrameIdle;

                Sprite icon = isRandom ? null : GetRoleArt(roleInfo);
                slotIcons[i] = Helpers.CreateCenteredImage("Icon", slot, icon, new Vector2(IconSize, IconSize));
                slotIcons[i].color = icon == null ? new Color(1f, 1f, 1f, 0f) : Color.white;
                slotIcons[i].preserveAspect = true;

                var glyphObj = new GameObject("Glyph");
                glyphObj.transform.SetParent(slot, false);
                var glyphRect = glyphObj.AddComponent<RectTransform>();
                glyphRect.anchorMin = glyphRect.anchorMax = new Vector2(0.5f, 0.5f);
                glyphRect.pivot = new Vector2(0.5f, 0.5f);
                glyphRect.anchoredPosition = Vector2.zero;
                glyphRect.sizeDelta = new Vector2(IconSize, IconSize);
                var glyph = glyphObj.AddComponent<TextMeshProUGUI>();
                glyph.alignment = TextAlignmentOptions.Center;
                glyph.fontSize = 56f * uiScale;
                glyph.color = Color.white;
                glyph.raycastTarget = false;
                glyph.text = icon != null ? "" : (isRandom ? randomLabel : "?");
                if (glyph.text.Length > 2) glyph.fontSize = 16f * uiScale;
            }
        }

        private static void BuildCountdownLabel()
        {
            var obj = new GameObject("CountdownLabel");
            obj.transform.SetParent(container, false);
            var rect = obj.AddComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, -(PanelRadius + 34f));
            rect.sizeDelta = new Vector2(PanelRadius * 2f, 60f);
            countdownLabel = obj.AddComponent<TextMeshProUGUI>();
            countdownLabel.alignment = TextAlignmentOptions.Center;
            countdownLabel.fontSize = 30f * uiScale;
            countdownLabel.color = new Color(1f, 1f, 1f, 0.85f);
            countdownLabel.raycastTarget = false;
            countdownLabel.text = "";
        }

        private static Sprite GetRoleArt(RoleInfo roleInfo)
        {
            var art = ButtonIllustrationRoles.Contains(roleInfo.roleId) ? null : RoleHelpers.GetIllustration(roleInfo)?.GetSprite();
            if (art != null) return art;

            if (roleInfo.isImpostor) return Helpers.loadSpriteFromResources("TheOtherRoles.Resources.Imposter.png", 100f);
            if (roleInfo.isNeutral) return Helpers.loadSpriteFromResources("TheOtherRoles.Resources.Netural.png", 100f);
            return Helpers.loadSpriteFromResources("TheOtherRoles.Resources.Crewmate.png", 100f);
        }

        private static void BuildViewfinder()
        {
            viewfinder = Helpers.CreateCenteredRect("Viewfinder", container, new Vector2(ViewfinderSize, ViewfinderSize));
            viewfinder.anchoredPosition = Vector2.zero;
            viewfinderAlpha = 0f;

            AddBracket(new Vector2(-1f, 1f));
            AddBracket(new Vector2(1f, 1f));
            AddBracket(new Vector2(-1f, -1f));
            AddBracket(new Vector2(1f, -1f));
        }

        private static void AddBracket(Vector2 corner)
        {
            var color = new Color(1f, 1f, 1f, 0f);

            var horizontal = Helpers.CreateCenteredImage("BracketH", viewfinder, null, new Vector2(BracketLength, BracketThickness));
            horizontal.rectTransform.anchoredPosition = new Vector2(corner.x * (ViewfinderSize - BracketLength) * 0.5f, corner.y * ViewfinderSize * 0.5f);
            horizontal.color = color;

            var vertical = Helpers.CreateCenteredImage("BracketV", viewfinder, null, new Vector2(BracketThickness, BracketLength));
            vertical.rectTransform.anchoredPosition = new Vector2(corner.x * ViewfinderSize * 0.5f, corner.y * (ViewfinderSize - BracketLength) * 0.5f);
            vertical.color = color;
        }

        internal static void Tick()
        {
            if (wheelRoot == null) return;

            float fade = Mathf.Clamp01((Time.unscaledTime - openTime) / OpenFade);
            canvasGroup.alpha = fade;

            Vector2 pointer = ((Vector2)Input.mousePosition - new Vector2(Screen.width * 0.5f, Screen.height * 0.5f)) / uiScale - CenterOffset;
            float distance = pointer.magnitude;

            int previous = hoveredIndex;
            hoveredIndex = -1;
            if (distance >= DeadZoneRadius)
            {
                float angle = Mathf.Atan2(pointer.x, pointer.y) * Mathf.Rad2Deg;
                if (angle < 0f) angle += 360f;
                int index = Mathf.RoundToInt(angle / sliceAngle) % slotCount;
                if (index >= 0 && index < slotCount) hoveredIndex = index;
            }
            if (hoveredIndex != previous && hoveredIndex >= 0) VanillaAsset.PlayHoverSE();

            for (int i = 0; i < slotCount; i++)
            {
                bool hovered = i == hoveredIndex;
                slotScales[i] = Mathf.MoveTowards(slotScales[i], hovered ? HoverScale : 1f, Time.unscaledDeltaTime * 6f);
                slotRects[i].localScale = Vector3.one * slotScales[i];

                RoleInfo role = slotRoles[i];
                Color target = role == null ? RandomColor : hovered ? role.color : FrameIdle;
                slotFrames[i].color = Color.Lerp(slotFrames[i].color, target, Time.unscaledDeltaTime * 8f);
            }

            UpdateViewfinder();
            UpdateHubLabel();

            if (maxTimer > 0f)
            {
                float remain = Mathf.Max(0f, maxTimer - RoleDraft.timer);
                if (countdownRing != null) countdownRing.fillAmount = Mathf.Clamp01(remain / maxTimer);

                int secondsLeft = Mathf.CeilToInt(remain);
                if (secondsLeft != lastSecondsLeft)
                {
                    lastSecondsLeft = secondsLeft;
                    countdownLabel.text = string.Format(countdownFormat, secondsLeft);
                }
            }

            if (accepting && Time.unscaledTime - openTime >= InputGrace && Input.GetMouseButtonDown(0) && hoveredIndex >= 0)
            {
                accepting = false;
                var role = slotRoles[hoveredIndex];
                if (role == null) randomAction?.Invoke();
                else pickAction?.Invoke(role);
            }
        }

        private static void UpdateViewfinder()
        {
            if (viewfinder == null) return;

            Vector2 target = hoveredIndex >= 0 ? slotRects[hoveredIndex].anchoredPosition : Vector2.zero;
            viewfinder.anchoredPosition = Vector2.MoveTowards(viewfinder.anchoredPosition, target, Time.unscaledDeltaTime * 1400f);
            viewfinder.localEulerAngles = new Vector3(0f, 0f, hoveredIndex >= 0 ? -hoveredIndex * sliceAngle : 0f);

            viewfinderAlpha = Mathf.MoveTowards(viewfinderAlpha, hoveredIndex >= 0 ? 0.9f : 0f, Time.unscaledDeltaTime * 4f);
            for (int i = 0; i < viewfinder.childCount; i++)
            {
                var bracket = viewfinder.GetChild(i).GetComponent<Image>();
                var color = bracket.color;
                color.a = viewfinderAlpha;
                bracket.color = color;
            }
        }

        private static void UpdateHubLabel()
        {
            if (hubLabel == null) return;

            if (hoveredIndex < 0)
            {
                hubLabel.text = "";
                return;
            }

            var role = slotRoles[hoveredIndex];
            hubLabel.text = role == null ? randomLabel : role.name;
            hubLabel.color = role == null ? RandomColor : role.color;
        }

        internal static void Close(bool animated)
        {
            accepting = false;

            if (outroRoot != null)
            {
                UnityEngine.Object.Destroy(outroRoot);
                outroRoot = null;
            }

            if (wheelRoot == null) return;

            var root = wheelRoot;
            var group = canvasGroup;
            var flash = flashImage;
            wheelRoot = null;
            canvasGroup = null;
            container = null;
            viewfinder = null;
            flashImage = null;
            hubLabel = null;
            countdownLabel = null;
            countdownRing = null;
            slotRects = null;
            slotFrames = null;
            slotIcons = null;
            slotScales = null;
            slotRoles = null;
            pickAction = null;
            randomAction = null;
            hoveredIndex = -1;

            if (!animated || flash == null || HudManager.Instance == null)
            {
                UnityEngine.Object.Destroy(root);
                return;
            }

            outroRoot = root;
            HudManager.Instance.StartCoroutine(CoOutro(root, group, flash).WrapToIl2Cpp());
        }

        private static IEnumerator CoOutro(GameObject root, CanvasGroup group, Image flash)
        {
            float elapsed = 0f;
            while (elapsed < OutroDuration && root != null)
            {
                elapsed += Time.unscaledDeltaTime;
                float p = Mathf.Clamp01(elapsed / OutroDuration);

                if (flash != null)
                {
                    var color = flash.color;
                    color.a = p < 0.2f ? Mathf.Lerp(0f, 0.85f, p / 0.2f) : Mathf.Lerp(0.85f, 0f, (p - 0.2f) / 0.8f);
                    flash.color = color;
                }
                if (group != null) group.alpha = 1f - p;

                yield return null;
            }

            if (root != null) UnityEngine.Object.Destroy(root);
            if (outroRoot == root) outroRoot = null;
        }
    }
}
