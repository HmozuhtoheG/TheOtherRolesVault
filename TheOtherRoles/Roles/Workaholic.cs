using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TheOtherRoles.Modules;
using TheOtherRoles.Objects;
using TMPro;
using UnityEngine;
using static TheOtherRoles.TheOtherRoles;

namespace TheOtherRoles.Roles
{
    [TORRPCHolder]
    public class Workaholic : RoleBase<Workaholic>
    {
        public static Color color = new Color32(255, 165, 0, byte.MaxValue);

        public static float deathCountdownTime = 60f;
        public static float getTaskCooldownTime = 180f;

        private static Sprite getTaskButtonSprite;
        private static Sprite protectSelfButtonSprite;

        public float countdownTimer = 0f;
        public float shieldTimer = 0f;
        public int shieldCount = 0;
        public int tasksCompleted = 0;
        public int tasksTotal = 0;
        public float getTaskCooldown = 0f;

        public static CustomButton getTaskButton;
        public static CustomButton protectSelfButton;
        public static TMPro.TMP_Text countdownText;

        public Workaholic()
        {
            RoleId = roleId = RoleId.Workaholic;
            countdownTimer = deathCountdownTime;
            shieldTimer = 0f;
            shieldCount = 0;
            tasksCompleted = 0;
            tasksTotal = 0;
            getTaskCooldown = 0f;
        }

        public static Sprite getGetTaskButtonSprite()
        {
            if (getTaskButtonSprite) return getTaskButtonSprite;
            getTaskButtonSprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.GetTask.png", 115f);
            return getTaskButtonSprite;
        }

        public static Sprite getProtectSelfButtonSprite()
        {
            if (protectSelfButtonSprite) return protectSelfButtonSprite;
            protectSelfButtonSprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.ProtectSelf.png", 115f);
            return protectSelfButtonSprite;
        }

        public static RemoteProcess<byte> ActivateShield = RemotePrimitiveProcess.OfByte("WorkaholicActivateShield", (message, _) =>
        {
            var pc = Helpers.playerById(message);
            var workaholic = getRole(pc);
            if (workaholic == null) return;
            workaholic.shieldTimer = 5f;
            if (workaholic.player == PlayerControl.LocalPlayer)
                SoundEffectsManager.play("medicShield");
        });

        public static RemoteProcess<byte> BreakShield = RemotePrimitiveProcess.OfByte("WorkaholicBreakShield", (killerId, _) =>
        {
            var workaholic = getRole();
            if (workaholic != null && workaholic.player != null && workaholic.player == PlayerControl.LocalPlayer)
            {
                Helpers.flashScreen(Color.yellow, 0.1f, 0.3f, 0.5f, 0.2f, ModTranslation.getString("workaholicShieldBroken"), Color.yellow);
                SoundEffectsManager.play("fail");
            }
            var killer = Helpers.playerById(killerId);
            if (killer != null && killer == PlayerControl.LocalPlayer)
                Helpers.flashScreen(Color.yellow, 0.1f, 0.3f, 0.5f, 0.2f, ModTranslation.getString("workaholicShieldBroken"), Color.yellow);
            if (killer != null)
                killer.killTimer = killer.GetKillCooldown();
        });

        public static RemoteProcess<byte> ResetCountdown = RemotePrimitiveProcess.OfByte("WorkaholicResetCountdown", (playerId, _) =>
        {
            var pc = Helpers.playerById(playerId);
            var workaholic = getRole(pc);
            if (workaholic != null)
                workaholic.countdownTimer = deathCountdownTime;
        });

        public override void PostInit()
        {
            if (PlayerControl.LocalPlayer != player) return;
            var hudManager = HudManager.Instance;

            getTaskButton = new CustomButton(
                () =>
                {
                    var work = getRole();
                    if (work != null && work.getTaskCooldown <= 0f && !player.Data.IsDead)
                    {
                        player.clearAllTasks();
                        player.generateNormalTasks();
                        work.tasksCompleted = 0;
                        work.countdownTimer = deathCountdownTime;
                        work.getTaskCooldown = getTaskCooldownTime;
                        new CustomMessage(ModTranslation.getString("workaholicNewTask"), 3f);
                        SoundEffectsManager.play("medicShield");
                    }
                },
                () => PlayerControl.LocalPlayer.isRole(RoleId.Workaholic) && !player.Data.IsDead,
                () => { var w = getRole(); return w != null && w.getTaskCooldown <= 0f && !player.Data.IsDead && player.CanMove; },
                () => { },
                getGetTaskButtonSprite(),
                CustomButton.ButtonPositions.lowerRowRight,
                hudManager,
                KeyCode.F,
                buttonText: ModTranslation.getString("workaholicGetTaskText"),
                abilityTexture: CustomButton.ButtonLabelType.UseButton
            );

            protectSelfButton = new CustomButton(
                () =>
                {
                    var work = getRole();
                    if (work != null && work.shieldCount > 0 && !player.Data.IsDead && work.shieldTimer <= 0f)
                    {
                        ActivateShield.Invoke(player.PlayerId);
                        work.shieldCount--;
                    }
                },
                () => PlayerControl.LocalPlayer.isRole(RoleId.Workaholic) && !player.Data.IsDead,
                () => { var w = getRole(); return w != null && w.shieldCount > 0 && w.shieldTimer <= 0f && !player.Data.IsDead && player.CanMove; },
                () => { },
                getProtectSelfButtonSprite(),
                CustomButton.ButtonPositions.lowerRowCenter,
                hudManager,
                KeyCode.G,
                buttonText: ModTranslation.getString("workaholicShieldText"),
                abilityTexture: CustomButton.ButtonLabelType.UseButton
            );

            if (countdownText == null)
            {
                GameObject textObj = UnityEngine.Object.Instantiate(hudManager.roomTracker.gameObject);
                textObj.transform.SetParent(hudManager.transform);
                textObj.SetActive(true);
                UnityEngine.Object.DestroyImmediate(textObj.GetComponent<RoomTracker>());
                countdownText = textObj.GetComponent<TMPro.TMP_Text>();
                countdownText.transform.localPosition = new Vector3(0f, 2.2f, -10f);
                countdownText.fontSize = 1.2f;
                countdownText.fontSizeMin = 1.0f;
                countdownText.fontSizeMax = 1.5f;
                countdownText.alignment = TMPro.TextAlignmentOptions.Center;
                countdownText.enableWordWrapping = false;
                countdownText.rectTransform.sizeDelta = new Vector2(4f, 0.5f);
            }
        }

        public override void FixedUpdate()
        {
            if (player != PlayerControl.LocalPlayer) return;
            if (MeetingHud.Instance) return;

            if (countdownTimer > 0f)
            {
                countdownTimer -= Time.deltaTime;
                if (countdownText != null)
                {
                    int seconds = Mathf.CeilToInt(countdownTimer);
                    countdownText.text = ModTranslation.getString("workaholicCountdown") + " " + seconds + " " + ModTranslation.getString("workaholicCountdownSuffix");
                    countdownText.color = seconds <= 10 ? Color.red : Color.white;
                }
                if (countdownTimer <= 0f)
                {
                    player.MurderPlayer(player, MurderResultFlags.Succeeded);
                    GameHistory.overrideDeathReasonAndKiller(player, DeadPlayer.CustomDeathReason.Suicide);
                    return;
                }
            }
            else
            {
                if (countdownText != null)
                {
                    countdownText.text = ModTranslation.getString("workaholicCountdown") + " " + tasksCompleted + "/" + tasksTotal;
                    countdownText.color = tasksCompleted >= tasksTotal ? Color.green : Color.white;
                }
            }

            if (shieldTimer > 0f)
            {
                shieldTimer -= Time.deltaTime;
                if (shieldTimer <= 0f) shieldTimer = 0f;
                if (countdownText != null)
                {
                    int shieldSeconds = Mathf.CeilToInt(shieldTimer);
                    countdownText.text = ModTranslation.getString("workaholicShieldText") + " " + shieldSeconds + "s";
                    countdownText.color = Color.cyan;
                }
            }

            if (getTaskCooldown > 0f)
            {
                getTaskCooldown -= Time.deltaTime;
                if (getTaskCooldown < 0f) getTaskCooldown = 0f;
            }
        }

        public override void OnMeetingStart() { }
        public override void OnMeetingEnd(PlayerControl exiled = null)
        {
            countdownTimer = deathCountdownTime;
            getTaskCooldown = 0f;
        }

        public override void OnDeath(PlayerControl killer = null)
        {
            if (countdownText != null) { UnityEngine.Object.Destroy(countdownText); countdownText = null; }
            if (getTaskButton != null) { UnityEngine.Object.Destroy(getTaskButton.actionButtonGameObject); getTaskButton = null; }
            if (protectSelfButton != null) { UnityEngine.Object.Destroy(protectSelfButton.actionButtonGameObject); protectSelfButton = null; }
        }

        public override void ResetRole(bool isShifted)
        {
            if (countdownText != null) { UnityEngine.Object.Destroy(countdownText); countdownText = null; }
            if (getTaskButton != null) { UnityEngine.Object.Destroy(getTaskButton.actionButtonGameObject); getTaskButton = null; }
            if (protectSelfButton != null) { UnityEngine.Object.Destroy(protectSelfButton.actionButtonGameObject); protectSelfButton = null; }
        }

        public override void OnFinishShipStatusBegin()
        {
            if (PlayerControl.LocalPlayer == player)
            {
                player.clearAllTasks();
                player.generateNormalTasks();
                countdownTimer = deathCountdownTime;
                tasksCompleted = 0;
                tasksTotal = 0;
                shieldCount = 0;
                shieldTimer = 0f;
                getTaskCooldown = 0f;
            }
        }

        public static void onTaskComplete(PlayerControl pc)
        {
            if (!pc.isRole(RoleId.Workaholic)) return;
            var workaholic = getRole(pc);
            if (workaholic == null) return;

            workaholic.tasksCompleted++;

            if (workaholic.tasksCompleted >= workaholic.tasksTotal && workaholic.tasksTotal > 0)
            {
                workaholic.tasksCompleted = 0;
                workaholic.tasksTotal = 0;
                workaholic.player.clearAllTasks();
                workaholic.player.generateNormalTasks();
                workaholic.countdownTimer = deathCountdownTime;
                ResetCountdown.Invoke(pc.PlayerId);
                if (workaholic.player == PlayerControl.LocalPlayer)
                {
                    new CustomMessage(ModTranslation.getString("workaholicTaskComplete"), 3f);
                    SoundEffectsManager.play("select");
                }
            }

            if (workaholic.shieldCount < 1 && workaholic.shieldTimer <= 0f)
                workaholic.shieldCount++;
        }

        public static bool isShielded(PlayerControl target)
        {
            return players.Any(x => x.player == target && x.shieldTimer > 0f);
        }

        public static void clearAndReload()
        {
            players = [];
        }
    }

    [HarmonyPatch(typeof(GameData), nameof(GameData.CompleteTask))]
    public static class WorkaholicTaskCompletePatch
    {
        public static void Postfix([HarmonyArgument(0)] PlayerControl pc, [HarmonyArgument(1)] uint taskId)
            => Workaholic.onTaskComplete(pc);
    }

    [HarmonyPatch(typeof(RPCProcedure), nameof(RPCProcedure.uncheckedSetTasks))]
    public static class WorkaholicSetTasksPatch
    {
        public static void Postfix(byte playerId, byte[] taskTypeIds)
        {
            var pc = Helpers.playerById(playerId);
            if (pc == null || !pc.isRole(RoleId.Workaholic)) return;
            var workaholic = Workaholic.getRole(pc);
            if (workaholic == null) return;
            workaholic.tasksTotal = taskTypeIds.Length;
        }
    }
}
