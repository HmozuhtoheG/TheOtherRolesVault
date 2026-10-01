using System;
using System.Collections.Generic;
using System.Linq;
using TheOtherRoles.MetaContext;
using TheOtherRoles.Modules;
using TheOtherRoles.Objects;
using TheOtherRoles.Patches;
using TheOtherRoles.Utilities;
using UnityEngine;
using static TheOtherRoles.TheOtherRoles;

namespace TheOtherRoles.Roles
{
    public enum HigurumaCrime
    {
        Kill,
        Sabotage,
        Vent,
        Ability,
        Task
    }

    public enum HigurumaChoice
    {
        None,
        Confess,
        Deny,
        Silence
    }

    public enum HigurumaVerdict
    {
        None,
        Death,
        Forfeit,
        Innocent
    }

    [TORRPCHolder]
    public class HiromiHiguruma : RoleBase<HiromiHiguruma>
    {
        public static Color color = new Color32(176, 141, 87, byte.MaxValue);

        public static int maxUses => Mathf.RoundToInt(CustomOptionHolder.hiromiUses.getFloat());
        public static float trialTime => CustomOptionHolder.hiromiTrialTime.getFloat();
        public static int deathThreshold => Mathf.RoundToInt(CustomOptionHolder.hiromiDeathThreshold.getFloat());
        public static int forfeitThreshold => Mathf.RoundToInt(CustomOptionHolder.hiromiForfeitThreshold.getFloat());
        public static int forfeitRounds => Mathf.RoundToInt(CustomOptionHolder.hiromiForfeitRounds.getFloat());

        private const int WeightKill = 3;
        private const int WeightSabotage = 2;
        private const int WeightVent = 1;
        private const int WeightAbility = 1;
        private const int WeightTask = 1;

        public int usesLeft;

        public static bool trialActive;
        public static bool awaitingChoice;
        public static bool showingVerdict;
        public static byte defendantId = byte.MaxValue;
        public static float trialTimer;
        public static HigurumaChoice choice = HigurumaChoice.None;
        public static HigurumaVerdict verdict = HigurumaVerdict.None;
        public static float verdictTimer;

        public static byte executionTarget = byte.MaxValue;
        public static bool hasExecutionerSword;

        private static readonly Dictionary<byte, float> crimeScore = new();
        private static readonly Dictionary<byte, int> crimeKill = new();
        private static readonly Dictionary<byte, int> crimeSabotage = new();
        private static readonly Dictionary<byte, int> crimeVent = new();
        private static readonly Dictionary<byte, int> crimeAbility = new();
        private static readonly Dictionary<byte, int> crimeTask = new();
        private static readonly Dictionary<byte, int> forfeitMeetings = new();

        private static Sprite buttonSprite;
        private static Sprite swordSprite;
        private static readonly List<(GameObject button, byte targetId)> meetingButtons = new();
        private static MetaScreen confirmScreen;
        private static int lastLoggedShow = -1;
        private static int lastLoggedActive = -1;

        public HiromiHiguruma()
        {
            RoleId = roleId = RoleId.HiromiHiguruma;
            usesLeft = maxUses;
        }

        public static Sprite getButtonSprite()
        {
            if (buttonSprite) return buttonSprite;
            buttonSprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.JusticeIcon.png", 115f);
            return buttonSprite;
        }

        public static Sprite getSwordSprite()
        {
            if (swordSprite) return swordSprite;
            swordSprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.AssassinAssassinateButton.png", 115f);
            return swordSprite;
        }

        public static bool canUse => local != null
            && local.player == PlayerControl.LocalPlayer
            && local.usesLeft > 0
            && !trialActive
            && PlayerControl.LocalPlayer.Data != null
            && !PlayerControl.LocalPlayer.Data.IsDead;

        public static bool isDefendant(byte playerId) => trialActive && defendantId == playerId;

        public static void Record(byte playerId, HigurumaCrime crime, int amount = 1)
        {
            if (playerId == byte.MaxValue || amount <= 0) return;

            switch (crime)
            {
                case HigurumaCrime.Kill: Add(crimeKill, playerId, amount); break;
                case HigurumaCrime.Sabotage: Add(crimeSabotage, playerId, amount); break;
                case HigurumaCrime.Vent: Add(crimeVent, playerId, amount); break;
                case HigurumaCrime.Ability: Add(crimeAbility, playerId, amount); break;
                case HigurumaCrime.Task: Add(crimeTask, playerId, amount); break;
            }

            crimeScore[playerId] = GetScore(playerId);
        }

        private static void Add(Dictionary<byte, int> table, byte playerId, int amount)
        {
            table.TryGetValue(playerId, out int current);
            table[playerId] = current + amount;
        }

        private static int Get(Dictionary<byte, int> table, byte playerId)
        {
            return table.TryGetValue(playerId, out int value) ? value : 0;
        }

        public static int GetScore(byte playerId)
        {
            return Get(crimeKill, playerId) * WeightKill
                + Get(crimeSabotage, playerId) * WeightSabotage
                + Get(crimeVent, playerId) * WeightVent
                + Get(crimeAbility, playerId) * WeightAbility
                + Get(crimeTask, playerId) * WeightTask;
        }

        private static readonly string[] ChargeKeys =
        [
            "hiromiCharge1", "hiromiCharge2", "hiromiCharge3", "hiromiCharge4",
            "hiromiCharge5", "hiromiCharge6", "hiromiCharge7", "hiromiCharge8"
        ];

        public static List<string> DescribeCrimes(byte playerId)
        {
            List<string> lines = new();

            void Line(int count, string key)
            {
                if (count <= 0) return;
                lines.Add(string.Format(ModTranslation.getString(key), count));
            }

            Line(Get(crimeKill, playerId), "hiromiCrimeKill");
            Line(Get(crimeSabotage, playerId), "hiromiCrimeSabotage");
            Line(Get(crimeVent, playerId), "hiromiCrimeVent");
            Line(Get(crimeAbility, playerId), "hiromiCrimeAbility");
            Line(Get(crimeTask, playerId), "hiromiCrimeTask");

            if (lines.Count == 0)
                lines = ChargeKeys.OrderBy(_ => rnd.Next()).Take(3)
                    .Select(key => ModTranslation.getString(key)).ToList();

            return lines;
        }

        public static HigurumaVerdict ComputeVerdict(byte playerId, HigurumaChoice defendantChoice)
        {
            int score = GetScore(playerId);

            HigurumaVerdict result = score >= deathThreshold ? HigurumaVerdict.Death
                : score >= forfeitThreshold ? HigurumaVerdict.Forfeit
                : HigurumaVerdict.Innocent;

            if (defendantChoice == HigurumaChoice.Confess)
                result = result switch
                {
                    HigurumaVerdict.Innocent => HigurumaVerdict.Forfeit,
                    HigurumaVerdict.Forfeit => HigurumaVerdict.Death,
                    _ => HigurumaVerdict.Death
                };

            return result;
        }

        public static bool canExecute(PlayerControl target)
        {
            return hasExecutionTarget() && target != null && target.PlayerId == executionTarget;
        }

        private static bool hasExecutionTarget()
        {
            return hasExecutionerSword && executionTarget != byte.MaxValue;
        }

        public static bool isForfeited(PlayerControl player)
        {
            if (player == null) return false;
            return forfeitMeetings.TryGetValue(player.PlayerId, out int left) && left > 0;
        }

        public static RemoteProcess<(byte justiceId, byte defendantId)> StartTrial = new("HiromiStartTrial", (message, __) =>
        {
            var justice = getRole(Helpers.playerById(message.justiceId));
            if (justice == null) return;

            justice.usesLeft = Mathf.Max(0, justice.usesLeft - 1);

            if (justice.player == PlayerControl.LocalPlayer) _ = new StaticAchievementToken("hiromiHiguruma.common1");

            trialActive = true;
            awaitingChoice = false;
            showingVerdict = false;
            defendantId = message.defendantId;
            choice = HigurumaChoice.None;
            verdict = HigurumaVerdict.None;
            trialTimer = trialTime;

            CloseConfirmScreen();
            ClearButtons();

            HigurumaDomain.Play(Helpers.playerById(message.defendantId));
        });

        public static RemoteProcess<(byte defendantId, byte choiceCode)> SubmitChoice = new("HiromiSubmitChoice", (message, _) =>
        {
            if (!trialActive || message.defendantId != defendantId) return;
            if (!awaitingChoice) return;

            ApplyChoice((HigurumaChoice)message.choiceCode);
        });

        public static void ApplyChoice(HigurumaChoice newChoice)
        {
            if (!trialActive || !awaitingChoice) return;

            awaitingChoice = false;
            choice = newChoice;
            HigurumaTrial.CloseChoiceUI();

            var defendant = Helpers.playerById(defendantId);
            verdict = ComputeVerdict(defendantId, choice);

            AnnounceChoice(defendant, choice);
            HigurumaTrial.ShowVerdict(defendant, choice, verdict, DescribeCrimes(defendantId), 0);

            showingVerdict = true;
            verdictTimer = 0f;

            TheOtherRolesPlugin.Logger.LogInfo($"[Higuruma] defendant={defendantId} score={GetScore(defendantId)} choice={choice} verdict={verdict}");
        }

        private static void AnnounceChoice(PlayerControl defendant, HigurumaChoice defendantChoice)
        {
            string key = defendantChoice switch
            {
                HigurumaChoice.Confess => "hiromiAnnounceConfess",
                HigurumaChoice.Deny => "hiromiAnnounceDeny",
                _ => "hiromiAnnounceSilence"
            };

            string name = defendant?.Data?.PlayerName ?? "";
            HigurumaTrial.Announce(string.Format(ModTranslation.getString(key), name));
        }

        public static void FinishTrial()
        {
            var defendant = Helpers.playerById(defendantId);

            if (verdict == HigurumaVerdict.Death && defendant != null)
            {
                executionTarget = defendant.PlayerId;
                hasExecutionerSword = true;

                if (local != null && local.player == PlayerControl.LocalPlayer)
                {
                    _ = new StaticAchievementToken("hiromiHiguruma.another1");
                    if (choice == HigurumaChoice.Confess && GetScore(defendantId) == 0)
                        _ = new StaticAchievementToken("hiromiHiguruma.challenge");
                }
            }
            else if (verdict == HigurumaVerdict.Forfeit && defendant != null)
            {
                forfeitMeetings[defendant.PlayerId] = forfeitRounds;
                HiromiHigurumaPatches.ApplyForfeit(defendant);

                if (local != null && local.player == PlayerControl.LocalPlayer)
                    _ = new StaticAchievementToken("hiromiHiguruma.another2");
            }

            HigurumaDomain.Hide();
            HigurumaTrial.Close();

            trialActive = false;
            awaitingChoice = false;
            showingVerdict = false;
            defendantId = byte.MaxValue;
            trialTimer = 0f;

            var meeting = MeetingHud.Instance;
            if (meeting != null && AmongUsClient.Instance != null && AmongUsClient.Instance.AmHost)
                meeting.RpcVotingComplete(System.Array.Empty<MeetingHud.VoterState>(), null, false, false, 0);
        }

        public static void ClearTrial()
        {
            HigurumaDomain.Hide();
            HigurumaTrial.Close();
            CloseConfirmScreen();

            trialActive = false;
            awaitingChoice = false;
            showingVerdict = false;
            defendantId = byte.MaxValue;
            trialTimer = 0f;
            verdictTimer = 0f;
            choice = HigurumaChoice.None;
            verdict = HigurumaVerdict.None;
        }

        public static void HoldVanillaTimer(MeetingHud meeting)
        {
            if (!trialActive || meeting == null) return;

            var options = GameOptionsManager.Instance != null ? GameOptionsManager.Instance.currentNormalGameOptions : null;
            if (options == null) return;

            float limit = options.DiscussionTime + options.VotingTime - 0.05f;
            if (limit < 0f) return;

            if (meeting.discussionTimer > limit) meeting.discussionTimer = limit;
        }

        public static void Update()
        {
            var meeting = MeetingHud.Instance;
            if (meeting == null) return;

            if (!trialActive)
            {
                if (PlayerControl.LocalPlayer != null && PlayerControl.LocalPlayer.isRole(RoleId.HiromiHiguruma))
                    UpdateButtons(meeting);

                return;
            }

            HoldVanillaTimer(meeting);

            if (meeting.SkipVoteButton != null) meeting.SkipVoteButton.SetDisabled();

            if (awaitingChoice)
            {
                trialTimer -= Time.deltaTime;
                HigurumaTrial.UpdateTimer(trialTimer);

                if (trialTimer <= 0f)
                {
                    if (AmongUsClient.Instance != null && AmongUsClient.Instance.AmHost)
                        SubmitChoice.Invoke((defendantId, (byte)HigurumaChoice.Silence));
                    else if (defendantId == PlayerControl.LocalPlayer.PlayerId)
                        SubmitChoice.Invoke((defendantId, (byte)HigurumaChoice.Silence));

                    ApplyChoice(HigurumaChoice.Silence);
                }
                return;
            }

            if (showingVerdict)
            {
                verdictTimer += Time.deltaTime;
                HigurumaTrial.UpdateVerdict(verdictTimer);
            }
        }

        public static void BeginChoice()
        {
            awaitingChoice = true;
            trialTimer = trialTime;
            HigurumaTrial.OpenChoice(defendantId);
        }

        public static void CreateMeetingButtons(MeetingHud meeting)
        {
            ClearButtons();

            if (trialActive) return;

            var role = local;
            bool isSelf = role != null && role.player == PlayerControl.LocalPlayer;
            bool hasRoleId = PlayerControl.LocalPlayer != null && PlayerControl.LocalPlayer.isRole(RoleId.HiromiHiguruma);
            int stateCount = meeting.playerStates == null ? -1 : meeting.playerStates.Length;

            TheOtherRolesPlugin.Logger.LogInfo($"[Higuruma] createButtons: roleId={hasRoleId} instance={role != null} self={isSelf} uses={(role != null ? role.usesLeft : -1)} states={stateCount}");

            if (role == null || role.player != PlayerControl.LocalPlayer) return;
            if (PlayerControl.LocalPlayer.Data == null || PlayerControl.LocalPlayer.Data.IsDead) return;
            if (role.usesLeft <= 0) return;
            if (meeting.playerStates == null) return;

            JudgementButtons.Build(meeting, meetingButtons, getButtonSprite(), OpenConfirm, true);

            TheOtherRolesPlugin.Logger.LogInfo($"[Higuruma] created {meetingButtons.Count} trial buttons");

            UpdateButtons(meeting);
        }

        public static void UpdateButtons(MeetingHud meeting)
        {
            bool show = canUse
                && meeting != null
                && !trialActive
                && meeting.state is MeetingHud.MeetingStates.NotVoted or MeetingHud.MeetingStates.Voted;

            JudgementButtons.Refresh(meeting, meetingButtons, show);

            int activeCount = meetingButtons.Count(x => x.button != null && x.button.activeSelf);
            int flag = show ? 1 : 0;
            if (flag != lastLoggedShow || activeCount != lastLoggedActive)
            {
                lastLoggedShow = flag;
                lastLoggedActive = activeCount;
                string detail = "none";
                if (meetingButtons.Count > 0 && meetingButtons[0].button != null)
                {
                    var probe = meetingButtons[0].button;
                    var probeRenderer = probe.GetComponent<SpriteRenderer>();
                    detail = $"layer={probe.layer} activeSelf={probe.activeSelf} inHierarchy={probe.activeInHierarchy} world={probe.transform.position.ToString()} "
                        + $"sr={(probeRenderer == null ? "null" : $"enabled={probeRenderer.enabled} sprite={(probeRenderer.sprite == null ? "null" : probeRenderer.sprite.name)} order={probeRenderer.sortingOrder} color={probeRenderer.color.ToString()}")}";
                }
                TheOtherRolesPlugin.Logger.LogInfo($"[Higuruma] updateButtons show={show} state={meeting.state} canUse={canUse} active={activeCount}/{meetingButtons.Count} {detail}");
            }
        }

        public static void ClearButtons()
        {
            JudgementButtons.Destroy(meetingButtons);
        }

        public static void CloseConfirmScreen()
        {
            if (confirmScreen != null) confirmScreen.CloseScreen();
            confirmScreen = null;
        }

        public static void OpenConfirm(byte targetId)
        {
            CloseConfirmScreen();

            var target = Helpers.playerById(targetId);
            if (target == null || target.Data == null || !canUse) return;

            var gui = TORGUIContextEngine.API;
            var attr = gui.GetAttribute(AttributeAsset.CenteredBoldFixed);

            confirmScreen = MetaScreen.GenerateWindow(new Vector2(4.6f, 1.7f), HudManager.Instance.transform, Vector3.zero, true, false);

            confirmScreen.SetContext(gui.VerticalHolder(GUIAlignment.Center,
                gui.RawText(GUIAlignment.Center, attr, string.Format(ModTranslation.getString("hiromiTrialConfirm"), Helpers.cs(target.Data.Color, target.Data.PlayerName))),
                gui.VerticalMargin(0.25f),
                gui.HorizontalHolder(GUIAlignment.Center,
                    gui.LocalizedButton(GUIAlignment.Center, attr, "hiromiTrialYes", () => StartTrial.Invoke((PlayerControl.LocalPlayer.PlayerId, targetId))),
                    gui.HorizontalMargin(0.25f),
                    gui.LocalizedButton(GUIAlignment.Center, attr, "hiromiTrialNo", CloseConfirmScreen))), out _);
        }

        public override void OnMeetingStart()
        {
            ClearTrial();
        }

        public override void OnMeetingEnd(PlayerControl exiled = null)
        {
            ClearButtons();
            ClearTrial();

            foreach (var key in forfeitMeetings.Keys.ToList())
                forfeitMeetings[key] = Mathf.Max(0, forfeitMeetings[key] - 1);

            if (executionTarget != byte.MaxValue)
            {
                var target = Helpers.playerById(executionTarget);
                if (target == null || target.Data == null || target.Data.IsDead)
                {
                    executionTarget = byte.MaxValue;
                    hasExecutionerSword = false;
                }
            }
        }

        public override void ResetRole(bool isShifted)
        {
            ClearButtons();
            ClearTrial();
        }

        public override void OnDeath(PlayerControl killer = null)
        {
            if (player == null || player.PlayerId != executionTarget) return;

            executionTarget = byte.MaxValue;
            hasExecutionerSword = false;
        }

        public static void clearAndReload()
        {
            ClearButtons();
            ClearTrial();

            crimeScore.Clear();
            crimeKill.Clear();
            crimeSabotage.Clear();
            crimeVent.Clear();
            crimeAbility.Clear();
            crimeTask.Clear();
            forfeitMeetings.Clear();

            executionTarget = byte.MaxValue;
            hasExecutionerSword = false;

            players = [];
        }

        static public IEnumerable<DocumentReplacement> GetReplacementPart()
        {
            yield return new("%USES%", maxUses.ToString());
            yield return new("%TIME%", trialTime.ToString());
        }
    }
}
