using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using TheOtherRoles.Utilities;
using UnityEngine;

namespace TheOtherRoles.Modules.Emotes
{
    public static class EmoteDance
    {
        private static readonly Dictionary<byte, PlayerControl> dancing = new();

        public static void Play(PlayerControl player)
        {
            if (player == null || player.cosmetics == null) return;
            var host = FastDestroyableSingleton<HudManager>.Instance;
            if (host == null) return;
            if (dancing.ContainsKey(player.PlayerId)) return;

            dancing[player.PlayerId] = player;
            host.StartCoroutine(CoDance(player).WrapToIl2Cpp());
        }

        public static void ResetAll()
        {
            foreach (var player in dancing.Values)
            {
                Restore(player);
            }
            dancing.Clear();
        }

        private static void Restore(PlayerControl player)
        {
            if (player == null || player.cosmetics == null) return;
            var origin = OriginalScale(player);
            if (origin.HasValue) player.cosmetics.transform.localScale = origin.Value;
        }

        private static readonly Dictionary<byte, Vector3> originalScales = new();

        private static Vector3? OriginalScale(PlayerControl player)
        {
            return originalScales.TryGetValue(player.PlayerId, out var scale) ? scale : null;
        }

        private static void SetSize(PlayerControl player, float x, float y)
        {
            if (!originalScales.TryGetValue(player.PlayerId, out var origin)) return;
            player.cosmetics.transform.localScale = new Vector3(origin.x * x, origin.y * y, origin.z);
        }

        private static IEnumerator CoDance(PlayerControl player)
        {
            originalScales[player.PlayerId] = player.cosmetics.transform.localScale;

            const float halfPi = Mathf.PI * 0.5f;

            float t = 0f;
            while (t < 0.15f)
            {
                if (!Alive(player)) { Cleanup(player); yield break; }
                SetSize(player, 1f, 1f + t / 0.15f * 0.1f);
                t += Time.deltaTime;
                yield return null;
            }

            for (int cycle = 0; cycle < 3; cycle++)
            {
                float p = 0f;
                while (p < halfPi)
                {
                    if (!Alive(player)) { Cleanup(player); yield break; }
                    float sin = Mathf.Sin(p);
                    SetSize(player, 1f + sin * 0.35f, 1.1f - sin * 0.3f);
                    yield return null;
                    p += Time.deltaTime * 5.5f;
                }

                p = 0f;
                while (p < halfPi)
                {
                    if (!Alive(player)) { Cleanup(player); yield break; }
                    float sin = Mathf.Sin(halfPi - p);
                    SetSize(player, 1f + sin * 0.35f, 1.1f - sin * 0.3f);
                    yield return null;
                    p += Time.deltaTime * 12f;
                }
            }

            t = 0f;
            while (t < 0.15f)
            {
                if (!Alive(player)) { Cleanup(player); yield break; }
                SetSize(player, 1f, 1.1f - t / 0.15f * 0.1f);
                t += Time.deltaTime;
                yield return null;
            }

            SetSize(player, 1f, 1f);
            Cleanup(player);
        }

        private static bool Alive(PlayerControl player)
        {
            return player != null && player.cosmetics != null && player.Data != null && !player.Data.IsDead;
        }

        private static void Cleanup(PlayerControl player)
        {
            Restore(player);
            if (player != null) originalScales.Remove(player.PlayerId);
            if (player != null) dancing.Remove(player.PlayerId);
        }
    }
}
