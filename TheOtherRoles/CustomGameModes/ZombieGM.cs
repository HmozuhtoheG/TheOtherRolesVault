using UnityEngine;

namespace TheOtherRoles.CustomGameModes {
    public static class Zombie { // Zombie Infection Gamemode
        public static bool isZombieGM = false;
        public static float timer = 300f;
        public static int initialZombieCount = 1;
        public static bool taskWinPossible = false;

        public static void clearAndReload() {
            isZombieGM = TORMapOptions.gameMode == CustomGamemodes.Zombie;
            timer = CustomOptionHolder.zombieTimer.getFloat() * 60;
            initialZombieCount = Mathf.RoundToInt(CustomOptionHolder.zombieInitialCount.getFloat());
            taskWinPossible = CustomOptionHolder.zombieTaskWin.getBool();
        }
    }
}
