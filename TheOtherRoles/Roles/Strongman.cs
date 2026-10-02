using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Unity.IL2CPP.Utils.Collections;
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
    public class Strongman : RoleBase<Strongman>
    {
        public static Color color = Palette.ImpostorRed;

        public static float cooldown = 30f;
        public static bool canNormalKill = true;
        public static float holdSpeedFactor = 0.5f;

        private const float HoldOffsetY = 0.7f;
        private const float ThrowDistance = 90f;
        private const float ThrowSpeed = 25f;
        private const float KnockDistance = 32f;
        private const float KnockSpeed = 8f;
        private const float HitRadius = 0.6f;

        private static readonly Vector2 HoldOffset = new(0f, HoldOffsetY);
        private static readonly List<Flight> flights = [];
        private static Rect mapRect;
        private static bool mapRectReady;
        private static Sprite buttonSprite;
        private static AudioClip liftClip;
        private static AudioClip flyClip;
        private static AudioClip knockClip;

        public byte heldId = byte.MaxValue;

        private class Flight
        {
            public byte victimId;
            public byte killerId;
            public Vector2 start;
            public Vector2 direction;
            public float spin;
            public float distance;
            public float speed;
            public float elapsed;
            public bool canHit;
            public readonly List<byte> hit = [];
        }

        public Strongman()
        {
            RoleId = roleId = RoleId.Strongman;
        }

        public static Sprite getGrabButtonSprite()
        {
            if (buttonSprite) return buttonSprite;
            buttonSprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.TrapperButton.png", 115f);
            return buttonSprite;
        }

        private static void play(AudioClip clip, float volume = 0.8f)
        {
            if (clip == null || !Constants.ShouldPlaySfx()) return;
            SoundManager.Instance.PlaySound(clip, false, volume);
        }

        public static Strongman getHolder(byte victimId) => players.FirstOrDefault(s => s.heldId == victimId);

        public static bool isFlying(byte playerId) => flights.Any(f => f.victimId == playerId);

        public static bool isRestricted(PlayerControl player)
        {
            if (player == null) return false;
            return getHolder(player.PlayerId) != null || isFlying(player.PlayerId);
        }

        public static RemoteProcess<(byte holderId, byte victimId)> Grab = new("StrongmanGrab", (message, __) =>
        {
            var holder = getRole(Helpers.playerById(message.holderId));
            var victim = Helpers.playerById(message.victimId);
            if (holder == null || victim == null) return;
            if (victim.Data == null || victim.Data.IsDead) return;
            if (getHolder(message.victimId) != null) return;

            holder.heldId = message.victimId;
            applyHold(victim);
            play(liftClip);
        });

        public static RemoteProcess<(byte holderId, byte victimId)> Release = new("StrongmanRelease", (message, __) =>
        {
            var holder = getRole(Helpers.playerById(message.holderId));
            if (holder != null && holder.heldId == message.victimId) holder.heldId = byte.MaxValue;
            releaseHold(Helpers.playerById(message.victimId));
        });

        public static RemoteProcess<(byte holderId, byte victimId, float dirX, float dirY, float spin)> Throw = new("StrongmanThrow", (message, __) =>
        {
            var holder = getRole(Helpers.playerById(message.holderId));
            var victim = Helpers.playerById(message.victimId);
            if (holder != null && holder.heldId == message.victimId) holder.heldId = byte.MaxValue;
            if (victim == null) return;

            releaseHold(victim);
            startFlight(victim, message.holderId, message.dirX, message.dirY, message.spin, ThrowDistance, ThrowSpeed, true);
        });

        public static RemoteProcess<(byte holderId, byte victimId, float dirX, float dirY, float spin)> Knock = new("StrongmanKnock", (message, __) =>
        {
            var victim = Helpers.playerById(message.victimId);
            if (victim == null) return;

            startFlight(victim, message.holderId, message.dirX, message.dirY, message.spin, KnockDistance, KnockSpeed, false);
            play(knockClip);
        });

        public void GrabTarget(PlayerControl target)
        {
            if (target == null || heldId != byte.MaxValue) return;
            if (target == player || target.Data == null || target.Data.IsDead) return;
            Grab.Invoke((player.PlayerId, target.PlayerId));
        }

        public void ThrowHeld()
        {
            if (heldId == byte.MaxValue) return;

            var victim = Helpers.playerById(heldId);
            if (victim == null)
            {
                heldId = byte.MaxValue;
                return;
            }

            float dirX = player.cosmetics.FlipX ? -1f : 1f;
            float spin = UnityEngine.Random.Range(-900f, 900f);

            _ = new StaticAchievementToken("strongman.common1");
            Throw.Invoke((player.PlayerId, heldId, dirX, 0f, spin));

            PlayerControl.LocalPlayer.SetKillTimerUnchecked(cooldown, cooldown);

            var grab = HudManagerStartPatch.strongmanGrabButton;
            if (grab != null) grab.Timer = grab.MaxTimer;
        }

        public static void knockInto(PlayerControl source, Vector2 direction, PlayerControl target)
        {
            float angle = Mathf.Atan2(direction.y, direction.x) + UnityEngine.Random.Range(-0.5f, 0.5f);
            Knock.Invoke((source.PlayerId, target.PlayerId, Mathf.Cos(angle), Mathf.Sin(angle), UnityEngine.Random.Range(-1440f, 1440f)));
        }

        private static void applyHold(PlayerControl victim)
        {
            if (victim == null || victim.MyPhysics == null) return;

            victim.Collider.enabled = false;
            victim.transform.SetLocalEulerAngles(new Vector3(0f, 0f, 90f), RotationOrder.OrderXYZ);
        }

        private static void releaseHold(PlayerControl victim)
        {
            if (victim == null || victim.MyPhysics == null) return;

            victim.transform.SetLocalEulerAngles(Vector3.zero, RotationOrder.OrderXYZ);
            victim.Collider.enabled = true;
        }

        private static void startFlight(PlayerControl victim, byte killerId, float dirX, float dirY, float spin, float distance, float speed, bool canHit)
        {
            releaseHold(victim);

            flights.RemoveAll(f => f.victimId == victim.PlayerId);
            flights.Add(new Flight
            {
                victimId = victim.PlayerId,
                killerId = killerId,
                start = victim.transform.position,
                direction = new Vector2(dirX, dirY).normalized,
                spin = spin,
                distance = distance,
                speed = speed,
                canHit = canHit,
            });

            victim.Collider.enabled = false;
            if (victim.AmOwner) victim.NetTransform.Halt();

            play(flyClip);
        }

        private static bool isOutsideMap(Vector2 position)
        {
            if (!mapRectReady)
            {
                mapRectReady = true;
                var ship = MapUtilities.CachedShipStatus;
                var colliders = ship == null ? null : ship.GetComponentsInChildren<Collider2D>(true);
                if (colliders != null && colliders.Length > 0)
                {
                    var bounds = colliders[0].bounds;
                    for (int i = 1; i < colliders.Length; i++) bounds.Encapsulate(colliders[i].bounds);
                    mapRect = new Rect(bounds.min.x, bounds.min.y, bounds.size.x, bounds.size.y);
                }
            }

            if (mapRect.width <= 0f || mapRect.height <= 0f) return false;
            return position.x < mapRect.xMin || position.x > mapRect.xMax
                || position.y < mapRect.yMin || position.y > mapRect.yMax;
        }

        private static void TickFlights()
        {
            if (flights.Count == 0) return;

            for (int i = flights.Count - 1; i >= 0; i--)
            {
                var flight = flights[i];
                var victim = Helpers.playerById(flight.victimId);
                if (victim == null || victim.Data == null)
                {
                    flights.RemoveAt(i);
                    continue;
                }

                flight.elapsed += Time.fixedDeltaTime;
                float duration = flight.distance / flight.speed;
                float p = Mathf.Clamp01(flight.elapsed / duration);

                Vector2 previous = victim.transform.position;
                Vector2 position = flight.start + flight.direction * flight.distance * p;
                victim.transform.position = new Vector3(position.x, position.y, position.y / 1000f + 0.01f);
                victim.transform.SetLocalEulerAngles(new Vector3(0f, 0f, 90f + flight.spin * p), RotationOrder.OrderXYZ);

                if (flight.canHit && PlayerControl.LocalPlayer.PlayerId == flight.killerId)
                    hitTargets(previous, position, flight.direction, victim, flight);

                if (p < 1f && !isOutsideMap(position)) continue;

                flights.RemoveAt(i);
                victim.transform.SetLocalEulerAngles(Vector3.zero, RotationOrder.OrderXYZ);
                victim.Collider.enabled = true;
                if (victim.cosmetics != null) victim.cosmetics.SetPetVisible(false);

                if (PlayerControl.LocalPlayer.PlayerId != flight.killerId) continue;
                var killer = Helpers.playerById(flight.killerId);
                if (killer == null) continue;
                Helpers.forceMurderPlayer(killer, victim, false);
                HudManager.Instance.StartCoroutine(CoHideBody(flight.victimId).WrapToIl2Cpp());
            }
        }

        private static void hitTargets(Vector2 from, Vector2 to, Vector2 direction, PlayerControl victim, Flight flight)
        {
            int samples = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(from, to) / HitRadius));
            List<PlayerControl> victims = [];

            for (int step = 1; step <= samples; step++)
            {
                Vector2 point = Vector2.Lerp(from, to, (float)step / samples);

                foreach (PlayerControl target in PlayerControl.AllPlayerControls.ToArray())
                {
                    if (target == null || target.Data == null) continue;
                    if (target.Data.IsDead || target.Data.Disconnected) continue;
                    if (target == victim || target.PlayerId == flight.killerId) continue;
                    if (flight.hit.Contains(target.PlayerId)) continue;
                    if (Vector2.Distance(target.GetTruePosition(), point) > HitRadius) continue;

                    flight.hit.Add(target.PlayerId);
                    victims.Add(target);
                }
            }

            if (victims.Count == 0) return;
            if (flight.hit.Count == 1) _ = new StaticAchievementToken("strongman.another1");
            if (flight.hit.Count == 2) _ = new StaticAchievementToken("strongman.challenge");

            var source = Helpers.playerById(flight.killerId);
            if (source == null) return;
            foreach (var target in victims) knockInto(source, direction, target);
        }

        private static IEnumerator CoHideBody(byte playerId)
        {
            for (int i = 0; i < 30; i++)
            {
                foreach (DeadBody body in UnityEngine.Object.FindObjectsOfType<DeadBody>())
                {
                    if (body.ParentId != playerId) continue;
                    UnityEngine.Object.Destroy(body.gameObject);
                    yield break;
                }
                yield return Effects.Wait(0.1f);
            }
        }

        private void releaseHeld()
        {
            if (heldId == byte.MaxValue) return;
            Release.Invoke((player.PlayerId, heldId));
        }

        public override void FixedUpdate()
        {
            if (heldId == byte.MaxValue) return;

            var victim = Helpers.playerById(heldId);
            if (victim == null || victim.Data == null || victim.Data.IsDead)
            {
                releaseHeld();
                return;
            }

            applyHold(victim);
        }

        public override void OnDeath(PlayerControl killer = null) => releaseHeld();

        public override void OnMeetingStart() => releaseHeld();

        public override void ResetRole(bool isShifted) => releaseHeld();

        public override void HandleDisconnect(PlayerControl player, DisconnectReasons reason)
        {
            if (player != this.player) return;
            releaseHeld();
        }

        public override void OnKill(PlayerControl target)
        {
            if (PlayerControl.LocalPlayer != player) return;
            var grab = HudManagerStartPatch.strongmanGrabButton;
            if (grab != null) grab.Timer = grab.MaxTimer;
        }

        static public IEnumerable<DocumentReplacement> GetReplacementPart()
        {
            yield return new("%CD%", Mathf.RoundToInt(cooldown).ToString());
            yield return new("%SPEED%", Mathf.RoundToInt(holdSpeedFactor * 100f).ToString());
        }

        public static void clearAndReload()
        {
            cooldown = CustomOptionHolder.strongmanCooldown.getFloat();
            canNormalKill = CustomOptionHolder.strongmanCanNormalKill.getBool();
            holdSpeedFactor = CustomOptionHolder.strongmanHoldSpeed.getFloat();

            liftClip ??= Helpers.loadWavFromResources("TheOtherRoles.Resources.Lift.wav", "StrongmanLift");
            flyClip ??= Helpers.loadWavFromResources("TheOtherRoles.Resources.Fly.wav", "StrongmanFly");
            knockClip ??= Helpers.loadWavFromResources("TheOtherRoles.Resources.Knock.wav", "StrongmanKnock");

            flights.Clear();
            mapRectReady = false;
            players = [];
        }

        [HarmonyPatch(typeof(TORGUIManager), nameof(TORGUIManager.Update))]
        public static class StrongmanTickPatch
        {
            public static void Postfix() => TickFlights();
        }

        [HarmonyPatch(typeof(PlayerPhysics), nameof(PlayerPhysics.FixedUpdate))]
        public static class StrongmanPhysicsPatch
        {
            public static void Postfix(PlayerPhysics __instance)
            {
                if (!__instance.AmOwner) return;

                var me = __instance.myPlayer;
                if (me == null || me.Data == null || me.Data.IsDead || MeetingHud.Instance) return;

                if (isFlying(me.PlayerId))
                {
                    __instance.body.velocity = Vector2.zero;
                    return;
                }

                var holder = getHolder(me.PlayerId);
                if (holder == null || holder.player == null) return;

                Vector2 seat = (Vector2)holder.player.transform.position + HoldOffset;
                __instance.body.velocity = (seat - __instance.body.position) / Time.fixedDeltaTime;
            }
        }

        [HarmonyPatch(typeof(PlayerPhysics), nameof(PlayerPhysics.FixedUpdate))]
        public static class StrongmanHolderPhysicsPatch
        {
            public static void Postfix(PlayerPhysics __instance)
            {
                if (!__instance.AmOwner) return;

                var me = __instance.myPlayer;
                if (me == null || me.Data == null || me.Data.IsDead || MeetingHud.Instance) return;
                if (!me.CanMove) return;

                var self = getRole(me);
                if (self == null || self.heldId == byte.MaxValue) return;

                __instance.body.velocity *= holdSpeedFactor;
            }
        }

        [HarmonyPatch(typeof(CustomButton), nameof(CustomButton.onClickEvent))]
        public static class StrongmanAbilityBlockPatch
        {
            public static bool Prefix() => !isRestricted(PlayerControl.LocalPlayer);
        }

        [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.ReportDeadBody))]
        public static class StrongmanReportBlockPatch
        {
            public static bool Prefix(PlayerControl __instance) => __instance == null || !isRestricted(__instance);
        }
    }
}
