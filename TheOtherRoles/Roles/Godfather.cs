using System.Collections.Generic;
using System.Linq;
using TheOtherRoles.MetaContext;
using TheOtherRoles.Modules;
using TheOtherRoles.Utilities;
using TMPro;
using UnityEngine;
using static TheOtherRoles.TheOtherRoles;

namespace TheOtherRoles.Roles;

[TORRPCHolder]
public class Godfather : RoleBase<Godfather>
{
    public static Color color = Palette.ImpostorRed;

    public Godfather()
    {
        RoleId = roleId = RoleId.Godfather;
    }

    public static List<PlayerControl> killed = [];
    public static bool shareInfo = true;

    public static int usesThisMeeting = 1;
    public static int usesTotal = 2;
    public static List<GameObject> reckoningButtons = [];
    public static MetaScreen confirmScreen = null;

    public static int remainingUses => Mathf.Min(usesThisMeeting, usesTotal);
    public static bool canUse => remainingUses > 0;

    private static Sprite reckoningSprite;
    private static TextMeshPro meetingText;

    private static readonly Vector3 MeetingTextMargin = new Vector3(0.35f, 0.35f, 0f);
    private static readonly Vector3 MeetingTextFallback = new Vector3(-4.9f, -2.6f, -20f);

    public static Sprite GetReckoningSprite()
    {
        if (reckoningSprite) return reckoningSprite;
        reckoningSprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.TargetIcon.png", 115f);
        return reckoningSprite;
    }

    static public IEnumerable<HelpSprite> GetHelpSprites()
    {
        yield return new(GetReckoningSprite(), "godfatherReckoningHint");
    }

    public static void CreateMeetingText()
    {
        DestroyMeetingText();
        if (!MeetingHud.Instance) return;

        var cam = Helpers.FindCamera(MeetingHud.Instance.gameObject.layer);
        if (!cam) cam = Camera.main;

        var position = MeetingTextFallback;
        if (cam)
        {
            float halfHeight = cam.orthographicSize;
            float halfWidth = halfHeight * cam.aspect;
            position = new Vector3(-halfWidth + MeetingTextMargin.x, -halfHeight + MeetingTextMargin.y, -20f);
        }

        var holder = Helpers.CreateObject("ReckoningStatus", MeetingHud.Instance.transform, position);
        holder.layer = MeetingHud.Instance.gameObject.layer;

        meetingText = holder.AddComponent<TextMeshPro>();
        if (VanillaAsset.StandardTextPrefab) meetingText.font = VanillaAsset.StandardTextPrefab.font;
        meetingText.fontSize = 1.5f;
        meetingText.alignment = TextAlignmentOptions.BottomLeft;
        meetingText.color = color;
        meetingText.enableWordWrapping = false;
        meetingText.rectTransform.pivot = new Vector2(0f, 0f);
        meetingText.rectTransform.sizeDelta = new Vector2(5f, 1f);

        UpdateMeetingText();
    }

    public static void UpdateMeetingText()
    {
        if (!meetingText) return;
        meetingText.text = string.Format(ModTranslation.getString("godfatherReckoningStatus"), usesTotal, usesThisMeeting);
    }

    public static void DestroyMeetingText()
    {
        if (meetingText) Object.Destroy(meetingText.gameObject);
        meetingText = null;
    }

    public override void OnKill(PlayerControl target)
    {
        killed.TryAdd(target);
    }

    public static void OnMeetingBegin()
    {
        usesThisMeeting = Mathf.RoundToInt(CustomOptionHolder.godfatherReckoningPerMeeting.getFloat());
        CloseConfirmScreen();
        CreateMeetingText();
    }

    public static bool shouldShowInfo(PlayerControl player) => isRole(player) || ((player.isRole(RoleId.Mafioso) || player.isRole(RoleId.Janitor)) && shareInfo);

    public static void CloseConfirmScreen()
    {
        if (confirmScreen != null) confirmScreen.CloseScreen();
        confirmScreen = null;
    }

    public static void ClearButtons()
    {
        foreach (var button in reckoningButtons)
            if (button) Object.Destroy(button);
        reckoningButtons.Clear();
        CloseConfirmScreen();
        DestroyMeetingText();
    }

    public static void UpdateButtons(MeetingHud __instance)
    {
        bool show = canUse
            && PlayerControl.LocalPlayer != null
            && !PlayerControl.LocalPlayer.Data.IsDead
            && __instance.state is MeetingHud.MeetingStates.NotVoted or MeetingHud.MeetingStates.Voted;

        foreach (var button in reckoningButtons)
        {
            if (!button) continue;
            if (button.activeSelf != show) button.SetActive(show);
        }
    }

    public static void OpenConfirm(byte targetId)
    {
        CloseConfirmScreen();

        var target = Helpers.playerById(targetId);
        if (target == null || target.Data == null || !canUse) return;

        var gui = TORGUIContextEngine.API;
        var attr = gui.GetAttribute(AttributeAsset.CenteredBoldFixed);

        confirmScreen = MetaScreen.GenerateWindow(new Vector2(4.4f, 1.7f), HudManager.Instance.transform, Vector3.zero, true, false);

        confirmScreen.SetContext(gui.VerticalHolder(GUIAlignment.Center,
            gui.RawText(GUIAlignment.Center, attr, string.Format(ModTranslation.getString("godfatherReckoningConfirm"), Helpers.cs(target.Data.Color, target.Data.PlayerName))),
            gui.VerticalMargin(0.25f),
            gui.HorizontalHolder(GUIAlignment.Center,
                gui.LocalizedButton(GUIAlignment.Center, attr, "godfatherReckoningYes", () => Execute(targetId)),
                gui.HorizontalMargin(0.25f),
                gui.LocalizedButton(GUIAlignment.Center, attr, "godfatherReckoningNo", CloseConfirmScreen))), out _);
    }

    public static void Execute(byte targetId)
    {
        CloseConfirmScreen();

        var godfather = local?.player;
        if (godfather == null || godfather.Data == null || godfather.Data.IsDead || !canUse) return;

        var target = Helpers.playerById(targetId);
        if (target == null || target.Data == null || target.Data.IsDead) return;

        bool success = false;

        if (MeetingHud.Instance != null
            && MeetingHud.Instance.state is MeetingHud.MeetingStates.NotVoted or MeetingHud.MeetingStates.Voted)
        {
            var area = MeetingHud.Instance.playerStates.FirstOrDefault(x => (byte)x.PlayerId == targetId);
            if (area != null) success = (byte)area.VotedForId == godfather.PlayerId;
        }

        Reckoning.Invoke((godfather.PlayerId, targetId, success, getAnnouncerId(godfather.PlayerId, targetId)));
    }

    private static byte getAnnouncerId(byte godfatherId, byte targetId)
    {
        var candidates = PlayerControl.AllPlayerControls.ToArray()
            .Where(x => x != null && x.Data != null && !x.Data.IsDead && !x.Data.Disconnected && x.PlayerId != godfatherId && x.PlayerId != targetId)
            .ToList();
        if (candidates.Count == 0) return targetId;
        return candidates[rnd.Next(candidates.Count)].PlayerId;
    }

    public static RemoteProcess<(byte godfatherId, byte targetId, bool success, byte announcerId)> Reckoning = new("GodfatherReckoning", (message, _) =>
    {
        usesThisMeeting = Mathf.Max(0, usesThisMeeting - 1);
        usesTotal = Mathf.Max(0, usesTotal - 1);

        var godfather = Helpers.playerById(message.godfatherId);

        if (!message.success)
        {
            if (godfather != null && PlayerControl.LocalPlayer == godfather)
                Helpers.showFlash(Palette.ImpostorRed, 0.5f);
            return;
        }

        var target = Helpers.playerById(message.targetId);
        if (godfather == null || target == null || target.Data == null || target.Data.IsDead) return;

        target.Exiled();
        GameHistory.overrideDeathReasonAndKiller(target, DeadPlayer.CustomDeathReason.Reckoning, godfather);

        if (Constants.ShouldPlaySfx()) SoundManager.Instance.PlaySound(target.KillSfx, false, 0.8f);

        if (AmongUsClient.Instance.AmClient && FastDestroyableSingleton<HudManager>.Instance != null)
        {
            var announcer = Helpers.playerById(message.announcerId);
            if (announcer == null || announcer.Data == null) announcer = godfather;

            ChatCommands.CurrentChatType = ChatCommands.ChatTypes.GodfatherMessage;
            HudManager.Instance.Chat.AddChat(announcer,
                string.Format(ModTranslation.getString("godfatherReckoningAnnouncement"), Helpers.cs(target.Data.Color, target.Data.PlayerName)), false);
            ChatCommands.CurrentChatType = ChatCommands.ChatTypes.Default;
        }
    });

    public static void clearAndReload()
    {
        killed = [];
        shareInfo = CustomOptionHolder.godfatherShareInfo.getBool();
        usesThisMeeting = Mathf.RoundToInt(CustomOptionHolder.godfatherReckoningPerMeeting.getFloat());
        usesTotal = Mathf.RoundToInt(CustomOptionHolder.godfatherReckoningTotal.getFloat());
        ClearButtons();
        players = [];
    }
}
