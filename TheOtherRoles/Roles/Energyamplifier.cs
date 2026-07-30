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
    public class Energyamplifier : RoleBase<Energyamplifier>
    {
        public static Color color = new Color32(0, 255, 200, byte.MaxValue);

        public Energyamplifier()
        {
            RoleId = roleId = RoleId.Energyamplifier;
            isFieldActive = false;
            fieldTimer = 0f;
            currentEnergy = maxEnergy;
        }

        static public IEnumerable<DocumentReplacement> GetReplacementPart()
        {
            yield return new("%DURATION%", fieldDuration.ToString());
            yield return new("%RADIUS%", fieldRadius.ToString());
            yield return new("%SPEED%", baseSpeedBoost.ToString());
            yield return new("%MAXSPEED%", maxSpeedBoost.ToString());
            yield return new("%COST%", activationCost.ToString());
            yield return new("%MAXENERGY%", maxEnergy.ToString());
            yield return new("%SHIELD%", shieldDuration.ToString());
        }

        public static float fieldDuration = 10f;
        public static float fieldRadius = 3f;
        public static float killCooldownReduction = 0.3f;
        public static float shieldDuration = 5f;

        public static float maxEnergy = 100f;
        public static float activationCost = 50f;
        public static float energyRegenPerSecond = 4f;

        public static float baseSpeedBoost = 0.25f;
        public static float extraSpeedPerPlayer = 0.1f;
        public static float maxSpeedBoost = 1f;

        public bool isFieldActive;
        public float fieldTimer;
        public float currentEnergy;

        public static Dictionary<byte, float> shieldedPlayers = new();
        private static int lastShieldUpdateFrame = -1;

        private GameObject fieldIndicatorObject;
        private SpriteRenderer fieldIndicatorRenderer;

        private static Sprite ringSprite;
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
                    tex.SetPixel(x, y, (dist <= outerRadius && dist >= innerRadius) ? Color.white : clear);
                }
            }
            tex.Apply();
            ringSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            return ringSprite;
        }

        private void ShowFieldIndicator()
        {
            if (player != PlayerControl.LocalPlayer) return; 

            if (fieldIndicatorObject == null)
            {
                fieldIndicatorObject = new GameObject("EnergyFieldIndicator");
                fieldIndicatorObject.transform.SetParent(player.transform, false);
                fieldIndicatorObject.transform.localPosition = new Vector3(0f, 0f, 1f);
                fieldIndicatorRenderer = fieldIndicatorObject.AddComponent<SpriteRenderer>();
                fieldIndicatorRenderer.sprite = GetRingSprite();
                fieldIndicatorRenderer.color = new Color(color.r, color.g, color.b, 0.6f);
            }

            float diameter = fieldRadius * 2f;
            fieldIndicatorObject.transform.localScale = new Vector3(diameter, diameter, 1f);
            fieldIndicatorObject.SetActive(true);
        }

        private void HideFieldIndicator()
        {
            if (fieldIndicatorObject != null) fieldIndicatorObject.SetActive(false);
        }

        private static Sprite buttonSprite;
        public static Sprite getButtonSprite()
        {
            if (buttonSprite) return buttonSprite;
            buttonSprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.EnergyFieldButton.png", 115f);
            return buttonSprite;
        }

        public static RemoteProcess<(byte playerId, float duration)> ActivateField = new("EnergyAmplifierActivate", (message, _) =>
        {
            var role = getRole(Helpers.playerById(message.playerId));
            if (role == null) return;
            role.isFieldActive = true;
            role.fieldTimer = message.duration;
            role.ShowFieldIndicator();
        });

        public static RemoteProcess<(byte playerId, byte unused)> DeactivateField = new("EnergyAmplifierDeactivate", (message, _) =>
        {
            var role = getRole(Helpers.playerById(message.playerId));
            if (role == null) return;
            role.isFieldActive = false;
            role.fieldTimer = 0f;
            role.HideFieldIndicator();
        });

        public void TryActivate()
        {
            if (player != PlayerControl.LocalPlayer) return;
            if (isFieldActive) return;
            if (currentEnergy < activationCost) return;

            currentEnergy -= activationCost;
            ActivateField.Invoke((player.PlayerId, fieldDuration));
        }

        public override void FixedUpdate()
        {
            if (player != PlayerControl.LocalPlayer) return;

            if (!player.Data.IsDead)
                currentEnergy = Mathf.Min(maxEnergy, currentEnergy + energyRegenPerSecond * Time.fixedDeltaTime);

            if (isFieldActive)
            {
                fieldTimer -= Time.fixedDeltaTime;
                if (fieldTimer <= 0f)
                {
                    fieldTimer = 0f;
                    DeactivateField.Invoke((player.PlayerId, (byte)0));
                }
            }
        }

        public override void OnMeetingStart()
        {
            if (isFieldActive)
            {
                isFieldActive = false;
                fieldTimer = 0f;
                HideFieldIndicator();
            }
        }

        public static bool IsInField(PlayerControl target)
        {
            if (target == null || target.Data == null || target.Data.IsDead) return false;
            return players.Any(amp =>
                amp.isFieldActive &&
                amp.player != null &&
                !amp.player.Data.IsDead &&
                Vector2.Distance(amp.player.GetTruePosition(), target.GetTruePosition()) <= fieldRadius);
        }

        private static int CountPlayersInSameField(PlayerControl target)
        {
            int best = 0;
            foreach (var amp in players)
            {
                if (!amp.isFieldActive || amp.player == null || amp.player.Data.IsDead) continue;
                if (Vector2.Distance(amp.player.GetTruePosition(), target.GetTruePosition()) > fieldRadius) continue;

                int count = 0;
                foreach (var p in PlayerControl.AllPlayerControls)
                {
                    if (p == null || p.Data == null || p.Data.IsDead) continue;
                    if (Vector2.Distance(amp.player.GetTruePosition(), p.GetTruePosition()) <= fieldRadius) count++;
                }
                best = Mathf.Max(best, count);
            }
            return best;
        }

        public static float GetSpeedBoostFor(PlayerControl target)
        {
            if (!IsInField(target)) return 0f;
            int countInField = CountPlayersInSameField(target);
            float extra = extraSpeedPerPlayer * Mathf.Max(0, countInField - 1);
            return Mathf.Min(baseSpeedBoost + extra, maxSpeedBoost);
        }

        public static bool HasTemporaryShield(PlayerControl target)
        {
            return target != null && shieldedPlayers.TryGetValue(target.PlayerId, out var remaining) && remaining > 0f;
        }

        private static void UpdateShieldBookkeeping()
        {
            if (Time.frameCount == lastShieldUpdateFrame) return;
            lastShieldUpdateFrame = Time.frameCount;

            foreach (var p in PlayerControl.AllPlayerControls)
            {
                if (p == null || p.Data == null || p.Data.IsDead) continue;

                if (IsInField(p))
                {
                    shieldedPlayers[p.PlayerId] = shieldDuration;
                }
                else if (shieldedPlayers.TryGetValue(p.PlayerId, out var remaining))
                {
                    remaining -= Time.fixedDeltaTime;
                    if (remaining <= 0f) shieldedPlayers.Remove(p.PlayerId);
                    else shieldedPlayers[p.PlayerId] = remaining;
                }
            }
        }

        public static void clearAndReload()
        {
            fieldDuration = CustomOptionHolder.energyAmplifierDuration.getFloat();
            fieldRadius = CustomOptionHolder.energyAmplifierRadius.getFloat();
            killCooldownReduction = CustomOptionHolder.energyAmplifierKillCooldownReduction.getFloat();
            shieldDuration = CustomOptionHolder.energyAmplifierShieldDuration.getFloat();

            maxEnergy = CustomOptionHolder.energyAmplifierMaxEnergy.getFloat();
            activationCost = CustomOptionHolder.energyAmplifierActivationCost.getFloat();
            energyRegenPerSecond = CustomOptionHolder.energyAmplifierEnergyRegenRate.getFloat();

            baseSpeedBoost = CustomOptionHolder.energyAmplifierSpeedBoost.getFloat();
            extraSpeedPerPlayer = CustomOptionHolder.energyAmplifierExtraSpeedPerPlayer.getFloat();
            maxSpeedBoost = CustomOptionHolder.energyAmplifierMaxSpeedBoost.getFloat();

            shieldedPlayers = new();
            lastShieldUpdateFrame = -1;
            players = [];
        }

        [HarmonyPatch(typeof(PlayerPhysics), nameof(PlayerPhysics.FixedUpdate))]
        public static class PlayerPhysicsEnergyFieldPatch
        {
            public static void Postfix(PlayerPhysics __instance)
            {
                UpdateShieldBookkeeping();

                var target = __instance.myPlayer;
                if (target == null || target.Data == null || target.Data.IsDead) return;
                if (!__instance.AmOwner || !target.CanMove) return;

                float boost = GetSpeedBoostFor(target);
                if (boost > 0f)
                    __instance.body.velocity *= 1 + boost;
            }
        }

        [HarmonyPatch(typeof(PlayerControlSetCoolDownPatch), nameof(PlayerControlSetCoolDownPatch.SetKillTimerUnchecked))]
        public static class PlayerControlKillTimerEnergyFieldPatch
        {
            public static void Postfix(PlayerControl player, float time)
            {
                if (player == null || player.Data == null || player.Data.IsDead) return;
                if (IsInField(player))
                    player.killTimer = Mathf.Min(player.killTimer, time * (1f - killCooldownReduction));
            }
        }
    }
}
