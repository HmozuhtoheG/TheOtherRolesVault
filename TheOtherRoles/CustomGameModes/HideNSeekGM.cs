using System;
using System.Collections.Generic;
using Hazel;
using TheOtherRoles.Objects;
using TheOtherRoles.Utilities;
using UnityEngine;

namespace TheOtherRoles.CustomGameModes {
    public static class HideNSeek { // HideNSeek Gamemode
        public static bool isHideNSeekGM = false;
        public static TMPro.TMP_Text timerText = null;
        public static Vent polusVent = null;
        public static bool isWaitingTimer = true;
        public static DateTime startTime = DateTime.UtcNow;

        public static float timer = 300f;
        public static float hunterVision = 0.5f;
        public static float huntedVision = 2f;
        public static bool taskWinPossible = false;
        public static float taskPunish = 10f;
        public static int impNumber = 2;
        public static bool canSabotage = false;
        public static float killCooldown = 10f;
        public static float hunterWaitingTime = 15f;

        public static int botCount = 0;
        public static float botPenalty = 15f;
        public static float disguiseCooldown = 10f;

        private static GameObject blackOverlay;

        public static bool isFrozen() {
            return isHideNSeekGM && isWaitingTimer;
        }

        public static List<PlayerControl> getHunted() {
            List<PlayerControl> hunted = new(PlayerControl.AllPlayerControls.ToArray());
            hunted.RemoveAll(x => x == null || x.Data == null || x.Data.Role.IsImpostor || x.isDummy);
            return hunted;
        }

        public static void applyBlackScreen(bool on) {
            var hud = FastDestroyableSingleton<HudManager>.Instance;
            if (hud == null || hud.FullScreen == null) return;

            if (!on) {
                if (blackOverlay != null) {
                    UnityEngine.Object.Destroy(blackOverlay);
                    blackOverlay = null;
                }
                return;
            }

            if (blackOverlay != null) return;

            var renderer = UnityEngine.Object.Instantiate(hud.FullScreen, hud.transform);
            if (renderer == null) return;
            blackOverlay = renderer.gameObject;
            blackOverlay.SetActive(true);
            blackOverlay.name = "HnSBlackScreen";
            renderer.enabled = true;
            renderer.color = new Color(0f, 0f, 0f, 1f);
        }

        public static void punishTimer(float amount) {
            if (amount <= 0f) return;
            MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(PlayerControl.LocalPlayer.NetId, (byte)CustomRPC.ShareTimer, Hazel.SendOption.Reliable, -1);
            writer.Write(amount);
            AmongUsClient.Instance.FinishRpcImmediately(writer);
            RPCProcedure.shareTimer(amount);
        }
        public static bool isHunter() {
            return isHideNSeekGM && PlayerControl.LocalPlayer != null && PlayerControl.LocalPlayer.Data.Role.IsImpostor;
        }

        public static List<PlayerControl> getHunters() {
            List<PlayerControl> hunters = new(PlayerControl.AllPlayerControls.ToArray());
            hunters.RemoveAll(x => !x.Data.Role.IsImpostor);
            return hunters;
        }

        public static bool isHunted() {
            return isHideNSeekGM && PlayerControl.LocalPlayer != null && !PlayerControl.LocalPlayer.Data.Role.IsImpostor;
        }

        public static void clearAndReload() {
            isHideNSeekGM = TORMapOptions.gameMode == CustomGamemodes.HideNSeek;
            if (timerText != null) UnityEngine.Object.Destroy(timerText);
            timerText = null;
            if (polusVent != null) UnityEngine.Object.Destroy(polusVent);
            polusVent = null;
            isWaitingTimer = true;
            startTime = DateTime.UtcNow;

            timer = CustomOptionHolder.hideNSeekTimer.getFloat() * 60;
            hunterVision = CustomOptionHolder.hideNSeekHunterVision.getFloat();
            huntedVision = CustomOptionHolder.hideNSeekHuntedVision.getFloat();
            taskWinPossible = CustomOptionHolder.hideNSeekTaskWin.getBool();
            taskPunish = CustomOptionHolder.hideNSeekTaskPunish.getFloat();
            impNumber = Mathf.RoundToInt(CustomOptionHolder.hideNSeekHunterCount.getFloat());
            canSabotage = CustomOptionHolder.hideNSeekCanSabotage.getBool();
            killCooldown = CustomOptionHolder.hideNSeekKillCooldown.getFloat();
            hunterWaitingTime = CustomOptionHolder.hideNSeekHunterWaiting.getFloat();

            botCount = Mathf.RoundToInt(CustomOptionHolder.hideNSeekBotCount.getFloat());
            botPenalty = CustomOptionHolder.hideNSeekBotPenalty.getFloat();
            disguiseCooldown = CustomOptionHolder.hideNSeekDisguiseCooldown.getFloat();

            applyBlackScreen(false);
            HiderDisguise.Invalidate();
            HiderDisguise.RestoreAll();
            HiderInvisibility.clearAndReload();
            HideNSeekBots.Clear();

            HudManagerStartPatch.hiderInvisibilityUses = Mathf.RoundToInt(CustomOptionHolder.hideNSeekInvisCount.getFloat());

            Hunter.clearAndReload();
            Hunted.clearAndReload();
        }
    }

    public static class Hunter {
        public static List<Arrow> localArrows = new();
        public static List<byte> lightActive = new();
        public static bool arrowActive = false;
        public static Dictionary<byte, int> playerKillCountMap = new();

        public static float lightCooldown = 30f;
        public static float lightDuration = 5f;
        public static float lightVision = 2f;
        public static float lightPunish = 5f;
        public static float AdminCooldown = 30f;
        public static float AdminDuration = 5f;
        public static float AdminPunish = 5f;
        public static float ArrowCooldown = 30f;
        public static float ArrowDuration = 5f;
        public static float ArrowPunish = 5f;
        private static Sprite buttonSpriteLight;
        private static Sprite buttonSpriteArrow;

        public static bool isLightActive (byte playerId) {
            return lightActive.Contains(playerId);
        }

        public static Sprite getArrowSprite() {
            if (buttonSpriteArrow) return buttonSpriteArrow;
            buttonSpriteArrow = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.HideNSeekArrowButton.png", 115f);
            return buttonSpriteArrow;
        }

        public static Sprite getLightSprite() {
            if (buttonSpriteLight) return buttonSpriteLight;
            buttonSpriteLight = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.LighterButton.png", 115f);
            return buttonSpriteLight;
        }

        public static void clearAndReload() {
            if (localArrows != null) {
                foreach (Arrow arrow in localArrows)
                    if (arrow?.arrow != null)
                        UnityEngine.Object.Destroy(arrow.arrow);
            }
            localArrows = new List<Arrow>();
            lightActive = new List<byte>();
            arrowActive = false;

            lightCooldown = CustomOptionHolder.hunterLightCooldown.getFloat();
            lightDuration = CustomOptionHolder.hunterLightDuration.getFloat();
            lightVision = CustomOptionHolder.hunterLightVision.getFloat();
            lightPunish = CustomOptionHolder.hunterLightPunish.getFloat();
            AdminCooldown = CustomOptionHolder.hunterAdminCooldown.getFloat();
            AdminDuration = CustomOptionHolder.hunterAdminDuration.getFloat();
            AdminPunish = CustomOptionHolder.hunterAdminPunish.getFloat();
            ArrowCooldown = CustomOptionHolder.hunterArrowCooldown.getFloat();
            ArrowDuration = CustomOptionHolder.hunterArrowDuration.getFloat();
            ArrowPunish = CustomOptionHolder.hunterArrowPunish.getFloat();
        }
    }

    public static class Hunted {
        public static List<byte> timeshieldActive = new();
        public static int shieldCount = 3;

        public static float shieldCooldown = 30f;
        public static float shieldDuration = 5f;
        public static float shieldRewindTime = 3f;
        public static bool taskPunish = false;
        public static void clearAndReload() {
            timeshieldActive = new List<byte>();
            taskPunish = false;

            shieldCount = Mathf.RoundToInt(CustomOptionHolder.huntedShieldNumber.getFloat());
            shieldCooldown = CustomOptionHolder.huntedShieldCooldown.getFloat();
            shieldDuration = CustomOptionHolder.huntedShieldDuration.getFloat();
            shieldRewindTime = CustomOptionHolder.huntedShieldRewindTime.getFloat();
        }
    }
}
