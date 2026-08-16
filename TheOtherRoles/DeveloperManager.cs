using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using TheOtherRoles.Modules;

namespace TheOtherRoles
{
	public static class DeveloperManager
	{
		//此处填入开发者ID
		public static readonly HashSet<string> DevFriendCodes = new() {
			"midplace#8957"//汪汪狗
		
		};
		//使用IsDev(好友代码)方法检查玩家是否为开发者（非TheOtherRoles命名空间需加DeveloperManager.前缀）
		public static bool IsDev(PlayerControl player) =>
			player != null && player.FriendCode != null && DevFriendCodes.Contains(player.FriendCode);

        private static bool granted = false;

        public static void GrantDevAchievement()
        {
            if (granted) return;//防多次授予
            if (PlayerControl.LocalPlayer == null || !IsDev(PlayerControl.LocalPlayer)) return;//不是开发者

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
