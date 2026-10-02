using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace TheOtherRoles.Modules
{
    internal static class SystemChat
    {
        private static object Get(object target, string name)
        {
            var type = target.GetType();
            var property = AccessTools.Property(type, name);
            if (property != null) return property.GetValue(target);
            return AccessTools.Field(type, name)?.GetValue(target);
        }

        private static object Invoke(object target, string name, params object[] args)
        {
            return AccessTools.Method(target.GetType(), name)?.Invoke(target, args);
        }

        internal static void Post(string message)
        {
            var chat = HudManager.Instance?.Chat;
            if (chat == null || message == null) return;

            var scroller = Get(chat, "scroller") as Scroller;
            var bubble = Invoke(chat, "GetPooledBubble") as ChatBubble;
            if (scroller == null || bubble == null)
            {
                TheOtherRolesPlugin.Logger.LogWarning("[SystemChat] no scroller or bubble");
                return;
            }

            var player = Get(bubble, "Player") as PoolablePlayer;
            var nameText = Get(bubble, "NameText") as TextMeshPro;
            var colorBlindName = Get(bubble, "ColorBlindName") as TextMeshPro;
            var textArea = Get(bubble, "TextArea") as TextMeshPro;
            var background = Get(bubble, "Background") as SpriteRenderer;
            if (textArea == null || background == null)
            {
                TheOtherRolesPlugin.Logger.LogWarning("[SystemChat] bubble parts missing");
                return;
            }

            bubble.transform.SetParent(scroller.Inner);
            bubble.transform.localScale = Vector3.one;
            Invoke(bubble, "SetLeft");
            if (player != null) player.gameObject.SetActive(false);
            if (nameText != null) nameText.gameObject.SetActive(false);
            if (colorBlindName != null) colorBlindName.gameObject.SetActive(false);

            textArea.color = Color.black;
            Invoke(bubble, "SetText", message);
            textArea.rectTransform.pivot = new Vector2(0f, 0f);
            textArea.transform.localPosition = new Vector3(-0.25f, -0.15f, 0f);
            textArea.horizontalAlignment = HorizontalAlignmentOptions.Left;
            background.color = new Color(0.6f, 0.8f, 1f, 1f);

            Invoke(bubble, "AlignChildren");
            Invoke(chat, "AlignAllBubbles");
        }
    }
}
