using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace TheOtherRoles.Modules
{
    internal static class SystemChat
    {
        private static readonly MethodInfo GetPooledBubble = AccessTools.Method(typeof(ChatController), "GetPooledBubble");
        private static readonly MethodInfo AlignAllBubbles = AccessTools.Method(typeof(ChatController), "AlignAllBubbles");
        private static readonly MethodInfo SetBubbleText = AccessTools.Method(typeof(ChatBubble), "SetText");
        private static readonly FieldInfo ScrollerField = AccessTools.Field(typeof(ChatController), "scroller");

        internal static void Post(string message)
        {
            var chat = HudManager.Instance?.Chat;
            if (chat == null || message == null) return;

            var scroller = (Scroller)ScrollerField.GetValue(chat);
            var bubble = (ChatBubble)GetPooledBubble.Invoke(chat, null);
            if (scroller == null || bubble == null) return;

            bubble.transform.SetParent(scroller.Inner);
            bubble.transform.localScale = Vector3.one;
            bubble.SetLeft();
            bubble.Player.gameObject.SetActive(false);
            bubble.NameText.gameObject.SetActive(false);
            bubble.ColorBlindName.gameObject.SetActive(false);
            bubble.TextArea.color = Color.black;

            SetBubbleText.Invoke(bubble, new object[] { message });

            bubble.TextArea.rectTransform.pivot = new Vector2(0f, 0f);
            bubble.TextArea.transform.localPosition = new Vector3(-0.25f, -0.15f, 0f);
            bubble.TextArea.horizontalAlignment = HorizontalAlignmentOptions.Left;
            bubble.Background.color = new Color(0.6f, 0.8f, 1f, 1f);

            bubble.AlignChildren();
            AlignAllBubbles.Invoke(chat, null);
        }
    }
}
