using System;
using System.Collections.Generic;
using System.Linq;
using TheOtherRoles.Modules;
using TheOtherRoles.Objects;
using TheOtherRoles.Patches;
using TheOtherRoles.Utilities;
using UnityEngine;
using static TheOtherRoles.TheOtherRoles;

namespace TheOtherRoles.Roles
{
    public class NiceVoteEater : RoleBase<NiceVoteEater>
    {
        public NiceVoteEater()
        {
            RoleId = roleId = RoleId.NiceVoteEater;
        }

        static public IEnumerable<DocumentReplacement> GetReplacementPart()
        {
            yield return new("%USES%", VoteEater.maxUses.ToString());
        }
    }

    public class EvilVoteEater : RoleBase<EvilVoteEater>
    {
        public EvilVoteEater()
        {
            RoleId = roleId = RoleId.EvilVoteEater;
        }

        static public IEnumerable<DocumentReplacement> GetReplacementPart() => NiceVoteEater.GetReplacementPart();
    }

    [TORRPCHolder]
    public static class VoteEater
    {
        public static Color color = new Color32(35, 165, 175, byte.MaxValue);
        public static List<byte> eatenTargetPlayerIds = [];

        private static int remainingUseCount = 2;
        public static int eatenThisMeeting;
        public static int maxUses = 2;
        private static Sprite eatVoteSprite;

        private const float EatVoteSpritePixelsPerUnit = 258f;
        private const float EatVoteVisibleHeightRatio = 258f / 600f;

        public static RemoteProcess<(byte voteEaterId, byte targetId)> EatVote = new("VoteEaterEatVote", (message, __) =>
        {
            if (!MeetingHud.Instance) return;
            if (!isVoteEater(message.voteEaterId)) return;
            if (isVoteEaten(message.targetId)) return;

            var target = Helpers.playerById(message.targetId);
            if (target == null || target.Data == null || target.Data.IsDead) return;

            eatenTargetPlayerIds.Add(message.targetId);
            remainingUses(true);

            if (message.voteEaterId != PlayerControl.LocalPlayer.PlayerId) return;

            var local = PlayerControl.LocalPlayer;
            if (local.isRole(RoleId.NiceVoteEater))
            {
                _ = new StaticAchievementToken("niceVoteEater.common1");
                if (remainingUses() <= 0) _ = new StaticAchievementToken("niceVoteEater.challenge");
            }
            else if (local.isRole(RoleId.EvilVoteEater))
            {
                _ = new StaticAchievementToken("evilVoteEater.common1");
                eatenThisMeeting++;
                if (eatenThisMeeting >= 2) _ = new StaticAchievementToken("evilVoteEater.challenge");
            }
        });

        public static Sprite getEatVoteSprite()
        {
            if (eatVoteSprite) return eatVoteSprite;
            eatVoteSprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.EatVote.png", EatVoteSpritePixelsPerUnit);
            return eatVoteSprite;
        }

        public static bool isVoteEater(byte playerId)
        {
            var player = Helpers.playerById(playerId);
            return player != null && (player.isRole(RoleId.NiceVoteEater) || player.isRole(RoleId.EvilVoteEater));
        }

        public static int remainingUses(bool consume = false)
        {
            if (consume) remainingUseCount = Mathf.Max(0, remainingUseCount - 1);
            return remainingUseCount;
        }

        public static bool isVoteEaten(byte targetPlayerId) => eatenTargetPlayerIds.Contains(targetPlayerId);

        public static void addEatenVoteIcon(MeetingHud meetingHud, PlayerVoteArea playerVoteArea)
        {
            if (meetingHud == null || meetingHud.PlayerVotePrefab == null || playerVoteArea == null) return;

            var voteSpreader = playerVoteArea.GetComponent<VoteSpreader>();
            if (voteSpreader == null) return;

            var renderer = UnityEngine.Object.Instantiate<SpriteRenderer>(meetingHud.PlayerVotePrefab);
            if (renderer == null) return;

            var sprite = getEatVoteSprite();
            if (sprite == null) return;
            renderer.sprite = sprite;

            float scale = 1f;
            var voteSprite = meetingHud.PlayerVotePrefab.sprite;
            if (voteSprite != null && voteSprite.pixelsPerUnit > 0f && sprite.pixelsPerUnit > 0f)
            {
                float voteIconHeight = voteSprite.rect.height / voteSprite.pixelsPerUnit;
                float eatenIconHeight = sprite.rect.height / sprite.pixelsPerUnit * EatVoteVisibleHeightRatio;
                if (eatenIconHeight > 0f) scale = voteIconHeight / eatenIconHeight;
            }

            var transform = renderer.transform;
            transform.SetParent(playerVoteArea.transform);
            transform.localPosition = Vector3.zero;
            transform.localScale = Vector3.one * scale;
            PlayerMaterial.SetColors(Palette.White, renderer);
            renderer.material.SetInt(PlayerMaterial.MaskLayer, playerVoteArea.MaskLayer);
            voteSpreader.AddVote(renderer);
        }

        public static void checkAndReturnUses(MeetingHud meetingHud, byte dyingPlayerId)
        {
            if (meetingHud == null || meetingHud.state == MeetingHud.MeetingStates.Results) return;
            if (!eatenTargetPlayerIds.Remove(dyingPlayerId)) return;

            remainingUseCount++;
            if (!isVoteEater(PlayerControl.LocalPlayer.PlayerId)) return;
            if (PlayerControl.LocalPlayer.isRole(RoleId.NiceVoteEater)) _ = new StaticAchievementToken("niceVoteEater.another1");

            for (int i = 0; i < meetingHud.playerStates.Length; i++)
            {
                var playerVoteArea = meetingHud.playerStates[i];
                Transform button = playerVoteArea.transform.FindChild("EatVoteButton");
                if (button != null) button.gameObject.SetActive(!playerVoteArea.AmDead && !isVoteEaten((byte)playerVoteArea.PlayerId));
            }
        }

        public static void clearAndReload()
        {
            eatenTargetPlayerIds.Clear();
            maxUses = Mathf.RoundToInt(CustomOptionHolder.voteEaterNumberOfUses.getFloat());
            remainingUseCount = maxUses;
            NiceVoteEater.players = [];
            EvilVoteEater.players = [];
        }
    }
}
