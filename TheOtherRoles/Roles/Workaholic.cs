using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Hazel;
using TheOtherRoles.Modules;
using TheOtherRoles.Objects;
using TheOtherRoles.Patches;
using TheOtherRoles.Utilities;
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
        public static float shieldDuration = 5f;
        public static int maxShieldCount = 1;

        private static Sprite getTaskButtonSprite;
        private static Sprite protectSelfButtonSprite;
        private static MethodInfo turnOnProtectionMethod;
        private static MethodInfo removeProtectionMethod;
        private static bool warnedMissingShieldApi;

        public float countdownTimer = 0f;
        public float shieldTimer = 0f;
        public int shieldCount = 0;
        public int tasksCompleted = 0;
        public int tasksTotal = 0;
        public float getTaskCooldown = 0f;
        public bool shieldVisualActive = false;
        private int lastShownSecond = int.MinValue;
        private int lastShownShieldSecond = int.MinValue;
        private int lastShownShieldCount = int.MinValue;
        private int lastShownTasksCompleted = int.MinValue;
        private int lastShownTasksTotal = int.MinValue;

        public static CustomButton getTaskButton;
        public static CustomButton protectSelfButton;
        public static TMPro.TMP_Text statusText;

        public Workaholic()
        {
            RoleId = roleId = RoleId.Workaholic;
            countdownTimer = deathCountdownTime;
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

        public static RemoteProcess<byte> ActivateShield = RemotePrimitiveProcess.OfByte("WorkaholicActivateShield", (playerId, _) =>
        {
            var pc = Helpers.playerById(playerId);
            var workaholic = getRole(pc);
            if (workaholic == null) return;

            workaholic.shieldTimer = shieldDuration;
            workaholic.shieldCount--;
            setShieldVisual(pc, true);
            if (pc == PlayerControl.LocalPlayer)
                SoundEffectsManager.play("medicShield");
        });

        public static RemoteProcess<(byte killerId, byte shieldedId)> BreakShield = new("WorkaholicBreakShield", (message, _) =>
        {
            var shielded = Helpers.playerById(message.shieldedId);
            var workaholic = getRole(shielded);
            if (workaholic != null)
            {
                workaholic.shieldTimer = 0f;
                setShieldVisual(shielded, false);
            }

            bool localShielded = shielded != null && shielded == PlayerControl.LocalPlayer;
            if (localShielded)
            {
                Helpers.flashScreen(Color.yellow, 0.1f, 0.3f, 0.5f, 0.2f, ModTranslation.getString("workaholicShieldBroken"), Color.yellow);
                SoundEffectsManager.play("fail");
            }

            var killer = Helpers.playerById(message.killerId);
            if (killer == null) return;

            if (!localShielded && killer == PlayerControl.LocalPlayer)
                Helpers.flashScreen(Color.yellow, 0.1f, 0.3f, 0.5f, 0.2f, ModTranslation.getString("workaholicShieldBroken"), Color.yellow);

            killer.killTimer = killer.GetKillCooldown();
        });

        public static RemoteProcess<byte> ExpireShield = RemotePrimitiveProcess.OfByte("WorkaholicExpireShield", (playerId, _) =>
        {
            var pc = Helpers.playerById(playerId);
            var workaholic = getRole(pc);
            if (workaholic == null) return;

            workaholic.shieldTimer = 0f;
            setShieldVisual(pc, false);
        });

        public static RemoteProcess<byte> ResetCountdown = RemotePrimitiveProcess.OfByte("WorkaholicResetCountdown", (playerId, _) =>
        {
            var workaholic = getRole(Helpers.playerById(playerId));
            if (workaholic != null)
                workaholic.countdownTimer = deathCountdownTime;
        });

        public static RemoteProcess<byte> Suicide = RemotePrimitiveProcess.OfByte("WorkaholicSuicide", (playerId, _) =>
        {
            var pc = Helpers.playerById(playerId);
            if (pc == null) return;
            pc.MurderPlayer(pc, MurderResultFlags.Succeeded);
            GameHistory.overrideDeathReasonAndKiller(pc, DeadPlayer.CustomDeathReason.Suicide);
        });

        public override void PostInit()
        {
            if (PlayerControl.LocalPlayer != player) return;
            var hudManager = HudManager.Instance;
            if (hudManager == null) return;

            getTaskButton = new CustomButton(
                () =>
                {
                    var workaholic = getRole();
                    if (workaholic == null || workaholic.getTaskCooldown > 0f || player.Data.IsDead) return;

                    player.clearAllTasks();
                    assignRandomTask(player);
                    workaholic.tasksCompleted = 0;
                    workaholic.tasksTotal = 1;
                    workaholic.countdownTimer = deathCountdownTime;
                    workaholic.getTaskCooldown = getTaskCooldownTime;
                    new CustomMessage(ModTranslation.getString("workaholicNewTask"), 3f);
                    SoundEffectsManager.play("medicShield");
                },
                () => PlayerControl.LocalPlayer.isRole(RoleId.Workaholic) && !player.Data.IsDead,
                () => { var workaholic = getRole(); return workaholic != null && workaholic.getTaskCooldown <= 0f && !player.Data.IsDead && player.CanMove; },
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
                    var workaholic = getRole();
                    if (workaholic == null || workaholic.shieldCount <= 0 || workaholic.shieldTimer > 0f || player.Data.IsDead) return;
                    ActivateShield.Invoke(player.PlayerId);
                },
                () => PlayerControl.LocalPlayer.isRole(RoleId.Workaholic) && !player.Data.IsDead,
                () => { var workaholic = getRole(); return workaholic != null && workaholic.shieldCount > 0 && workaholic.shieldTimer <= 0f && !player.Data.IsDead && player.CanMove; },
                () => { },
                getProtectSelfButtonSprite(),
                CustomButton.ButtonPositions.lowerRowCenter,
                hudManager,
                KeyCode.G,
                buttonText: ModTranslation.getString("workaholicShieldText"),
                abilityTexture: CustomButton.ButtonLabelType.UseButton
            );

            if (statusText == null && hudManager.roomTracker != null)
            {
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
            }
        }

        public override void FixedUpdate()
        {
            if (player != PlayerControl.LocalPlayer) return;
            if (player.Data == null || player.Data.IsDead || MeetingHud.Instance) return;

            if (getTaskCooldown > 0f)
            {
                getTaskCooldown -= Time.deltaTime;
                if (getTaskCooldown < 0f) getTaskCooldown = 0f;
            }

            if (shieldTimer > 0f)
            {
                shieldTimer -= Time.deltaTime;
                if (shieldTimer <= 0f)
                {
                    shieldTimer = 0f;
                    ExpireShield.Invoke(player.PlayerId);
                }
            }

            if (countdownTimer > 0f)
            {
                countdownTimer -= Time.deltaTime;
                if (countdownTimer <= 0f)
                {
                    countdownTimer = 0f;
                    updateStatusText();
                    Suicide.Invoke(player.PlayerId);
                    return;
                }
            }

            updateStatusText();
        }

        private void updateStatusText()
        {
            if (statusText == null) return;

            int seconds = Mathf.CeilToInt(countdownTimer);
            int shieldSeconds = Mathf.CeilToInt(shieldTimer);
            if (seconds == lastShownSecond && shieldSeconds == lastShownShieldSecond && shieldCount == lastShownShieldCount
                && tasksCompleted == lastShownTasksCompleted && tasksTotal == lastShownTasksTotal) return;
            lastShownSecond = seconds;
            lastShownShieldSecond = shieldSeconds;
            lastShownShieldCount = shieldCount;
            lastShownTasksCompleted = tasksCompleted;
            lastShownTasksTotal = tasksTotal;

            string text = ModTranslation.getString("workaholicCountdown") + " " + seconds + " " + ModTranslation.getString("workaholicCountdownSuffix")
                + "   " + tasksCompleted + "/" + tasksTotal
                + "   " + ModTranslation.getString("workaholicShields") + " " + shieldCount;
            Color textColor;

            if (shieldTimer > 0f)
            {
                text += "   " + ModTranslation.getString("workaholicShieldText") + " " + shieldSeconds + "s";
                textColor = Color.cyan;
            }
            else
            {
                textColor = seconds <= 10 ? Color.red : Color.white;
            }

            statusText.text = text;
            statusText.color = textColor;
        }

        public override void OnMeetingStart() { }

        public override void OnMeetingEnd(PlayerControl exiled = null)
        {
            countdownTimer = deathCountdownTime;
            getTaskCooldown = 0f;
        }

        public override void OnDeath(PlayerControl killer = null)
        {
            shieldTimer = 0f;
            setShieldVisual(player, false);
            destroyUi();
        }

        public override void ResetRole(bool isShifted)
        {
            shieldTimer = 0f;
            setShieldVisual(player, false);
            destroyUi();
        }

        public override void OnFinishShipStatusBegin()
        {
            if (PlayerControl.LocalPlayer != player) return;
            player.clearAllTasks();
            assignRandomTask(player);
            countdownTimer = deathCountdownTime;
            tasksCompleted = 0;
            tasksTotal = 1;
            shieldCount = 0;
            shieldTimer = 0f;
            getTaskCooldown = 0f;
        }

        public static void onTaskComplete(PlayerControl pc)
        {
            var workaholic = getRole(pc);
            if (workaholic == null) return;

            workaholic.tasksCompleted++;
            if (workaholic.shieldCount < maxShieldCount && workaholic.shieldTimer <= 0f)
                workaholic.shieldCount++;

            if (workaholic.tasksTotal <= 0 || workaholic.tasksCompleted < workaholic.tasksTotal) return;

            workaholic.tasksCompleted = 0;
            workaholic.countdownTimer = deathCountdownTime;
            ResetCountdown.Invoke(pc.PlayerId);

            if (pc != PlayerControl.LocalPlayer) return;
            pc.clearAllTasks();
            assignRandomTask(pc);
            new CustomMessage(ModTranslation.getString("workaholicTaskComplete"), 3f);
            SoundEffectsManager.play("select");
        }

        public static bool isShielded(PlayerControl target)
        {
            return players.Any(x => x.player == target && x.shieldTimer > 0f);
        }

        private static void assignRandomTask(PlayerControl player)
        {
            if (player == null || ShipStatus.Instance == null) return;

            var tasks = new Il2CppSystem.Collections.Generic.List<byte>();
            var hashSet = new Il2CppSystem.Collections.Generic.HashSet<TaskTypes>();
            var taskTypeIds = new System.Collections.Generic.List<byte>();

            var allTasks = new System.Collections.Generic.List<NormalPlayerTask>();
            foreach (var task in MapUtilities.CachedShipStatus.CommonTasks) allTasks.Add(task);
            foreach (var task in MapUtilities.CachedShipStatus.ShortTasks) allTasks.Add(task);
            foreach (var task in MapUtilities.CachedShipStatus.LongTasks) allTasks.Add(task);
            allTasks.Shuffle();

            var il2CppTasks = new Il2CppSystem.Collections.Generic.List<NormalPlayerTask>();
            foreach (var t in allTasks) il2CppTasks.Add(t);

            int start = 0;
            MapUtilities.CachedShipStatus.AddTasksFromList(ref start, Mathf.Min(1, il2CppTasks.Count), tasks, hashSet, il2CppTasks);
            taskTypeIds.AddRange(tasks.ToArray());

            MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(player.NetId, (byte)CustomRPC.UncheckedSetTasks, Hazel.SendOption.Reliable, -1);
            writer.Write(player.PlayerId);
            writer.WriteBytesAndSize(taskTypeIds.ToArray());
            AmongUsClient.Instance.FinishRpcImmediately(writer);
            RPCProcedure.uncheckedSetTasks(player.PlayerId, taskTypeIds.ToArray());
        }

        private static void setShieldVisual(PlayerControl pc, bool active)
        {
            if (pc == null || pc.Data == null) return;

            var workaholic = getRole(pc);
            if (workaholic == null) return;
            if (!active && !workaholic.shieldVisualActive) return;

            try
            {
                if (active)
                {
                    turnOnProtectionMethod ??= typeof(PlayerControl).GetMethod("TurnOnProtection", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (turnOnProtectionMethod == null) { warnMissingShieldApi(); return; }
                    turnOnProtectionMethod.Invoke(pc, new object[] { true, pc.Data.DefaultOutfit.ColorId, (int)pc.PlayerId });
                }
                else
                {
                    removeProtectionMethod ??= typeof(PlayerControl).GetMethod("RemoveProtection", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (removeProtectionMethod == null) { warnMissingShieldApi(); return; }
                    removeProtectionMethod.Invoke(pc, null);
                }
                workaholic.shieldVisualActive = active;
            }
            catch (Exception e)
            {
                TheOtherRolesPlugin.Logger.LogWarning("Workaholic shield visual failed: " + e.Message);
            }
        }

        private static void warnMissingShieldApi()
        {
            if (warnedMissingShieldApi) return;
            warnedMissingShieldApi = true;
            TheOtherRolesPlugin.Logger.LogWarning("Workaholic: vanilla shield API not found, shield stays invisible");
        }

        private static void destroyUi()
        {
            if (statusText != null) { UnityEngine.Object.Destroy(statusText.gameObject); statusText = null; }
            if (getTaskButton != null) { UnityEngine.Object.Destroy(getTaskButton.actionButtonGameObject); getTaskButton = null; }
            if (protectSelfButton != null) { UnityEngine.Object.Destroy(protectSelfButton.actionButtonGameObject); protectSelfButton = null; }
        }

        public static void clearAndReload()
        {
            deathCountdownTime = CustomOptionHolder.workaholicDeathCountdown.getFloat();
            getTaskCooldownTime = CustomOptionHolder.workaholicGetTaskCooldown.getFloat();
            shieldDuration = CustomOptionHolder.workaholicShieldDuration.getFloat();
            maxShieldCount = Mathf.RoundToInt(CustomOptionHolder.workaholicMaxShieldCount.getFloat());

            foreach (var workaholic in players)
                setShieldVisual(workaholic.player, false);

            destroyUi();
            players = [];
        }

        static public IEnumerable<HelpSprite> GetHelpSprites()
        {
            yield return new(getGetTaskButtonSprite(), "workaholicGetTaskHint");
            yield return new(getProtectSelfButtonSprite(), "workaholicShieldHint");
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
            if (pc == null) return;
            var workaholic = Workaholic.getRole(pc);
            if (workaholic == null) return;
            workaholic.tasksTotal = taskTypeIds.Length;
        }
    }
}
