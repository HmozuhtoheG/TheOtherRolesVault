using System.Collections.Generic;
using TheOtherRoles.Modules;
using TheOtherRoles.Objects;
using TheOtherRoles.Patches;
using UnityEngine;
using static TheOtherRoles.TheOtherRoles;

namespace TheOtherRoles.Roles
{
    [TORRPCHolder]
    public class VoidEater : RoleBase<VoidEater>
    {
        public static Color color = Palette.ImpostorRed;

        public static float swallowCooldown = 24f;
        public static float speedBoostDuration = 5f;
        public static float speedBoostMultiplier = 0.3f;
        public static float killCooldownReduction = 10f;

        private const float BodyScanInterval = 0.2f;

        public float speedBoostTimer = 0f;
        public List<Arrow> bodyArrows = new();
        private int lastBodyCount = -1;
        private float scanTimer = 0f;
        private readonly List<DeadBody> seenBodies = new();

        public static CustomButton swallowButton;
        private static TMPro.TMP_Text countdownText;
        private static Sprite swallowButtonSprite;

        public VoidEater()
        {
            RoleId = roleId = RoleId.VoidEater;
        }

        public override void PostInit()
        {
            if (PlayerControl.LocalPlayer != player) return;
            var hudManager = HudManager.Instance;
            if (hudManager == null) return;

            swallowButton = new CustomButton(
                OnSwallowClick,
                () => PlayerControl.LocalPlayer.isRole(RoleId.VoidEater) && !player.Data.IsDead,
                () =>
                {
                    if (countdownText != null)
                        countdownText.text = Mathf.CeilToInt(swallowButton.Timer).ToString();
                    return HudManager.Instance != null && HudManager.Instance.ReportButton.graphic.color == Palette.EnabledColor && PlayerControl.LocalPlayer.CanMove;
                },
                () => { if (swallowButton != null) swallowButton.Timer = swallowButton.MaxTimer; },
                getSwallowButtonSprite(),
                CustomButton.ButtonPositions.lowerRowCenter,
                hudManager,
                KeyCode.F,
                buttonText: ModTranslation.getString("voidEaterSwallow"),
                abilityTexture: CustomButton.ButtonLabelType.UseButton
            );
            swallowButton.MaxTimer = swallowCooldown;
            swallowButton.Timer = swallowCooldown;

            if (countdownText == null && hudManager.roomTracker != null)
            {
                GameObject textObj = UnityEngine.Object.Instantiate(hudManager.roomTracker.gameObject);
                textObj.transform.SetParent(hudManager.transform);
                textObj.SetActive(true);
                UnityEngine.Object.DestroyImmediate(textObj.GetComponent<RoomTracker>());
                countdownText = textObj.GetComponent<TMPro.TMP_Text>();
                countdownText.transform.localPosition = new Vector3(0f, -1.8f, -10f);
                countdownText.fontSize = 1.0f;
                countdownText.alignment = TMPro.TextAlignmentOptions.Center;
            }
        }

        private void OnSwallowClick()
        {
            var local = PlayerControl.LocalPlayer;
            if (local == null || local.Data == null || !local.CanMove) return;

            DeadBody body = findSwallowTarget(local);
            if (body == null) return;

            var playerInfo = GameData.Instance != null ? GameData.Instance.GetPlayerById(body.ParentId) : null;
            if (playerInfo == null) return;

            RPCProcedure.CleanBody.Invoke((playerInfo.PlayerId, local.PlayerId));
            if (swallowButton != null) swallowButton.Timer = swallowButton.MaxTimer;

            float maxCooldown = local.GetKillCooldown();
            local.killTimer = Mathf.Max(0f, local.killTimer - killCooldownReduction);
            local.SetKillTimerUnchecked(local.killTimer, maxCooldown);

            speedBoostTimer = speedBoostDuration;
            scanTimer = 0f;
            SoundEffectsManager.play("vultureEat");
        }

        private static DeadBody findSwallowTarget(PlayerControl local)
        {
            Vector2 origin = local.GetTruePosition();
            float maxDistance = local.MaxReportDistance;
            DeadBody nearest = null;
            float nearestDistance = float.MaxValue;

            foreach (Collider2D collider2D in Physics2D.OverlapCircleAll(origin, maxDistance, Constants.PlayersOnlyMask))
            {
                if (collider2D.tag != "DeadBody") continue;

                DeadBody body = collider2D.GetComponent<DeadBody>();
                if (body == null || body.Reported) continue;

                Vector2 bodyPosition = body.TruePosition;
                float distance = Vector2.Distance(bodyPosition, origin);
                if (distance > maxDistance || distance >= nearestDistance) continue;
                if (PhysicsHelpers.AnythingBetween(origin, bodyPosition, Constants.ShipAndObjectsMask, false)) continue;

                nearest = body;
                nearestDistance = distance;
            }
            return nearest;
        }

        public static Sprite getSwallowButtonSprite()
        {
            if (swallowButtonSprite != null) return swallowButtonSprite;
            swallowButtonSprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.VoidEaterButton.png", 115f);
            return swallowButtonSprite;
        }

        public override void FixedUpdate()
        {
            if (player != PlayerControl.LocalPlayer) return;
            if (player.Data == null) return;

            if (speedBoostTimer > 0f)
            {
                speedBoostTimer -= Time.deltaTime;
                if (speedBoostTimer < 0f) speedBoostTimer = 0f;
            }

            scanTimer -= Time.deltaTime;
            if (scanTimer <= 0f)
            {
                scanTimer = BodyScanInterval;
                scanBodies();
            }

            updateArrowPositions();
        }

        private void scanBodies()
        {
            seenBodies.Clear();
            seenBodies.AddRange(UnityEngine.Object.FindObjectsOfType<DeadBody>());

            int bodyCount = seenBodies.Count;
            if (lastBodyCount >= 0 && bodyCount > lastBodyCount)
            {
                Helpers.flashScreen(Color.red, 0.1f, 0.3f, 0.5f, 0.2f,
                    ModTranslation.getString("voidEaterDeathFlash"), Color.white);
            }
            lastBodyCount = bodyCount;

            if (player.Data.IsDead || bodyArrows.Count == bodyCount) return;
            rebuildArrows();
        }

        private void rebuildArrows()
        {
            destroyArrows();
            for (int i = 0; i < seenBodies.Count; i++)
            {
                var body = seenBodies[i];
                if (body == null) continue;
                var arrow = new Arrow(color);
                arrow.arrow.SetActive(true);
                arrow.Update(body.transform.position);
                bodyArrows.Add(arrow);
            }
        }

        private void updateArrowPositions()
        {
            int count = Mathf.Min(bodyArrows.Count, seenBodies.Count);
            for (int i = 0; i < count; i++)
            {
                var arrow = bodyArrows[i];
                var body = seenBodies[i];
                if (arrow?.arrow == null || body == null) continue;
                arrow.Update(body.transform.position);
            }
        }

        private void destroyArrows()
        {
            for (int i = 0; i < bodyArrows.Count; i++)
            {
                var arrow = bodyArrows[i];
                if (arrow?.arrow != null) UnityEngine.Object.Destroy(arrow.arrow);
            }
            bodyArrows.Clear();
        }

        public override void OnMeetingStart()
        {
            if (swallowButton != null) swallowButton.setActive(false);
            if (countdownText != null) countdownText.text = "";
            speedBoostTimer = 0f;
        }

        public override void OnMeetingEnd(PlayerControl exiled = null)
        {
            if (swallowButton == null) return;
            swallowButton.MaxTimer = swallowCooldown;
            swallowButton.Timer = swallowCooldown;
        }

        public override void OnDeath(PlayerControl killer = null) => cleanup();

        public override void ResetRole(bool isShifted) => cleanup();

        private void cleanup()
        {
            destroyUi();
            destroyArrows();
            lastBodyCount = -1;
            speedBoostTimer = 0f;
            scanTimer = 0f;
        }

        private static void destroyUi()
        {
            if (swallowButton != null)
            {
                UnityEngine.Object.Destroy(swallowButton.actionButtonGameObject);
                swallowButton = null;
            }
            if (countdownText != null)
            {
                UnityEngine.Object.Destroy(countdownText.gameObject);
                countdownText = null;
            }
        }

        public static void clearAndReload()
        {
            swallowCooldown = CustomOptionHolder.voidEaterSwallowCooldown.getFloat();
            speedBoostDuration = CustomOptionHolder.voidEaterSpeedBoostDuration.getFloat();
            speedBoostMultiplier = CustomOptionHolder.voidEaterSpeedBoostMultiplier.getFloat();
            killCooldownReduction = CustomOptionHolder.voidEaterKillCooldownReduction.getFloat();
            destroyUi();
            swallowButtonSprite = null;
            players = [];
        }

        static public IEnumerable<HelpSprite> GetHelpSprites()
        {
            yield return new(getSwallowButtonSprite(), "voidEaterSwallowHint");
        }

        static public IEnumerable<DocumentReplacement> GetReplacementPart()
        {
            yield return new("%CDR%", Mathf.RoundToInt(killCooldownReduction).ToString());
            yield return new("%SBD%", Mathf.RoundToInt(speedBoostDuration).ToString());
        }
    }
}

//I see you
