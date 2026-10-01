using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TheOtherRoles.Modules;
using TheOtherRoles.Patches;
using UnityEngine;
using static TheOtherRoles.Patches.PlayerControlFixedUpdatePatch;
using static TheOtherRoles.TheOtherRoles;

namespace TheOtherRoles.Roles
{
    [TORRPCHolder]
    public class YutaOkkotsu : RoleBase<YutaOkkotsu>
    {
        public static Color color = new Color32(186, 156, 224, byte.MaxValue);

        public static float cooldown = 25f;
        public static float stopDuration = 2.5f;
        public static float throatDuration = 1.5f;

        public PlayerControl currentTarget;
        public bool isThroatHurt;
        public float throatTimer;

        private static readonly Dictionary<byte, float> frozenUntil = [];
        private static readonly HashSet<byte> silenced = [];

        private static Sprite stopSprite;
        private static Sprite silenceSprite;

        public YutaOkkotsu()
        {
            RoleId = roleId = RoleId.YutaOkkotsu;
            currentTarget = null;
            isThroatHurt = false;
            throatTimer = 0f;
        }

        public static Sprite getStopButtonSprite()
        {
            if (stopSprite) return stopSprite;
            stopSprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.BlockDashButton.png", 115f);
            return stopSprite;
        }

        public static Sprite getSilenceButtonSprite()
        {
            if (silenceSprite) return silenceSprite;
            silenceSprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.BlackmailerBlackmailButton.png", 115f);
            return silenceSprite;
        }

        public static RemoteProcess<byte> CommandStop = RemotePrimitiveProcess.OfByte("YutaCommandStop", (targetId, __) =>
        {
            var target = Helpers.playerById(targetId);
            if (target == null || target.Data == null || target.Data.IsDead) return;
            frozenUntil[targetId] = Time.time + stopDuration;
        });

        public static RemoteProcess<byte> CommandSilence = RemotePrimitiveProcess.OfByte("YutaCommandSilence", (targetId, __) =>
        {
            var target = Helpers.playerById(targetId);
            if (target == null || target.Data == null || target.Data.IsDead) return;
            silenced.Add(targetId);
        });

        public static bool isFrozen(PlayerControl target)
        {
            return target != null && frozenUntil.TryGetValue(target.PlayerId, out float until) && Time.time < until;
        }

        public static bool isSilenced(PlayerControl target)
        {
            return target != null && silenced.Contains(target.PlayerId);
        }

        public bool canCommand => player != null && player.Data != null && !player.Data.IsDead
            && !isThroatHurt && currentTarget != null && currentTarget.Data != null && !currentTarget.Data.IsDead;

        public void useStop()
        {
            if (player != PlayerControl.LocalPlayer || !canCommand) return;
            CommandStop.Invoke(currentTarget.PlayerId);
            startThroat();
            _ = new StaticAchievementToken("yutaOkkotsu.common1");
        }

        public void useSilence()
        {
            if (player != PlayerControl.LocalPlayer || !canCommand) return;
            CommandSilence.Invoke(currentTarget.PlayerId);
            startThroat();
            _ = new StaticAchievementToken("yutaOkkotsu.another1");
        }

        private void startThroat()
        {
            isThroatHurt = true;
            throatTimer = throatDuration;
        }

        public override void FixedUpdate()
        {
            if (player == null || player.Data == null) return;

            if (player != PlayerControl.LocalPlayer) return;

            if (player.Data.IsDead)
            {
                EndThroat();
                return;
            }

            if (isThroatHurt)
            {
                throatTimer -= Time.fixedDeltaTime;
                player.moveable = false;
                if (player.MyPhysics != null && player.MyPhysics.body != null)
                    player.MyPhysics.body.velocity = Vector2.zero;

                if (throatTimer <= 0f) EndThroat();
            }

            currentTarget = setTarget();
            setPlayerOutline(currentTarget, color);
        }

        private void EndThroat()
        {
            if (!isThroatHurt) return;

            isThroatHurt = false;
            throatTimer = 0f;
            player.moveable = true;
        }

        public override void OnMeetingEnd(PlayerControl exiled = null)
        {
            if (player != PlayerControl.LocalPlayer) return;

            if (exiled != null && silenced.Contains(exiled.PlayerId))
                _ = new StaticAchievementToken("yutaOkkotsu.challenge");

            silenced.Clear();
        }

        public override void OnDeath(PlayerControl killer = null)
        {
            if (player == PlayerControl.LocalPlayer) EndThroat();
            currentTarget = null;
        }

        public override void ResetRole(bool isShifted)
        {
            if (player == PlayerControl.LocalPlayer) EndThroat();
            currentTarget = null;
        }

        static public IEnumerable<DocumentReplacement> GetReplacementPart()
        {
            yield return new("%CD%", Mathf.RoundToInt(cooldown).ToString());
            yield return new("%STOP%", stopDuration.ToString("0.#"));
            yield return new("%THROAT%", throatDuration.ToString("0.#"));
        }

        public static void clearAndReload()
        {
            cooldown = CustomOptionHolder.yutaCooldown.getFloat();
            stopDuration = CustomOptionHolder.yutaStopDuration.getFloat();
            throatDuration = CustomOptionHolder.yutaThroatDuration.getFloat();

            frozenUntil.Clear();
            silenced.Clear();
            players = [];
        }

        [HarmonyPatch(typeof(PlayerPhysics), nameof(PlayerPhysics.FixedUpdate))]
        public static class YutaFreezePatch
        {
            public static void Postfix(PlayerPhysics __instance)
            {
                var target = __instance.myPlayer;
                if (target == null || target.Data == null || target.Data.IsDead) return;
                if (!__instance.AmOwner || !target.CanMove) return;
                if (!frozenUntil.TryGetValue(target.PlayerId, out float until)) return;

                if (Time.time >= until)
                {
                    frozenUntil.Remove(target.PlayerId);
                    return;
                }

                __instance.body.velocity = Vector2.zero;
            }
        }

        [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Select))]
        public static class YutaSilencePatch
        {
            public static bool Prefix(ref bool __result)
            {
                if (silenced.Count == 0) return true;
                if (PlayerControl.LocalPlayer == null || PlayerControl.LocalPlayer.Data == null) return true;
                if (!silenced.Contains(PlayerControl.LocalPlayer.PlayerId)) return true;

                __result = false;
                return false;
            }
        }
    }
}
