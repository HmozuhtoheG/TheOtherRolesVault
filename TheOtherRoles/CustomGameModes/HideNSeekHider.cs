using System;
using System.Collections.Generic;
using TheOtherRoles.Utilities;
using UnityEngine;
using static TheOtherRoles.TheOtherRoles;

namespace TheOtherRoles.CustomGameModes
{
    [TORRPCHolder]
    public static class HiderDisguise
    {
        public const float DisguiseRange = 2.2f;
        public const float MinPropSize = 0.45f;
        public const float MaxPropSize = 2.5f;
        public const float MinPropAspect = 0.5f;
        public const float MaxPropAspect = 2f;

        private static readonly string[] PropNameBlocklist =
        {
            "confetti", "shadow", "garland", "balloon", "door", "vent", "arrow", "wall", "rail",
            "cam", "slime", "glow", "light", "tile", "floor", "decal", "spot", "fire", "smoke",
            "ring", "line", "grid", "pip", "splash", "mark", "particle", "fx", "glitter", "sparkle"
        };

        private static bool IsUsableProp(SpriteRenderer renderer)
        {
            if (renderer == null || renderer.sprite == null || !renderer.enabled) return false;

            var size = renderer.bounds.size;
            float max = Mathf.Max(size.x, size.y);
            float min = Mathf.Min(size.x, size.y);
            if (max < MinPropSize || max > MaxPropSize) return false;
            if (min <= 0.01f) return false;

            float aspect = max / min;
            if (aspect < MinPropAspect || aspect > MaxPropAspect) return false;

            string name = renderer.sprite.name.ToLowerInvariant();
            foreach (var blocked in PropNameBlocklist)
            {
                if (name.Contains(blocked)) return false;
            }

            return true;
        }

        private class State
        {
            public GameObject Holder;
            public SpriteRenderer Renderer;
            public string SpriteName;
            public float Scale;
        }

        private static readonly Dictionary<byte, State> disguised = new();

        private static Sprite disguiseSprite;

        public static Sprite getDisguiseSprite()
        {
            if (disguiseSprite) return disguiseSprite;
            disguiseSprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.CamoButton.png", 115f);
            return disguiseSprite;
        }

        public static RemoteProcess<(byte playerId, string spriteName, float scale)> ApplyDisguise =
            new("HnSDisguise", (message, _) =>
            {
                var player = Helpers.playerById(message.playerId);
                if (player == null) return;
                Apply(player, message.spriteName, message.scale);
            });

        public static RemoteProcess<byte> RemoveDisguise = RemotePrimitiveProcess.OfByte("HnSUndisguise", (message, _) =>
        {
            var player = Helpers.playerById(message);
            if (player == null) return;
            Restore(player);
        });

        public static bool isDisguised(PlayerControl player)
        {
            return player != null && disguised.ContainsKey(player.PlayerId);
        }

        private static List<SpriteRenderer> cachedProps;
        private static int cachedCount = -1;

        public static void Invalidate()
        {
            cachedProps = null;
            cachedCount = -1;
        }

        public static List<SpriteRenderer> ScanProps()
        {
            var ship = MapUtilities.CachedShipStatus;
            if (ship == null) return new List<SpriteRenderer>();

            int count = ship.GetComponentsInChildren<SpriteRenderer>(true).Length;
            if (cachedProps != null && count == cachedCount) return cachedProps;

            var result = new List<SpriteRenderer>();
            foreach (var renderer in ship.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (IsUsableProp(renderer)) result.Add(renderer);
            }

            cachedProps = result;
            cachedCount = count;
            TheOtherRolesPlugin.Logger.LogInfo($"[HnS] prop scan: {count} renderer(s) on the ship, {result.Count} usable as props");

            var seen = new HashSet<string>();
            var dump = new System.Text.StringBuilder();
            foreach (var prop in result)
            {
                if (!seen.Add(prop.sprite.name)) continue;
                var size = prop.bounds.size;
                dump.Append($"\n    {prop.sprite.name}  {size.x:0.##}x{size.y:0.##}");
                if (seen.Count >= 30) break;
            }
            TheOtherRolesPlugin.Logger.LogInfo($"[HnS] prop candidates ({seen.Count} distinct):{dump}");

            return result;
        }

        public static SpriteRenderer RandomProp()
        {
            var props = ScanProps();
            if (props.Count == 0) return null;
            return props[rnd.Next(props.Count)];
        }

        public static SpriteRenderer FindNearestProp(PlayerControl player)
        {
            if (player == null) return null;

            Vector2 origin = player.transform.position;
            SpriteRenderer best = null;
            float bestDistance = DisguiseRange;

            foreach (var prop in ScanProps())
            {
                float distance = Vector2.Distance(origin, prop.transform.position);
                if (distance > bestDistance) continue;
                bestDistance = distance;
                best = prop;
            }

            return best;
        }

        public static Sprite FindSpriteByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            var props = ScanProps();
            foreach (var prop in props)
            {
                if (prop.sprite.name == name) return prop.sprite;
            }

            return null;
        }

        public static bool TryDisguise(PlayerControl player)
        {
            if (player == null) return false;

            var props = ScanProps();
            if (props.Count == 0)
            {
                TheOtherRolesPlugin.Logger.LogWarning("[HnS] disguise: no prop found on the map");
                return false;
            }

            Vector2 origin = player.transform.position;
            SpriteRenderer best = null;
            float bestDistance = DisguiseRange;
            float nearest = float.MaxValue;

            foreach (var prop in props)
            {
                float distance = Vector2.Distance(origin, prop.transform.position);
                if (distance < nearest) nearest = distance;
                if (distance > bestDistance) continue;
                bestDistance = distance;
                best = prop;
            }

            if (best == null || best.sprite == null)
            {
                TheOtherRolesPlugin.Logger.LogWarning($"[HnS] disguise: {props.Count} prop(s) scanned, nearest is {nearest:0.##} away (range {DisguiseRange})");
                return false;
            }

            float world = Mathf.Max(best.bounds.size.x, best.bounds.size.y);
            float spriteSize = Mathf.Max(best.sprite.bounds.size.x, best.sprite.bounds.size.y);
            if (spriteSize <= 0.01f) return false;

            float scale = world / spriteSize;

            TheOtherRolesPlugin.Logger.LogInfo($"[HnS] disguise: {props.Count} prop(s), using '{best.sprite.name}' at {bestDistance:0.##}");
            ApplyDisguise.Invoke((player.PlayerId, best.sprite.name, scale));
            return true;
        }

        public static void Apply(PlayerControl player, string spriteName, float scale)
        {
            if (player == null || player.cosmetics == null) return;

            var sprite = FindSpriteByName(spriteName);
            if (sprite == null) return;

            Restore(player);

            HidePlayer(player);

            var holder = new GameObject("HnSDisguise");
            holder.transform.SetParent(player.transform, false);
            holder.transform.localPosition = Vector3.zero;

            var renderer = holder.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = 5;

            float spriteSize = Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y);
            float local = 1f;
            var parentScale = player.transform.lossyScale;
            if (spriteSize > 0.01f && parentScale.x > 0.001f) local = scale / parentScale.x;
            holder.transform.localScale = new Vector3(local, local, 1f);

            disguised[player.PlayerId] = new State
            {
                Holder = holder,
                Renderer = renderer,
                SpriteName = spriteName,
                Scale = scale
            };
        }

        public static void Undisguise(PlayerControl player)
        {
            if (player == null) return;
            RemoveDisguise.Invoke(player.PlayerId);
        }

        public static GameObject CreatePropObject(string name, string spriteName, float scale, Vector2 position)
        {
            var sprite = FindSpriteByName(spriteName);
            if (sprite == null) return null;

            var holder = new GameObject(name);
            holder.transform.position = new Vector3(position.x, position.y, position.y / 1000f + 0.001f);
            holder.AddSubmergedComponent(SubmergedCompatibility.Classes.ElevatorMover);

            var renderer = holder.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = 5;

            holder.transform.localScale = new Vector3(scale, scale, 1f);
            return holder;
        }

        public static void Restore(PlayerControl player)
        {
            if (player == null) return;

            if (!disguised.TryGetValue(player.PlayerId, out var state)) return;
            disguised.Remove(player.PlayerId);

            if (state.Holder != null) UnityEngine.Object.Destroy(state.Holder);

            ShowPlayer(player);
        }

        public static void RestoreAll()
        {
            foreach (var playerId in new List<byte>(disguised.Keys))
            {
                var player = Helpers.playerById(playerId);
                if (player != null) Restore(player);
            }
            disguised.Clear();
        }

        public static void SetAlpha(byte playerId, float alpha)
        {
            if (!disguised.TryGetValue(playerId, out var state)) return;
            if (state.Renderer == null) return;
            state.Renderer.color = new Color(1f, 1f, 1f, alpha);
        }

        public static void Update()
        {
            if (disguised.Count == 0) return;

            foreach (var pair in disguised)
            {
                var player = Helpers.playerById(pair.Key);
                var state = pair.Value;
                if (player == null || state.Holder == null) continue;

                bool flip = player.cosmetics != null && player.cosmetics.FlipX;
                if (state.Renderer != null) state.Renderer.flipX = flip;
                state.Holder.SetActive(player.Visible);
            }
        }

        private static void HidePlayer(PlayerControl player)
        {
            try
            {
                player.cosmetics.SetBodyCosmeticsVisible(false);
                if (player.cosmetics.nameText != null) player.cosmetics.nameText.gameObject.SetActive(false);
                Helpers.setInvisible(player, Color.clear, 0f);
            }
            catch (Exception ex)
            {
                TheOtherRolesPlugin.Logger.LogWarning($"[HnS] hide player failed: {ex.Message}");
            }
        }

        private static void ShowPlayer(PlayerControl player)
        {
            try
            {
                player.cosmetics.SetBodyCosmeticsVisible(true);
                if (player.cosmetics.nameText != null) player.cosmetics.nameText.gameObject.SetActive(true);
                if (player.cosmetics.currentBodySprite != null && player.cosmetics.currentBodySprite.BodySprite != null)
                    player.cosmetics.currentBodySprite.BodySprite.color = Color.white;

                Helpers.setInvisible(player, Color.white, 1f);
                player.setDefaultLook();
            }
            catch (Exception ex)
            {
                TheOtherRolesPlugin.Logger.LogWarning($"[HnS] show player failed: {ex.Message}");
            }
        }
    }
}
