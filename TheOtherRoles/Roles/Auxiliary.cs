using System.Collections.Generic;
using System.Linq;
using TheOtherRoles.MetaContext;
using TheOtherRoles.Modules;
using TheOtherRoles.Objects;
using UnityEngine;
using static TheOtherRoles.Patches.PlayerControlFixedUpdatePatch;
using static TheOtherRoles.TheOtherRoles;

namespace TheOtherRoles.Roles
{
    [TORRPCHolder]
    public class Auxiliary : RoleBase<Auxiliary>
    {
        public static Color color = Sheriff.color;
        public static Color markColor = new Color32(255, 140, 40, byte.MaxValue);

        public static float markCooldown = 25f;
        public static int maxMarks = 2;

        public Sheriff sheriff;
        public PlayerControl currentTarget;
        public PlayerControl markedPlayer;
        public int remainingMarks;
        public bool promoted;

        private static Sprite buttonSprite;

        public Auxiliary()
        {
            RoleId = roleId = RoleId.Auxiliary;
            sheriff = Sheriff.players.FirstOrDefault(x => !players.Any(a => a.sheriff != null && a.sheriff.player == x.player));
            currentTarget = null;
            markedPlayer = null;
            remainingMarks = Mathf.RoundToInt(CustomOptionHolder.auxiliaryNumberOfMarks.getFloat());
            promoted = false;
        }

        public static RemoteProcess<(byte auxiliaryId, byte targetId)> Mark = new("AuxiliaryMark", (message, _) =>
        {
            var auxiliary = getRole(Helpers.playerById(message.auxiliaryId));
            var target = Helpers.playerById(message.targetId);
            if (auxiliary == null || auxiliary.player == null || target == null || target.Data == null) return;
            if (auxiliary.remainingMarks <= 0) return;

            auxiliary.remainingMarks--;
            auxiliary.markedPlayer = target;

            if (PlayerControl.LocalPlayer == target)
            {
                SoundEffectsManager.play("warlockCurse");
                new CustomMessage(string.Format(ModTranslation.getString("auxiliaryMarkedNotice"), auxiliary.player.Data.PlayerName), 4f);
            }
        });

        public static RemoteProcess<byte> PromoteToSheriff = RemotePrimitiveProcess.OfByte("AuxiliaryPromotes", (message, _) =>
        {
            PlayerControl auxiliary = Helpers.playerById(message);
            if (auxiliary == null || getRole(auxiliary) == null) return;

            Sheriff.replaceCurrentSheriff(auxiliary);
            eraseRole(auxiliary);
        });

        public static Sprite getButtonSprite()
        {
            if (buttonSprite) return buttonSprite;
            buttonSprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.AssassinMarkButton.png", 115f);
            return buttonSprite;
        }

        public static Auxiliary getAuxiliary(PlayerControl sheriff)
        {
            return players.FirstOrDefault(x => x.sheriff != null && x.sheriff.player == sheriff);
        }

        public static Auxiliary getProtectorOf(PlayerControl target)
        {
            if (target == null || target.Data == null) return null;
            return players.FirstOrDefault(x => x.player != null && x.player.Data != null && !x.player.Data.IsDead && x.markedPlayer == target);
        }

        public override GUIContext ProgressContext
        {
            get
            {
                if (sheriff == null || sheriff.player == null || sheriff.player.Data == null) return null;
                return ProgressGUI.Holder(ProgressGUI.OneLineText(ModTranslation.getString("sheriff") + ": " + sheriff.player.Data.PlayerName));
            }
        }

        public override void FixedUpdate()
        {
            if (player != PlayerControl.LocalPlayer) return;

            if (HudManagerStartPatch.auxiliaryMarksText != null)
                HudManagerStartPatch.auxiliaryMarksText.text = remainingMarks.ToString();

            if (player.Data == null || player.Data.IsDead) { currentTarget = null; return; }
            if (MeetingHud.Instance || ExileController.Instance) { currentTarget = null; return; }

            if (remainingMarks > 0)
            {
                var untargetable = new List<PlayerControl>();
                if (markedPlayer != null) untargetable.Add(markedPlayer);
                currentTarget = setTarget(untargetablePlayers: untargetable);
                setPlayerOutline(currentTarget, color);
            }
            else
            {
                currentTarget = null;
            }

            if (markedPlayer != null && markedPlayer.Data != null && !markedPlayer.Data.IsDead)
                setPlayerOutline(markedPlayer, markColor);

            checkPromotion();
        }

        public override void OnMeetingEnd(PlayerControl exiled = null)
        {
            if (PlayerControl.LocalPlayer == player) checkPromotion();
        }

        public void checkPromotion()
        {
            if (promoted || player == null || player.Data == null || player.Data.IsDead) return;
            if (sheriff != null && sheriff.player != null && sheriff.player.Data != null && !sheriff.player.Data.IsDead && !sheriff.player.Data.Disconnected) return;

            promoted = true;
            PromoteToSheriff.Invoke(player.PlayerId);
        }

        static public IEnumerable<DocumentReplacement> GetReplacementPart()
        {
            yield return new("%MARKS%", maxMarks.ToString());
            yield return new("%CD%", Mathf.RoundToInt(markCooldown).ToString());
        }

        public static void clearAndReload()
        {
            markCooldown = CustomOptionHolder.auxiliaryMarkCooldown.getFloat();
            maxMarks = Mathf.RoundToInt(CustomOptionHolder.auxiliaryNumberOfMarks.getFloat());
            players = [];
        }
    }
}
