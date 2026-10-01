using System;
using System.Collections;
using System.Linq;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using TheOtherRoles.MetaContext;
using TheOtherRoles.Patches;
using TheOtherRoles.Roles;
using TheOtherRoles.Utilities;
using UnityEngine;
using static TheOtherRoles.TheOtherRoles;

namespace TheOtherRoles.Objects
{
    public static class HigurumaDomain
    {
        private const float ExpandDuration = 3.6f;
        private const float VerseOneAt = 0.35f;
        private const float VerseTwoAt = 2.2f;
        private const float SettleAt = 4.0f;
        private const float PlateAt = 4.1f;
        private const float ChoiceAt = 5.7f;
        private const float DefendantY = -1.5f;
        private const float PlateScale = 1.35f;

        public const int SphereOrder = 900;
        public const int InnerOrder = 901;
        public const int RimOrder = 902;
        public const int VignetteOrder = 903;
        public const int ParticleOrder = 905;
        public const int RuneOrder = 910;

        private const float RuneOuterRadius = 3.5f;
        private const float RuneInnerRadius = 2.6f;
        private const float RuneOuterSpin = 5.5f;
        private const float RuneInnerSpin = -3.5f;
        private const int RuneGlyphs = 10;
        private const int ParticleCount = 12;
        private const float InnerSwapAlpha = 0.34f;
        private const float ScreenSpan = 22f;
        public const int PlateOrder = 950;
        public const int VerseOrder = 960;

        private static readonly Color CursedRed = new(0.78f, 0.05f, 0.05f);
        private static readonly Color CursedRedBright = new(1f, 0.16f, 0.12f);

        private static AudioSource loopSource;
        private static AudioClip domain1Clip;
        private static AudioClip domain2Clip;

        private static GameObject root;
        private static GameObject defendantPlate;
        private static SpriteRenderer sphere;
        private static SpriteRenderer inner;
        private static SpriteRenderer rim;
        private static GameObject runeOuterHolder;
        private static GameObject runeInnerHolder;
        private static readonly System.Collections.Generic.List<(SpriteRenderer renderer, float angle, float radius, float speed, float life, float maxLife)> particles = new();
        private static Coroutine ambienceRoutine;
        private static readonly System.Collections.Generic.List<(GameObject obj, float revealAt)> runeGlyphs = new();
        private static readonly System.Collections.Generic.List<(Material material, Color baseColor, float start, float end)> runeMaterials = new();
        private static SpriteRenderer vignette;
        private static SpriteRenderer plateGlow;
        private static Vector3 plateBasePosition;

        private static BitmapText verseOne;
        private static BitmapText verseTwo;
        private static BitmapText verseOneGlow;
        private static BitmapText verseTwoGlow;

        private static BitmapFont gothicFont;
        private static BitmapFont minchoFont;

        private static Sprite sphereSprite;
        private static Sprite innerSprite;
        private static Sprite rimSprite;
        private static Sprite glowSprite;
        private static Sprite shockSprite;
        private static Sprite vignetteSprite;

        private static Coroutine routine;
        private static int objectLayer;

        public static void Play(PlayerControl defendant)
        {
            Hide();

            var meeting = MeetingHud.Instance;
            var hud = FastDestroyableSingleton<HudManager>.Instance;
            if (meeting == null || hud == null) return;

            objectLayer = meeting.gameObject.layer;
            root = createChild("HigurumaDomain", meeting.transform, Vector3.zero);

            vignette = addSprite(root.transform, "Vignette", VignetteSprite(), Vector3.zero, VignetteOrder, new Color(1f, 1f, 1f, 0f));
            sphere = addSprite(root.transform, "Sphere", SphereSprite(), Vector3.zero, SphereOrder, Color.white);

            inner = addSprite(root.transform, "Inner", InnerSprite(), Vector3.zero, InnerOrder, new Color(CursedRedBright.r, 0.06f, 0.06f, 0f));
            if (inner != null) inner.transform.localScale = Vector3.one * (ScreenSpan / 12.8f);

            BuildRuneRings(root.transform);
            SpawnParticles(root.transform);

            rim = addSprite(root.transform, "Rim", RimSprite(), Vector3.zero, RimOrder, new Color(CursedRedBright.r, CursedRedBright.g, CursedRedBright.b, 0f));

            if (sphere != null) sphere.transform.localScale = Vector3.zero;
            if (rim != null) rim.transform.localScale = Vector3.zero;

            verseOne = CreateBitmapText(root.transform, "", GothicFont(), 1.7f, Color.white, new Vector3(0f, -1.1f, -1f), VerseOrder);
            verseOneGlow = CreateBitmapText(root.transform, "", GothicFont(), 1.78f, new Color(CursedRed.r, CursedRed.g, CursedRed.b, 0.55f), new Vector3(0f, -1.1f, -1f), VerseOrder - 1);
            verseTwo = CreateBitmapText(root.transform, "", MinchoFont(), 2.2f, CursedRedBright, new Vector3(0f, 0.9f, -1f), VerseOrder + 1);
            verseTwoGlow = CreateBitmapText(root.transform, "", MinchoFont(), 2.32f, new Color(0.35f, 0f, 0f, 0.7f), new Vector3(0f, 0.9f, -1f), VerseOrder);

            PlaySoundOnce(0);
            StartLoop(1);

            routine = hud.StartCoroutine(CoDomain(meeting, defendant).WrapToIl2Cpp());
            ambienceRoutine = hud.StartCoroutine(CoAmbience().WrapToIl2Cpp());
        }

        private static IEnumerator CoDomain(MeetingHud meeting, PlayerControl defendant)
        {
            float halfWidth = 8f;
            float halfHeight = 4.5f;
            var camera = Helpers.FindCamera(objectLayer);
            if (camera == null) camera = Camera.main;
            if (camera != null && camera.orthographic)
            {
                halfHeight = camera.orthographicSize;
                halfWidth = halfHeight * camera.aspect;
            }

            float targetRadius = Mathf.Sqrt(halfWidth * halfWidth + halfHeight * halfHeight) * 1.05f;
            float sphereRadius = SpriteRadius(SphereSprite());
            float rimRadius = SpriteRadius(RimSprite());

            Vector3 basePosition = sphere != null ? sphere.transform.position : Vector3.zero;

            float elapsed = 0f;
            float verseOneTyped = 0f;
            float verseTwoTyped = 0f;
            bool verseOneDone = false;
            bool verseTwoDone = false;

            while (elapsed < SettleAt)
            {
                elapsed += Time.deltaTime;

                float p = Mathf.Clamp01(elapsed / ExpandDuration);
                float eased = easeOutBack(p);
                float radius = Mathf.Lerp(0.05f, targetRadius, eased);

                if (sphere != null)
                    sphere.transform.localScale = Vector3.one * (radius / sphereRadius);

                if (rim != null)
                {
                    rim.transform.localScale = Vector3.one * ((radius * 1.07f) / rimRadius);
                    float rimAlpha = Mathf.Clamp01((1f - p) * 1.4f) * (0.55f + 0.45f * Mathf.Sin(Time.time * 14f));
                    rim.color = new Color(CursedRedBright.r, CursedRedBright.g, CursedRedBright.b, rimAlpha);
                }

                if (sphere != null && p < 1f)
                {
                    float shake = 0.07f * (1f - p);
                    sphere.transform.position = basePosition + (Vector3)(UnityEngine.Random.insideUnitCircle * shake);
                }

                if (!verseOneDone && elapsed >= VerseOneAt)
                {
                    verseOneTyped += Time.deltaTime / 0.09f;
                    int chars = Mathf.Min(4, Mathf.FloorToInt(verseOneTyped));
                    SetPair(verseOne, verseOneGlow, "領域展開".Substring(0, chars));
                    if (chars >= 4) verseOneDone = true;
                }

                if (!verseTwoDone && elapsed >= VerseTwoAt)
                {
                    verseTwoTyped += Time.deltaTime / 0.12f;
                    int chars = Mathf.Min(4, Mathf.FloorToInt(verseTwoTyped));
                    SetPair(verseTwo, verseTwoGlow, "誅伏賜死".Substring(0, chars));
                    if (chars >= 4) verseTwoDone = true;
                }

                PulseVerse(verseOne, verseOneGlow, elapsed - VerseOneAt);
                PulseVerse(verseTwo, verseTwoGlow, elapsed - VerseTwoAt);

                if (vignette != null)
                {
                    float fade = Mathf.Clamp01((elapsed - ExpandDuration * 0.55f) / 1.6f);
                    vignette.color = new Color(1f, 1f, 1f, fade * 0.62f);
                }

                RevealRunes(elapsed);

                yield return null;
            }

            if (sphere != null)
            {
                sphere.transform.position = basePosition;
                sphere.transform.localScale = Vector3.one * (targetRadius / sphereRadius);
            }

            if (rim != null) rim.color = new Color(CursedRedBright.r, CursedRedBright.g, CursedRedBright.b, 0f);

            RevealRunes(SettleAt);

            SpawnShockwave(Vector3.zero, targetRadius * 1.25f, CursedRedBright, 0.55f);

            StopLoop();

            yield return Effects.Wait(Mathf.Max(0f, PlateAt - elapsed));

            yield return CoDropPlate(meeting, defendant);

            yield return Effects.Wait(0.8f);

            yield return CoFadeVerse(verseOne, verseOneGlow);
            yield return CoFadeVerse(verseTwo, verseTwoGlow);

            routine = null;

            if (HiromiHiguruma.trialActive) HiromiHiguruma.BeginChoice();
        }

        private static void BuildRuneRings(Transform parent)
        {
            const string outer = "領域展開誅伏賜死天稱";
            const string inner = "死賜伏誅開展域領認罪";

            runeOuterHolder = createChild("RuneOuter", parent, new Vector3(0f, 0f, -0.8f));
            runeInnerHolder = createChild("RuneInner", parent, new Vector3(0f, 0f, -0.8f));

            BuildRing(runeOuterHolder.transform, outer, RuneOuterRadius, 0.36f, new Color(CursedRedBright.r, CursedRedBright.g, CursedRedBright.b, 0.5f), RuneOrder, 0.5f, 2.7f);
            BuildRing(runeInnerHolder.transform, inner, RuneInnerRadius, 0.26f, new Color(CursedRed.r, CursedRed.g, CursedRed.b, 0.3f), RuneOrder - 1, 1.6f, 3.7f);
        }

        private static void BuildRing(Transform parent, string chars, float radius, float size, Color color, int order, float revealStart, float revealEnd)
        {
            var shared = BitmapText.CreateSharedMaterial(MinchoFont(), new Color(color.r, color.g, color.b, 0f));
            runeMaterials.Add((shared, color, revealStart, revealEnd));

            int count = Mathf.Min(RuneGlyphs, chars.Length);

            for (int i = 0; i < count; i++)
            {
                float angle = i / (float)count * Mathf.PI * 2f + Mathf.Sin(i * 2.7f) * 0.06f;
                float r = radius + Mathf.Sin(i * 4.1f) * 0.07f;

                var position = new Vector3(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r, 0f);

                var glyph = new BitmapText("HigurumaRune", parent, position, MinchoFont(), order, "higuruma", shared)
                {
                    FontSize = size * (1f + Mathf.Sin(i * 3.3f) * 0.07f),
                    Pivot = new Vector2(0.5f, 0.5f),
                    Alignment = BitmapTextAlignment.Center
                };
                glyph.SetText(chars[i].ToString());
                glyph.Color = color;
                glyph.GameObject.transform.localRotation = Quaternion.Euler(0f, 0f, angle * Mathf.Rad2Deg - 90f);
                glyph.GameObject.transform.localScale = Vector3.zero;

                runeGlyphs.Add((glyph.GameObject, Mathf.Lerp(revealStart, revealEnd, count <= 1 ? 0f : i / (float)(count - 1))));
            }
        }

        private static void SpawnParticles(Transform parent)
        {
            particles.Clear();

            for (int i = 0; i < ParticleCount; i++)
            {
                var renderer = addSprite(parent, "Mote", GlowSprite(), Vector3.zero, ParticleOrder, new Color(CursedRedBright.r, CursedRedBright.g, CursedRedBright.b, 0f));
                if (renderer == null) continue;

                renderer.transform.localScale = Vector3.one * UnityEngine.Random.Range(0.22f, 0.55f);
                float life = UnityEngine.Random.Range(2.4f, 4.6f);

                particles.Add((renderer, UnityEngine.Random.Range(-2.6f, 2.6f), UnityEngine.Random.Range(0.4f, 2.2f), UnityEngine.Random.Range(0.5f, 0.95f), UnityEngine.Random.Range(0f, life), life));
            }
        }

        private static IEnumerator CoAmbience()
        {
            float time = 0f;
            float innerAngle = 0f;
            float plateHover = 0f;

            while (root != null)
            {
                float dt = Time.deltaTime;
                time += dt;

                if (runeOuterHolder != null)
                {
                    float outerAngle = time * RuneOuterSpin + Mathf.Sin(time * 0.31f) * 7f;
                    runeOuterHolder.transform.localRotation = Quaternion.Euler(0f, 0f, outerAngle);
                }

                if (runeInnerHolder != null)
                {
                    float innerRuneAngle = time * RuneInnerSpin + Mathf.Sin(time * 0.23f + 1.7f) * 5f;
                    runeInnerHolder.transform.localRotation = Quaternion.Euler(0f, 0f, innerRuneAngle);
                }

                if (inner != null)
                {
                    float breath = 0.78f + 0.22f * Mathf.Sin(time * 0.9f);
                    inner.color = new Color(CursedRedBright.r, 0.06f, 0.06f, InnerSwapAlpha * breath * 0.85f);
                    innerAngle += dt * 3.2f;
                    inner.transform.localRotation = Quaternion.Euler(0f, 0f, innerAngle);
                }

                if (defendantPlate != null)
                {
                    plateHover += dt;
                    defendantPlate.transform.localPosition = plateBasePosition + new Vector3(0f, Mathf.Sin(plateHover * 1.7f) * 0.045f, 0f);

                    if (plateGlow != null)
                        plateGlow.color = new Color(CursedRedBright.r, CursedRedBright.g, CursedRedBright.b, 0.44f + 0.07f * Mathf.Sin(plateHover * 1.1f));
                }

                for (int i = 0; i < particles.Count; i++)
                {
                    var (renderer, angle, radius, speed, life, maxLife) = particles[i];
                    if (renderer == null) continue;

                    life += dt;
                    if (life >= maxLife)
                    {
                        life = 0f;
                        angle = UnityEngine.Random.Range(-2.6f, 2.6f);
                        radius = UnityEngine.Random.Range(0.4f, 2.2f);
                        speed = UnityEngine.Random.Range(0.5f, 0.95f);
                        maxLife = UnityEngine.Random.Range(2.4f, 4.6f);
                    }

                    radius += speed * dt;
                    float progress = life / maxLife;
                    float alpha = Mathf.Sin(progress * Mathf.PI) * 0.42f;

                    renderer.transform.localPosition = new Vector3(radius + Mathf.Sin(time * 0.7f + angle) * 0.22f, radius * angle * 0.32f, -0.9f);
                    renderer.color = new Color(CursedRedBright.r, CursedRedBright.g, CursedRedBright.b, alpha);

                    particles[i] = (renderer, angle, radius, speed, life, maxLife);
                }

                yield return null;
            }
        }

        private static void RevealRunes(float elapsed)
        {
            for (int i = 0; i < runeGlyphs.Count; i++)
            {
                var (obj, revealAt) = runeGlyphs[i];
                if (obj == null) continue;

                float s = elapsed <= revealAt ? 0f : easeOutBack(Mathf.Clamp01((elapsed - revealAt) / 0.3f));
                obj.transform.localScale = Vector3.one * s;
            }


        }

        public static void Collapse(Action onDone)
        {
            var hud = FastDestroyableSingleton<HudManager>.Instance;
            if (root == null || hud == null)
            {
                onDone?.Invoke();
                return;
            }

            hud.StartCoroutine(CoCollapse(onDone).WrapToIl2Cpp());
        }

        private static IEnumerator CoCollapse(Action onDone)
        {
            var hud = FastDestroyableSingleton<HudManager>.Instance;
            if (hud != null && ambienceRoutine != null)
            {
                hud.StopCoroutine(ambienceRoutine);
                ambienceRoutine = null;
            }

            Vector3 startScale = root != null ? root.transform.localScale : Vector3.one;
            Color vignetteStart = vignette != null ? vignette.color : Color.clear;
            Color innerStart = inner != null ? inner.color : Color.clear;

            float t = 0f;
            const float duration = 0.55f;

            while (t < duration)
            {
                t += Time.deltaTime;
                if (root == null) break;

                float p = Mathf.Clamp01(t / duration);
                float eased = p * p;
                float k = 1f - eased;

                root.transform.localScale = startScale * Mathf.Max(0.02f, k);
                if (vignette != null) vignette.color = new Color(vignetteStart.r, vignetteStart.g, vignetteStart.b, vignetteStart.a * k);
                if (inner != null) inner.color = new Color(innerStart.r, innerStart.g, innerStart.b, innerStart.a * k);

                yield return null;
            }

            Hide();
            onDone?.Invoke();
        }

        private static void PulseVerse(BitmapText main, BitmapText glow, float since)
        {
            if (since < 0f) return;
            if (main?.GameObject == null) return;

            float punch = 1f + 0.3f * Mathf.Exp(-7f * since);
            main.GameObject.transform.localScale = Vector3.one * punch;
            if (glow?.GameObject != null) glow.GameObject.transform.localScale = Vector3.one * punch;
        }

        private static void SetPair(BitmapText main, BitmapText glow, string text)
        {
            main?.SetText(text);
            glow?.SetText(text);

            bool visible = !string.IsNullOrEmpty(text);
            main?.SetActive(visible);
            glow?.SetActive(visible);
        }

        private static IEnumerator CoDropPlate(MeetingHud meeting, PlayerControl defendant)
        {
            if (meeting.playerStates == null || root == null) yield break;

            byte defendantId = defendant != null ? defendant.PlayerId : HiromiHiguruma.defendantId;

            foreach (var area in meeting.playerStates)
                if (area != null) area.gameObject.SetActive(false);

            var source = meeting.playerStates.FirstOrDefault(x => x != null && (byte)x.PlayerId == defendantId);
            if (source == null) yield break;

            defendantPlate = UnityEngine.Object.Instantiate(source.gameObject, root.transform);
            defendantPlate.name = "HigurumaDefendantPlate";
            defendantPlate.transform.localPosition = new Vector3(0f, DefendantY + 3.6f, -0.5f);
            defendantPlate.transform.localScale = Vector3.one * (PlateScale * 0.55f);

            foreach (var child in defendantPlate.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = objectLayer;

            foreach (var renderer in defendantPlate.GetComponentsInChildren<SpriteRenderer>(true))
                renderer.sortingOrder = PlateOrder;

            foreach (var text in defendantPlate.GetComponentsInChildren<TMPro.TextMeshPro>(true))
            {
                var mesh = text.GetComponent<MeshRenderer>();
                if (mesh != null) mesh.sortingOrder = PlateOrder;
            }

            foreach (var collider in defendantPlate.GetComponentsInChildren<Collider2D>(true))
                collider.enabled = false;

            foreach (var passive in defendantPlate.GetComponentsInChildren<PassiveButton>(true))
                passive.enabled = false;

            defendantPlate.SetActive(true);

            plateGlow = addSprite(root.transform, "PlateGlow", GlowSprite(), new Vector3(0f, DefendantY, -0.6f), PlateOrder - 1, new Color(CursedRedBright.r, CursedRedBright.g, CursedRedBright.b, 0.45f));
            if (plateGlow != null) plateGlow.transform.localScale = Vector3.one * 3.4f;

            float t = 0f;
            while (t < 0.36f)
            {
                t += Time.deltaTime;
                if (defendantPlate == null) yield break;

                float p = Mathf.Clamp01(t / 0.36f);
                float eased = easeOutBack(p);
                float y = Mathf.Lerp(DefendantY + 3.6f, DefendantY, eased);

                defendantPlate.transform.localPosition = new Vector3(0f, y, -0.5f);
                defendantPlate.transform.localScale = Vector3.one * Mathf.Lerp(PlateScale * 0.55f, PlateScale, eased);
                yield return null;
            }

            plateBasePosition = new Vector3(0f, DefendantY, -0.5f);
            defendantPlate.transform.localPosition = plateBasePosition;
            defendantPlate.transform.localScale = Vector3.one * PlateScale;

            PlayLandingImpact();
        }

        private static void PlayLandingImpact()
        {
            PlaySmashSound();
            SpawnShockwave(new Vector3(0f, DefendantY, -0.4f), 4.2f, CursedRed, 0.4f);

            var camera = Camera.main;
            if (camera == null) return;

            var follower = camera.GetComponent<FollowerCamera>();
            if (follower != null) follower.ShakeScreen(0.3f, 0.35f);
        }

        public static void SpawnVerdictSweep(Transform parent, float y, float length, int order)
        {
            var hud = FastDestroyableSingleton<HudManager>.Instance;
            if (hud == null || parent == null) return;

            var obj = new GameObject("VerdictSweep");
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = new Vector3(-length, y, -1.5f);
            obj.layer = parent.gameObject.layer;

            var renderer = obj.AddComponent<SpriteRenderer>();
            renderer.sprite = VanillaAsset.FullScreenSprite;
            renderer.color = new Color(CursedRedBright.r, CursedRedBright.g, CursedRedBright.b, 0f);
            renderer.sortingOrder = order;
            obj.transform.localScale = new Vector3(length * 0.5f, 0.035f, 1f);

            hud.StartCoroutine(CoSweep(obj, renderer, length).WrapToIl2Cpp());
        }

        private static IEnumerator CoSweep(GameObject obj, SpriteRenderer renderer, float length)
        {
            float t = 0f;
            const float duration = 0.42f;
            float y = obj != null ? obj.transform.localPosition.y : 0f;

            while (t < duration)
            {
                t += Time.deltaTime;
                if (obj == null || renderer == null) yield break;

                float p = t / duration;
                obj.transform.localPosition = new Vector3(Mathf.Lerp(-length, length, p), y, -1.5f);
                renderer.color = new Color(CursedRedBright.r, CursedRedBright.g, CursedRedBright.b, Mathf.Sin(p * Mathf.PI) * 0.85f);
                yield return null;
            }

            if (obj != null) UnityEngine.Object.Destroy(obj);
        }

        public static void SpawnShockwave(Vector3 localPosition, float maxScale, Color color, float duration)
        {
            if (root == null) return;

            var hud = FastDestroyableSingleton<HudManager>.Instance;
            if (hud == null) return;

            var ring = addSprite(root.transform, "Shockwave", ShockSprite(), localPosition, PlateOrder - 2, color);
            if (ring == null) return;

            hud.StartCoroutine(CoShockwave(ring, SpriteRadius(ShockSprite()), maxScale, duration).WrapToIl2Cpp());
        }

        private static IEnumerator CoShockwave(SpriteRenderer ring, float spriteRadius, float maxScale, float duration)
        {
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                if (ring == null) yield break;

                float p = Mathf.Clamp01(t / duration);
                float eased = 1f - (1f - p) * (1f - p);
                float radius = Mathf.Lerp(spriteRadius * 0.35f, maxScale, eased);

                ring.transform.localScale = Vector3.one * (radius / spriteRadius);

                var c = ring.color;
                ring.color = new Color(c.r, c.g, c.b, (1f - p) * 0.85f);
                yield return null;
            }

            if (ring != null) UnityEngine.Object.Destroy(ring.gameObject);
        }

        private static IEnumerator CoFadeVerse(BitmapText main, BitmapText glow)
        {
            float t = 0f;
            while (t < 0.45f)
            {
                t += Time.deltaTime;
                float a = Mathf.Clamp01(1f - t / 0.45f);
                FadeText(main, a);
                FadeText(glow, a * 0.55f);
                yield return null;
            }

            if (main?.GameObject != null) main.SetActive(false);
            if (glow?.GameObject != null) glow.SetActive(false);
        }

        private static void FadeText(BitmapText text, float alpha)
        {
            if (text?.GameObject == null) return;

            var c = text.Color;
            text.Color = new Color(c.r, c.g, c.b, alpha);
        }

        public static void Hide()
        {
            var hud = FastDestroyableSingleton<HudManager>.Instance;
            if (hud != null && routine != null) hud.StopCoroutine(routine);
            if (hud != null && ambienceRoutine != null) hud.StopCoroutine(ambienceRoutine);
            routine = null;
            ambienceRoutine = null;
            particles.Clear();
            runeGlyphs.Clear();
            runeMaterials.Clear();
            runeOuterHolder = null;
            runeInnerHolder = null;
            inner = null;

            StopLoop();

            BitmapText.DestroyGroup("higuruma");

            if (root != null) UnityEngine.Object.Destroy(root);
            root = null;
            defendantPlate = null;
            plateGlow = null;
            sphere = null;
            rim = null;
            vignette = null;
            verseOne = null;
            verseTwo = null;
            verseOneGlow = null;
            verseTwoGlow = null;
        }

        private static GameObject createChild(string name, Transform parent, Vector3 localPosition)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = localPosition;
            obj.layer = objectLayer;
            return obj;
        }

        private static SpriteRenderer addSprite(Transform parent, string name, Sprite sprite, Vector3 localPosition, int order, Color color)
        {
            if (sprite == null) return null;

            var obj = createChild(name, parent, localPosition);
            var renderer = obj.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            renderer.sortingOrder = order;
            return renderer;
        }

        public static BitmapText CreateBitmapText(Transform parent, string content, BitmapFont font, float fontSize, Color color, Vector3 localPosition, int order)
        {
            var text = new BitmapText("HigurumaBitmap", parent, localPosition, font, order, "higuruma")
            {
                FontSize = fontSize,
                Pivot = new Vector2(0.5f, 0.5f),
                Alignment = BitmapTextAlignment.Center
            };
            text.SetText(content);
            text.Color = color;
            text.SetActive(!string.IsNullOrEmpty(content));
            return text;
        }

        public static TMPro.TextMeshPro CreateText(Transform parent, string content, float size, Color color, Vector3 localPosition, int order)
        {
            var font = JapaneseFont();
            if (font == null || VanillaAsset.StandardTextPrefab == null) return null;

            var text = UnityEngine.Object.Instantiate(VanillaAsset.StandardTextPrefab, parent);
            text.gameObject.name = "HigurumaText";
            text.gameObject.layer = parent != null ? parent.gameObject.layer : 5;
            text.transform.localPosition = localPosition;
            text.text = content;
            text.font = font;
            text.fontSharedMaterial = font.material;
            text.fontSize = size;
            text.color = color;
            text.alignment = TMPro.TextAlignmentOptions.Center;
            text.enableWordWrapping = false;
            text.rectTransform.sizeDelta = new Vector2(20f, 4f);

            var renderer = text.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.sortingOrder = order;

            text.gameObject.SetActive(false);
            return text;
        }

        public static BitmapFont GothicFont()
        {
            return gothicFont ??= new BitmapFont("TheOtherRoles.Resources.HigurumaGothic.json", "TheOtherRoles.Resources.HigurumaGothic.png");
        }

        public static BitmapFont MinchoFont()
        {
            return minchoFont ??= new BitmapFont("TheOtherRoles.Resources.HigurumaMincho.json", "TheOtherRoles.Resources.HigurumaMincho.png");
        }

        public static TMPro.TMP_FontAsset JapaneseFont()
        {
            return Helpers.FindAsset<TMPro.TMP_FontAsset>("NotoSansJP-Regular SDF");
        }

        private static float easeOutBack(float p)
        {
            const float c1 = 1.9f;
            const float c3 = c1 + 1f;
            float x = p - 1f;
            return 1f + c3 * x * x * x + c1 * x * x;
        }

        private static float SpriteRadius(Sprite sprite)
        {
            if (sprite == null) return 1f;
            return sprite.rect.width * 0.5f / sprite.pixelsPerUnit;
        }

        private static Sprite SphereSprite()
        {
            if (sphereSprite) return sphereSprite;

            const int size = 512;
            var texture = new Texture2D(size, size, TextureFormat.ARGB32, false);
            var center = new Vector2(size / 2f, size / 2f);
            float outer = size / 2f - 1f;
            const float feather = 3f;

            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), center);
                    if (distance <= outer - feather) texture.SetPixel(x, y, new Color(0f, 0f, 0f, 1f));
                    else if (distance >= outer) texture.SetPixel(x, y, new Color(0f, 0f, 0f, 0f));
                    else texture.SetPixel(x, y, new Color(0f, 0f, 0f, Mathf.InverseLerp(outer, outer - feather, distance)));
                }

            texture.Apply();
            sphereSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 200f);
            return sphereSprite;
        }

        private static Sprite InnerSprite()
        {
            if (innerSprite) return innerSprite;

            const int size = 256;
            var texture = new Texture2D(size, size, TextureFormat.ARGB32, false);
            var center = new Vector2(size / 2f, size / 2f);
            float outer = size / 2f - 1f;

            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var offset = new Vector2(x, y) - center;
                    float distance = offset.magnitude / outer;
                    float angle = Mathf.Atan2(offset.y, offset.x);

                    float swirl = Mathf.Sin(angle * 9f + distance * 26f) * 0.5f + 0.5f;
                    float falloff = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((distance - 0.06f) / 0.9f));
                    float streak = Mathf.Pow(swirl, 2.2f) * 0.75f + 0.25f;

                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(falloff * streak)));
                }

            texture.Apply();
            innerSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 20f);
            return innerSprite;
        }

        private static Sprite RimSprite()
        {
            if (rimSprite) return rimSprite;

            const int size = 512;
            var texture = new Texture2D(size, size, TextureFormat.ARGB32, false);
            var center = new Vector2(size / 2f, size / 2f);
            float outer = size / 2f - 1f;
            const float band = 9f;

            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), center);
                    float alpha = distance <= outer - band || distance >= outer
                        ? 0f
                        : 1f - Mathf.Abs((distance - (outer - band * 0.5f)) / (band * 0.5f));
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(alpha)));
                }

            texture.Apply();
            rimSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 200f);
            return rimSprite;
        }

        private static Sprite ShockSprite()
        {
            if (shockSprite) return shockSprite;

            const int size = 256;
            var texture = new Texture2D(size, size, TextureFormat.ARGB32, false);
            var center = new Vector2(size / 2f, size / 2f);
            float outer = size / 2f - 1f;
            const float band = 14f;

            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), center);
                    float alpha = distance <= outer - band || distance >= outer
                        ? 0f
                        : 1f - Mathf.Abs((distance - (outer - band * 0.5f)) / (band * 0.5f));
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(alpha * alpha)));
                }

            texture.Apply();
            shockSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            return shockSprite;
        }

        private static Sprite GlowSprite()
        {
            if (glowSprite) return glowSprite;

            const int size = 256;
            var texture = new Texture2D(size, size, TextureFormat.ARGB32, false);
            var center = new Vector2(size / 2f, size / 2f);
            float outer = size / 2f - 1f;

            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), center) / outer;
                    float alpha = Mathf.Clamp01(1f - distance);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha * alpha * 0.9f));
                }

            texture.Apply();
            glowSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            return glowSprite;
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
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), center) / outer;
                    float alpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((distance - 0.45f) / 0.55f));
                    texture.SetPixel(x, y, new Color(edge.r, edge.g, edge.b, alpha));
                }

            texture.Apply();
            vignetteSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            return vignetteSprite;
        }

        private static AudioClip GetClip(int index)
        {
            string file = index == 0 ? "HiromiHiguromiDomain1" : "HiromHiguromiDomain2";

            if (index == 0)
                return domain1Clip ??= Helpers.loadWavFromResources($"TheOtherRoles.Resources.domain.{file}.wav", "TORV_Higuruma1");

            return domain2Clip ??= Helpers.loadWavFromResources($"TheOtherRoles.Resources.domain.{file}.wav", "TORV_Higuruma2");
        }

        private static bool SoundAllowed()
        {
            return ClientOption.GetValue(ClientOption.ClientOptionType.EnableSoundEffects) != 0 && Constants.ShouldPlaySfx();
        }

        private static void PlaySoundOnce(int index)
        {
            if (!SoundAllowed()) return;

            var clip = GetClip(index);
            if (clip != null && SoundManager.Instance != null)
                SoundManager.Instance.PlaySound(clip, false, 0.9f);
        }

        public static void PlaySmashSound()
        {
            if (!SoundAllowed()) return;

            var clip = GetClip(1);
            if (clip == null || SoundManager.Instance == null) return;

            SoundManager.Instance.PlaySound(clip, false, 0.9f);
        }

        private static void StartLoop(int index)
        {
            if (!SoundAllowed()) return;

            var clip = GetClip(index);
            if (clip == null) return;

            if (loopSource == null)
            {
                var holder = new GameObject("HigurumaDomainLoop");
                UnityEngine.Object.DontDestroyOnLoad(holder);
                loopSource = holder.AddComponent<AudioSource>();
                loopSource.playOnAwake = false;
                loopSource.loop = true;
                loopSource.spatialBlend = 0f;
                if (SoundManager.Instance != null && SoundManager.Instance.SfxChannel != null)
                    loopSource.outputAudioMixerGroup = SoundManager.Instance.SfxChannel;
            }

            loopSource.Stop();
            loopSource.clip = clip;
            loopSource.Play();
        }

        private static void StopLoop()
        {
            if (loopSource != null && loopSource.isPlaying) loopSource.Stop();
        }
    }
}
