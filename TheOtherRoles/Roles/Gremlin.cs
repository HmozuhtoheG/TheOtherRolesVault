using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TheOtherRoles.MetaContext;
using TheOtherRoles.Modules;
using TheOtherRoles.Objects;
using TheOtherRoles.Patches;
using TheOtherRoles.Utilities;
using UnityEngine;
using static TheOtherRoles.TheOtherRoles;

namespace TheOtherRoles.Roles
{
    [TORRPCHolder]
    public class Gremlin : RoleBase<Gremlin>
    {
        public static Color color = new Color32(191, 64, 255, byte.MaxValue);

        public static float showCooldown = 15f;
        public static float passiveInterval = 45f;
        public static float comboChance = 35f;
        public static float dashDuration = 6f;
        public static float dashMultiplier = 0.5f;
        public static float slumpDuration = 5f;
        public static float slumpMultiplier = 0.45f;
        public static float giantDuration = 8f;
        public static float giantScale = 2f;
        public static float freezeDuration = 4f;

        public const int EffectCount = 10;

        public float passiveTimer = passiveInterval;
        public float dashTimer = 0f;
        public float slumpTimer = 0f;
        public float giantTimer = 0f;
        public byte giantTargetId = byte.MaxValue;

        public static CustomButton showButton;
        private static Sprite showButtonSprite;

        public static MetaContext.Image RoleIcon = SpriteLoader.FromResource("TheOtherRoles.Resources.GremlinIcon.png", 115f);
        public static MetaContext.Image Illustration = SpriteLoader.FromResource("TheOtherRoles.Resources.GremlinIllustration.png", 115f);

        public Gremlin()
        {
            RoleId = roleId = RoleId.Gremlin;
            passiveTimer = passiveInterval;
            dashTimer = 0f;
            slumpTimer = 0f;
            giantTimer = 0f;
            giantTargetId = byte.MaxValue;
        }

        public static Sprite getShowButtonSprite()
        {
            if (showButtonSprite) return showButtonSprite;
            showButtonSprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.GremlinButton.png", 115f);
            return showButtonSprite;
        }

        public static bool isInflated(PlayerControl target)
        {
            return target != null && players.Any(x => x.giantTimer > 0f && x.giantTargetId == target.PlayerId);
        }

        private static List<PlayerControl> livingOthers(PlayerControl self)
        {
            return PlayerControl.AllPlayerControls.ToArray()
                .Where(x => x != null && x != self && x.Data != null && !x.Data.IsDead && !x.Data.Disconnected)
                .ToList();
        }

        private static Vector2 getAnchor(PlayerControl gremlin, byte seed)
        {
            if (gremlin.Data != null && !gremlin.Data.IsDead) return gremlin.GetTruePosition();

            var living = PlayerControl.AllPlayerControls.ToArray()
                .Where(x => x != null && x.Data != null && !x.Data.IsDead && !x.Data.Disconnected)
                .ToList();
            if (living.Count == 0) return gremlin.GetTruePosition();

            return living[seed % living.Count].GetTruePosition();
        }

        public static RemoteProcess<(byte gremlinId, byte effect, byte targetId, byte seed)> PutOnShow = new("GremlinPutOnShow", (message, _) =>
        {
            var local = PlayerControl.LocalPlayer;
            if (local == null || local.Data == null) return;

            PlayerControl gremlin = Helpers.playerById(message.gremlinId);
            if (gremlin == null || gremlin.Data == null) return;

            string actor = Helpers.cs(color, gremlin.Data.PlayerName);
            string effectName = ModTranslation.getString("gremlinEffect" + message.effect);
            string shout = string.Format(ModTranslation.getString("gremlinShout"), actor, effectName);
            Sprite icon = RoleHelpers.GetRoleIcon(RoleId.Gremlin).GetSprite();

            switch (message.effect)
            {
                case 0:
                    Helpers.flashScreen(Color.white, 0.1f, 0.35f, 0.45f, 0.25f, shout, color, icon);
                    SoundEffectsManager.play("select", 0.7f);
                    break;

                case 1:
                    var blinkTarget = Helpers.playerById(message.targetId);
                    if (blinkTarget == null || blinkTarget.Data == null || blinkTarget.Data.IsDead) return;
                    if (gremlin.inVent) return;
                    gremlin.MyPhysics.ResetMoveState();
                    gremlin.NetTransform.SnapTo(new Vector2(blinkTarget.GetTruePosition().x, blinkTarget.GetTruePosition().y + 0.3636f));
                    if (local == gremlin)
                    {
                        Helpers.showFlash(color);
                        if (Minigame.Instance) Minigame.Instance.Close();
                        SoundEffectsManager.play("teleporterTeleport");
                    }
                    return;

                case 2:
                case 3:
                    var self = getRole(gremlin);
                    if (self == null) return;
                    if (message.effect == 2)
                    {
                        self.dashTimer = dashDuration;
                        self.slumpTimer = 0f;
                        SoundEffectsManager.play("select", 0.7f);
                    }
                    else
                    {
                        self.slumpTimer = slumpDuration;
                        self.dashTimer = 0f;
                        SoundEffectsManager.play("fail", 0.6f);
                    }
                    break;

                case 4:
                    var origin = getAnchor(gremlin, message.seed);
                    var crowd = livingOthers(gremlin).Where(x => x.moveable && !x.inVent).ToList();
                    for (int i = 0; i < crowd.Count; i++)
                    {
                        float angle = i * Mathf.PI * 2f / Mathf.Max(1, crowd.Count);
                        var offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 0.85f;
                        crowd[i].MyPhysics.ResetMoveState();
                        crowd[i].NetTransform.SnapTo(new Vector2(origin.x + offset.x, origin.y + offset.y + 0.3636f));
                    }
                    SoundEffectsManager.play("teleporterTeleport");
                    break;

                case 5:
                    Helpers.flashScreen(Color.black, 0.05f, 0.4f, 0.72f, 0.9f, shout, color, icon);
                    SoundEffectsManager.play("fail", 0.7f);
                    break;

                case 6:
                    var inflated = Helpers.playerById(message.targetId);
                    if (inflated == null || inflated.Data == null || inflated.Data.IsDead) return;
                    var inflater = getRole(gremlin);
                    if (inflater == null) return;
                    inflater.giantTargetId = message.targetId;
                    inflater.giantTimer = giantDuration;
                    SoundEffectsManager.play("select", 0.8f);
                    break;

                case 7:
                    var frozen = Helpers.playerById(message.targetId);
                    if (frozen == null || frozen.Data == null || frozen.Data.IsDead) return;
                    frozen.NetTransform.Halt();
                    if (local == frozen)
                    {
                        local.moveable = false;
                        local.NetTransform.Halt();
                        HudManager.Instance.StartCoroutine(Effects.Lerp(freezeDuration, new Action<float>((p) =>
                        {
                            if (p == 1f && local != null && local.Data != null) local.moveable = true;
                        })));
                        SoundEffectsManager.play("fail", 0.7f);
                    }
                    break;

                case 8:
                    var seated = livingOthers(gremlin).Where(x => x.moveable && !x.inVent).ToList();
                    if (seated.Count < 2) break;
                    var seats = seated.Select(x => x.GetTruePosition()).ToList();
                    var shuffler = new System.Random(message.seed);
                    for (int i = seats.Count - 1; i > 0; i--)
                    {
                        int j = shuffler.Next(i + 1);
                        (seats[i], seats[j]) = (seats[j], seats[i]);
                    }
                    for (int i = 0; i < seated.Count; i++)
                    {
                        seated[i].MyPhysics.ResetMoveState();
                        seated[i].NetTransform.SnapTo(new Vector2(seats[i].x, seats[i].y + 0.3636f));
                    }
                    SoundEffectsManager.play("teleporterTeleport");
                    break;

                case 9:
                    Helpers.flashScreen(Color.red, 0.05f, 0.5f, 0.6f, 0.45f, shout, Color.white, icon);
                    if (Constants.ShouldPlaySfx() && ShipStatus.Instance != null && ShipStatus.Instance.SabotageSound != null)
                        SoundManager.Instance.PlaySound(ShipStatus.Instance.SabotageSound, false, 0.85f);
                    break;
            }

            if (local == gremlin)
            {
                Helpers.showFlash(color);
                new CustomMessage(shout, 3f);
            }
        });

        private static readonly int[] passivePool = [0, 1, 2, 3, 5, 6, 7, 9];

        public static void doShow(PlayerControl gremlinPlayer, bool includeHeavy = true)
        {
            if (gremlinPlayer == null || gremlinPlayer.Data == null) return;

            var others = livingOthers(gremlinPlayer).Where(x => x.moveable && !x.inVent).ToList();

            byte targetId = others.Count > 0 ? others[rnd.Next(others.Count)].PlayerId : byte.MaxValue;
            int effect = includeHeavy ? rnd.Next(EffectCount) : passivePool[rnd.Next(passivePool.Length)];

            PutOnShow.Invoke((gremlinPlayer.PlayerId, (byte)effect, targetId, (byte)rnd.Next(256)));
        }

        public override void PostInit()
        {
            if (PlayerControl.LocalPlayer != player) return;
            var hudManager = HudManager.Instance;

            showButton = new CustomButton(
                () =>
                {
                    doShow(player);
                    passiveTimer = passiveInterval;
                    if (rnd.Next(100) < comboChance)
                    {
                        showButton.Timer = 0f;
                        new CustomMessage(ModTranslation.getString("gremlinCombo"), 2f);
                    }
                    else
                    {
                        showButton.Timer = showButton.MaxTimer;
                    }
                },
                () => PlayerControl.LocalPlayer.isRole(RoleId.Gremlin),
                () => (player.Data.IsDead ? player.moveable : player.CanMove) && !MeetingHud.Instance && !Minigame.Instance,
                () => { showButton.Timer = showButton.MaxTimer; },
                getShowButtonSprite(),
                CustomButton.ButtonPositions.lowerRowRight,
                hudManager,
                KeyCode.F,
                buttonText: ModTranslation.getString("gremlinShow"),
                abilityTexture: CustomButton.ButtonLabelType.UseButton
            );
            showButton.MaxTimer = showCooldown;
            showButton.Timer = showCooldown;
        }

        public override void FixedUpdate()
        {
            if (giantTimer > 0f)
            {
                giantTimer -= Time.deltaTime;
                if (giantTimer < 0f)
                {
                    giantTimer = 0f;
                    giantTargetId = byte.MaxValue;
                }
            }

            if (player != PlayerControl.LocalPlayer) return;
            if (player.Data == null || player.Data.IsDead) return;

            if (dashTimer > 0f)
            {
                dashTimer -= Time.deltaTime;
                if (dashTimer < 0f) dashTimer = 0f;
            }

            if (slumpTimer > 0f)
            {
                slumpTimer -= Time.deltaTime;
                if (slumpTimer < 0f) slumpTimer = 0f;
            }

            if (passiveInterval <= 0f) return;
            if (MeetingHud.Instance || ExileController.Instance || Minigame.Instance) return;
            if (!player.CanMove) return;

            passiveTimer -= Time.deltaTime;
            if (passiveTimer <= 0f)
            {
                passiveTimer = passiveInterval;
                doShow(player, false);
            }
        }

        public override void OnMeetingStart()
        {
            passiveTimer = passiveInterval;
            dashTimer = 0f;
            slumpTimer = 0f;
            giantTimer = 0f;
            giantTargetId = byte.MaxValue;
        }

        public override void OnMeetingEnd(PlayerControl exiled = null) => passiveTimer = passiveInterval;

        public override void OnDeath(PlayerControl killer = null)
        {
            dashTimer = 0f;
            slumpTimer = 0f;
        }

        public override void ResetRole(bool isShifted) => destroyUi();

        private static void destroyUi()
        {
            if (showButton != null)
            {
                UnityEngine.Object.Destroy(showButton.actionButtonGameObject);
                showButton = null;
            }
        }

        public override void OnFinishShipStatusBegin()
        {
            if (PlayerControl.LocalPlayer != player) return;
            passiveTimer = passiveInterval;
            dashTimer = 0f;
            slumpTimer = 0f;
        }

        static public IEnumerable<HelpSprite> GetHelpSprites()
        {
            yield return new(getShowButtonSprite(), "gremlinShowHint");
        }

        static public IEnumerable<DocumentReplacement> GetReplacementPart()
        {
            yield return new("%CD%", showCooldown.ToString());
            yield return new("%PASSIVE%", passiveInterval.ToString());
            yield return new("%COMBO%", comboChance.ToString());
            yield return new("%DASH%", dashDuration.ToString());
            yield return new("%SLUMP%", slumpDuration.ToString());
            yield return new("%GIANT%", giantDuration.ToString());
            yield return new("%SIZE%", giantScale.ToString());
            yield return new("%FREEZE%", freezeDuration.ToString());
        }

        public static void clearAndReload()
        {
            showCooldown = CustomOptionHolder.gremlinCooldown.getFloat();
            passiveInterval = CustomOptionHolder.gremlinPassiveInterval.getFloat();
            comboChance = CustomOptionHolder.gremlinComboChance.getFloat();
            dashDuration = CustomOptionHolder.gremlinDashDuration.getFloat();
            dashMultiplier = CustomOptionHolder.gremlinDashMultiplier.getFloat();
            slumpDuration = CustomOptionHolder.gremlinSlumpDuration.getFloat();
            slumpMultiplier = CustomOptionHolder.gremlinSlumpMultiplier.getFloat();
            giantDuration = CustomOptionHolder.gremlinGiantDuration.getFloat();
            giantScale = CustomOptionHolder.gremlinGiantScale.getFloat();
            freezeDuration = CustomOptionHolder.gremlinFreezeDuration.getFloat();

            if (showButton != null)
            {
                UnityEngine.Object.Destroy(showButton.actionButtonGameObject);
                showButton = null;
            }
            showButtonSprite = null;
            players = [];
        }
    }

    [HarmonyPatch(typeof(PlayerControlFixedUpdatePatch), nameof(PlayerControlFixedUpdatePatch.playerSizeUpdate))]
    public static class GremlinSizePatch
    {
        public static void Postfix(PlayerControl p)
        {
            if (p == null || p.Data == null) return;
            if (!Gremlin.isInflated(p)) return;
            p.transform.localScale = new Vector3(Gremlin.giantScale, Gremlin.giantScale, 1f);
        }
    }

    [HarmonyPatch(typeof(PlayerPhysics), nameof(PlayerPhysics.FixedUpdate))]
    public static class GremlinSpeedPatch
    {
        public static void Postfix(PlayerPhysics __instance)
        {
            if (!__instance.AmOwner || __instance.body == null) return;
            var player = __instance.myPlayer;
            if (player == null || player.Data == null || player.Data.IsDead) return;
            var gremlin = Gremlin.getRole(player);
            if (gremlin == null) return;
            if (gremlin.dashTimer > 0f) __instance.body.velocity *= 1f + Gremlin.dashMultiplier;
            if (gremlin.slumpTimer > 0f) __instance.body.velocity *= 1f - Gremlin.slumpMultiplier;
        }
    }
}
