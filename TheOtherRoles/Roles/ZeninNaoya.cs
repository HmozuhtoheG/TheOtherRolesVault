using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using TheOtherRoles.Modules;
using TheOtherRoles.Patches;
using UnityEngine;
using static TheOtherRoles.TheOtherRoles;

namespace TheOtherRoles.Roles
{
    [TORRPCHolder]
    public class ZeninNaoya : RoleBase<ZeninNaoya>
    {
        public static Color color = Palette.ImpostorRed;

        public static float cooldown = 30f;
        public static float maxPathLength = 8f;
        public static float dashSpeed = 4.5f;
        public static float touchRadius = 0.8f;
        public static float freezeDuration = 1.5f;
        public static bool hasArrogance = true;

        private const float MinPathLength = 1.2f;
        private const float WallSampleStep = 0.2f;

        public bool isDashing;
        public float arroganceTimer;

        private static readonly Dictionary<byte, float> frozenUntil = [];
        private static readonly HashSet<byte> freezeCredited = [];

        private static bool aiming;
        private static Vector2 aimOrigin;
        private static Vector2 aimDirection;
        private static float aimLength;
        private static LineRenderer aimLine;
        private static GameObject aimLineObject;
        private static bool mouseOnButton;
        private static Coroutine dashRoutine;

        private static Sprite buttonSprite;
        private static readonly Collider2D[] wallBuffer = PhysicsHelpers.colliderHits;

        public ZeninNaoya()
        {
            RoleId = roleId = RoleId.ZeninNaoya;
            isDashing = false;
            arroganceTimer = 0f;
        }

        public static Sprite getButtonSprite()
        {
            if (buttonSprite) return buttonSprite;
            buttonSprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.BlockDashButton.png", 115f);
            return buttonSprite;
        }

        public static RemoteProcess<(byte naoyaId, Vector2 direction, float length)> StartDash = new("ZeninNaoyaDash", (message, __) =>
        {
            var role = getRole(Helpers.playerById(message.naoyaId));
            if (role == null || role.player == null) return;

            role.isDashing = true;
            role.BeginDash(message.direction, message.length);
        });

        public static RemoteProcess<byte> Freeze = RemotePrimitiveProcess.OfByte("ZeninNaoyaFreeze", (targetId, __) =>
        {
            var target = Helpers.playerById(targetId);
            if (target == null || target.Data == null || target.Data.IsDead) return;
            frozenUntil[targetId] = Time.time + freezeDuration;
        });

        public static bool IsFrozen(PlayerControl target)
        {
            return target != null && frozenUntil.TryGetValue(target.PlayerId, out float until) && Time.time < until;
        }

        public bool canUse => player != null && player.Data != null && !player.Data.IsDead
            && !isDashing && !aiming && !MeetingHud.Instance && !ExileController.Instance
            && HudManagerStartPatch.zeninDashButton != null && HudManagerStartPatch.zeninDashButton.Timer <= 0f;

        public void BeginDash(Vector2 direction, float length)
        {
            if (player != PlayerControl.LocalPlayer) return;

            var hud = HudManager.Instance;
            if (hud == null) return;

            if (dashRoutine != null) hud.StopCoroutine(dashRoutine);
            dashRoutine = hud.StartCoroutine(CoDash(direction, length).WrapToIl2Cpp());
        }

        private IEnumerator CoDash(Vector2 direction, float length)
        {
            var local = player;
            var physics = local.MyPhysics;
            if (physics == null) { isDashing = false; yield break; }

            Vector2 target = local.GetTruePosition() + direction * length;
            float savedKillTimer = local.killTimer;

            local.moveable = false;
            freezeCredited.Clear();

            yield return physics.WalkPlayerTo(target, 0.05f, dashSpeed, true);

            local.transform.position = new Vector3(target.x, target.y, local.transform.position.z);
            local.moveable = true;
            local.killTimer = savedKillTimer;

            isDashing = false;
            dashRoutine = null;
        }

        private void UpdateAiming()
        {
            if (player != PlayerControl.LocalPlayer) return;

            if (player.Data == null || player.Data.IsDead || isDashing || MeetingHud.Instance || ExileController.Instance)
            {
                StopAiming(false);
                return;
            }

            var button = HudManagerStartPatch.zeninDashButton;
            bool held = false;
            if (button != null && button.Timer <= 0f)
            {
                if (button.hotkey.HasValue && Input.GetKey(button.hotkey.Value)) held = true;
                if (mouseOnButton && Input.GetMouseButton(0)) held = true;
            }

            if (held && !aiming) StartAiming();
            else if (!held && aiming) StopAiming(true);

            if (aiming) UpdateAimLine();
        }

        private void StartAiming()
        {
            if (aiming) return;

            var hud = HudManager.Instance;
            if (hud == null) return;

            aiming = true;
            Helpers.enableCursor(true);

            if (aimLineObject == null)
            {
                aimLine = Helpers.SetUpLineRenderer("ZeninNaoyaAim", null, Vector3.zero, 5, 0.12f);
                aimLineObject = aimLine.gameObject;
                aimLine.useWorldSpace = true;
                aimLine.positionCount = 2;
                aimLine.sortingOrder = 460;
            }

            aimLine.gameObject.SetActive(true);
            aimLine.SetColors(new Color(color.r, color.g, color.b, 0.9f), new Color(color.r, color.g, color.b, 0.25f));
        }

        private void UpdateAimLine()
        {
            var camera = Camera.main;
            if (camera == null || aimLine == null) return;

            aimOrigin = player.GetTruePosition();
            Vector3 mouseWorld = camera.ScreenToWorldPoint(Input.mousePosition);
            Vector2 delta = new Vector2(mouseWorld.x, mouseWorld.y) - aimOrigin;

            if (delta.sqrMagnitude < 0.0001f)
            {
                aimDirection = Vector2.right;
                aimLength = 0f;
            }
            else
            {
                aimDirection = delta.normalized;
                aimLength = TruncateByWalls(aimOrigin, aimDirection, Mathf.Min(delta.magnitude, maxPathLength));
            }

            aimLine.SetPosition(0, new Vector3(aimOrigin.x, aimOrigin.y, 0f));
            aimLine.SetPosition(1, new Vector3(aimOrigin.x + aimDirection.x * aimLength, aimOrigin.y + aimDirection.y * aimLength, 0f));
        }

        private static float TruncateByWalls(Vector2 origin, Vector2 direction, float limit)
        {
            for (float travelled = WallSampleStep; travelled <= limit; travelled += WallSampleStep)
            {
                Vector2 point = origin + direction * travelled;
                int count = Physics2D.OverlapCircleNonAlloc(point, 0.16f, wallBuffer, Constants.ShipAndObjectsMask);
                for (int i = 0; i < count; i++)
                {
                    var collider = wallBuffer[i];
                    if (collider == null || collider.isTrigger) continue;
                    return Mathf.Max(0f, travelled - WallSampleStep);
                }
            }
            return limit;
        }

        private void StopAiming(bool release)
        {
            if (!aiming) return;
            aiming = false;

            if (aimLine != null) aimLine.gameObject.SetActive(false);
            Helpers.enableCursor(ClientOption.GetValue(ClientOption.ClientOptionType.ToggleCursor) == 1);

            if (!release) return;
            if (aimLength < MinPathLength) return;
            if (player == null || player.Data == null || player.Data.IsDead) return;

            var button = HudManagerStartPatch.zeninDashButton;
            if (button == null || button.Timer > 0f) return;

            button.Timer = cooldown;
            StartDash.Invoke((player.PlayerId, aimDirection, aimLength));
        }

        public static void applyTouchFreeze(PlayerControl naoya)
        {
            if (naoya == null || naoya.Data == null) return;

            Vector2 origin = naoya.GetTruePosition();
            int count = 0;

            foreach (PlayerControl p in PlayerControl.AllPlayerControls)
            {
                if (p == null || p.Data == null || p.Data.IsDead || p == naoya) continue;
                if (freezeCredited.Contains(p.PlayerId)) continue;
                if (Vector2.Distance(p.GetTruePosition(), origin) > touchRadius) continue;

                freezeCredited.Add(p.PlayerId);
                Freeze.Invoke(p.PlayerId);
                count++;
            }

            if (count <= 0) return;

            int total = freezeCredited.Count;
            _ = new StaticAchievementToken("zeninNaoya.common1");
            if (total >= 2) _ = new StaticAchievementToken("zeninNaoya.another1");
            if (total >= 3) _ = new StaticAchievementToken("zeninNaoya.challenge");
        }

        public static void registerLocalAimButton(bool over)
        {
            mouseOnButton = over;
        }

        public override void OnKill(PlayerControl target)
        {
            if (target != null && HeavenlyRestriction.isRestricted(target))
                _ = new StaticAchievementToken("zeninNaoya.heavenlyRestriction");

            if (!hasArrogance) return;
            if (!IsFrozen(target)) return;
            arroganceTimer = 1.5f;
        }

        public override void FixedUpdate()
        {
            if (player == null || player.Data == null) return;

            if (player == PlayerControl.LocalPlayer)
            {
                if (player.Data.IsDead) StopAiming(false);
                UpdateAiming();

                if (isDashing)
                {
                    player.killTimer = Mathf.Max(player.killTimer, 0.5f);
                    applyTouchFreeze(player);
                }

                if (arroganceTimer > 0f)
                {
                    arroganceTimer -= Time.fixedDeltaTime;
                    if (player.killTimer > 0f) player.killTimer = 0f;
                    if (arroganceTimer <= 0f) arroganceTimer = 0f;
                }
            }
        }

        public override void OnMeetingStart()
        {
            StopAiming(false);
            isDashing = false;
        }

        public override void OnDeath(PlayerControl killer = null)
        {
            StopAiming(false);
            isDashing = false;
        }

        public override void ResetRole(bool isShifted)
        {
            StopAiming(false);
            isDashing = false;
        }

        static public IEnumerable<DocumentReplacement> GetReplacementPart()
        {
            yield return new("%CD%", Mathf.RoundToInt(cooldown).ToString());
            yield return new("%LEN%", maxPathLength.ToString("0.#"));
            yield return new("%TOUCH%", touchRadius.ToString("0.##"));
            yield return new("%FREEZE%", freezeDuration.ToString("0.#"));
        }

        public static void clearAndReload()
        {
            cooldown = CustomOptionHolder.zeninNaoyaCooldown.getFloat();
            maxPathLength = CustomOptionHolder.zeninNaoyaPathLength.getFloat();
            dashSpeed = CustomOptionHolder.zeninNaoyaDashSpeed.getFloat();
            touchRadius = CustomOptionHolder.zeninNaoyaTouchRadius.getFloat();
            freezeDuration = CustomOptionHolder.zeninNaoyaFreezeDuration.getFloat();
            hasArrogance = CustomOptionHolder.zeninNaoyaArrogance.getBool();

            aiming = false;
            aimLength = 0f;
            frozenUntil.Clear();
            freezeCredited.Clear();
            mouseOnButton = false;
            dashRoutine = null;

            if (aimLine != null) aimLine.gameObject.SetActive(false);
            Helpers.enableCursor(ClientOption.GetValue(ClientOption.ClientOptionType.ToggleCursor) == 1);

            if (aimLineObject != null) UnityEngine.Object.Destroy(aimLineObject);
            aimLineObject = null;
            aimLine = null;

            players = [];
        }

        [HarmonyPatch(typeof(PlayerPhysics), nameof(PlayerPhysics.FixedUpdate))]
        public static class ZeninNaoyaFreezePatch
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
    }
}
