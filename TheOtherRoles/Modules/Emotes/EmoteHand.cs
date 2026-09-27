using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using TheOtherRoles.MetaContext;
using TheOtherRoles.Utilities;
using UnityEngine;

namespace TheOtherRoles.Modules.Emotes
{
    public static class EmoteHand
    {
        private const float HandScale = 1.4f;
        private const float HandZ = -0.5f;

        private static Sprite[] handSprites;
        private static Sprite waveSprite;
        private static bool waveChecked;

        private static readonly Dictionary<byte, GameObject> activeHands = new();

        public static Sprite GetHandSprite(int index)
        {
            if (handSprites == null)
            {
                handSprites = new Sprite[4];
                var texture = Helpers.loadTextureFromResources("TheOtherRoles.Resources.EmoteHand.png");
                if (texture == null) return null;
                int w = texture.width / 2;
                int h = texture.height / 2;
                for (int i = 0; i < 4; i++)
                {
                    int column = i % 2;
                    int row = i / 2;
                    handSprites[i] = texture.ToSprite(new Rect(column * w, (1 - row) * h, w, h), new Vector2(0.5f, 0.5f), 100f);
                }
            }
            return handSprites[index];
        }

        public static Sprite GetWaveSprite()
        {
            if (waveChecked) return waveSprite;
            waveChecked = true;
            try
            {
                foreach (var map in VanillaAsset.MapAsset)
                {
                    if (!map || !map.ExileCutscenePrefab) continue;
                    var sprite = map.ExileCutscenePrefab.Player.transform.GetChild(3).GetComponent<SpriteRenderer>().sprite;
                    if (sprite)
                    {
                        waveSprite = sprite;
                        break;
                    }
                }
            }
            catch { }
            return waveSprite;
        }

        private static float HeadLocalY(PlayerControl player)
        {
            float scale = player.transform.lossyScale.y;
            if (scale <= 0f) scale = 1f;
            var body = player.cosmetics != null && player.cosmetics.currentBodySprite != null
                ? player.cosmetics.currentBodySprite.BodySprite
                : null;
            if (body == null) return 0.75f;
            return (body.bounds.max.y - player.transform.position.y) / scale;
        }

        private static void Apply(SpriteRenderer hand, PlayerControl player, float x, float y, float angle, Sprite sprite)
        {
            if (hand == null || player == null) return;
            bool flip = player.cosmetics != null && player.cosmetics.FlipX;

            if (sprite != null) hand.sprite = sprite;

            hand.transform.localPosition = new Vector3(flip ? -x : x, HeadLocalY(player) + y, HandZ);
            hand.transform.localEulerAngles = new Vector3(0f, 0f, flip ? -angle : angle);
            hand.flipX = flip;
            hand.enabled = player.Visible;
        }

        public static void Play(HandEmote.HandKind kind, PlayerControl player)
        {
            if (player == null) return;
            var host = FastDestroyableSingleton<HudManager>.Instance;
            if (host == null) return;

            if (activeHands.TryGetValue(player.PlayerId, out var previous) && previous)
            {
                activeHands.Remove(player.PlayerId);
                Object.Destroy(previous);
            }

            var hand = Spawn(player);
            if (hand == null) return;
            activeHands[player.PlayerId] = hand.gameObject;

            IEnumerator routine = kind switch
            {
                HandEmote.HandKind.Good => CoGood(hand, player),
                HandEmote.HandKind.Bad => CoBad(hand, player),
                _ => CoWave(hand, player)
            };

            host.StartCoroutine(routine.WrapToIl2Cpp());
        }

        private static SpriteRenderer Spawn(PlayerControl player)
        {
            var holder = new GameObject("TOREmoteHand");
            holder.transform.SetParent(player.transform, false);
            holder.transform.localScale = new Vector3(HandScale, HandScale, 1f);
            var renderer = holder.AddComponent<SpriteRenderer>();
            var playerMaterial = FastDestroyableSingleton<HatManager>.Instance.PlayerMaterial;
            if (playerMaterial != null) renderer.material = playerMaterial;
            player.SetPlayerMaterialColors(renderer);
            return renderer;
        }

        private static IEnumerator CoGood(SpriteRenderer hand, PlayerControl player)
        {
            Apply(hand, player, 1.05f, 0.14f, 20f, GetHandSprite(0));
            yield return Effects.Wait(0.15f);
            Apply(hand, player, 1.1f, 0.1f, 0f, GetHandSprite(1));
            yield return Effects.Wait(1f);
            Finish(hand, player);
        }

        private static IEnumerator CoBad(SpriteRenderer hand, PlayerControl player)
        {
            Apply(hand, player, 1.1f, 0.15f, 0f, GetHandSprite(2));
            yield return Effects.Wait(0.15f);
            Apply(hand, player, 1.1f, 0.15f, 0f, GetHandSprite(3));

            for (int i = 0; i < 3; i++)
            {
                Apply(hand, player, 1.1f, 0.18f, 0f, null);
                yield return Effects.Wait(0.05f);
                Apply(hand, player, 1.1f, 0.14f, 0f, null);
                yield return Effects.Wait(0.05f);
                Apply(hand, player, 1.1f, 0.1f, 0f, null);
                yield return Effects.Wait(0.25f);
            }

            yield return Effects.Wait(0.8f);
            Finish(hand, player);
        }

        private static IEnumerator CoWave(SpriteRenderer hand, PlayerControl player)
        {
            var sprite = GetWaveSprite();
            if (sprite == null) sprite = GetHandSprite(2);

            Apply(hand, player, 1.05f, 0.6f, 30f, sprite);

            for (int i = 0; i < 5; i++)
            {
                yield return Effects.Wait(0.1f);
                Apply(hand, player, 0.98f, 0.6f, 40f, null);
                yield return Effects.Wait(0.1f);
                Apply(hand, player, 1.02f, 0.6f, 35f, null);
                yield return Effects.Wait(0.05f);
                Apply(hand, player, 1.05f, 0.6f, 30f, null);
            }

            Finish(hand, player);
        }

        private static void Finish(SpriteRenderer hand, PlayerControl player)
        {
            if (player != null && activeHands.TryGetValue(player.PlayerId, out var current) && current == hand.gameObject)
            {
                activeHands.Remove(player.PlayerId);
            }
            if (hand) Object.Destroy(hand.gameObject);
        }
    }
}
