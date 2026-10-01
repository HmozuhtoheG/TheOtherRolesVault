using System;
using System.Collections.Generic;
using TheOtherRoles.Utilities;
using UnityEngine;

namespace TheOtherRoles.CustomGameModes
{
    [TORRPCHolder]
    public static class HiderInvisibility
    {
        public static int maxUses = 2;
        public static float duration = 5f;
        public static float cooldown = 20f;

        private static readonly Dictionary<byte, float> active = new();

        private static Sprite buttonSprite;

        public static Sprite getButtonSprite()
        {
            if (buttonSprite) return buttonSprite;
            buttonSprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.NinjaButton.png", 115f);
            return buttonSprite;
        }

        public static RemoteProcess<(byte playerId, bool on)> SetInvisible = new("HnSInvisible", (message, _) =>
        {
            var player = Helpers.playerById(message.playerId);
            if (player == null) return;

            if (message.on) Begin(player);
            else End(player);
        });

        public static bool isInvisible(PlayerControl player)
        {
            return player != null && active.ContainsKey(player.PlayerId);
        }

        public static float Remaining(PlayerControl player)
        {
            if (player == null) return 0f;
            return active.TryGetValue(player.PlayerId, out var remaining) ? remaining : 0f;
        }

        public static void Activate(PlayerControl player)
        {
            if (player == null) return;
            SetInvisible.Invoke((player.PlayerId, true));
        }

        public static void Update()
        {
            if (active.Count == 0) return;

            float dt = Time.deltaTime;
            List<byte> expired = null;

            foreach (var pair in active)
            {
                if (pair.Value - dt > 0f) continue;
                (expired ??= new List<byte>()).Add(pair.Key);
            }

            if (expired != null)
            {
                foreach (var playerId in expired)
                {
                    var player = Helpers.playerById(playerId);
                    if (player != null) SetInvisible.Invoke((playerId, false));
                    else active.Remove(playerId);
                }
                return;
            }

            var keys = new List<byte>(active.Keys);
            foreach (var playerId in keys) active[playerId] -= dt;
        }

        private static void Begin(PlayerControl player)
        {
            active[player.PlayerId] = duration;
            Apply(player, 0f);
        }

        private static void End(PlayerControl player)
        {
            active.Remove(player.PlayerId);
            Apply(player, 1f);
        }

        private static void Apply(PlayerControl player, float alpha)
        {
            try
            {
                Helpers.setInvisible(player, new Color(1f, 1f, 1f, alpha), alpha);
                HiderDisguise.SetAlpha(player.PlayerId, alpha);
            }
            catch (Exception ex)
            {
                TheOtherRolesPlugin.Logger.LogWarning($"[HnS] invisibility failed: {ex.Message}");
            }
        }

        public static void ResetAll()
        {
            foreach (var playerId in new List<byte>(active.Keys))
            {
                var player = Helpers.playerById(playerId);
                if (player != null) SetInvisible.Invoke((playerId, false));
            }
            active.Clear();
        }

        public static void clearAndReload()
        {
            ResetAll();
            maxUses = Mathf.RoundToInt(CustomOptionHolder.hideNSeekInvisCount.getFloat());
            duration = CustomOptionHolder.hideNSeekInvisDuration.getFloat();
            cooldown = CustomOptionHolder.hideNSeekInvisCooldown.getFloat();
        }
    }
}
