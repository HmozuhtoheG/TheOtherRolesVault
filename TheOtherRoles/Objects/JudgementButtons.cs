using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheOtherRoles.Objects
{
    public static class JudgementButtons
    {
        public const float ButtonX = -0.95f;
        public const float ButtonY = 0.03f;
        public const float ButtonZ = -2.2f;

        public static void Build(MeetingHud meeting, List<(GameObject button, byte targetId)> target, Sprite sprite, Action<byte> onClick, bool skipSelf)
        {
            Destroy(target);

            if (meeting == null || meeting.playerStates == null) return;

            foreach (var area in meeting.playerStates)
            {
                if (area == null || area.AmDead) continue;
                if (skipSelf && (byte)area.PlayerId == PlayerControl.LocalPlayer.PlayerId) continue;
                if (area.Buttons == null) continue;

                var template = area.Buttons.transform.Find("CancelButton");
                if (template == null) continue;

                var box = UnityEngine.Object.Instantiate(template.gameObject, area.transform);
                box.name = "JudgementButton";
                box.transform.localPosition = new Vector3(ButtonX, ButtonY, ButtonZ);

                var renderer = box.GetComponent<SpriteRenderer>();
                if (renderer != null) renderer.sprite = sprite;

                var button = box.GetComponent<PassiveButton>();
                if (button == null) continue;

                button.OnClick.RemoveAllListeners();
                byte copiedId = (byte)area.PlayerId;
                button.OnClick.AddListener((Action)(() => onClick(copiedId)));

                box.SetActive(false);
                target.Add((box, copiedId));
            }
        }

        public static void Refresh(MeetingHud meeting, List<(GameObject button, byte targetId)> target, bool show)
        {
            foreach (var (button, targetId) in target)
            {
                if (button == null) continue;

                bool visible = show;
                if (visible)
                {
                    var player = Helpers.playerById(targetId);
                    if (player == null || player.Data == null || player.Data.IsDead) visible = false;
                }

                if (button.activeSelf != visible) button.SetActive(visible);
            }
        }

        public static void Destroy(List<(GameObject button, byte targetId)> target)
        {
            foreach (var (button, _) in target)
                if (button != null) UnityEngine.Object.Destroy(button);

            target.Clear();
        }
    }
}
