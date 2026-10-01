using TMPro;
using UnityEngine;

namespace TheOtherRoles.Modules
{
    internal static class RoleDraftStatus
    {
        private const int SortingOrder = 10390;

        private static GameObject root;
        private static TextMeshProUGUI label;
        private static string format;
        private static int lastAhead = -1;

        internal static void Show()
        {
            Hide();

            format = ModTranslation.getString("roleDraftTurns");
            root = Helpers.CreateOverlayCanvas("RoleDraftStatus", SortingOrder);

            var obj = new GameObject("TurnLabel");
            obj.transform.SetParent(root.transform, false);
            var rect = obj.AddComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-40f, 40f);
            rect.sizeDelta = new Vector2(560f, 60f);

            label = obj.AddComponent<TextMeshProUGUI>();
            label.alignment = TextAlignmentOptions.BottomRight;
            label.fontSize = 30f;
            label.color = new Color(1f, 1f, 1f, 0.85f);
            label.raycastTarget = false;
            label.text = "";
        }

        internal static void SetAhead(int ahead)
        {
            if (label == null || ahead == lastAhead) return;

            lastAhead = ahead;
            label.gameObject.SetActive(ahead > 0);
            if (ahead > 0) label.text = string.Format(format, ahead);
        }

        internal static void Hide()
        {
            if (root != null) Object.Destroy(root);
            root = null;
            label = null;
            lastAhead = -1;
        }
    }
}
