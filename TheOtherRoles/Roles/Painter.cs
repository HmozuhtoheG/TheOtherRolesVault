using System;
using System.Collections.Generic;
using HarmonyLib;
using TheOtherRoles.MetaContext;
using TheOtherRoles.Modules;
using TheOtherRoles.Objects;
using TheOtherRoles.Utilities;
using UnityEngine;
using static TheOtherRoles.TheOtherRoles;

namespace TheOtherRoles.Roles
{
    [TORRPCHolder]
    public class Painter : RoleBase<Painter>
    {
        public static Color color = Palette.ImpostorRed;

        public static float cooldown = 30f;
        public static int maxUses = 2;
        public static float illusionDuration = 15f;

        public int usesLeft;

        public static CustomButton paintButton;
        private static Sprite buttonSprite;
        private static MetaScreen taskScreen;

        public static readonly HashSet<int> painted = new();
        public static readonly Dictionary<byte, float> illusion = new();
        private static readonly Dictionary<byte, bool> hiddenByIllusion = new();
        public static int painterVictims;
        private static GameObject illusionOverlay;

        public Painter()
        {
            RoleId = roleId = RoleId.Painter;
            usesLeft = maxUses;
        }

        public static Sprite getButtonSprite()
        {
            if (buttonSprite) return buttonSprite;
            buttonSprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.SpellButton.png", 115f);
            return buttonSprite;
        }

        public static bool isInIllusion(PlayerControl player)
        {
            return player != null && illusion.ContainsKey(player.PlayerId);
        }

        public static RemoteProcess<(byte consoleIndex, byte painterId)> PaintTask = new("PainterPaint", (message, _) =>
        {
            painted.Add(message.consoleIndex);
        });

        public static RemoteProcess<(byte playerId, bool on)> SetIllusion = new("PainterIllusion", (message, __) =>
        {
            var player = Helpers.playerById(message.playerId);
            if (player == null) return;

            if (message.on)
            {
                illusion[player.PlayerId] = illusionDuration;

                if (Painter.local != null && Painter.local.player == PlayerControl.LocalPlayer)
                {
                    _ = new StaticAchievementToken("painter.another1");
                    painterVictims++;
                    if (painterVictims >= 2) _ = new StaticAchievementToken("painter.challenge");
                }

                if (PlayerControl.LocalPlayer != null && PlayerControl.LocalPlayer.PlayerId == player.PlayerId)
                {
                    var hud = FastDestroyableSingleton<HudManager>.Instance;
                    if (hud != null)
                    {
                        try
                        {
                            hud.StopReactorFlash();
                            hud.StopOxyFlash();
                        }
                        catch { }
                    }
                }
            }
            else
            {
                illusion.Remove(player.PlayerId);
            }
        });

        private static int ConsoleIndex(Console console)
        {
            var ship = MapUtilities.CachedShipStatus;
            if (ship == null || ship.AllConsoles == null || console == null) return -1;

            for (int i = 0; i < ship.AllConsoles.Length; i++)
            {
                if (ship.AllConsoles[i] == console) return i;
            }
            return -1;
        }

        private static string ConsoleLabel(Console console)
        {
            var translator = TranslationController.Instance;
            var tasks = new List<string>();
            if (console.TaskTypes != null)
                foreach (var taskType in console.TaskTypes) tasks.Add(translator.GetString(taskType));

            return translator.GetString(console.Room) + "  " + string.Join(",", tasks);
        }

        public static void OpenTaskScreen()
        {
            var ship = MapUtilities.CachedShipStatus;
            if (ship == null || ship.AllConsoles == null) return;

            var gui = TORGUIContextEngine.API;
            var attr = gui.GetAttribute(AttributeAsset.CenteredBoldFixed);

            var rows = new List<GUIContext>();
            for (int i = 0; i < ship.AllConsoles.Length; i++)
            {
                var console = ship.AllConsoles[i];
                if (console == null || console.TaskTypes == null || console.TaskTypes.Length == 0) continue;

                int index = i;
                rows.Add(gui.RawButton(GUIAlignment.Center, attr, ConsoleLabel(console), () => Paint(index)));
            }

            if (rows.Count == 0) return;

            CloseTaskScreen();

            var scroll = gui.ScrollView(GUIAlignment.Center, new Size(4.6f, 3.4f), "painterTasks",
                gui.VerticalHolder(GUIAlignment.Center, rows), out _);

            taskScreen = MetaScreen.GenerateWindow(new Vector2(5.2f, 4.2f), HudManager.Instance.transform, Vector3.zero, true, true, true);
            taskScreen.SetContext(scroll, out _);
        }

        private static void Paint(int consoleIndex)
        {
            var painter = local;
            if (painter == null || painter.player != PlayerControl.LocalPlayer) return;
            if (painter.usesLeft <= 0) return;

            painter.usesLeft--;

            _ = new StaticAchievementToken("painter.common1");
            PaintTask.Invoke(((byte)consoleIndex, painter.player.PlayerId));
            CloseTaskScreen();

            if (paintButton != null)
            {
                paintButton.MaxTimer = cooldown;
                paintButton.Timer = paintButton.MaxTimer;
            }
        }

        public override void PostInit()
        {
            if (PlayerControl.LocalPlayer != player) return;

            paintButton = new CustomButton(
                () => { if (taskScreen == null) OpenTaskScreen(); else CloseTaskScreen(); },
                () => PlayerControl.LocalPlayer.isRole(RoleId.Painter) && usesLeft > 0,
                () => player.CanMove && !MeetingHud.Instance && !Minigame.Instance && !ExileController.Instance && MapUtilities.CachedShipStatus != null,
                () => { paintButton.MaxTimer = cooldown; paintButton.Timer = paintButton.MaxTimer; },
                getButtonSprite(),
                CustomButton.ButtonPositions.upperRowLeft,
                HudManager.Instance,
                KeyCode.F,
                buttonText: ModTranslation.getString("painterPaintText"),
                abilityTexture: CustomButton.ButtonLabelType.UseButton
            );

            paintButton.MaxTimer = cooldown;
            paintButton.Timer = cooldown;
        }

        public override void FixedUpdate()
        {
            if (player != PlayerControl.LocalPlayer) return;
            if (MeetingHud.Instance || ExileController.Instance) CloseTaskScreen();
        }

        public override void ResetRole(bool isShifted)
        {
            if (player != PlayerControl.LocalPlayer) return;
            CloseTaskScreen();
            RestoreVision();
            destroyUi();
            painterVictims = 0;
        }

        private static void destroyUi()
        {
            if (paintButton != null)
            {
                UnityEngine.Object.Destroy(paintButton.actionButtonGameObject);
                paintButton = null;
            }
        }

        public static void CloseTaskScreen()
        {
            if (taskScreen != null)
            {
                taskScreen.CloseScreen();
                taskScreen = null;
            }
        }

        private static void UpdateIllusionVision()
        {
            var localPlayer = PlayerControl.LocalPlayer;
            if (localPlayer == null || localPlayer.Data == null)
            {
                RestoreVision();
                return;
            }

            bool inIllusion = isInIllusion(localPlayer);
            ApplyOverlay(inIllusion);

            foreach (PlayerControl target in PlayerControl.AllPlayerControls)
            {
                if (target == null || target.Data == null || target == localPlayer) continue;
                SetVisible(target, !inIllusion);
            }

            if (!inIllusion) hiddenByIllusion.Clear();
        }

        private static void SetVisible(PlayerControl target, bool visible)
        {
            if (visible && (Camouflager.camouflageTimer > 0f || Helpers.MushroomSabotageActive())) return;
            if (hiddenByIllusion.TryGetValue(target.PlayerId, out bool hidden) && hidden == !visible) return;
            hiddenByIllusion[target.PlayerId] = !visible;

            try
            {
                if (target.cosmetics != null)
                {
                    target.cosmetics.SetBodyCosmeticsVisible(visible);
                    if (target.cosmetics.nameText != null) target.cosmetics.nameText.gameObject.SetActive(visible);
                }
            }
            catch (Exception ex)
            {
                TheOtherRolesPlugin.Logger.LogWarning($"[Painter] vision failed: {ex.Message}");
            }
        }

        public static void RestoreVision()
        {
            var localPlayer = PlayerControl.LocalPlayer;
            if (localPlayer != null && hiddenByIllusion.Count > 0)
            {
                foreach (PlayerControl target in PlayerControl.AllPlayerControls)
                {
                    if (target == null || target.Data == null || target == localPlayer) continue;
                    if (!hiddenByIllusion.TryGetValue(target.PlayerId, out bool hidden) || !hidden) continue;
                    SetVisible(target, true);
                }
            }

            hiddenByIllusion.Clear();
            ApplyOverlay(false);
        }

        private static void ApplyOverlay(bool on)
        {
            if (!on)
            {
                if (illusionOverlay != null)
                {
                    UnityEngine.Object.Destroy(illusionOverlay);
                    illusionOverlay = null;
                }
                return;
            }

            if (illusionOverlay != null) return;

            var root = Helpers.CreateOverlayCanvas("PainterIllusionOverlay", 9400);
            var rect = Helpers.CreateCenteredRect("Fog", root.transform, new Vector2(Screen.width, Screen.height));
            var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
            image.color = new Color(0.25f, 0.08f, 0.35f, 0.45f);
            image.raycastTarget = false;
            illusionOverlay = root;
        }

        public static void Update()
        {
            if (illusion.Count == 0)
            {
                UpdateIllusionVision();
                return;
            }

            float dt = Time.deltaTime;
            List<byte> expired = null;

            foreach (var pair in illusion)
            {
                if (pair.Value - dt > 0f) continue;
                (expired ??= new List<byte>()).Add(pair.Key);
            }

            if (expired != null)
            {
                foreach (var playerId in expired) SetIllusion.Invoke((playerId, false));
            }
            else
            {
                foreach (var playerId in new List<byte>(illusion.Keys)) illusion[playerId] -= dt;
            }

            UpdateIllusionVision();
        }

        public static void clearAndReload()
        {
            cooldown = CustomOptionHolder.painterCooldown.getFloat();
            maxUses = Mathf.RoundToInt(CustomOptionHolder.painterUses.getFloat());
            illusionDuration = CustomOptionHolder.painterDuration.getFloat();

            illusion.Clear();
            painted.Clear();
            hiddenByIllusion.Clear();
            ApplyOverlay(false);
            CloseTaskScreen();
            destroyUi();
            players = [];
        }

        static public IEnumerable<HelpSprite> GetHelpSprites()
        {
            yield return new(getButtonSprite(), "painterPaintText");
        }

        static public IEnumerable<DocumentReplacement> GetReplacementPart()
        {
            yield return new("%CD%", cooldown.ToString());
            yield return new("%USES%", maxUses.ToString());
            yield return new("%DUR%", illusionDuration.ToString());
        }
    }

    [HarmonyPatch(typeof(Console), nameof(Console.Use))]
    public static class PainterConsoleUsePatch
    {
        public static bool Prefix(Console __instance)
        {
            if (Painter.painted.Count == 0) return true;

            var localPlayer = PlayerControl.LocalPlayer;
            if (localPlayer == null || localPlayer.Data == null || localPlayer.Data.IsDead) return true;
            if (localPlayer.Data.Role.IsImpostor) return true;
            if (Painter.isInIllusion(localPlayer)) return true;

            var ship = MapUtilities.CachedShipStatus;
            if (ship == null || ship.AllConsoles == null) return true;

            for (int i = 0; i < ship.AllConsoles.Length; i++)
            {
                if (ship.AllConsoles[i] != __instance) continue;
                if (!Painter.painted.Contains(i)) return true;

                Painter.SetIllusion.Invoke((localPlayer.PlayerId, true));
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(Console), nameof(Console.CanUse))]
    public static class PainterIllusionConsolePatch
    {
        public static bool Prefix(ref float __result, [HarmonyArgument(0)] NetworkedPlayerInfo pc, [HarmonyArgument(1)] out bool canUse, [HarmonyArgument(2)] out bool couldUse)
        {
            canUse = couldUse = false;

            if (pc == null || pc.Object == null || !Painter.isInIllusion(pc.Object)) return true;

            __result = float.MaxValue;
            return false;
        }
    }

    [HarmonyPatch(typeof(HudManager), nameof(HudManager.StartReactorFlash))]
    public static class PainterReactorFlashPatch
    {
        public static bool Prefix()
        {
            return !Painter.isInIllusion(PlayerControl.LocalPlayer);
        }
    }

    [HarmonyPatch(typeof(HudManager), nameof(HudManager.StartOxyFlash))]
    public static class PainterOxyFlashPatch
    {
        public static bool Prefix()
        {
            return !Painter.isInIllusion(PlayerControl.LocalPlayer);
        }
    }

    [HarmonyPatch(typeof(NormalPlayerTask), nameof(NormalPlayerTask.UpdateArrowAndLocation))]
    public static class PainterTaskArrowPatch
    {
        public static void Postfix(NormalPlayerTask __instance)
        {
            if (__instance == null || __instance.Arrow == null) return;
            if (!Painter.isInIllusion(PlayerControl.LocalPlayer)) return;

            __instance.Arrow.gameObject.SetActive(false);
        }
    }

    [HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.CalculateLightRadius))]
    public static class PainterLightRadiusPatch
    {
        public static bool Prefix(ref float __result, ShipStatus __instance, [HarmonyArgument(0)] NetworkedPlayerInfo player)
        {
            if (player == null || player.Object == null) return true;
            if (!Painter.isInIllusion(player.Object)) return true;

            __result = __instance.MaxLightRadius;
            return false;
        }
    }

    [HarmonyPatch(typeof(TORGUIManager), nameof(TORGUIManager.Update))]
    public static class PainterUpdatePatch
    {
        public static void Postfix()
        {
            if (!Painter.exists) return;
            Painter.Update();
        }
    }
}
