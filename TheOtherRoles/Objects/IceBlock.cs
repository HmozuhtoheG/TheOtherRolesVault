using System;
using System.Collections.Generic;
using TheOtherRoles.Utilities;
using UnityEngine;

namespace TheOtherRoles.Objects
{
    public class IceBlock
    {
        public static Dictionary<byte, IceBlock> blocks = new();
        public static Dictionary<byte, DateTime> slowUntil = new();
        private static byte nextId = 0;
        private static Sprite iceBlockSprite;

        private const float FormDuration = 0.6f;
        private const float ShatterDuration = 0.35f;
        public const float TouchRadius = 0.55f;

        public byte id;
        public GameObject obj;
        public SpriteRenderer spriteRenderer;
        public Vector2 position;
        public DateTime spawnTime;
        public DateTime? shatterTime;
        public HashSet<byte> creditedPlayers = new();

        public static Sprite getIceBlockSprite()
        {
            if (iceBlockSprite) return iceBlockSprite;
            iceBlockSprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.PermafrostIceBlock.png", 200f);
            return iceBlockSprite;
        }

        private IceBlock(byte id, Vector2 pos)
        {
            this.id = id;
            position = pos;
            obj = new GameObject("PermafrostIceBlock_" + id);
            Vector3 p = new(pos.x, pos.y, pos.y / 1000f + 0.015f);
            obj.transform.position = p;
            obj.transform.localPosition = p;
            obj.transform.localScale = Vector3.zero;
            spriteRenderer = obj.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = getIceBlockSprite();
            Color c = spriteRenderer.color;
            c.a = 0f;
            spriteRenderer.color = c;
            spawnTime = DateTime.UtcNow;
            obj.SetActive(true);
        }

        public bool isMature => !shatterTime.HasValue && (DateTime.UtcNow - spawnTime).TotalSeconds >= FormDuration;

        public static IceBlock Create(Vector2 pos)
        {
            byte id = nextId++;
            var block = new IceBlock(id, pos);
            blocks[id] = block;
            return block;
        }

        public void Shatter()
        {
            if (shatterTime.HasValue) return;
            shatterTime = DateTime.UtcNow;
        }

        public static void Tick()
        {
            if (blocks.Count == 0) return;
            var now = DateTime.UtcNow;
            List<byte> expired = null;

            foreach (var kv in blocks)
            {
                var block = kv.Value;
                if (block.obj == null)
                {
                    (expired ??= new()).Add(kv.Key);
                    continue;
                }

                if (block.shatterTime.HasValue)
                {
                    float t = (float)(now - block.shatterTime.Value).TotalSeconds / ShatterDuration;
                    if (t >= 1f)
                    {
                        UnityEngine.Object.Destroy(block.obj);
                        (expired ??= new()).Add(kv.Key);
                        continue;
                    }
                    float shatterScale = Mathf.Lerp(1.15f, 0f, t);
                    block.obj.transform.localScale = Vector3.one * shatterScale;
                    Color sc = block.spriteRenderer.color;
                    sc.a = Mathf.Lerp(0.95f, 0f, t);
                    block.spriteRenderer.color = sc;
                    continue;
                }

                float age = (float)(now - block.spawnTime).TotalSeconds;
                float formT = Mathf.Clamp01(age / FormDuration);
                block.obj.transform.localScale = Vector3.one * Mathf.SmoothStep(0f, 1f, formT);
                Color col = block.spriteRenderer.color;
                col.a = Mathf.SmoothStep(0f, 0.95f, formT);
                block.spriteRenderer.color = col;
            }

            if (expired != null)
                foreach (var key in expired) blocks.Remove(key);
        }

        public static void ExpireOld(float lifetimeSeconds)
        {
            if (blocks.Count == 0) return;
            var now = DateTime.UtcNow;
            foreach (var block in blocks.Values)
            {
                if (block.shatterTime.HasValue) continue;
                if ((now - block.spawnTime).TotalSeconds >= lifetimeSeconds) block.Shatter();
            }
        }

        public static void ClearAll()
        {
            foreach (var block in blocks.Values)
                if (block.obj != null) UnityEngine.Object.Destroy(block.obj);
            blocks.Clear();
            slowUntil.Clear();
            nextId = 0;
        }
    }
}
