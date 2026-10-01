using System;
using System.Collections.Generic;
using TheOtherRoles.Roles;
using TheOtherRoles.Utilities;
using UnityEngine;
using static TheOtherRoles.TheOtherRoles;

namespace TheOtherRoles.CustomGameModes
{
    [TORRPCHolder]
    public static class HideNSeekBots
    {
        private class Bot
        {
            public GameObject Holder;
            public Vector2 Position;
            public bool Dead;
        }

        private static readonly List<Bot> bots = new();
        private static bool spawned;

        public static RemoteProcess<(string spriteName, float scale, float x, float y)> PlaceBot =
            new("HnSBotPlace", (message, _) =>
            {
                var holder = HiderDisguise.CreatePropObject("HnSBot", message.spriteName, message.scale, new Vector2(message.x, message.y));
                if (holder == null) return;
                bots.Add(new Bot { Holder = holder, Position = new Vector2(message.x, message.y) });
            });

        public static RemoteProcess<(byte killerId, byte index)> KillBot = new("HnSBotKill", (message, _) =>
        {
            if (message.index >= bots.Count) return;

            var bot = bots[message.index];
            if (bot.Dead) return;

            bot.Dead = true;
            if (bot.Holder != null) UnityEngine.Object.Destroy(bot.Holder);

            if (PlayerControl.LocalPlayer != null && PlayerControl.LocalPlayer.PlayerId == message.killerId)
            {
                HideNSeek.punishTimer(HideNSeek.botPenalty);
            }
        });

        public static void SpawnBots(int count)
        {
            if (!AmongUsClient.Instance.AmHost) return;
            if (spawned) return;
            spawned = true;

            var used = new List<Vector2>();
            var props = HiderDisguise.ScanProps();

            for (int i = 0; i < count; i++)
            {
                var prop = props.Count > 0 ? props[rnd.Next(props.Count)] : null;
                if (prop == null || prop.sprite == null) break;

                float world = Mathf.Max(prop.bounds.size.x, prop.bounds.size.y);
                float spriteSize = Mathf.Max(prop.sprite.bounds.size.x, prop.sprite.bounds.size.y);
                if (spriteSize <= 0.01f) continue;

                var position = PickScatterPoint(used);
                used.Add(position);
                PlaceBot.Invoke((prop.sprite.name, world / spriteSize, position.x, position.y));
            }

            TheOtherRolesPlugin.Logger.LogInfo($"[HnS] placed {bots.Count} decoy prop(s)");
        }

        private static Vector2 PickScatterPoint(List<Vector2> used)
        {
            var ship = MapUtilities.CachedShipStatus;
            if (ship != null && ship.FastRooms != null)
            {
                var rooms = new List<PlainShipRoom>();
                foreach (var room in ship.FastRooms)
                {
                    if (room.Value != null && room.Value.roomArea != null) rooms.Add(room.Value);
                }

                if (rooms.Count > 0)
                {
                    for (int attempt = 0; attempt < 24; attempt++)
                    {
                        var picked = rooms[rnd.Next(rooms.Count)];
                        var bounds = picked.roomArea.bounds;

                        float x = bounds.min.x + (float)rnd.NextDouble() * (bounds.max.x - bounds.min.x);
                        float y = bounds.min.y + (float)rnd.NextDouble() * (bounds.max.y - bounds.min.y);
                        var point = new Vector2(x, y);

                        if (!picked.roomArea.OverlapPoint(point)) continue;
                        if (Itadori.blockedByWorld(point)) continue;

                        bool taken = false;
                        foreach (var other in used)
                        {
                            if (Vector2.Distance(other, point) < 0.8f) { taken = true; break; }
                        }
                        if (!taken) return point;
                    }
                }
            }

            var player = PlayerControl.LocalPlayer;
            return player != null ? (Vector2)player.transform.position : Vector2.zero;
        }

        public static int FindInRange(PlayerControl hunter)
        {
            if (hunter == null) return -1;

            float range = AmongUs.GameOptions.LegacyGameOptions.KillDistances[
                Mathf.Clamp(GameOptionsManager.Instance.currentNormalGameOptions.KillDistance, 0, 2)];

            Vector2 origin = hunter.transform.position;
            int best = -1;
            float bestDistance = range;

            for (int i = 0; i < bots.Count; i++)
            {
                if (bots[i].Dead) continue;
                float distance = Vector2.Distance(origin, bots[i].Position);
                if (distance > bestDistance) continue;
                bestDistance = distance;
                best = i;
            }

            return best;
        }

        public static void Kill(int index, PlayerControl killer)
        {
            if (killer == null || index < 0 || index >= bots.Count) return;
            KillBot.Invoke((killer.PlayerId, (byte)index));
        }

        public static void Clear()
        {
            foreach (var bot in bots)
            {
                if (bot.Holder != null) UnityEngine.Object.Destroy(bot.Holder);
            }
            bots.Clear();
            spawned = false;
        }
    }
}
