using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TheOtherRoles.Modules;
using TheOtherRoles.Objects;
using TheOtherRoles.Utilities;
using UnityEngine;
using static TheOtherRoles.Patches.PlayerControlFixedUpdatePatch;
using static TheOtherRoles.TheOtherRoles;

namespace TheOtherRoles.Roles
{
    [TORRPCHolder]
    public class PlayerRole : RoleBase<PlayerRole>
    {
        public static Color color = new Color32(0, 200, 0, byte.MaxValue);

        public static float baseKillCooldown = 45f;
        public static float cooldownReducePerLevel = 5f;
        public static float minKillCooldown = 10f;
        public static int expPerTask = 1;
        public static int expPerKill = 3;
        public static int expPerLevel = 5;
        public static int numCommonTasks = 1;
        public static int numShortTasks = 3;
        public static int numLongTasks = 2;

        public int exp = 0;
        public int level = 0;
        public PlayerControl currentTarget;
        public static TMPro.TMP_Text statusText;

        public PlayerRole()
        {
            RoleId = roleId = RoleId.PlayerRole;
            exp = 0;
            level = 0;
            currentTarget = null;
        }

        public float currentCooldown => getCooldown(level);

        public static float getCooldown(int level) => Mathf.Max(minKillCooldown, baseKillCooldown - level * cooldownReducePerLevel);

        public static float getCooldownOf(PlayerControl pc)
        {
            var role = getRole(pc);
            return role != null ? getCooldown(role.level) : baseKillCooldown;
        }

        public override void PostInit()
        {
            if (PlayerControl.LocalPlayer != player) return;
            if (statusText != null) return;

            var hudManager = HudManager.Instance;
            if (hudManager == null || hudManager.roomTracker == null) return;

            GameObject textObj = UnityEngine.Object.Instantiate(hudManager.roomTracker.gameObject);
            textObj.transform.SetParent(hudManager.transform);
            textObj.SetActive(true);
            UnityEngine.Object.DestroyImmediate(textObj.GetComponent<RoomTracker>());
            statusText = textObj.GetComponent<TMPro.TMP_Text>();
            statusText.transform.localPosition = new Vector3(0f, 2.2f, -10f);
            statusText.fontSize = 1.2f;
            statusText.fontSizeMin = 1.0f;
            statusText.fontSizeMax = 1.5f;
            statusText.alignment = TMPro.TextAlignmentOptions.Center;
            statusText.enableWordWrapping = false;
            statusText.rectTransform.sizeDelta = new Vector2(4f, 0.5f);
            updateStatusText();
        }

        public override void FixedUpdate()
        {
            if (player != PlayerControl.LocalPlayer) return;

            if (!player.Data.IsDead && !MeetingHud.Instance && !ExileController.Instance)
            {
                currentTarget = setTarget();
                setPlayerOutline(currentTarget, color);
            }

            updateStatusText();
        }

        public override void OnKill(PlayerControl target)
        {
            if (target == null || target == player) return;
            addExp(player, expPerKill);
        }

        public override void OnFinishShipStatusBegin()
        {
            if (player == null) return;
            player.generateAndAssignTasks(numCommonTasks, numShortTasks, numLongTasks);
        }

        public override void OnDeath(PlayerControl killer = null) => destroyUi();

        public override void ResetRole(bool isShifted) => destroyUi();

        private void updateStatusText()
        {
            if (statusText == null) return;
            statusText.text = ModTranslation.getString("playerRoleStatus") + "  Lv." + level + "  " + exp + "/" + expPerLevel
                + "  " + ModTranslation.getString("playerRoleCooldownShort") + " " + Mathf.RoundToInt(currentCooldown) + "s";
            statusText.color = color;
        }

        public static void onTaskComplete(PlayerControl pc)
        {
            if (pc == null || !pc.isRole(RoleId.PlayerRole)) return;
            addExp(pc, expPerTask);
        }

        public static void addExp(PlayerControl pc, int amount)
        {
            var role = getRole(pc);
            if (role == null || amount <= 0) return;

            role.exp += amount;
            int gainedLevels = 0;
            while (expPerLevel > 0 && role.exp >= expPerLevel)
            {
                role.exp -= expPerLevel;
                role.level++;
                gainedLevels++;
            }
            if (gainedLevels == 0) return;
            if (role.player != PlayerControl.LocalPlayer) return;

            new CustomMessage(ModTranslation.getString("playerRoleLevelUp") + "  Lv." + role.level, 3f);
            SoundEffectsManager.play("select");
            if (HudManagerStartPatch.playerRoleKillButton != null)
                HudManagerStartPatch.playerRoleKillButton.MaxTimer = role.currentCooldown;
        }

        private static void destroyUi()
        {
            if (statusText != null) { UnityEngine.Object.Destroy(statusText.gameObject); statusText = null; }
        }

        static public IEnumerable<DocumentReplacement> GetReplacementPart()
        {
            yield return new("%TASKEXP%", expPerTask.ToString());
            yield return new("%KILLEXP%", expPerKill.ToString());
            yield return new("%LEVELUP%", expPerLevel.ToString());
            yield return new("%REDUCE%", Mathf.RoundToInt(cooldownReducePerLevel).ToString());
            yield return new("%CD%", Mathf.RoundToInt(baseKillCooldown).ToString());
            yield return new("%MIN%", Mathf.RoundToInt(minKillCooldown).ToString());
        }

        public static void clearAndReload()
        {
            baseKillCooldown = CustomOptionHolder.playerRoleKillCooldown.getFloat();
            cooldownReducePerLevel = CustomOptionHolder.playerRoleCooldownReduce.getFloat();
            minKillCooldown = CustomOptionHolder.playerRoleMinKillCooldown.getFloat();
            expPerTask = Mathf.RoundToInt(CustomOptionHolder.playerRoleExpPerTask.getFloat());
            expPerKill = Mathf.RoundToInt(CustomOptionHolder.playerRoleExpPerKill.getFloat());
            expPerLevel = Mathf.Max(1, Mathf.RoundToInt(CustomOptionHolder.playerRoleExpPerLevel.getFloat()));
            numCommonTasks = Mathf.RoundToInt(CustomOptionHolder.playerRoleCommonTasks.getFloat());
            numShortTasks = Mathf.RoundToInt(CustomOptionHolder.playerRoleShortTasks.getFloat());
            numLongTasks = Mathf.RoundToInt(CustomOptionHolder.playerRoleLongTasks.getFloat());
            players = [];
        }
    }

    [HarmonyPatch(typeof(GameData), nameof(GameData.CompleteTask))]
    public static class PlayerRoleTaskCompletePatch
    {
        public static void Postfix([HarmonyArgument(0)] PlayerControl pc, [HarmonyArgument(1)] uint taskId)
            => PlayerRole.onTaskComplete(pc);
    }
}
