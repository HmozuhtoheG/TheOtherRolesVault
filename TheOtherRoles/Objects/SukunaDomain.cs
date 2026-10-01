using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using TheOtherRoles.Roles;
using UnityEngine;

namespace TheOtherRoles.Objects
{
    public static class SukunaDomain
    {
        public const int EmberOrder = 395;
        public const int RimOrder = 400;
        public const int RimLineOrder = 401;
        public const int VignetteOrder = 402;
        public const int SkyOrder = 403;
        public const int GroundCutOrder = 408;
        public const int SlashOrder = 410;
        public const int ShockOrder = 411;
        public const int ExpandOrder = 420;

        private const int EmberCount = 12;
        private const int SkySize = 256;
        private const float SkyHoleRatio = 0.125f;
        private const float SettleDuration = 1.4f;
        private const float ShakeInterval = 0.25f;
        private const float VignetteFloor = 0.12f;
        private const float GroundCutInterval = 0.22f;
        private const float SlashBaseSize = 0.6f;
        private const float SlashFadeDuration = 0.3f;

        private static readonly Color CursedRed = new(0.78f, 0.05f, 0.05f);
        private static readonly Color CursedRedBright = new(1f, 0.16f, 0.12f);
        private static readonly Color CursedEmber = new(1f, 0.34f, 0.18f);
        private static readonly Color RimLineColor = new(1f, 0.72f, 0.66f);

        private static GameObject root;
        private static GameObject expandRoot;
        private static SpriteRenderer rimGlow;
        private static SpriteRenderer rimLine;
        private static SpriteRenderer vignette;
        private static SpriteRenderer sky;
        private static readonly List<(SpriteRenderer renderer, float angle, float dist, float speed, float life, float maxLife)> embers = new();

        private static Coroutine routine;
        private static Coroutine ambienceRoutine;

        private static Vector2 domainCenter;
        private static float domainRadius = 5f;
        private static float ambienceTime;
        private static float settle;
        private static float nextShake;
        private static float nextGroundCut;

        private static Sprite glowSprite;
        private static Sprite rimSprite;
        private static Sprite rimThinSprite;
        private static Sprite shockSprite;
        private static Sprite vignetteSprite;
        private static Sprite skySprite;

        private static int WorldLayer()
        {
            int ship = LayerMask.NameToLayer("Ship");
            if (ship >= 0) return ship;
            var local = PlayerControl.LocalPlayer;
            return local != null ? local.gameObject.layer : 0;
        }

        private static float SpriteRadius(Sprite sprite)
        {
            if (sprite == null) return 1f;
            return sprite.rect.width * 0.5f / sprite.pixelsPerUnit;
        }

        private static float EaseOutBack(float p)
        {
            const float c1 = 1.9f;
            const float c3 = c1 + 1f;
            float x = p - 1f;
            return 1f + c3 * x * x * x + c1 * x * x;
        }

        private static GameObject NewRoot(string name)
        {
            var obj = new GameObject(name) { layer = WorldLayer() };
            var ship = ShipStatus.Instance;
            if (ship != null)
            {
                obj.transform.SetParent(ship.transform, false);
                obj.transform.localPosition = Vector3.zero;
                obj.transform.localRotation = Quaternion.identity;
                obj.transform.localScale = Vector3.one;
            }
            return obj;
        }

        private static SpriteRenderer AddSprite(Transform parent, string name, Sprite sprite, int order, Color color)
        {
            if (sprite == null) return null;

            var obj = new GameObject(name) { layer = WorldLayer() };
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = Vector3.zero;

            var renderer = obj.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            renderer.sortingOrder = order;
            return renderer;
        }

        private static void SetActive(SpriteRenderer renderer, bool active)
        {
            if (renderer != null && renderer.gameObject != null) renderer.gameObject.SetActive(active);
        }

        private static Sprite GlowSprite()
        {
            if (glowSprite) return glowSprite;

            const int size = 256;
            var texture = new Texture2D(size, size, TextureFormat.ARGB32, false);
            var center = new Vector2(size / 2f, size / 2f);
            float outer = size / 2f - 1f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), center) / outer;
                    float alpha = Mathf.Clamp01(1f - distance);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha * alpha * 0.9f));
                }
            }

            texture.Apply();
            glowSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            return glowSprite;
        }

        private static Sprite RingSprite(int size, float ppu, float band, bool squared)
        {
            var texture = new Texture2D(size, size, TextureFormat.ARGB32, false);
            var center = new Vector2(size / 2f, size / 2f);
            float outer = size / 2f - 1f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), center);
                    float alpha = distance <= outer - band || distance >= outer
                        ? 0f
                        : 1f - Mathf.Abs((distance - (outer - band * 0.5f)) / (band * 0.5f));
                    alpha = Mathf.Clamp01(alpha);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, squared ? alpha * alpha : alpha));
                }
            }

            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), ppu);
        }

        private static Sprite RimSprite()
        {
            return rimSprite ??= RingSprite(512, 200f, 9f, false);
        }

        private static Sprite RimThinSprite()
        {
            return rimThinSprite ??= RingSprite(512, 200f, 2.5f, false);
        }

        private static Sprite ShockSprite()
        {
            return shockSprite ??= RingSprite(256, 100f, 14f, true);
        }

        private static Sprite VignetteSprite()
        {
            if (vignetteSprite) return vignetteSprite;

            const int size = 256;
            var texture = new Texture2D(size, size, TextureFormat.ARGB32, false);
            var center = new Vector2(size / 2f, size / 2f);
            float outer = size / 2f - 1f;
            var edge = new Color(0.30f, 0f, 0f, 1f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), center) / outer;
                    float falloff = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((distance - 0.45f) / 0.55f));
                    float cutoff = 1f - Mathf.SmoothStep(0.82f, 1f, distance);
                    float alpha = Mathf.Clamp01(VignetteFloor + (1f - VignetteFloor) * falloff) * cutoff;
                    texture.SetPixel(x, y, new Color(edge.r, edge.g, edge.b, alpha));
                }
            }

            texture.Apply();
            vignetteSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            return vignetteSprite;
        }

        private static Sprite SkySprite()
        {
            if (skySprite) return skySprite;

            var texture = new Texture2D(SkySize, SkySize, TextureFormat.ARGB32, false);
            var center = new Vector2(SkySize / 2f, SkySize / 2f);
            float hole = SkySize * SkyHoleRatio;
            float span = SkySize * 0.70f;

            for (int y = 0; y < SkySize; y++)
            {
                for (int x = 0; x < SkySize; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), center);
                    float t = Mathf.Clamp01((distance - hole) / span);
                    float falloff = Mathf.Pow(t, 0.6f);
                    float red = Mathf.Lerp(0.72f, 0.04f, falloff);
                    float green = Mathf.Lerp(0.07f, 0.004f, falloff);
                    float alpha = distance < hole - 1.5f
                        ? 0f
                        : Mathf.Clamp01((distance - hole + 1.5f) / 3f) * 0.985f;
                    texture.SetPixel(x, y, new Color(red, green, green, alpha));
                }
            }

            texture.Apply();
            skySprite = Sprite.Create(texture, new Rect(0, 0, SkySize, SkySize), new Vector2(0.5f, 0.5f), 100f);
            return skySprite;
        }

        public static void BeginCast(byte sukunaId, float castTime)
        {
            var hud = HudManager.Instance;
            if (hud == null) return;

            Hide();

            expandRoot = NewRoot("SukunaDomainExpand");
            var glow = AddSprite(expandRoot.transform, "SukunaExpandGlow", GlowSprite(), ExpandOrder,
                new Color(CursedRed.r, CursedRed.g, CursedRed.b, 0f));
            var ring = AddSprite(expandRoot.transform, "SukunaExpandRing", ShockSprite(), ExpandOrder + 1, CursedRedBright);

            nextShake = 0f;
            routine = hud.StartCoroutine(CoExpand(sukunaId, Mathf.Max(0.1f, castTime), glow, ring).WrapToIl2Cpp());
        }

        public static void Begin(Vector2 center, float radius)
        {
            var hud = HudManager.Instance;
            if (hud == null) return;

            StopAll(hud);
            DestroyExpand();

            domainCenter = center;
            domainRadius = Mathf.Max(0.5f, radius);
            ambienceTime = 0f;
            settle = SettleDuration;
            nextGroundCut = GroundCutInterval;

            root = NewRoot("SukunaDomain");
            root.transform.position = new Vector3(center.x, center.y, 0f);

            float rimRadius = SpriteRadius(RimSprite());
            float thinRadius = SpriteRadius(RimThinSprite());
            float softRadius = SpriteRadius(GlowSprite());
            float vignetteRadius = SpriteRadius(VignetteSprite());
            float skyHoleWorld = SkySize * SkyHoleRatio / 100f;

            rimGlow = AddSprite(root.transform, "SukunaDomainRim", RimSprite(), RimOrder, CursedRedBright);
            if (rimGlow != null) rimGlow.transform.localScale = Vector3.one * (domainRadius * 1.04f / rimRadius);

            rimLine = AddSprite(root.transform, "SukunaDomainRimLine", RimThinSprite(), RimLineOrder, RimLineColor);
            if (rimLine != null) rimLine.transform.localScale = Vector3.one * (domainRadius / thinRadius);

            vignette = AddSprite(root.transform, "SukunaDomainVignette", VignetteSprite(), VignetteOrder, Color.white);
            if (vignette != null) vignette.transform.localScale = Vector3.one * (domainRadius / vignetteRadius);

            sky = AddSprite(root.transform, "SukunaDomainSky", SkySprite(), SkyOrder, Color.white);
            if (sky != null) sky.transform.localScale = Vector3.one * (domainRadius / skyHoleWorld);

            float emberScale = 0.34f / softRadius;
            for (int i = 0; i < EmberCount; i++)
            {
                var ember = AddSprite(root.transform, "SukunaDomainEmber", GlowSprite(), EmberOrder, Color.clear);
                if (ember == null) continue;
                ember.transform.localScale = Vector3.one * emberScale;
                ember.transform.localPosition = new Vector3(
                    UnityEngine.Random.Range(-domainRadius, domainRadius),
                    UnityEngine.Random.Range(-domainRadius, domainRadius), 0f);
                embers.Add((ember, UnityEngine.Random.Range(0f, Mathf.PI * 2f), UnityEngine.Random.Range(0.15f, 0.95f),
                    UnityEngine.Random.Range(0.3f, 0.7f), UnityEngine.Random.Range(0f, 4f), UnityEngine.Random.Range(2.2f, 4.2f)));
            }

            SetInsideView(false);
            ambienceRoutine = hud.StartCoroutine(CoAmbience().WrapToIl2Cpp());
        }

        public static void SetInsideView(bool inside)
        {
            SetActive(rimGlow, !inside);
            SetActive(rimLine, !inside);
            SetActive(vignette, inside);
            SetActive(sky, inside);
        }

        public static void SpawnSlash(PlayerControl target)
        {
            if (target == null || target.cosmetics == null) return;

            var hud = HudManager.Instance;
            if (hud == null) return;

            Vector3 origin = target.transform.position;
            origin.z = 0f;

            for (int i = 0; i < 2; i++)
            {
                Vector2 offset = UnityEngine.Random.insideUnitCircle * 0.45f;
                SpawnCut(hud, new Vector3(origin.x + offset.x, origin.y + offset.y, 0f),
                    SlashBaseSize, 0.9f, SlashOrder);
            }
        }

        private static void SpawnGroundCut(HudManager hud)
        {
            float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            float distance = Mathf.Sqrt(UnityEngine.Random.Range(0.04f, 1f)) * domainRadius * 0.9f;
            var position = new Vector3(Mathf.Cos(angle) * distance, Mathf.Sin(angle) * distance, 0f);
            SpawnCut(hud, position, SlashBaseSize * UnityEngine.Random.Range(1f, 2f), 0.7f, GroundCutOrder);
        }

        private static void SpawnCut(HudManager hud, Vector3 position, float size, float alpha, int order)
        {
            var sprite = Sukuna.getSlashSprite();
            if (sprite == null) return;

            float spriteSize = sprite.rect.width / sprite.pixelsPerUnit;
            float baseScale = size / Mathf.Max(0.01f, spriteSize);

            var obj = new GameObject("SukunaDomainCut") { layer = WorldLayer() };
            obj.transform.position = position;
            obj.transform.rotation = Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(0f, 180f));
            obj.transform.localScale = Vector3.one * baseScale;

            var renderer = obj.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = order;
            renderer.color = new Color(1f, 1f, 1f, alpha);

            hud.StartCoroutine(CoCut(obj, renderer, alpha).WrapToIl2Cpp());
        }

        public static void Hide()
        {
            var hud = HudManager.Instance;
            StopAll(hud);
            DestroyExpand();
            embers.Clear();
            rimGlow = null;
            rimLine = null;
            vignette = null;
            sky = null;
            settle = 0f;

            if (root != null) UnityEngine.Object.Destroy(root);
            root = null;
        }

        private static void StopAll(HudManager hud)
        {
            if (hud != null)
            {
                if (routine != null) hud.StopCoroutine(routine);
                if (ambienceRoutine != null) hud.StopCoroutine(ambienceRoutine);
            }
            routine = null;
            ambienceRoutine = null;
        }

        private static void DestroyExpand()
        {
            if (expandRoot != null) UnityEngine.Object.Destroy(expandRoot);
            expandRoot = null;
        }

        private static void ShakeIfNear(Vector2 position, float strength)
        {
            var local = PlayerControl.LocalPlayer;
            if (local == null || local.Data == null || local.Data.IsDead) return;
            if (Vector2.Distance(local.transform.position, position) > Sukuna.domainRadius * 1.8f) return;

            var camera = Camera.main;
            if (camera == null) return;
            var follower = camera.GetComponent<FollowerCamera>();
            if (follower != null) follower.ShakeScreen(0.4f, strength);
        }

        private static IEnumerator CoExpand(byte sukunaId, float castTime, SpriteRenderer glow, SpriteRenderer ring)
        {
            float targetRadius = Mathf.Max(0.5f, Sukuna.domainRadius);
            float glowRadius = SpriteRadius(GlowSprite());
            float ringRadius = SpriteRadius(ShockSprite());
            float t = 0f;

            while (t < castTime)
            {
                t += Time.deltaTime;
                if (expandRoot == null) yield break;

                var caster = Helpers.playerById(sukunaId);
                if (caster == null || caster.Data == null) break;

                Vector3 position = caster.transform.position;
                position.z = 0f;
                expandRoot.transform.position = position;

                float p = Mathf.Clamp01(t / castTime);
                float eased = EaseOutBack(p);
                float growing = Mathf.Lerp(0.1f, targetRadius, eased);

                if (glow != null)
                {
                    glow.transform.localScale = Vector3.one * (growing * 1.1f / glowRadius);
                    glow.color = new Color(CursedRed.r, CursedRed.g, CursedRed.b, 0.55f * p);
                }
                if (ring != null)
                {
                    ring.transform.localScale = Vector3.one * (growing / ringRadius);
                    ring.color = new Color(CursedRedBright.r, CursedRedBright.g, CursedRedBright.b,
                        Mathf.Clamp01(1f - p * 0.75f));
                }

                if (Time.time >= nextShake)
                {
                    nextShake = Time.time + ShakeInterval;
                    ShakeIfNear(position, 0.6f + p * 1.1f);
                }

                yield return null;
            }

            float fade = 0f;
            while (fade < 0.45f)
            {
                fade += Time.deltaTime;
                if (expandRoot == null) yield break;

                float k = 1f - Mathf.Clamp01(fade / 0.45f);
                if (glow != null) glow.color = new Color(CursedRed.r, CursedRed.g, CursedRed.b, 0.55f * k);
                if (ring != null)
                {
                    ring.transform.localScale = Vector3.one * (targetRadius * (1f + (1f - k) * 0.12f) / ringRadius);
                    ring.color = new Color(CursedRedBright.r, CursedRedBright.g, CursedRedBright.b, 0.25f * k);
                }
                yield return null;
            }

            DestroyExpand();
        }

        private static IEnumerator CoAmbience()
        {
            while (root != null)
            {
                float dt = Time.deltaTime;
                ambienceTime += dt;
                if (settle > 0f) settle = Mathf.Max(0f, settle - dt);

                float boost = 1f + settle / SettleDuration * 1.4f;

                if (rimGlow != null)
                {
                    float pulse = 0.62f + 0.38f * Mathf.Sin(ambienceTime * 2.1f);
                    rimGlow.color = new Color(CursedRedBright.r, CursedRedBright.g, CursedRedBright.b,
                        Mathf.Clamp01(0.40f * pulse * boost));
                }
                if (rimLine != null)
                {
                    float pulse = 0.70f + 0.30f * Mathf.Sin(ambienceTime * 2.1f + 0.9f);
                    rimLine.color = new Color(RimLineColor.r, RimLineColor.g, RimLineColor.b,
                        Mathf.Clamp01(0.72f * pulse * boost));
                }
                if (vignette != null)
                {
                    float breath = 0.92f + 0.08f * Mathf.Sin(ambienceTime * 0.8f);
                    vignette.color = new Color(1f, 1f, 1f, Mathf.Clamp01(0.86f * breath * boost));
                }

                if (ambienceTime >= nextGroundCut)
                {
                    nextGroundCut = ambienceTime + GroundCutInterval;
                    var hud = HudManager.Instance;
                    if (hud != null) SpawnGroundCut(hud);
                }

                for (int i = 0; i < embers.Count; i++)
                {
                    var (renderer, angle, dist, speed, life, maxLife) = embers[i];
                    if (renderer == null) continue;

                    life += dt;
                    if (life >= maxLife)
                    {
                        life = 0f;
                        angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                        dist = UnityEngine.Random.Range(0.15f, 0.95f);
                        speed = UnityEngine.Random.Range(0.3f, 0.7f);
                        maxLife = UnityEngine.Random.Range(2.2f, 4.2f);
                    }

                    float progress = life / maxLife;
                    float alpha = Mathf.Sin(progress * Mathf.PI) * 0.5f;
                    float radial = dist * domainRadius;

                    renderer.transform.localPosition = new Vector3(
                        Mathf.Cos(angle) * radial + Mathf.Sin(ambienceTime * 0.6f + angle) * 0.25f,
                        Mathf.Sin(angle) * radial + progress * speed * 1.6f, 0f);
                    renderer.color = new Color(CursedEmber.r, CursedEmber.g, CursedEmber.b, alpha);

                    embers[i] = (renderer, angle, dist, speed, life, maxLife);
                }

                yield return null;
            }
        }

        private static IEnumerator CoCut(GameObject obj, SpriteRenderer renderer, float alpha)
        {
            float t = 0f;

            while (t < SlashFadeDuration)
            {
                t += Time.deltaTime;
                if (obj == null || renderer == null) yield break;

                float p = Mathf.Clamp01(t / SlashFadeDuration);
                renderer.color = new Color(1f, 1f, 1f, alpha * (1f - p));
                yield return null;
            }

            if (obj != null) UnityEngine.Object.Destroy(obj);
        }
    }
}
