using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Hazel;
using TheOtherRoles.MetaContext;
using TheOtherRoles.Modules;
using TheOtherRoles.Objects;
using TheOtherRoles.Patches;
using TheOtherRoles.Utilities;
using UnityEngine;
using static TheOtherRoles.TheOtherRoles;

namespace TheOtherRoles.Roles
{
    [TORRPCHolder]
    public class Justice : RoleBase<Justice>
    {
        public static Color color = new Color32(255, 128, 0, byte.MaxValue);

        public static bool putOnBalance = false;
        public static float meetingTime = 60f;

        public bool usedBalance;
        public PlayerControl first;
        public byte selectedId = byte.MaxValue;

        public static bool activeJustice;
        public static bool IntroPending;
        public static byte extraExiledId = byte.MaxValue;
        public static byte candidateA = byte.MaxValue;
        public static byte candidateB = byte.MaxValue;
        public static float elapsed;

        private static Sprite buttonSprite;
        private static readonly List<(GameObject button, byte targetId)> meetingButtons = new();

        public Justice()
        {
            RoleId = roleId = RoleId.Justice;
            usedBalance = false;
            first = null;
            selectedId = byte.MaxValue;
        }

        public static Sprite getButtonSprite()
        {
            if (buttonSprite) return buttonSprite;
            buttonSprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.JusticeIcon.png", 115f);
            return buttonSprite;
        }

        public static RemoteProcess<(byte a, byte b)> StartJusticeMeeting = new("JusticeMeeting", (message, _) =>
        {
            activeJustice = true;
            IntroPending = true;
            candidateA = message.a;
            candidateB = message.b;
            elapsed = 0f;

            var meeting = MeetingHud.Instance;
            if (meeting != null && meeting.playerStates != null)
            {
                foreach (var area in meeting.playerStates)
                    if (area != null) area.SetDisabled();
            }

            JusticeMeetingVisual.Play(Helpers.playerById(message.a), Helpers.playerById(message.b), () =>
            {
                IntroPending = false;
                elapsed = 0f;
                ReopenVoting();
                ApplyVoteRestriction();
            });
        });

        public static void ReopenVoting()
        {
            var meeting = MeetingHud.Instance;
            if (meeting == null || meeting.playerStates == null) return;

            if (AmongUsClient.Instance != null && AmongUsClient.Instance.AmHost)
            {
                foreach (var area in meeting.playerStates)
                {
                    if (area == null || !area.DidVote) continue;
                    meeting.RpcClearVote(area.PlayerId);
                }
            }

            foreach (var area in meeting.playerStates)
            {
                if (area == null) continue;

                area.ClearButtons();
                area.UnsetVote();
                area.UnsetHasVoted();
                area.VoteComplete = false;
            }

            if (meeting.SkipVoteButton != null)
            {
                meeting.SkipVoteButton.ClearButtons();
                meeting.SkipVoteButton.VoteComplete = false;
            }

            if (meeting.state == MeetingHud.MeetingStates.Voted)
                meeting.state = MeetingHud.MeetingStates.NotVoted;

            TheOtherRolesPlugin.Logger.LogInfo("[Justice] votes cleared, voting reopened");
        }

        public static bool CanVoteFor(byte playerId)
        {
            if (!activeJustice) return true;
            return playerId == candidateA || playerId == candidateB;
        }

        public static void ApplyVoteRestriction()
        {
            var meeting = MeetingHud.Instance;
            if (meeting == null || meeting.playerStates == null) return;

            foreach (var area in meeting.playerStates)
            {
                if (area == null) continue;

                byte id = (byte)area.PlayerId;
                if (CanVoteFor(id)) area.SetEnabled();
                else area.SetDisabled();
            }

            if (meeting.SkipVoteButton != null) meeting.SkipVoteButton.SetDisabled();
        }

        public static void HandleTie(MeetingHud meeting, ref NetworkedPlayerInfo exiled, ref bool tie, List<NetworkedPlayerInfo> potential, out NetworkedPlayerInfo extra)
        {
            extra = null;
            if (!activeJustice || potential == null || !tie) return;
            if (candidateA == byte.MaxValue || candidateB == byte.MaxValue) return;

            var a = potential.FirstOrDefault(x => x != null && x.PlayerId == candidateA);
            var b = potential.FirstOrDefault(x => x != null && x.PlayerId == candidateB);
            if (a == null || b == null) return;

            exiled = a;
            tie = false;
            extra = b;

            extraExiledId = b.PlayerId;
            if (local != null && local.player == PlayerControl.LocalPlayer)
                _ = new StaticAchievementToken("justice.challenge");

            TheOtherRolesPlugin.Logger.LogInfo($"[Justice] tie between {candidateA} and {candidateB}, both exiled");
        }

        public static void ClearMeeting()
        {
            activeJustice = false;
            IntroPending = false;
            candidateA = byte.MaxValue;
            candidateB = byte.MaxValue;
            elapsed = 0f;
        }

        private static void OnButtonClick(byte targetId)
        {
            var justice = local;
            if (justice == null || PlayerControl.LocalPlayer == null) return;
            if (justice.usedBalance) return;

            var target = Helpers.playerById(targetId);
            if (target == null || target.Data == null || target.Data.IsDead) return;
            if (targetId == PlayerControl.LocalPlayer.PlayerId && !putOnBalance) return;

            if (putOnBalance)
            {
                justice.usedBalance = true;
                _ = new StaticAchievementToken("justice.common1");
                StartJusticeMeeting.Invoke((targetId, PlayerControl.LocalPlayer.PlayerId));
                UpdateButtons(MeetingHud.Instance);
                return;
            }

            if (justice.first == null)
            {
                justice.first = target;
                justice.selectedId = targetId;
            }
            else if (justice.first.PlayerId == targetId)
            {
                justice.first = null;
                justice.selectedId = byte.MaxValue;
            }
            else
            {
                byte a = justice.first.PlayerId;
                justice.usedBalance = true;
                _ = new StaticAchievementToken("justice.common1");
                justice.first = null;
                justice.selectedId = byte.MaxValue;
                StartJusticeMeeting.Invoke((a, targetId));
            }

            UpdateButtons(MeetingHud.Instance);
        }

        public static void CreateMeetingButtons(MeetingHud meeting)
        {
            ClearButtons();

            var justice = local;
            if (justice == null || justice.player != PlayerControl.LocalPlayer) return;
            if (PlayerControl.LocalPlayer.Data == null || PlayerControl.LocalPlayer.Data.IsDead) return;
            if (justice.usedBalance) return;

            JudgementButtons.Build(meeting, meetingButtons, getButtonSprite(), OnButtonClick, putOnBalance);

            UpdateButtons(meeting);
        }

        public static void UpdateButtons(MeetingHud meeting)
        {
            var justice = local;
            bool show = justice != null
                && PlayerControl.LocalPlayer != null
                && PlayerControl.LocalPlayer.Data != null
                && !PlayerControl.LocalPlayer.Data.IsDead
                && meeting != null
                && !justice.usedBalance
                && !activeJustice
                && meeting.state is MeetingHud.MeetingStates.NotVoted or MeetingHud.MeetingStates.Voted;

            foreach (var (button, targetId) in meetingButtons)
            {
                if (button == null) continue;

                bool visible = show;
                if (visible)
                {
                    var target = Helpers.playerById(targetId);
                    if (target == null || target.Data == null || target.Data.IsDead) visible = false;
                }

                if (button.activeSelf != visible) button.SetActive(visible);

                if (visible && justice != null && justice.selectedId == targetId)
                {
                    var renderer = button.GetComponent<SpriteRenderer>();
                    if (renderer != null) renderer.color = Color.green;
                }
                else
                {
                    var renderer = button.GetComponent<SpriteRenderer>();
                    if (renderer != null) renderer.color = Color.white;
                }
            }
        }

        public static void ClearButtons()
        {
            JudgementButtons.Destroy(meetingButtons);
        }

        public static void UpdateTimer(MeetingHud meeting)
        {
            if (!activeJustice || meeting == null) return;

            HoldVanillaTimer(meeting);

            if (meeting.SkipVoteButton != null) meeting.SkipVoteButton.SetDisabled();

            if (IntroPending) return;

            elapsed += Time.deltaTime;

            if (meeting.TimerText != null)
                meeting.TimerText.text = Mathf.Max(0, Mathf.CeilToInt(meetingTime - elapsed)).ToString();

            if (elapsed < meetingTime) return;

            if (AmongUsClient.Instance != null && AmongUsClient.Instance.AmHost)
            {
                MeetingHudPatch.completeVoting(meeting);
            }
        }

        private static void HoldVanillaTimer(MeetingHud meeting)
        {
            var options = GameOptionsManager.Instance != null ? GameOptionsManager.Instance.currentNormalGameOptions : null;
            if (options == null) return;

            float limit = options.DiscussionTime + options.VotingTime - 0.05f;
            if (limit < 0f) return;

            if (meeting.discussionTimer > limit) meeting.discussionTimer = limit;
        }

        public override void OnMeetingStart()
        {
            ClearMeeting();
        }

        public override void OnMeetingEnd(PlayerControl exiled = null)
        {
            if (player == PlayerControl.LocalPlayer && exiled != null && activeJustice)
            {
                if (exiled.PlayerId == extraExiledId) { }
                else if (exiled.PlayerId == candidateA || exiled.PlayerId == candidateB)
                    _ = new StaticAchievementToken("justice.another1");
            }

            extraExiledId = byte.MaxValue;

            ClearButtons();
            ClearMeeting();
            JusticeMeetingVisual.Hide();
            selectedId = byte.MaxValue;
        }

        public override void ResetRole(bool isShifted)
        {
            ClearButtons();
            ClearMeeting();
            JusticeMeetingVisual.Hide();
        }

        public static void clearAndReload()
        {
            putOnBalance = CustomOptionHolder.justicePutOnBalance.getBool();
            meetingTime = CustomOptionHolder.justiceMeetingTime.getFloat();

            ClearButtons();
            ClearMeeting();
            JusticeMeetingVisual.Hide();
            players = [];
        }

        static public IEnumerable<DocumentReplacement> GetReplacementPart()
        {
            yield return new("%NUM%", putOnBalance ? "1" : "2");
            yield return new("%TIME%", meetingTime.ToString());
        }
    }

    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Select))]
    public static class JusticeSelectPatch
    {
        public static bool Prefix(ref bool __result, MeetingHud __instance, [HarmonyArgument(0)] int suspectStateIdx)
        {
            if (!Justice.activeJustice) return true;
            if (__instance.playerStates == null) return true;
            if (suspectStateIdx < 0 || suspectStateIdx >= __instance.playerStates.Length) return true;

            var area = __instance.playerStates[suspectStateIdx];
            if (area == null) return true;
            if (Justice.CanVoteFor((byte)area.PlayerId)) return true;

            __result = false;
            return false;
        }
    }
}
