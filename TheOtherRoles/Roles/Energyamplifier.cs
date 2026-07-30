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

        // ---- 可配置数值 ----
        public static float fieldDuration = 10f;
        public static float fieldRadius = 3f;
        public static float killCooldownReduction = 0.3f;
        public static float shieldDuration = 5f;      // 离场后护盾还能扛多久

        public static float maxEnergy = 100f;
        public static float activationCost = 50f;
        public static float energyRegenPerSecond = 4f;

        public static float baseSpeedBoost = 0.25f;      // 场里只有1人时的移速加成
        public static float extraSpeedPerPlayer = 0.1f;  // 每多1人额外加成
        public static float maxSpeedBoost = 1f;          // 加成上限

        // ---- 每个持有该角色的玩家自己的状态 ----
        public bool isFieldActive;
        public float fieldTimer;
        public float currentEnergy;

        // ---- 护盾记录：所有客户端各自独立计算，规则完全一致，不需要联机同步 ----
        public static Dictionary<byte, float> shieldedPlayers = new();
        private static int lastShieldUpdateFrame = -1;

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
        });

        public static RemoteProcess<(byte playerId, byte unused)> DeactivateField = new("EnergyAmplifierDeactivate", (message, _) =>
        {
            var role = getRole(Helpers.playerById(message.playerId));
            if (role == null) return;
            role.isFieldActive = false;
            role.fieldTimer = 0f;
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

            // 能量回复：只需要在拥有者自己的客户端算，因为只影响自己的按钮能不能按
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
            }
        }

        // 目前这一瞬间，target是否站在某个生效中的场地里（纯粹靠已同步的位置计算，任何客户端算出来都一样）
        public static bool IsInField(PlayerControl target)
        {
            if (target == null || target.Data == null || target.Data.IsDead) return false;
            return players.Any(amp =>
                amp.isFieldActive &&
                amp.player != null &&
                !amp.player.Data.IsDead &&
                Vector2.Distance(amp.player.GetTruePosition(), target.GetTruePosition()) <= fieldRadius);
        }

        // target所在的、人数最多的那个场地里，一共有几个人
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

        // 移速加成：场地里人越多，加成越强（有上限）
        public static float GetSpeedBoostFor(PlayerControl target)
        {
            if (!IsInField(target)) return 0f;
            int countInField = CountPlayersInSameField(target);
            float extra = extraSpeedPerPlayer * Mathf.Max(0, countInField - 1);
            return Mathf.Min(baseSpeedBoost + extra, maxSpeedBoost);
        }

        // 临时护盾：离场后还能再扛 shieldDuration 秒
        public static bool HasTemporaryShield(PlayerControl target)
        {
            return target != null && shieldedPlayers.TryGetValue(target.PlayerId, out var remaining) && remaining > 0f;
        }

        private static void UpdateShieldBookkeeping()
        {
            if (Time.frameCount == lastShieldUpdateFrame) return; // 保证每个客户端每帧只算一次
            lastShieldUpdateFrame = Time.frameCount;

            foreach (var p in PlayerControl.AllPlayerControls)
            {
                if (p == null || p.Data == null || p.Data.IsDead) continue;

                if (IsInField(p))
                {
                    shieldedPlayers[p.PlayerId] = shieldDuration; // 站在场里就一直刷新到满
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

        // ---- 移速：场地里人越多加成越强 ----
        [HarmonyPatch(typeof(PlayerPhysics), nameof(PlayerPhysics.FixedUpdate))]
        public static class PlayerPhysicsEnergyFieldPatch
        {
            public static void Postfix(PlayerPhysics __instance)
            {
                UpdateShieldBookkeeping(); // 内部有frame去重，这里调用多少次都安全

                var target = __instance.myPlayer;
                if (target == null || target.Data == null || target.Data.IsDead) return;
                if (!__instance.AmOwner || !target.CanMove) return;

                float boost = GetSpeedBoostFor(target);
                if (boost > 0f)
                    __instance.body.velocity *= 1 + boost;
            }
        }

        // ---- 刀人CD缩短：站在场里的杀手 ----
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
