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
    public class Agnosia : RoleBase<Agnosia>
    {
        public static Color color = Palette.ImpostorRed;

        public static float skillCooldown = 30f;
        public static float duration = 15f;
        public static float killCooldownActive = 10f;
        public static float killCooldownIdle = 45f;
        public static int madnessKillsNeeded = 3;
        public static float madnessDuration = 30f;
        public static float transformSpeed = 2f;

        public float timer = 0f;
        public float seekerAnimTimer = 0f;
        public float screamTimer = -1f;
        public float lookRefresh = 0f;
        public float madnessTimer = 0f;
        public int madnessKills = 0;
        public bool mad = false;

        public static CustomButton agnosiaButton;
        private static Sprite buttonSprite;

        public static MetaContext.Image RoleIcon = SpriteLoader.FromResource("TheOtherRoles.Resources.CamoButton.png", 115f);
        public static MetaContext.Image Illustration = SpriteLoader.FromResource("TheOtherRoles.Resources.CamoButton.png", 115f);

        public Agnosia()
        {
            RoleId = roleId = RoleId.Agnosia;
            timer = 0f;
            lookRefresh = 0f;
            madnessTimer = 0f;
            madnessKills = 0;
            mad = false;
        }

        public static Sprite getButtonSprite()
        {
            if (buttonSprite) return buttonSprite;
            buttonSprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.CamoButton.png", 115f);
            return buttonSprite;
        }

        public static bool isActive(PlayerControl player)
        {
            if (player == null || player.Data == null) return false;
            var role = getRole(player);
            return role != null && (role.timer > 0f || role.mad);
        }

        public static AnimationClip seekerSpawnAnim;

        private static void applyLook()
        {
            foreach (PlayerControl p in PlayerControl.AllPlayerControls)
            {
                if (p == null || p.Data == null) continue;
                if (isMad(p)) continue;
                p.setLook("", 6, "", "", "", "");
            }
        }

        private static void restoreLook()
        {
            if (Camouflager.camouflageTimer > 0f || Helpers.MushroomSabotageActive()) return;
            foreach (PlayerControl p in PlayerControl.AllPlayerControls)
            {
                if (p == null || p.Data == null) continue;
                if (Assassin.players.Any(x => x.player == p && x.isInvisble)) continue;
                p.setDefaultLook();
            }
        }

        public static bool isMad(PlayerControl player)
        {
            if (player == null || player.Data == null) return false;
            var role = getRole(player);
            return role != null && role.mad;
        }

        public static bool madnessActive
        {
            get
            {
                foreach (var role in players)
                    if (role != null && role.mad) return true;
                return false;
            }
        }

        private static GameObject grayOverlay;

        private static void applyScreenGray()
        {
            if (grayOverlay != null) return;
            var hud = FastDestroyableSingleton<HudManager>.Instance;
            if (hud == null || hud.FullScreen == null) return;

            var renderer = UnityEngine.Object.Instantiate(hud.FullScreen, hud.transform);
            if (renderer == null) return;
            grayOverlay = renderer.gameObject;
            grayOverlay.SetActive(true);
            renderer.enabled = true;
            renderer.color = new Color(0.42f, 0.42f, 0.42f, 0.42f);
        }

        private static void clearScreenGray()
        {
            if (grayOverlay == null) return;
            UnityEngine.Object.Destroy(grayOverlay);
            grayOverlay = null;
        }

        private AnimationClip currentClip()
        {
            if (player == null || player.MyPhysics == null || player.MyPhysics.Animations == null) return null;
            var animator = player.MyPhysics.Animations.Animator;
            return animator != null ? animator.GetCurrentAnimation() : null;
        }

        private void finishSeekerAnim()
        {
            if (player == null || player.MyPhysics == null) return;
            if (player.MyPhysics.Animations != null) player.MyPhysics.Animations.PlayIdleAnimation();
            player.MyPhysics.DoingCustomAnimation = false;
            player.MyPhysics.ResetMoveState(true);
            player.moveable = true;

            if (player == PlayerControl.LocalPlayer)
            {
                var follower = Camera.main != null ? Camera.main.GetComponent<FollowerCamera>() : null;
                if (follower != null)
                {
                    follower.Locked = false;
                    follower.SnapToTarget();
                }
            }
        }

        private static void setSeekerBody(PlayerControl p, bool seeker)
        {
            if (p == null || p.MyPhysics == null || p.cosmetics == null) return;

            p.MyPhysics.SetBodyType(seeker ? PlayerBodyTypes.Seeker : PlayerBodyTypes.Normal);

            if (seeker)
            {
                p.cosmetics.SetBodyCosmeticsVisible(false);
                p.moveable = true;
                if (seekerSpawnAnim == null || p.MyPhysics.Animations == null) return;
                var animator = p.MyPhysics.Animations.Animator;
                if (animator != null) animator.Play(seekerSpawnAnim, transformSpeed);
            }
            else
            {
                p.cosmetics.SetBodyCosmeticsVisible(true);
                p.MyPhysics.Animations.PlayIdleAnimation();
                p.MyPhysics.DoingCustomAnimation = false;
            }
        }

        private static void playScream(PlayerControl p)
        {
            if (p == null) return;
            var sfx = p.GetComponent<HnSImpostorScreamSfx>();
            if (sfx == null) return;
            if (PlayerControl.LocalPlayer == p) sfx.LocalImpostorScream();
            else sfx.OtherImpostorScream();
        }

        private static float screamTiming()
        {
            if (seekerSpawnAnim == null) return 0f;
            try
            {
                var events = seekerSpawnAnim.events;
                if (events != null)
                {
                    foreach (var e in events)
                    {
                        if (e == null || string.IsNullOrEmpty(e.functionName)) continue;
                        if (e.functionName.Contains("Scream") || e.functionName.Contains("SeekerHand")) return e.time;
                    }
                }
            }
            catch { }
            return seekerSpawnAnim.length * 0.7f;
        }

        public static RemoteProcess<(byte playerId, bool active)> SetAgnosia = new("AgnosiaSetState", (message, __) =>
        {
            var target = Helpers.playerById(message.playerId);
            var role = getRole(target);
            if (role == null || role.mad) return;

            role.timer = message.active ? duration : 0f;

            if (PlayerControl.LocalPlayer != target) return;

            if (message.active)
            {
                _ = new StaticAchievementToken("agnosia.common1");

                applyLook();
                role.lookRefresh = 1f;
                target.SetKillTimerUnchecked(killCooldownActive, killCooldownActive);
                SoundEffectsManager.play("morphlingMorph", 0.7f);
                new CustomMessage(ModTranslation.getString("agnosiaStart"), 3f);
            }
            else
            {
                restoreLook();
                target.SetKillTimerUnchecked(killCooldownIdle, killCooldownIdle);
                SoundEffectsManager.play("fail", 0.6f);
            }
        });

        public static RemoteProcess<byte> EnterMadness = RemotePrimitiveProcess.OfByte("AgnosiaEnterMadness", (message, _) =>
        {
            var target = Helpers.playerById(message);
            var role = getRole(target);
            if (role == null || role.mad) return;

            role.mad = true;
            role.timer = 0f;
            role.madnessTimer = madnessDuration;

            role.seekerAnimTimer = seekerSpawnAnim != null && seekerSpawnAnim.length > 0.1f ? seekerSpawnAnim.length / transformSpeed + 0.25f : 3f;
            role.screamTimer = screamTiming() / transformSpeed;

            applyScreenGray();
            setSeekerBody(target, true);

            if (PlayerControl.LocalPlayer != target) return;

            applyLook();
            role.lookRefresh = 1f;
            target.SetKillTimerUnchecked(killCooldownActive, killCooldownActive);
            new CustomMessage(ModTranslation.getString("agnosiaMadness"), 4f);
        });

        public static RemoteProcess<byte> EndMadness = RemotePrimitiveProcess.OfByte("AgnosiaEndMadness", (playerId, _) =>
        {
            var pc = Helpers.playerById(playerId);
            var role = getRole(pc);
            if (role == null) return;

            role.mad = false;
            role.madnessTimer = 0f;

            clearScreenGray();
            setSeekerBody(pc, false);

            if (PlayerControl.LocalPlayer != pc) return;

            restoreLook();
            if (pc == null) return;
            pc.MurderPlayer(pc, MurderResultFlags.Succeeded);
            GameHistory.overrideDeathReasonAndKiller(pc, DeadPlayer.CustomDeathReason.Suicide);
        });

        private void onActivate()
        {
            SetAgnosia.Invoke((player.PlayerId, true));
            agnosiaButton.Timer = agnosiaButton.MaxTimer;
        }

        private void onExpire()
        {
            restoreLook();
            player.SetKillTimerUnchecked(killCooldownIdle, killCooldownIdle);
            SoundEffectsManager.play("fail", 0.6f);
        }

        private void onMadnessExpire() => EndMadness.Invoke(player.PlayerId);

        public override void PostInit()
        {
            if (PlayerControl.LocalPlayer != player) return;
            var hudManager = HudManager.Instance;

            agnosiaButton = new CustomButton(
                onActivate,
                () => PlayerControl.LocalPlayer.isRole(RoleId.Agnosia) && !PlayerControl.LocalPlayer.Data.IsDead,
                () => player.CanMove && timer <= 0f && !mad && !player.inVent && !MeetingHud.Instance && !Minigame.Instance,
                () => { agnosiaButton.Timer = agnosiaButton.MaxTimer; },
                getButtonSprite(),
                CustomButton.ButtonPositions.upperRowLeft,
                hudManager,
                KeyCode.F,
                buttonText: ModTranslation.getString("agnosiaActivate"),
                abilityTexture: CustomButton.ButtonLabelType.UseButton
            );
            agnosiaButton.MaxTimer = skillCooldown;
            agnosiaButton.Timer = skillCooldown;
        }

        public override void OnKill(PlayerControl target)
        {
            if (player != PlayerControl.LocalPlayer) return;
            if (target == null || target == player) return;

            player.SetKillTimerUnchecked(mad || timer > 0f ? killCooldownActive : killCooldownIdle);

            if (!mad && timer > 0f) _ = new StaticAchievementToken("agnosia.another1");

            if (mad) return;

            madnessKills++;
            if (madnessKills >= madnessKillsNeeded) EnterMadness.Invoke(player.PlayerId);
        }

        public override void OnMeetingEnd(PlayerControl exiled = null) => refreshKillTimer();

        public override void OnFinishShipStatusBegin() => refreshKillTimer();

        private void refreshKillTimer()
        {
            if (player != PlayerControl.LocalPlayer) return;
            if (player.Data == null || player.Data.IsDead) return;
            player.SetKillTimerUnchecked(mad || timer > 0f ? killCooldownActive : killCooldownIdle);
        }

        public override void FixedUpdate()
        {
            if (screamTimer >= 0f)
            {
                screamTimer -= Time.deltaTime;
                if (screamTimer <= 0f)
                {
                    screamTimer = -1f;
                    playScream(player);
                }
            }

            if (mad)
            {
                if (!MeetingHud.Instance && !ExileController.Instance && madnessTimer > 0f)
                {
                    madnessTimer -= Time.deltaTime;
                    if (madnessTimer <= 0f)
                    {
                        madnessTimer = 0f;
                        if (player == PlayerControl.LocalPlayer) onMadnessExpire();
                    }
                }
            }
            else if (timer > 0f)
            {
                timer -= Time.deltaTime;
                if (timer <= 0f)
                {
                    timer = 0f;
                    if (player == PlayerControl.LocalPlayer) onExpire();
                }
            }

            if (player != PlayerControl.LocalPlayer) return;

            if (mad && !MeetingHud.Instance && !ExileController.Instance)
            {
                player.moveable = true;

                if (seekerAnimTimer > 0f)
                {
                    seekerAnimTimer -= Time.deltaTime;
                    if (seekerAnimTimer <= 0f) finishSeekerAnim();
                }
            }

            if (!mad && timer <= 0f) return;

            lookRefresh -= Time.deltaTime;
            if (lookRefresh > 0f) return;
            lookRefresh = 1f;
            applyLook();
        }

        public override void OnMeetingStart()
        {
            if (mad) return;
            if (timer <= 0f) return;
            timer = 0f;
            if (player == PlayerControl.LocalPlayer) restoreLook();
        }

        public override void OnDeath(PlayerControl killer = null)
        {
            if (mad)
            {
                mad = false;
                madnessTimer = 0f;
                clearScreenGray();
                setSeekerBody(player, false);
            }
            if (timer <= 0f) return;
            timer = 0f;
            if (player == PlayerControl.LocalPlayer) restoreLook();
        }

        public override void ResetRole(bool isShifted)
        {
            if ((timer > 0f || mad) && player == PlayerControl.LocalPlayer) restoreLook();
            timer = 0f;
            mad = false;
            madnessTimer = 0f;
            clearScreenGray();
            destroyUi();
        }

        private static void destroyUi()
        {
            if (agnosiaButton != null)
            {
                UnityEngine.Object.Destroy(agnosiaButton.actionButtonGameObject);
                agnosiaButton = null;
            }
        }

        static public IEnumerable<HelpSprite> GetHelpSprites()
        {
            yield return new(getButtonSprite(), "agnosiaActivateHint");
        }

        static public IEnumerable<DocumentReplacement> GetReplacementPart()
        {
            yield return new("%CD%", skillCooldown.ToString());
            yield return new("%DURATION%", duration.ToString());
            yield return new("%FAST%", killCooldownActive.ToString());
            yield return new("%SLOW%", killCooldownIdle.ToString());
            yield return new("%MADKILLS%", madnessKillsNeeded.ToString());
            yield return new("%MADDUR%", madnessDuration.ToString());
        }

        public static void clearAndReload()
        {
            skillCooldown = CustomOptionHolder.agnosiaCooldown.getFloat();
            duration = CustomOptionHolder.agnosiaDuration.getFloat();
            killCooldownActive = CustomOptionHolder.agnosiaKillCooldownActive.getFloat();
            killCooldownIdle = CustomOptionHolder.agnosiaKillCooldownIdle.getFloat();
            madnessKillsNeeded = Mathf.RoundToInt(CustomOptionHolder.agnosiaMadnessKills.getFloat());
            madnessDuration = CustomOptionHolder.agnosiaMadnessDuration.getFloat();

            clearScreenGray();
            destroyUi();
            buttonSprite = null;
            players = [];
        }
    }

    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CmdCheckMurder))]
    public static class AgnosiaMurderPatch
    {
        public static bool Prefix(PlayerControl __instance, PlayerControl target)
        {
            if (!Agnosia.isActive(__instance)) return true;
            if (target == null || target.Data == null || target.Data.IsDead || target.Data.Disconnected) return true;
            if (target.Data.Role == null || !target.Data.Role.IsImpostor) return true;

            Helpers.checkMurderAttemptAndKill(__instance, target);

            if (__instance == PlayerControl.LocalPlayer)
            {
                var role = Agnosia.getRole(__instance);
                if (role != null && role.mad) _ = new StaticAchievementToken("agnosia.challenge");
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
    public static class AgnosiaMadnessTimerPatch
    {
        private static TMPro.TextMeshPro tabText;

        [HarmonyPriority(Priority.Last)]
        public static void Postfix()
        {
            if (!Agnosia.madnessActive) return;
            if (HudManager.Instance == null) return;

            if (HudManager.Instance.ReportButton != null) HudManager.Instance.ReportButton.Hide();

            var role = Agnosia.local;
            if (role == null || !role.mad) return;
            if (PlayerControl.LocalPlayer == null || PlayerControl.LocalPlayer.Data == null) return;
            if (HudManager.Instance.TaskPanel == null) return;

            if (tabText == null)
            {
                var tab = HudManager.Instance.TaskPanel.tab;
                if (tab == null) return;
                tabText = tab.transform.FindChild("TabText_TMP").GetComponent<TMPro.TextMeshPro>();
            }
            if (tabText == null) return;

            int minutes = (int)role.madnessTimer / 60;
            int seconds = (int)role.madnessTimer % 60;
            tabText.SetText($"<color=#FF0000FF>{minutes:00}:{seconds:00}</color>");
        }
    }

    [HarmonyPatch(typeof(ReportButton), nameof(ReportButton.DoClick))]
    public static class AgnosiaReportPatch
    {
        public static bool Prefix() => !Agnosia.madnessActive;
    }

    [HarmonyPatch(typeof(CosmeticsCache), nameof(CosmeticsCache.GetSkin))]
    public static class AgnosiaEmptySkinPatch
    {
        private static SkinViewData emptySkin = null;
        private static bool resolved = false;

        public static bool Prefix([HarmonyArgument(0)] string id, ref SkinViewData __result)
        {
            if (!string.IsNullOrEmpty(id) || !resolved) return true;
            __result = emptySkin;
            return false;
        }

        public static void Postfix([HarmonyArgument(0)] string id, SkinViewData __result)
        {
            if (!string.IsNullOrEmpty(id)) return;
            emptySkin = __result;
            resolved = true;
        }
    }
}
