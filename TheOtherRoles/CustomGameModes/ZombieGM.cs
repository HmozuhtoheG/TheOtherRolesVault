using System.Collections.Generic;
using UnityEngine;

namespace TheOtherRoles.CustomGameModes {
    public static class Zombie { // Zombie Infection Gamemode
        public static bool isZombieGM = false;
        public static float timer = 300f;
        public static int initialZombieCount = 1;
        public static bool taskWinPossible = false;
        public static bool isWaitingTimer = true;
        public static float zombieWaitingTime = 15f;

        public static List<PlayerControl> getZombies() {
            List<PlayerControl> zombies = new(PlayerControl.AllPlayerControls.ToArray());
            zombies.RemoveAll(x => !x.Data.Role.IsImpostor);
            return zombies;
        }

        public static void clearAndReload() {
            isZombieGM = TORMapOptions.gameMode == CustomGamemodes.Zombie;
            isWaitingTimer = true;
            timer = CustomOptionHolder.zombieTimer.getFloat() * 60;
            initialZombieCount = Mathf.RoundToInt(CustomOptionHolder.zombieInitialCount.getFloat());
            taskWinPossible = CustomOptionHolder.zombieTaskWin.getBool();
            zombieWaitingTime = CustomOptionHolder.zombieWaitingTime.getFloat();
        }
    }
}
