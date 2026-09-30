using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TheOtherRoles.MetaContext;
using TheOtherRoles.Modules;
using TheOtherRoles.Objects;
using TheOtherRoles.Patches;
using UnityEngine;
using static TheOtherRoles.TheOtherRoles;

namespace TheOtherRoles.Roles
{
    [TORRPCHolder]
    public class Gojo : RoleBase<Gojo>
    {
        public static Color color = new Color32(130, 200, 255, byte.MaxValue);

        public static float maxCursedEnergy = 100f;
        public static float drainPerSecond = 8f;
        public static float recoverPerTask = 25f;
        public static float infinityRadius = 2.5f;

        public bool isInfinityActive;
        public float cursedEnergy;

        public bool votedSukuna;
        public bool sukunaExiled;
        public bool hasDied;

        public static Color sixEyesScreenColor = new Color(0f, 1f, 1f, 0.22f);
        private static Dictionary<byte, Arrow> sixEyesArrows = [];
        private static SpriteRenderer sixEyesScreen;

        public static bool buttonHovering;

        private GameObject indicatorObject;
        private SpriteRenderer indicatorRenderer;

        private static Sprite ringSprite;
        private static Sprite buttonSprite;
        public Gojo()
        {
            RoleId = roleId = RoleId.Gojo;
            isInfinityActive = false;
            cursedEnergy = maxCursedEnergy;
        }

        public static Sprite getButtonSprite()
        {
            if (buttonSprite) return buttonSprite;
            buttonSprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.EnergyFieldButton.png", 115f);
            return buttonSprite;
        }

        private static void ClearSixEyes()
        {
            foreach (var arrow in sixEyesArrows.Values)
            {
                if (arrow != null && arrow.arrow != null) Object.Destroy(arrow.arrow);
            }
            sixEyesArrows.Clear();

            if (sixEyesScreen != null)
            {
                sixEyesScreen.enabled = false;
                sixEyesScreen.gameObject.SetActive(false);
            }
        }

        private static void UpdateSixEyes()
        {
            var localPlayer = PlayerControl.LocalPlayer;
            bool visible = localPlayer != null && localPlayer.Data != null && !localPlayer.Data.IsDead
                && !MeetingHud.Instance && !ExileController.Instance && !LobbyBehaviour.Instance;

            if (!visible)
            {
                ClearSixEyes();
                return;
            }

            if (sixEyesScreen == null && HudManager.Instance != null && HudManager.Instance.FullScreen != null)
            {
                sixEyesScreen = Object.Instantiate(HudManager.Instance.FullScreen, HudManager.Instance.transform);
                sixEyesScreen.name = "GojoSixEyesOverlay";
                sixEyesScreen.color = sixEyesScreenColor;
            }
            if (sixEyesScreen != null)
            {
                sixEyesScreen.enabled = true;
                sixEyesScreen.gameObject.SetActive(true);
            }

            foreach (PlayerControl p in PlayerControl.AllPlayerControls)
            {
                if (p == null || p.Data == null || p == localPlayer) continue;

                bool alive = !p.Data.IsDead && !p.Data.Disconnected;
                if (!sixEyesArrows.TryGetValue(p.PlayerId, out var arrow))
                {
                    if (!alive) continue;
                    arrow = new Arrow(color);
                    sixEyesArrows[p.PlayerId] = arrow;
                }
                if (arrow?.arrow == null) continue;

                if (!alive)
                {
                    if (arrow.arrow.activeSelf) arrow.arrow.SetActive(false);
                    continue;
                }

                if (!arrow.arrow.activeSelf) arrow.arrow.SetActive(true);
                arrow.Update(p.transform.position);
            }
        }

        private static Sprite GetRingSprite()
        {
            if (ringSprite) return ringSprite;

            int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
            Color clear = new Color(0, 0, 0, 0);
            Vector2 center = new Vector2(size / 2f, size / 2f);
            float outerRadius = size / 2f - 2f;
            float innerRadius = outerRadius - 6f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), center);
                    tex.SetPixel(x, y, dist <= outerRadius && dist >= innerRadius ? Color.white : clear);
                }
            }
            tex.Apply();
            ringSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            return ringSprite;
        }

        public static RemoteProcess<(byte playerId, bool active)> SetInfinity = new("GojoSetInfinity", (message, __) =>
        {
            var role = getRole(Helpers.playerById(message.playerId));
            if (role == null || role.player == null) return;

            role.isInfinityActive = message.active;
            if (message.active)
            {
                if (role.player == PlayerControl.LocalPlayer) _ = new StaticAchievementToken("gojo.common1");
                role.ShowIndicator();
                SoundEffectsManager.playAtPosition("medicShield", role.player.GetTruePosition(), 1f, 8f);
            }
            else
            {
                role.HideIndicator();
            }
        });

        private void ShowIndicator()
        {
            if (player == null || player != PlayerControl.LocalPlayer) return;

            if (indicatorObject == null)
            {
                indicatorObject = new GameObject("GojoInfinityIndicator");
                indicatorObject.transform.SetParent(player.transform, false);
                indicatorObject.transform.localPosition = new Vector3(0f, 0f, 1f);
                indicatorRenderer = indicatorObject.AddComponent<SpriteRenderer>();
                indicatorRenderer.sprite = GetRingSprite();
                indicatorRenderer.color = new Color(color.r, color.g, color.b, 0.4f);
            }

            float spriteSize = GetRingSprite().rect.width / GetRingSprite().pixelsPerUnit;
            indicatorObject.transform.localScale = Vector3.one * (infinityRadius * 2f / spriteSize);
            indicatorObject.SetActive(true);
        }

        private void HideIndicator()
        {
            if (indicatorObject != null) indicatorObject.SetActive(false);
        }

        private static bool IsHolding()
        {
            if (HudManagerStartPatch.gojoInfinityButton == null) return false;
            var hotkey = HudManagerStartPatch.gojoInfinityButton.hotkey;
            if (hotkey.HasValue && Input.GetKey(hotkey.Value)) return true;

            var actionButton = HudManagerStartPatch.gojoInfinityButton.actionButton;
            if (actionButton == null || !actionButton.gameObject.activeInHierarchy) return false;
            return buttonHovering && Input.GetMouseButton(0);
        }

        public void SetActiveInfinity(bool active)
        {
            if (player == null) return;
            if (isInfinityActive == active) return;
            SetInfinity.Invoke((player.PlayerId, active));
        }

        public override void FixedUpdate()
        {
            if (player != PlayerControl.LocalPlayer) return;

            if (HudManagerStartPatch.gojoInfinityEnergyText != null && player.Data != null)
                HudManagerStartPatch.gojoInfinityEnergyText.text = $"{Mathf.FloorToInt(cursedEnergy)}/{Mathf.FloorToInt(maxCursedEnergy)}";

            if (player.Data == null || player.Data.IsDead || MeetingHud.Instance || ExileController.Instance)
            {
                SetActiveInfinity(false);
                UpdateSixEyes();
                return;
            }

            UpdateSixEyes();

            if (isInfinityActive)
            {
                cursedEnergy -= drainPerSecond * Time.fixedDeltaTime;
                if (cursedEnergy <= 0f)
                {
                    cursedEnergy = 0f;
                    SetActiveInfinity(false);
                    _ = new StaticAchievementToken("gojo.another1");
                    new CustomMessage(ModTranslation.getString("gojoEnergyDepleted"), 3f);
                }
                else if (!IsHolding())
                {
                    SetActiveInfinity(false);
                }
            }
            else if (IsHolding() && cursedEnergy > 0f)
            {
                SetActiveInfinity(true);
            }
        }

        private static AudioClip assignmentClip;
        private static bool assignmentClipLoaded;

        private static AudioClip getAssignmentClip()
        {
            if (assignmentClipLoaded) return assignmentClip;
            assignmentClipLoaded = true;
            assignmentClip = Helpers.loadWavFromResources("TheOtherRoles.Resources.Gojo.wav", "TORV_Gojo");
            return assignmentClip;
        }

        public override void PostInit()
        {
            if (player != PlayerControl.LocalPlayer) return;
            if (ClientOption.GetValue(ClientOption.ClientOptionType.EnableSoundEffects) == 0) return;
            if (!Constants.ShouldPlaySfx()) return;

            var clip = getAssignmentClip();
            if (clip != null) SoundManager.Instance.PlaySound(clip, false, 0.8f);
        }

        public override void OnMeetingStart()
        {
            isInfinityActive = false;
            HideIndicator();
            ClearSixEyes();
            buttonHovering = false;
        }

        public override void OnDeath(PlayerControl killer = null)
        {
            isInfinityActive = false;
            HideIndicator();
            ClearSixEyes();
            hasDied = true;
        }

        public override void OnMeetingEnd(PlayerControl exiled = null)
        {
            if (player != PlayerControl.LocalPlayer) return;
            if (!votedSukuna) return;
            if (exiled == null || !exiled.isRole(RoleId.Sukuna)) return;
            sukunaExiled = true;
        }

        [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.CastVote))]
        public static class GojoChallengerVotePatch
        {
            public static void Postfix([HarmonyArgument(0)] InnerNet.PlayerId srcPlayerId, [HarmonyArgument(1)] InnerNet.PlayerId suspectPlayerId)
            {
                var gojo = local;
                if (gojo == null || gojo.player != PlayerControl.LocalPlayer) return;
                if ((byte)srcPlayerId != PlayerControl.LocalPlayer.PlayerId) return;

                var suspect = Helpers.playerById((byte)suspectPlayerId);
                if (suspect != null && suspect.isRole(RoleId.Sukuna)) gojo.votedSukuna = true;
            }
        }

        [HarmonyPatch(typeof(EndGameManager), nameof(EndGameManager.SetEverythingUp))]
        public static class GojoChallengerEndPatch
        {
            public static void Postfix()
            {
                var gojo = local;
                if (gojo == null || !gojo.sukunaExiled || gojo.hasDied) return;
                if (PlayerControl.LocalPlayer == null || PlayerControl.LocalPlayer.Data == null) return;

                bool won = false;
                var winners = EndGameResult.CachedWinners;
                if (winners != null)
                {
                    foreach (var winner in winners)
                    {
                        if (winner == null || !winner.IsYou) continue;
                        won = true;
                        break;
                    }
                }
                if (!won) return;

                _ = new StaticAchievementToken("gojo.challenger");
            }
        }

        public static void onTaskComplete(PlayerControl pc)
        {
            var gojo = getRole(pc);
            if (gojo == null || pc != PlayerControl.LocalPlayer) return;

            gojo.cursedEnergy = Mathf.Min(maxCursedEnergy, gojo.cursedEnergy + recoverPerTask);
            new CustomMessage(string.Format(ModTranslation.getString("gojoEnergyRecovered"), Mathf.RoundToInt(recoverPerTask)), 3f);
        }

        public static bool isInfinityProtected(PlayerControl target)
        {
            if (target == null || target.Data == null || target.Data.IsDead) return false;
            return players.Any(x => x.isInfinityActive && x.player == target && x.player.Data != null && !x.player.Data.IsDead);
        }

        static public IEnumerable<DocumentReplacement> GetReplacementPart()
        {
            yield return new("%MAX%", Mathf.RoundToInt(maxCursedEnergy).ToString());
            yield return new("%DRAIN%", drainPerSecond.ToString("0.#"));
            yield return new("%TASK%", Mathf.RoundToInt(recoverPerTask).ToString());
            yield return new("%R%", infinityRadius.ToString("0.##"));
            yield return new("%DURATION%", (maxCursedEnergy / Mathf.Max(0.01f, drainPerSecond)).ToString("0.#"));
        }

        public static void clearAndReload()
        {
            maxCursedEnergy = CustomOptionHolder.gojoMaxCursedEnergy.getFloat();
            drainPerSecond = CustomOptionHolder.gojoDrainPerSecond.getFloat();
            recoverPerTask = CustomOptionHolder.gojoRecoverPerTask.getFloat();
            infinityRadius = CustomOptionHolder.gojoRadius.getFloat();
            buttonHovering = false;
            ClearSixEyes();
            players = [];
        }

        [HarmonyPatch(typeof(PlayerPhysics), nameof(PlayerPhysics.FixedUpdate))]
        public static class GojoInfinityPhysicsPatch
        {
            public static void Postfix(PlayerPhysics __instance)
            {
                var target = __instance.myPlayer;
                if (target == null || target.Data == null || target.Data.IsDead) return;
                if (!__instance.AmOwner || !target.CanMove) return;
                if (MeetingHud.Instance || ExileController.Instance || target.inVent) return;

                foreach (var gojo in Gojo.players)
                {
                    if (!gojo.isInfinityActive || gojo.player == null || gojo.player == target) continue;
                    if (gojo.player.Data == null || gojo.player.Data.IsDead) continue;

                    Vector2 gojoPos = gojo.player.transform.position;
                    Vector2 selfPos = __instance.body.position;
                    Vector2 delta = selfPos - gojoPos;
                    float distance = delta.magnitude;
                    if (distance >= infinityRadius) continue;

                    Vector2 direction = distance > 0.001f ? delta / distance : Vector2.right;

                    __instance.body.velocity = Vector2.zero;

                    if (gojo.player == PlayerControl.LocalPlayer) _ = new StaticAchievementToken("gojo.challenge");

                    if (!PhysicsHelpers.AnyNonTriggersBetween(gojoPos, direction, infinityRadius, Constants.ShipAndObjectsMask))
                    {
                        Vector2 safePosition = gojoPos + direction * infinityRadius;
                        __instance.body.position = safePosition;
                        __instance.transform.position = safePosition;
                        continue;
                    }

                    float slide = Mathf.Min(0.5f, infinityRadius - distance);
                    Vector2 tangent = new Vector2(-direction.y, direction.x);
                    for (int i = 0; i < 2; i++)
                    {
                        Vector2 slideDirection = i == 0 ? tangent : -tangent;
                        if (PhysicsHelpers.AnyNonTriggersBetween(selfPos, slideDirection, slide, Constants.ShipAndObjectsMask)) continue;

                        Vector2 slidePosition = selfPos + slideDirection * slide;
                        __instance.body.position = slidePosition;
                        __instance.transform.position = slidePosition;
                        break;
                    }
                }
            }
        }

        [HarmonyPatch(typeof(GameData), nameof(GameData.CompleteTask))]
        public static class GojoTaskCompletePatch
        {
            public static void Postfix([HarmonyArgument(0)] PlayerControl pc, [HarmonyArgument(1)] uint taskId)
                => onTaskComplete(pc);
        }
    }
}
