using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using TheOtherRoles.Modules;

namespace TheOtherRoles
{
	public static class DeveloperManager
	{
		//此处填入开发者ID
		public static readonly HashSet<string> DevFriendCodes = new() {
			"midplace#8957",//wwg
            "offlinenil#5191",//zy
            "squishyhod#5187",//hg
            "copysworn#2096"//hvt

        };
		//使用IsDev(好友代码)方法检查玩家是否为开发者（非TheOtherRoles命名空间需加DeveloperManager.前缀）
		public static bool IsDev(PlayerControl player) =>
			player != null && player.FriendCode != null && DevFriendCodes.Contains(player.FriendCode);

        private static bool granted = false;

        private static bool logged = false;

        public static void GrantDevAchievement()
        {
            if (granted) return;//防多次授予
            if (PlayerControl.LocalPlayer == null) return;

            if (!logged)
            {
                logged = true;
                TheOtherRolesPlugin.Logger.LogInfo($"[Dev] FriendCode=[{PlayerControl.LocalPlayer.FriendCode}] name=[{PlayerControl.LocalPlayer.Data?.PlayerName}] isDev={IsDev(PlayerControl.LocalPlayer)} list={string.Join(",", DevFriendCodes)}");
            }

            if (!IsDev(PlayerControl.LocalPlayer)) return;//不是开发者

            if (!TORAchievementManager.GetAchievement("developer", out var ach))//防蠢
            {
                TheOtherRolesPlugin.Logger.LogError("Developer achievement not found");
                granted = true;
                return;
            }

            var state = ((ProgressRecord)ach).Unite(1, true);//授予成就

            granted = true;
        }

    }
}
