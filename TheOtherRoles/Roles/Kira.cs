using System;
using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using HarmonyLib;
using InnerNet;
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
    public class Kira : RoleBase<Kira>
    {
        public static Color color = new Color32(70, 70, 85, byte.MaxValue);
        public static Color senderColor = new Color32(170, 170, 170, byte.MaxValue);

        public static bool deathNoteAnnouncing;

        public static int killsToWin = 3;
        public static int writesPerMeeting = 1;
        public static bool canContinueAfterFail = false;

        public int kills;
        public int writesLeft;
        public bool lockedThisMeeting;
        public PlayerControl selectedTarget;

        public AchievementToken<int> acTokenObsession;

        private static readonly Dictionary<string, IntegerDataEntry> obsessionEntries = [];
        private static IntegerDataEntry obsessionBest;

        private static Sprite buttonSprite;
        private static Sprite writeSprite;

        private static MetaScreen writeScreen;
        private static GUITextField writeField;
        public static List<(GameObject button, byte targetId)> writeButtons = [];
        private static float nextLookRefresh;

        public Kira()
        {
            RoleId = roleId = RoleId.Kira;
            kills = 0;
            writesLeft = 0;
            lockedThisMeeting = false;
            selectedTarget = null;
            acTokenObsession = null;
        }

        public override void PostInit()
        {
            if (PlayerControl.LocalPlayer != player) return;
            acTokenObsession ??= new("kira.obsession", 0, (val, _) => val);
        }

        public static void recordObsessionKill(PlayerControl victim)
        {
            if (victim == null || victim.Data == null) return;

            string identity = !string.IsNullOrEmpty(victim.FriendCode) ? victim.FriendCode : victim.Data.PlayerName;
            if (string.IsNullOrEmpty(identity)) return;

            string key = identity.ComputeConstantHashAsString();
            if (!obsessionEntries.TryGetValue(key, out var entry))
            {
                entry = new IntegerDataEntry("k." + key, TORAchievementManager.AchievementDataSaver, 0);
                obsessionEntries[key] = entry;
            }

            entry.Value += 1;

            obsessionBest ??= new IntegerDataEntry("k.best", TORAchievementManager.AchievementDataSaver, 0);
            if (entry.Value <= obsessionBest.Value) return;

            var kira = local;
            if (kira != null && kira.acTokenObsession != null) kira.acTokenObsession.Value += entry.Value - obsessionBest.Value;
            obsessionBest.Value = entry.Value;
        }

        public static Sprite getButtonSprite()
        {
            if (buttonSprite) return buttonSprite;
            buttonSprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.SeerButton.png", 115f);
            return buttonSprite;
        }

        public static Sprite getWriteSprite()
        {
            if (writeSprite) return writeSprite;
            writeSprite = HandleGuesser.getTargetSprite();
            return writeSprite;
        }

        public static bool hasWon => players.Any(x => x.kills >= killsToWin);

        public static bool IsLocalKira =>
            PlayerControl.LocalPlayer != null
            && PlayerControl.LocalPlayer.Data != null
            && !PlayerControl.LocalPlayer.Data.IsDead
            && local != null
            && local.player == PlayerControl.LocalPlayer;

        public static RemoteProcess<(byte kiraId, byte targetId, byte announcerId)> KillByNote = new("KiraKillByNote", (message, __) =>
        {
            var kira = getRole(Helpers.playerById(message.kiraId));
            var target = Helpers.playerById(message.targetId);
            var announcer = Helpers.playerById(message.announcerId);
            if (kira == null || kira.player == null || target == null || target.Data == null) return;

            kira.kills++;
            Helpers.forceMurderPlayer(kira.player, target, false);

            if (Constants.ShouldPlaySfx()) SoundManager.Instance.PlaySound(target.KillSfx, false, 0.8f);

            if (announcer == null || announcer == kira.player || announcer == target) announcer = randomAnnouncer(kira.player, target);
            if (announcer != null && HudManager.Instance != null && HudManager.Instance.Chat != null)
            {
                ChatCommands.CurrentChatType = ChatCommands.ChatTypes.Default;
                deathNoteAnnouncing = true;
                HudManager.Instance.Chat.AddChat(announcer, ModTranslation.getString("kiraDeathNote").Replace("{0}", target.Data.PlayerName), false);
                deathNoteAnnouncing = false;
            }

            if (PlayerControl.LocalPlayer == kira.player)
            {
                recordObsessionKill(target);
                new CustomMessage(string.Format(ModTranslation.getString("kiraKillCount"), kira.kills, killsToWin), 3f);
                if (hasWon) _ = new StaticAchievementToken("kira.challenge");
            }

            if (AmongUsClient.Instance != null && AmongUsClient.Instance.AmHost && hasWon)
                GameManager.Instance.RpcEndGame((GameOverReason)CustomGameOverReason.KiraWin, false);
        });

        private static PlayerControl randomAnnouncer(PlayerControl kira, PlayerControl target)
        {
            var candidates = PlayerControl.AllPlayerControls.ToArray()
                .Where(x => x != null && x.Data != null && !x.Data.IsDead && !x.Data.Disconnected && x != kira && x != target)
                .ToList();
            if (candidates.Count == 0) return null;
            return candidates[rnd.Next(candidates.Count)];
        }

        public static void CreateMeetingButtons(MeetingHud meeting, Action<PassiveButton, string> guide)
        {
            ClearButtons();

            var kira = local;
            if (kira == null || kira.player != PlayerControl.LocalPlayer) return;
            if (PlayerControl.LocalPlayer.Data.IsDead) return;
            if (kira.writesLeft <= 0 || kira.lockedThisMeeting) return;

            for (int i = 0; i < meeting.playerStates.Length; i++)
            {
                PlayerVoteArea area = meeting.playerStates[i];
                if (area.AmDead || (byte)area.PlayerId == PlayerControl.LocalPlayer.PlayerId) continue;
                if (Jailor.isJailed(area.PlayerId)) continue;

                GameObject template = area.Buttons.transform.Find("CancelButton").gameObject;
                GameObject targetBox = UnityEngine.Object.Instantiate(template, area.transform);
                targetBox.name = "KiraWriteButton";
                targetBox.transform.localPosition = new Vector3(-0.95f, 0.03f, -1.6f);
                SpriteRenderer renderer = targetBox.GetComponent<SpriteRenderer>();
                renderer.sprite = getWriteSprite();
                PassiveButton button = targetBox.GetComponent<PassiveButton>();
                button.OnClick.RemoveAllListeners();
                byte copiedId = (byte)area.PlayerId;
                button.OnClick.AddListener((Action)(() => OpenWriteDialog(copiedId)));
                guide?.Invoke(button, string.Format(ModTranslation.getString("buttonLeftClick"), ModTranslation.getString("kiraWrite")));

                targetBox.SetActive(false);
                writeButtons.Add((targetBox, copiedId));
            }

            UpdateButtons(meeting);
        }

        public static void UpdateButtons(MeetingHud meeting)
        {
            var kira = local;
            bool show = kira != null && PlayerControl.LocalPlayer != null && PlayerControl.LocalPlayer.Data != null
                && !PlayerControl.LocalPlayer.Data.IsDead
                && meeting != null
                && meeting.state is MeetingHud.MeetingStates.NotVoted or MeetingHud.MeetingStates.Voted
                && kira.writesLeft > 0 && !kira.lockedThisMeeting;

            foreach (var (button, targetId) in writeButtons)
            {
                if (button == null) continue;

                bool visible = show;
                if (visible)
                {
                    var target = Helpers.playerById(targetId);
                    if (target == null || target.Data == null || target.Data.IsDead) visible = false;
                }

                if (button.activeSelf != visible) button.SetActive(visible);
            }
        }

        public static void ClearButtons()
        {
            foreach (var (button, _) in writeButtons)
                if (button != null) UnityEngine.Object.Destroy(button);
            writeButtons.Clear();
        }

        private static MeetingHud lastMeeting;

        public static void OnMeetingBegin()
        {
            var kira = local;
            if (kira == null) return;
            if (lastMeeting == MeetingHud.Instance) return;
            lastMeeting = MeetingHud.Instance;

            kira.writesLeft = writesPerMeeting;
            kira.lockedThisMeeting = false;
            kira.selectedTarget = null;
        }

        public static void OpenWriteDialog(byte targetId)
        {
            var kira = local;
            if (kira == null || kira.player != PlayerControl.LocalPlayer) return;
            if (kira.writesLeft <= 0 || kira.lockedThisMeeting) return;

            var target = Helpers.playerById(targetId);
            if (target == null || target.Data == null || target.Data.IsDead) return;

            CloseWriteDialog();
            kira.selectedTarget = target;

            var gui = TORGUIContextEngine.API;
            var attr = gui.GetAttribute(AttributeAsset.CenteredBoldFixed);

            writeField = new GUITextField(GUIAlignment.Center, new Size(4.3f, 0.4f))
            {
                HintText = ModTranslation.getString("kiraWriteHint").Color(Color.gray),
                IsSharpField = false,
                WithMaskMaterial = true,
                FontSize = 2f,
                MaxLines = 1,
                EnterAction = (text) => { SubmitWrite(text); return true; }
            };

            writeScreen = MetaScreen.GenerateWindow(new Vector2(5.2f, 2.0f), HudManager.Instance.transform, Vector3.zero, true, false, true);
            writeScreen.SetContext(gui.VerticalHolder(GUIAlignment.Center,
                gui.RawText(GUIAlignment.Center, attr, ModTranslation.getString("kiraWriteTitle")),
                gui.VerticalMargin(0.2f),
                writeField,
                gui.VerticalMargin(0.25f),
                gui.HorizontalHolder(GUIAlignment.Center,
                    gui.LocalizedButton(GUIAlignment.Center, attr, "kiraWriteConfirm", () => SubmitWrite(CurrentFieldText())),
                    gui.HorizontalMargin(0.25f),
                    gui.LocalizedButton(GUIAlignment.Center, attr, "kiraWriteCancel", CloseWriteDialog))), out _);
            writeField.Artifact.Do(field => field.GainFocus());
        }

        private static string CurrentFieldText()
        {
            string result = null;
            writeField?.Artifact.Do(field => { if (field != null) result = field.Text; });
            return result;
        }

        public static void CloseWriteDialog()
        {
            if (writeScreen != null)
            {
                writeScreen.CloseScreen();
                writeScreen = null;
            }
            writeField = null;
        }

        public static void SubmitWrite(string typed)
        {
            var kira = local;
            if (kira == null || kira.player != PlayerControl.LocalPlayer) { CloseWriteDialog(); return; }

            var target = kira.selectedTarget;
            if (target == null || target.Data == null) { CloseWriteDialog(); return; }

            string clean = typed == null ? "" : typed.Trim();
            bool correct = clean.Length > 0 && string.Equals(clean, target.Data.PlayerName, StringComparison.OrdinalIgnoreCase);

            kira.writesLeft--;
            kira.selectedTarget = null;

            if (correct)
            {
                _ = new StaticAchievementToken("kira.common1");

                var announcer = randomAnnouncer(kira.player, target);
                KillByNote.Invoke((kira.player.PlayerId, target.PlayerId, announcer != null ? announcer.PlayerId : byte.MaxValue));
            }
            else
            {
                _ = new StaticAchievementToken("kira.another1");
                if (!canContinueAfterFail) kira.lockedThisMeeting = true;
                Helpers.showFlash(new Color(0.5f, 0.5f, 0.5f, 1f), 0.5f);
                new CustomMessage(ModTranslation.getString("kiraWriteFail"), 3f);
            }

            CloseWriteDialog();
            UpdateButtons(MeetingHud.Instance);
        }

        public override void FixedUpdate()
        {
            if (player != PlayerControl.LocalPlayer) return;
            if (player.Data == null || player.Data.IsDead) return;

            if (MeetingHud.Instance != null)
            {
                MaskMeetingNames(MeetingHud.Instance);
                MaskReporter(MeetingHud.Instance);
                if (Time.time >= nextLookRefresh)
                {
                    nextLookRefresh = Time.time + 2f;
                    greyMeetingIcons(MeetingHud.Instance);
                }
                return;
            }

            if (Time.time >= nextLookRefresh && players.Any(x => x.player == PlayerControl.LocalPlayer))
            {
                nextLookRefresh = Time.time + 2f;
                hideEveryoneLook();
            }
        }

        public static void MaskMeetingNames(MeetingHud meeting)
        {
            if (meeting == null || meeting.playerStates == null) return;
            if (!IsLocalKira) return;

            foreach (PlayerVoteArea area in meeting.playerStates)
            {
                if (area == null || (byte)area.PlayerId == PlayerControl.LocalPlayer.PlayerId) continue;

                if (area.NameText != null)
                {
                    area.NameText.text = "？？？";
                    area.NameText.color = new Color(0.78f, 0.78f, 0.78f, 1f);
                }
                if (area.LevelNumberText != null)
                {
                    area.LevelNumberText.text = "？？？";
                    area.LevelNumberText.color = new Color(0.78f, 0.78f, 0.78f, 1f);
                }
                if (area.ColorBlindName != null) area.ColorBlindName.text = "？？？";
            }
        }

        public static void MaskReporter(MeetingHud meeting)
        {
            if (meeting == null || meeting.playerStates == null) return;
            if (!IsLocalKira) return;

            PlayerVoteArea reporter = null;
            PlayerVoteArea normal = null;

            foreach (PlayerVoteArea area in meeting.playerStates)
            {
                if (area == null || (byte)area.PlayerId == PlayerControl.LocalPlayer.PlayerId) continue;

                if (area.DidReport) reporter = area;
                else if (normal == null) normal = area;
            }

            if (reporter == null) return;

            if (reporter.Megaphone != null) reporter.Megaphone.enabled = false;

            if (reporter.Background != null && normal != null && normal.Background != null)
            {
                reporter.Background.sprite = normal.Background.sprite;
                reporter.Background.color = normal.Background.color;
            }
        }

        public static void AnnounceVote(byte voterId, byte targetId)
        {
            if (!IsLocalKira) return;
            if (PlayerControl.LocalPlayer.PlayerId == voterId) return;
            if (HudManager.Instance == null || HudManager.Instance.Chat == null) return;

            var voter = Helpers.playerById(voterId);
            if (voter == null || voter.Data == null || voter.Data.IsDead) return;

            var target = Helpers.playerById(targetId);
            string line = target != null && target.Data != null && !target.Data.IsDead
                ? string.Format(ModTranslation.getString("kiraVoteInfo"), target.Data.PlayerName)
                : ModTranslation.getString("kiraVoteSkipInfo");

            ChatCommands.CurrentChatType = ChatCommands.ChatTypes.Default;
            HudManager.Instance.Chat.AddChat(PlayerControl.LocalPlayer, line);
        }

        public static void greyMeetingIcons(MeetingHud meeting)
        {
            if (meeting == null || meeting.playerStates == null) return;
            if (!IsLocalKira) return;

            foreach (PlayerVoteArea area in meeting.playerStates)
            {
                if (area == null || area.PlayerIcon == null) continue;
                if ((byte)area.PlayerId == PlayerControl.LocalPlayer.PlayerId) continue;

                area.PlayerIcon.UpdateFromPlayerOutfit(new NetworkedPlayerInfo.PlayerOutfit
                {
                    ColorId = 15,
                    PlayerName = "",
                    HatId = "",
                    VisorId = "",
                    SkinId = "",
                    PetId = "",
                    NamePlateId = ""
                }, PlayerMaterial.MaskType.None, area.AmDead, false);
            }
        }

        public static void hideEveryoneLook()
        {
            if (PlayerControl.LocalPlayer == null || PlayerControl.LocalPlayer.Data == null) return;
            if (!PlayerControl.LocalPlayer.isRole(RoleId.Kira)) return;

            foreach (PlayerControl target in PlayerControl.AllPlayerControls)
            {
                if (target == null || target == PlayerControl.LocalPlayer) continue;
                target.setLook("", 15, "", "", "", "", false);
            }
        }

        public override void OnDeath(PlayerControl killer = null)
        {
            if (player == PlayerControl.LocalPlayer) restoreLooks();
        }

        public override void ResetRole(bool isShifted)
        {
            if (player == PlayerControl.LocalPlayer) restoreLooks();
        }

        private static void restoreLooks()
        {
            CloseWriteDialog();
            ClearButtons();
            foreach (PlayerControl target in PlayerControl.AllPlayerControls)
            {
                if (target == null || target.Data == null) continue;
                target.setDefaultLook();
            }
        }

        static public IEnumerable<DocumentReplacement> GetReplacementPart()
        {
            yield return new("%WIN%", killsToWin.ToString());
            yield return new("%WRITES%", writesPerMeeting.ToString());
        }

        public static void clearAndReload()
        {
            killsToWin = Mathf.RoundToInt(CustomOptionHolder.kiraKillsToWin.getFloat());
            writesPerMeeting = Mathf.RoundToInt(CustomOptionHolder.kiraWritesPerMeeting.getFloat());
            canContinueAfterFail = CustomOptionHolder.kiraCanContinueAfterFail.getBool();
            ClearButtons();
            CloseWriteDialog();
            lastMeeting = null;
            nextLookRefresh = 0f;
            players = [];
        }

        [HarmonyPatch(typeof(ChatController), nameof(ChatController.AddChatNote))]
        public static class KiraVoteNotePatch
        {
            public static bool Prefix() => !IsLocalKira;
        }

        [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.CastVote))]
        public static class KiraVoteWatchPatch
        {
            public static void Postfix([HarmonyArgument(0)] InnerNet.PlayerId srcPlayerId, [HarmonyArgument(1)] InnerNet.PlayerId suspectPlayerId)
                => AnnounceVote(srcPlayerId, suspectPlayerId);
        }
    }
}
