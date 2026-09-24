using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AmongUs.GameOptions;
using HarmonyLib;
using TheOtherRoles.CustomGameModes;
using TheOtherRoles.Patches;
using UnityEngine;
using static TheOtherRoles.TheOtherRoles;

namespace TheOtherRoles.Modules
{
    [HarmonyPatch]
    [TORRPCHolder]
    public static class DeveloperCommand
    {
        //正则命令解析（感谢DS）
        private static readonly Regex commandRegex = new(@"^/(\w+)(?:\s+(.+))?$", RegexOptions.IgnoreCase);

        private static readonly Color hostColor = new(255f / 255f, 205f / 255f, 70f / 255f, 1f);
        private static readonly Color devColor = new(255f / 255f, 90f / 255f, 90f / 255f, 1f);

        public static readonly Dictionary<byte, RoleId> pendingAssignments = [];

        [HarmonyPatch(typeof(ChatController), nameof(ChatController.SendChat))]
        private static class SendChatPatch
        {
            private static bool Prefix(ChatController __instance)
            {
                string text = __instance.freeChatField.Text;
                if (string.IsNullOrWhiteSpace(text) || !text.Trim().StartsWith("/")) return true;

                var match = commandRegex.Match(text.Trim());
                if (!match.Success) return true;

                string cmd = match.Groups[1].Value.ToLower();
                string arg = match.Groups[2].Value.Trim();

                //命令权限管理
                bool isDevCommand = cmd switch
                {
                    "s" => true,
                    "up" => true,
                    "skipmeeting" => true,
                    _ => false
                };
                if (!isDevCommand) return true;//非命令直接发送

                bool isHost = AmongUsClient.Instance != null && AmongUsClient.Instance.AmHost;
                bool allowed = DeveloperManager.IsDev(PlayerControl.LocalPlayer) || isHost;

                if (cmd == "skipmeeting")
                {
                    if (allowed) skipMeeting();
                    return true;//这条指令照常发到聊天栏
                }

                //拦截发送
                __instance.freeChatField.Clear();
                __instance.quickChatMenu.Clear();

                if (!allowed) return false;//非开发者或房主拦截

                switch (cmd)
                {
                    case "s"://全图公告
                        if (arg.Length > 0)
                            RPCProcedure.DevAnnouncement.Invoke((PlayerControl.LocalPlayer.PlayerId, arg));
                        break;
                    case "up"://指定开局职业
                        handleUp(arg);
                        break;
                }
                return false;
            }
        }

        //全图公告
        public static void Show(byte senderId, string text)
        {
            if (HudManager.Instance == null || HudManager.Instance.Notifier == null) return;

            var sender = Helpers.playerById(senderId);
            bool isDev = DeveloperManager.IsDev(sender);
            string senderName = sender != null && sender.Data != null ? sender.Data.PlayerName : "";
            string label = Helpers.cs(isDev ? devColor : hostColor, ModTranslation.getString(isDev ? "devMessage" : "hostMessage"));

            Helpers.CreateAndShowNotification($"{label} {senderName}: {text}", Color.white, new Vector3(0f, 1f, -20f));
        }

        [HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
        private static class InvalidGameHotkeyPatch
        {
            private static void Postfix()
            {
                if (!Input.GetKeyDown(KeyCode.F)) return;
                if (!Input.GetKey(KeyCode.LeftAlt) && !Input.GetKey(KeyCode.RightAlt)) return;

                tryEndGameAsInvalid();
            }
        }

        private static void tryEndGameAsInvalid()
        {
            if (AmongUsClient.Instance == null || AmongUsClient.Instance.GameState != InnerNet.InnerNetClient.GameStates.Started) return;
            if (AmongUsClient.Instance.IsGameOver) return;

            bool isHost = AmongUsClient.Instance.AmHost;
            bool allowed = DeveloperManager.IsDev(PlayerControl.LocalPlayer) || isHost;
            log($"invalidEnd hotkey: allowed={allowed} isHost={isHost}");
            if (!allowed) return;

            if (isHost) endGameAsInvalid();
            else InvalidEnd.Invoke(PlayerControl.LocalPlayer.PlayerId);
        }

        public static RemoteProcess<byte> InvalidEnd = RemotePrimitiveProcess.OfByte("InvalidGameEnd", (message, _) =>
        {
            log("invalidEnd request received");
            endGameAsInvalid();
        });

        private static void endGameAsInvalid()
        {
            if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
            if (AmongUsClient.Instance.GameState != InnerNet.InnerNetClient.GameStates.Started) return;
            if (AmongUsClient.Instance.IsGameOver) return;

            log("ending game as invalid");
            GameManager.Instance.RpcEndGame((GameOverReason)CustomGameOverReason.InvalidGame, false);
        }

        private static void skipMeeting()
        {
            var meeting = MeetingHud.Instance;
            if (meeting == null || meeting.state is MeetingHud.MeetingStates.Results or MeetingHud.MeetingStates.Proceeding or MeetingHud.MeetingStates.Animating)
            {
                localMessage(ModTranslation.getString("skipMeetingOnlyInMeeting"));
                return;
            }

            log($"skipmeeting: state={meeting.state} players={meeting.playerStates.Length}");
            MeetingHudPatch.completeVoting(meeting);
            localMessage(ModTranslation.getString("skipMeetingDone"));
        }

        private static void handleUp(string arg)
        {
            log($"handleUp arg='{arg}' lobby={LobbyBehaviour.Instance != null} state={AmongUsClient.Instance?.GameState} amHost={AmongUsClient.Instance?.AmHost}");

            if (!isInLobby())
            {
                localMessage(ModTranslation.getString("upLobbyOnly"));
                return;
            }

            var tokens = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
            {
                localMessage(ModTranslation.getString("upUsage"));
                return;
            }

            PlayerControl target = null;
            string roleToken = null;

            if (tokens.Length == 1)
            {
                target = PlayerControl.LocalPlayer;
                roleToken = tokens[0];
            }
            else
            {
                for (int i = tokens.Length - 1; i >= 1; i--)
                {
                    string candidate = string.Join(" ", tokens.Take(i));
                    var found = PlayerControl.AllPlayerControls.ToArray().FirstOrDefault(x => x != null && x.Data != null && x.Data.PlayerName.Equals(candidate, StringComparison.OrdinalIgnoreCase));
                    if (found == null) continue;

                    target = found;
                    roleToken = string.Join(" ", tokens.Skip(i));
                    break;
                }

                if (target == null && findRole(arg) != null)
                {
                    target = PlayerControl.LocalPlayer;
                    roleToken = arg;
                }
            }

            if (target == null)
            {
                log($"player not found for arg='{arg}'");
                localMessage(ModTranslation.getString("upPlayerNotFound"));
                return;
            }

            var roleInfo = findRole(roleToken);
            if (roleInfo == null)
            {
                log($"role not found for token='{roleToken}' target={target.Data.PlayerName}");
                localMessage(ModTranslation.getString("upRoleNotFound"));
                return;
            }

            log($"resolved target={target.Data.PlayerName}({target.PlayerId}) role={roleInfo.nameKey}({(int)roleInfo.roleId})");

            ForceRole.Invoke((target.PlayerId, (int)roleInfo.roleId));
            localMessage(string.Format(ModTranslation.getString("upSuccess"), target.Data.PlayerName, roleInfo.name));
        }

        private static bool isInLobby()
        {
            if (LobbyBehaviour.Instance != null) return true;
            return AmongUsClient.Instance != null && AmongUsClient.Instance.GameState == InnerNet.InnerNetClient.GameStates.Joined;
        }

        private static void log(string text)
        {
            TheOtherRolesPlugin.Logger.LogMessage("[UP] " + text);
        }

        private static RoleInfo findRole(string token)
        {
            string needle = token.Trim().ToLower();
            if (needle.Length == 0) return null;

            return RoleInfo.allRoleInfos.FirstOrDefault(x => !x.isModifier && RoleData.allRoleIds.ContainsKey(x.roleId)
                && (x.nameKey.ToLower() == needle || x.name.ToLower() == needle));
        }

        private static void localMessage(string text)
        {
            if (HudManager.Instance == null || HudManager.Instance.Chat == null) return;
            HudManager.Instance.Chat.AddChat(PlayerControl.LocalPlayer, text);
        }

        public static RemoteProcess<(byte playerId, int roleId)> ForceRole = new("UpForceRole", (message, _) =>
        {
            log($"rpc received player={message.playerId} role={message.roleId} amHost={AmongUsClient.Instance?.AmHost}");
            if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
            if (Helpers.playerById(message.playerId) == null) return;
            pendingAssignments[message.playerId] = (RoleId)message.roleId;
            log($"queued total={pendingAssignments.Count}");
        });

        public static bool hasPendingAssignments => pendingAssignments.Count > 0;

        public static void applyFactionSwaps()
        {
            if (pendingAssignments.Count == 0) return;
            if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;

            log($"swap: pending={pendingAssignments.Count} players={PlayerControl.AllPlayerControls.Count}");

            HashSet<byte> locked = new(pendingAssignments.Keys);
            List<(byte playerId, byte partnerId, bool playerShouldBeImpostor)> swaps = [];

            foreach (var entry in pendingAssignments)
            {
                var player = Helpers.playerById(entry.Key);
                var roleInfo = RoleInfo.allRoleInfos.FirstOrDefault(x => x.roleId == entry.Value);
                if (player == null || player.Data == null || roleInfo == null) continue;
                if (player.Data.Role.IsImpostor == roleInfo.isImpostor) continue;

                var partner = PlayerControl.AllPlayerControls.ToArray()
                    .Where(x => x != null && x.Data != null && !x.Data.Disconnected && !x.Data.IsDead
                        && x.Data.Role.IsImpostor == roleInfo.isImpostor
                        && !locked.Contains(x.PlayerId))
                    .OrderBy(_ => rnd.Next())
                    .FirstOrDefault();

                if (partner == null)
                {
                    log($"no partner for {player.Data.PlayerName}, faction set directly");
                    swaps.Add((player.PlayerId, byte.MaxValue, roleInfo.isImpostor));
                    continue;
                }

                locked.Add(partner.PlayerId);
                swaps.Add((player.PlayerId, partner.PlayerId, roleInfo.isImpostor));
            }

            foreach (var swap in swaps)
            {
                applyFactionSwap(swap.playerId, swap.partnerId, swap.playerShouldBeImpostor);
                SwapFaction.Invoke((swap.playerId, swap.partnerId, swap.playerShouldBeImpostor));
            }
        }

        private static void applyFactionSwap(byte playerId, byte partnerId, bool playerShouldBeImpostor)
        {
            var player = Helpers.playerById(playerId);
            if (player == null || player.Data == null) return;
            if (player.Data.Role.IsImpostor == playerShouldBeImpostor) return;

            var partner = partnerId == byte.MaxValue ? null : Helpers.playerById(partnerId);
            if (partner == null || partner.Data == null)
            {
                log($"direct faction: {player.Data.PlayerName} -> impostor={playerShouldBeImpostor}");
                player.FastSetRole(playerShouldBeImpostor ? RoleTypes.Impostor : RoleTypes.Crewmate);
                if (playerShouldBeImpostor) player.clearAllTasks();
                return;
            }

            log($"swap: {player.Data.PlayerName}({player.Data.Role.IsImpostor}) <-> {partner.Data.PlayerName}({partner.Data.Role.IsImpostor})");

            var playerType = player.Data.RoleType;
            var partnerType = partner.Data.RoleType;
            player.FastSetRole(partnerType);
            partner.FastSetRole(playerType);

            var newImpostor = playerShouldBeImpostor ? player : partner;
            var newCrewmate = playerShouldBeImpostor ? partner : player;

            newImpostor.clearAllTasks();

            if ((newCrewmate.Data.Tasks == null || newCrewmate.Data.Tasks?.Count == 0) && !newCrewmate.Data.IsDead && PlayerControl.LocalPlayer == newCrewmate)
                newCrewmate.generateNormalTasks();
        }

        public static RemoteProcess<(byte playerId, byte partnerId, bool playerShouldBeImpostor)> SwapFaction = new("UpSwapFaction", (message, _) =>
        {
            applyFactionSwap(message.playerId, message.partnerId, message.playerShouldBeImpostor);
        });
    }
}
