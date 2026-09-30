using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using HarmonyLib;
using Hazel;
using TheOtherRoles.MetaContext;
using TheOtherRoles.Modules;
using TheOtherRoles.Objects;
using UnityEngine;
using static TheOtherRoles.TheOtherRoles;

namespace TheOtherRoles.Roles
{
    [TORRPCHolder]
    public class Ambusher : RoleBase<Ambusher>
    {
        public enum TrapKind : byte
        {
            None = 0,
            Task = 1,
            Vent = 2,
            Report = 3,
            Kill = 4,
            Ability = 5,
            Console = 6,
        }

        public static Color color = Palette.ImpostorRed;

        public static float cooldown = 30f;

        private static readonly SystemTypes[] RepairSystems =
        {
            SystemTypes.Electrical, SystemTypes.Reactor, SystemTypes.Laboratory,
            SystemTypes.Comms, SystemTypes.LifeSupp,
        };

        public static TrapKind armedTrap = TrapKind.None;
        public static byte armedBy = byte.MaxValue;

        private static MetaScreen trapScreen;
        private static Sprite buttonSprite;

        public Ambusher()
        {
            RoleId = roleId = RoleId.Ambusher;
        }

        public static Sprite getButtonSprite()
        {
            if (buttonSprite) return buttonSprite;
            buttonSprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.TrapperButton.png", 115f);
            return buttonSprite;
        }

        public static bool isArmed => armedTrap != TrapKind.None;

        public static bool trapScreenOpen => trapScreen != null;

        public static RemoteProcess<(byte kind, byte ambusherId)> ArmTrap = new("AmbusherArm", (message, __) =>
        {
            armedTrap = (TrapKind)message.kind;
            armedBy = message.ambusherId;

            TheOtherRolesPlugin.Logger.LogMessage($"[Ambusher] armed kind={armedTrap} by={armedBy}");
            new CustomMessage(ModTranslation.getString("ambusherTrapSet"), 3f);
        });

        public static RemoteProcess<(byte victimId, byte kind)> TriggerTrap = new("AmbusherTrigger", (message, __) =>
        {
            ClearTrap();

            var victim = Helpers.playerById(message.victimId);
            if (victim == null || victim.Data == null || victim.Data.IsDead) return;
            victim.MurderPlayer(victim, MurderResultFlags.Succeeded);

            if (!PlayerControl.LocalPlayer.isRole(RoleId.Ambusher)) return;
            _ = new StaticAchievementToken("ambusher.common1");

            if ((TrapKind)message.kind != TrapKind.Kill) return;
            _ = new StaticAchievementToken("ambusher.another1");
            if (Helpers.isKiller(victim)) _ = new StaticAchievementToken("ambusher.challenge");
        });

        public static void ClearTrap()
        {
            armedTrap = TrapKind.None;
            armedBy = byte.MaxValue;
        }

        public static void arm(TrapKind kind)
        {
            var ambusher = local;
            TheOtherRolesPlugin.Logger.LogMessage($"[Ambusher] arm attempt kind={kind} hasRole={ambusher != null} armed={isArmed}");
            if (ambusher == null || ambusher.player != PlayerControl.LocalPlayer) return;
            if (isArmed || MeetingHud.Instance || ExileController.Instance) return;

            ArmTrap.Invoke(((byte)kind, ambusher.player.PlayerId));
        }

        public static bool canTrigger(PlayerControl victim, TrapKind kind)
        {
            if (armedTrap == TrapKind.None) return false;
            if (victim == null || victim.Data == null || victim.Data.IsDead) return false;
            if (armedTrap != kind || victim.PlayerId == armedBy) return false;

            TheOtherRolesPlugin.Logger.LogMessage($"[Ambusher] triggered kind={kind} victim={victim.PlayerId}");
            TriggerTrap.Invoke((victim.PlayerId, (byte)kind));
            return true;
        }

        public static void OpenTrapScreen()
        {
            var hud = HudManager.Instance;
            if (hud == null) return;

            var gui = TORGUIContextEngine.API;
            var attr = gui.GetAttribute(AttributeAsset.CenteredBoldFixed);

            var rows = new List<GUIContext>();
            rows.Add(gui.RawButton(GUIAlignment.Center, attr, ModTranslation.getString("ambusherTrapTask"), () => Pick(TrapKind.Task)));
            rows.Add(gui.RawButton(GUIAlignment.Center, attr, ModTranslation.getString("ambusherTrapVent"), () => Pick(TrapKind.Vent)));
            rows.Add(gui.RawButton(GUIAlignment.Center, attr, ModTranslation.getString("ambusherTrapReport"), () => Pick(TrapKind.Report)));
            rows.Add(gui.RawButton(GUIAlignment.Center, attr, ModTranslation.getString("ambusherTrapKill"), () => Pick(TrapKind.Kill)));
            rows.Add(gui.RawButton(GUIAlignment.Center, attr, ModTranslation.getString("ambusherTrapAbility"), () => Pick(TrapKind.Ability)));
            rows.Add(gui.RawButton(GUIAlignment.Center, attr, ModTranslation.getString("ambusherTrapConsole"), () => Pick(TrapKind.Console)));

            CloseTrapScreen();

            var scroll = gui.ScrollView(GUIAlignment.Center, new Size(4.6f, 3.4f), "ambusherTraps",
                gui.VerticalHolder(GUIAlignment.Center, rows), out _);

            trapScreen = MetaScreen.GenerateWindow(new Vector2(5.2f, 4.2f), hud.transform, Vector3.zero, true, true, true);
            trapScreen.SetContext(scroll, out _);
        }

        public static void CloseTrapScreen()
        {
            if (trapScreen == null) return;
            trapScreen.CloseScreen();
            trapScreen = null;
        }

        private static void Pick(TrapKind kind)
        {
            CloseTrapScreen();
            arm(kind);

            var button = HudManagerStartPatch.ambusherButton;
            if (button != null) button.Timer = button.MaxTimer;
        }

        public override void OnMeetingStart()
        {
            CloseTrapScreen();
            ClearTrap();
        }

        static public IEnumerable<DocumentReplacement> GetReplacementPart()
        {
            yield return new("%CD%", Mathf.RoundToInt(cooldown).ToString());
        }

        public static void clearAndReload()
        {
            cooldown = CustomOptionHolder.ambusherCooldown.getFloat();
            CloseTrapScreen();
            ClearTrap();
            players = [];
        }

        [HarmonyPatch(typeof(GameData), nameof(GameData.CompleteTask))]
        public static class AmbusherTaskPatch
        {
            public static void Postfix([HarmonyArgument(0)] PlayerControl player)
            {
                if (player == null || !player.AmOwner) return;
                canTrigger(player, TrapKind.Task);
            }
        }

        [HarmonyPatch(typeof(PlayerPhysics), nameof(PlayerPhysics.RpcEnterVent))]
        public static class AmbusherVentPatch
        {
            public static void Postfix(PlayerPhysics __instance)
            {
                var player = __instance.myPlayer;
                if (player == null || !player.AmOwner) return;
                canTrigger(player, TrapKind.Vent);
            }
        }

        [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.ReportDeadBody))]
        public static class AmbusherReportPatch
        {
            public static void Prefix(PlayerControl __instance)
            {
                if (__instance == null || !__instance.AmOwner) return;
                canTrigger(__instance, TrapKind.Report);
            }
        }

        [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.MurderPlayer))]
        public static class AmbusherKillPatch
        {
            public static void Postfix(PlayerControl __instance)
            {
                if (__instance == null || !__instance.AmOwner) return;
                canTrigger(__instance, TrapKind.Kill);
            }
        }

        [HarmonyPatch(typeof(CustomButton), nameof(CustomButton.onClickEvent))]
        public static class AmbusherAbilityPatch
        {
            public static void Postfix()
            {
                var local = PlayerControl.LocalPlayer;
                if (local == null) return;
                canTrigger(local, TrapKind.Ability);
            }
        }

        [HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.UpdateSystem), new[] { typeof(SystemTypes), typeof(PlayerControl), typeof(byte) })]
        public static class AmbusherConsolePatch
        {
            public static void Postfix([HarmonyArgument(0)] SystemTypes systemType, [HarmonyArgument(1)] PlayerControl player)
            {
                if (player == null || !player.AmOwner) return;
                if (RepairSystems.Contains(systemType)) return;
                canTrigger(player, TrapKind.Console);
            }
        }
    }
}
