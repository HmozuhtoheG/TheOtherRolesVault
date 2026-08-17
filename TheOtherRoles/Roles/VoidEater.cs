using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TheOtherRoles.Modules;
using TheOtherRoles.Objects;
using TheOtherRoles.Patches;
using TMPro;
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

        public float speedBoostTimer = 0f;
        public List<Arrow> bodyArrows = new List<Arrow>();
        private int lastBodyCount = -1;
        private bool bodiesDirty = true;

        public static CustomButton swallowButton;
        private static TMPro.TMP_Text countdownText;
        private static Sprite swallowButtonSprite;

        public VoidEater()
        {
            RoleId = roleId = RoleId.VoidEater;
            lastBodyCount = -1;
            speedBoostTimer = 0f;
            bodyArrows = new List<Arrow>();
        }

        public override void PostInit()
        {
            if (PlayerControl.LocalPlayer != player) return;
            var hudManager = HudManager.Instance;

            swallowButton = new CustomButton(
                OnSwallowClick,
                () => PlayerControl.LocalPlayer.isRole(RoleId.VoidEater) && !player.Data.IsDead,
                () =>
                {
                    if (countdownText != null)
                        countdownText.text = Mathf.CeilToInt(swallowButton.Timer).ToString();
                    return HudManager.Instance.ReportButton.graphic.color == Palette.EnabledColor && PlayerControl.LocalPlayer.CanMove;
                },
                () => { swallowButton.Timer = swallowButton.MaxTimer; },
                getSwallowButtonSprite(),
                CustomButton.ButtonPositions.lowerRowCenter,
                hudManager,
                KeyCode.F,
                buttonText: ModTranslation.getString("voidEaterSwallow"),
                abilityTexture: CustomButton.ButtonLabelType.UseButton
            );

            if (countdownText == null)
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
            if (Helpers.checkSuspendAction(PlayerControl.LocalPlayer, null)) return;
            foreach (Collider2D collider2D in Physics2D.OverlapCircleAll(PlayerControl.LocalPlayer.GetTruePosition(), PlayerControl.LocalPlayer.MaxReportDistance, Constants.PlayersOnlyMask))
            {
                if (collider2D.tag == "DeadBody")
                {
                    DeadBody component = collider2D.GetComponent<DeadBody>();
                    if (component != null && !component.Reported)
                    {
                        Vector2 truePosition = PlayerControl.LocalPlayer.GetTruePosition();
                        Vector2 truePosition2 = component.TruePosition;
                        if (Vector2.Distance(truePosition2, truePosition) <= PlayerControl.LocalPlayer.MaxReportDistance && PlayerControl.LocalPlayer.CanMove && !PhysicsHelpers.AnythingBetween(truePosition, truePosition2, Constants.ShipAndObjectsMask, false))
                        {
                            NetworkedPlayerInfo playerInfo = GameData.Instance.GetPlayerById(component.ParentId);
                            RPCProcedure.CleanBody.Invoke((playerInfo.PlayerId, PlayerControl.LocalPlayer.PlayerId));
                            swallowButton.Timer = swallowCooldown;
                            
                            var ke = PlayerControl.LocalPlayer;
                            float maxCooldown = ke.GetKillCooldown();
                            ke.killTimer = Mathf.Max(0f, ke.killTimer - 10f);
                            ke.SetKillTimerUnchecked(ke.killTimer, maxCooldown);
                            speedBoostTimer = speedBoostDuration;
                            SoundEffectsManager.play("vultureEat");
                            break;
                        }
                    }
                }
            }
        }

        public static Sprite getSwallowButtonSprite()
        {
            if (swallowButtonSprite != null) return swallowButtonSprite;
            swallowButtonSprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.VoidEaterButton.png", 115f);
            return swallowButtonSprite;
        }

        public static void clearAndReload()
        {
            swallowCooldown = CustomOptionHolder.voidEaterSwallowCooldown.getFloat();
            speedBoostDuration = CustomOptionHolder.voidEaterSpeedBoostDuration.getFloat();
            speedBoostMultiplier = CustomOptionHolder.voidEaterSpeedBoostMultiplier.getFloat();
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
            swallowButtonSprite = null;
            players = new List<VoidEater>();
        }

        public override void FixedUpdate()
        {
            if (player != PlayerControl.LocalPlayer) return;

            if (speedBoostTimer > 0f)
            {
                speedBoostTimer -= Time.deltaTime;
                if (speedBoostTimer < 0f) speedBoostTimer = 0f;
            }

            
            DeadBody[] deadBodies = UnityEngine.Object.FindObjectsOfType<DeadBody>();
            int bodyCount = deadBodies.Length;
            if (lastBodyCount >= 0 && bodyCount > lastBodyCount)
            {
                Helpers.flashScreen(Color.red, 0.1f, 0.3f, 0.5f, 0.2f,
                    ModTranslation.getString("voidEaterDeathFlash"), Color.white);
            }
            lastBodyCount = bodyCount;

            if (bodyArrows.Count != bodyCount)
                bodiesDirty = true;

            if (bodiesDirty && player.Data.IsDead == false)
            {
                for (int i = 0; i < bodyArrows.Count; i++)
                {
                    var arrow = bodyArrows[i];
                    if (arrow != null && arrow.arrow != null) UnityEngine.Object.Destroy(arrow.arrow);
                }
                bodyArrows = new List<Arrow>();

                for (int i = 0; i < deadBodies.Length; i++)
                {
                    bodyArrows.Add(new Arrow(Color.red));
                    bodyArrows[i].arrow.SetActive(true);
                    bodyArrows[i].Update(deadBodies[i].transform.position);
                }
                bodiesDirty = false;
            }
            else if (bodiesDirty == false && bodyArrows.Count == bodyCount && bodyCount > 0)
            {
                for (int i = 0; i < bodyArrows.Count; i++)
                {
                    var arrow = bodyArrows[i];
                    if (arrow != null) arrow.Update(deadBodies[i].transform.position);
                }
            }
        }

        public override void OnMeetingStart()
        {
            if (swallowButton != null) swallowButton.setActive(false);
            if (countdownText != null) countdownText.text = "";
            speedBoostTimer = 0f;
        }

        public override void OnMeetingEnd(PlayerControl exiled = null)
        {
        }

        public override void OnDeath(PlayerControl killer = null)
        {
            CleanupUI(this);
        }

        public override void ResetRole(bool isShifted)
        {
            CleanupUI(this);
        }

        private static void CleanupUI(VoidEater self)
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
            for (int i = 0; i < self.bodyArrows.Count; i++)
            {
                var arrow = self.bodyArrows[i];
                if (arrow != null && arrow.arrow != null) UnityEngine.Object.Destroy(arrow.arrow);
            }
            self.bodyArrows = new List<Arrow>();
            self.lastBodyCount = -1;
            self.speedBoostTimer = 0f;
        }

        static public IEnumerable<HelpSprite> GetHelpSprites()
        {
            yield return new(getSwallowButtonSprite(), "voidEaterSwallowHint");
        }
    }

    
    [HarmonyPatch(typeof(PlayerPhysics), nameof(PlayerPhysics.FixedUpdate))]
    public static class VoidEaterSpeedPatch
    {
        public static void Postfix(PlayerPhysics __instance)
        {
            if (!__instance.AmOwner || __instance.body == null) return;
            var player = __instance.myPlayer;
            if (player == null || player.Data.IsDead) return;
            var voidEater = VoidEater.getRole(player);
            if (voidEater == null || voidEater.speedBoostTimer <= 0f) return;
            __instance.body.velocity *= 1f + VoidEater.speedBoostMultiplier;
        }
    }
}







































//I see you