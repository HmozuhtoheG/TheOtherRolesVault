using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using TheOtherRoles.MetaContext;
using TheOtherRoles.Modules;
using TheOtherRoles.Roles;
using TheOtherRoles.Utilities;
using UnityEngine;
using UnityEngine.Rendering;
using static TheOtherRoles.TheOtherRoles;

namespace TheOtherRoles.Objects
{
    public static class JusticeMeetingVisual
    {
        private const float BackScaleX = 4.5f;
        private const float BackScaleY = 2.6f;
        private const float ReticleScale = 0.69f;
        private const int BandCount = 24;
        private const float BandWidth = 1.8f;
        private const float BandHeightFactor = 4.4f;

        private static readonly string[] RandomTexts =
        [
            "(despired)", "terminus", "<revolt>", "solitary", "bona vacantia", "despotism",
            "pizza", "elitism", "suspicion", "justice", "outsider", "discrepancy",
            "purge", "uniformity", "conviction", "tribunal", "triumph", "heroism", "u - majority"
        ];

        private static readonly string[] RandomAltTexts =
        [
            "HERO", "victor", "supreme", "the one", "genius", "prodigy", "detective", "clairvoyant"
        ];

        private static readonly (int photoIndex, Vector2 localPos, float scale)[] PhotoData =
        [
            (1, new Vector2(-0.845f, 0.816f), 0.76f),
            (2, new Vector2(-1.04f, 0.77f), 0.7f),
            (3, new Vector2(-1.06f, 0.805f), 0.73f),
            (4, new Vector2(-1.06f, 0.89f), 0.9f),
            (5, new Vector2(-1.07f, 0.81f), 0.75f),
            (6, new Vector2(-0.93f, 0.73f), 0.65f),
            (7, new Vector2(-1.06f, 0.77f), 0.7f),
        ];

        private static int objectLayer;
        private static int vanillaOrder;

        private static GameObject root;
        private static GameObject introRoot;
        private static GameObject backObject;

        private static BitmapFont justiceFont;

        private static SpriteRenderer backView;
        private static SpriteRenderer background1;
        private static BitmapText reticleText;

        private static int lastNumberIndex;
        private static int lastAltIndex;

        private class BandGroup
        {
            public readonly List<SpriteRenderer> renderers = new();
            public readonly float[] displayed = new float[BandCount];
            public readonly float[] secret = new float[BandCount];
            public readonly float[] filter = new float[BandCount];
        }

        private static readonly List<BandGroup> bandGroups = new();
        private static readonly List<(Material material, float goal, float value, float bigTimer, float smallTimer)> gauges = new();
        private static readonly Dictionary<string, Sprite> spriteCache = new();

        private static Sprite sprite(string file, float pixelsPerUnit)
        {
            string key = file + pixelsPerUnit;
            if (spriteCache.TryGetValue(key, out var cached)) return cached;

            var loaded = Helpers.loadSpriteFromResources("TheOtherRoles.Resources." + file, pixelsPerUnit);
            spriteCache[key] = loaded;
            return loaded;
        }

        private static GameObject createChild(string name, Transform parent, Vector3 localPosition)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = localPosition;
            obj.layer = objectLayer;
            return obj;
        }

        private static SpriteRenderer add(Transform parent, string name, Sprite sprite, Vector3 localPosition, float scale, int order)
        {
            if (sprite == null)
            {
                TheOtherRolesPlugin.Logger.LogError("[JusticeMeetingVisual] sprite missing: " + name);
                return null;
            }

            var obj = createChild(name, parent, localPosition);
            obj.transform.localScale = Vector3.one * scale;

            var renderer = obj.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = order;
            return renderer;
        }

        private static SpriteRenderer addFull(Transform parent, string name, Vector2 size, Color color, Vector3 localPosition, int order)
        {
            var renderer = add(parent, name, VanillaAsset.FullScreenSprite, localPosition, 1f, order);
            if (renderer == null) return null;

            renderer.color = color;
            renderer.transform.localScale = new Vector3(size.x, size.y, 1f);
            return renderer;
        }

        private static BitmapFont font()
        {
            justiceFont ??= new BitmapFont("TheOtherRoles.Resources.JusticeFont.json", "TheOtherRoles.Resources.JusticeFont.png");
            return justiceFont;
        }

        private static int MeasureVanillaOrder(MeetingHud meeting)
        {
            int voteAreaMax = int.MinValue;
            int meetingMax = int.MinValue;

            foreach (var renderer in meeting.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (renderer == null) continue;
                meetingMax = Mathf.Max(meetingMax, renderer.sortingOrder);
            }

            if (meeting.playerStates != null)
            {
                foreach (var area in meeting.playerStates)
                {
                    if (area == null) continue;
                    foreach (var renderer in area.GetComponentsInChildren<SpriteRenderer>(true))
                    {
                        if (renderer == null) continue;
                        voteAreaMax = Mathf.Max(voteAreaMax, renderer.sortingOrder);
                    }
                }
            }

            int order = voteAreaMax != int.MinValue ? voteAreaMax : (meetingMax != int.MinValue ? meetingMax : 0);
            TheOtherRolesPlugin.Logger.LogInfo($"[JusticeMeetingVisual] vanilla orders voteArea={voteAreaMax} meetingMax={meetingMax} -> using {order}");
            return order;
        }

        private static float delta(float value, float speed, float threshold)
        {
            float smooth = value * Mathf.Clamp01(Time.deltaTime * speed);
            if (value < 0f) return smooth < -threshold ? smooth : Mathf.Max(value, -threshold);
            return smooth > threshold ? smooth : Mathf.Min(value, threshold);
        }

        public static void Play(PlayerControl a, PlayerControl b, Action onStarted)
        {
            Hide();

            var meeting = MeetingHud.Instance;
            var hud = FastDestroyableSingleton<HudManager>.Instance;
            if (meeting == null || hud == null) return;

            objectLayer = meeting.gameObject.layer;
            vanillaOrder = MeasureVanillaOrder(meeting);

            TheOtherRolesPlugin.Logger.LogInfo($"[JusticeMeetingVisual] meetingLayer={objectLayer} vanillaOrder={vanillaOrder}");

            root = createChild("JusticeMeetingVisual", meeting.transform, Vector3.zero);
            introRoot = createChild("JusticeIntro", root.transform, Vector3.zero);

            byte idA = a != null ? a.PlayerId : (byte)0;
            byte idB = b != null ? b.PlayerId : (byte)0;

            int photoA = (int)(idA % PhotoData.Length);
            int photoB = (photoA + 1 + ((idB + 1) % (PhotoData.Length - 3))) % PhotoData.Length;

            hud.StartCoroutine(CoIntro(meeting, a, b, photoA, photoB, onStarted).WrapToIl2Cpp());
        }

        private static IEnumerator CoIntro(MeetingHud meeting, PlayerControl a, PlayerControl b, int photoA, int photoB, Action onStarted)
        {
            if (meeting.TimerText != null) meeting.TimerText.gameObject.SetActive(false);

            JusticeAudio.PlayIntro();

            var hud = FastDestroyableSingleton<HudManager>.Instance;
            hud.StartCoroutine(CoDisappearVotingArea(meeting, 2.3f).WrapToIl2Cpp());
            hud.StartCoroutine(CoAlertFlash().WrapToIl2Cpp());

            yield return Effects.Wait(0.1f);

            hud.StartCoroutine(CoAnimBackLine(introRoot).WrapToIl2Cpp());
            hud.StartCoroutine(CoAnimIntroText(introRoot).WrapToIl2Cpp());

            var black = add(introRoot != null ? introRoot.transform : null, "Black", sprite("MeetingUIMask.png", 100f), new Vector3(0f, 0f, -19f), 1f, vanillaOrder + 22);
            if (black != null)
            {
                black.transform.localScale = new Vector3(1.2f, 1f, 1f);
                black.color = new Color(0f, 0f, 0f, 0f);
            }

            yield return Effects.Wait(2.5f);

            hud.StartCoroutine(CoMeetingSound().WrapToIl2Cpp());

            if (black != null) yield return CoAnimColor(black, new Color(0f, 0f, 0f, 0f), Color.black, 1.2f);

            hud.StartCoroutine(CoDropBlack(black).WrapToIl2Cpp());

            if (introRoot != null) UnityEngine.Object.Destroy(introRoot);
            introRoot = null;


            BuildBackground();
            SpawnSide(meeting, a, new Vector3(-2f, 0f, 0f), "JusticeHolderLeft.png", 0, photoA);
            SpawnSide(meeting, b, new Vector3(2f, 0f, 0f), "JusticeHolderRight.png", 1, photoB);

            if (meeting.TimerText != null) meeting.TimerText.gameObject.SetActive(true);

            if (meeting.TitleText != null)
            {
                meeting.TitleText.transform.localPosition = new Vector3(-0.25f, 2.2f, -1f);
                meeting.TitleText.text = ModTranslation.getString("justiceMeetingTitle");
            }

            onStarted?.Invoke();
        }

        private static IEnumerator CoMeetingSound()
        {
            yield return Effects.Wait(0.2f);
            JusticeAudio.PlayMeeting();
        }

        private static IEnumerator CoDropBlack(SpriteRenderer black)
        {
            yield return Effects.Wait(1f);
            yield return CoAnimColor(black, Color.black, new Color(0f, 0f, 0f, 0f), 1f);
            if (black != null) UnityEngine.Object.Destroy(black.gameObject);
        }

        private static IEnumerator CoDisappearVotingArea(MeetingHud meeting, float duration)
        {
            if (meeting.playerStates == null) yield break;

            var states = meeting.playerStates.Where(x => x != null).OrderBy(_ => Guid.NewGuid()).ToArray();
            if (states.Length == 0) yield break;

            float interval = duration / states.Length;
            foreach (var state in states)
            {
                if (state != null) state.gameObject.SetActive(false);
                yield return Effects.Wait(interval);
            }
        }

        private static IEnumerator CoAlertFlash()
        {
            yield return Effects.Wait(0.15f);

            for (int i = 0; i < 3; i++)
            {
                var hud = FastDestroyableSingleton<HudManager>.Instance;
                if (hud != null && hud.FullScreen != null)
                    hud.StartCoroutine(CoFlash(Color.red, 0.2f, 0.2f, 0.3f, 0.6f).WrapToIl2Cpp());

                yield return Effects.Wait(1.3f);
            }
        }

        private static IEnumerator CoFlash(Color color, float fadeIn, float fadeOut, float maxAlpha, float hold)
        {
            var hud = FastDestroyableSingleton<HudManager>.Instance;
            if (hud == null || hud.FullScreen == null) yield break;

            var flash = UnityEngine.Object.Instantiate(hud.FullScreen, hud.transform);
            flash.enabled = true;
            flash.gameObject.SetActive(true);
            flash.color = new Color(color.r, color.g, color.b, 0f);

            float t = 0f;
            while (t < fadeIn)
            {
                t += Time.deltaTime;
                if (flash != null) flash.color = new Color(color.r, color.g, color.b, Mathf.Clamp01(maxAlpha * t / fadeIn));
                yield return null;
            }

            if (flash != null) flash.color = new Color(color.r, color.g, color.b, maxAlpha);
            yield return Effects.Wait(hold);

            t = 0f;
            while (t < fadeOut)
            {
                t += Time.deltaTime;
                if (flash != null) flash.color = new Color(color.r, color.g, color.b, Mathf.Clamp01(maxAlpha * (1f - t / fadeOut)));
                yield return null;
            }

            if (flash != null) UnityEngine.Object.Destroy(flash.gameObject);
        }

        private static IEnumerator CoAnimBackLine(GameObject parent)
        {
            if (parent == null) yield break;

            var line = addFull(parent.transform, "BackLine", new Vector2(8.79f, 0f), new Color(0f, 0f, 0f, 0.85f), new Vector3(0.01f, 0f, -18f), vanillaOrder + 21);
            if (line == null) yield break;

            float t = 0f;
            float p = 0f;
            while (t < 2f && parent != null)
            {
                p += (1f - p) * Mathf.Min(1f, Time.deltaTime * 3.5f);
                if (line != null) line.transform.localScale = new Vector3(8.79f, p * 0.76f, 1f);
                t += Time.deltaTime;
                yield return null;
            }

            yield return Effects.Wait(1.25f);

            t = 0f;
            while (t < 1f && parent != null)
            {
                p -= p * Mathf.Min(1f, Time.deltaTime * 6.9f);
                if (line != null) line.transform.localScale = new Vector3(8.79f, p * 0.76f, 1f);
                t += Time.deltaTime;
                yield return null;
            }
        }

        private static IEnumerator CoAnimIntroText(GameObject parent)
        {
            if (parent == null) yield break;

            string text = rnd.NextDouble() < 0.1f ? "The balance is at will!" : "Justice meeting begins...";

            var target = new BitmapText("IntroText", parent.transform, new Vector3(0f, 0f, -18.5f), font(), vanillaOrder + 22);
            target.FontSize = 0.48f;
            target.Pivot = new Vector2(0.5f, 0.5f);
            target.Alignment = BitmapTextAlignment.Center;
            target.SetText("");
            target.Color = Color.white;
            yield return null;

            for (int i = 0; i < text.Length; i++)
            {
                if (target.GameObject == null) yield break;
                target.SetText(text.Substring(0, i + 1));
                yield return Effects.Wait(0.078f);
            }

            for (int i = 0; i < 3; i++)
            {
                if (target.GameObject == null) yield break;
                target.SetActive(false);
                yield return Effects.Wait(0.04f);
                target.SetActive(true);
                yield return Effects.Wait(0.04f);
            }

            target.Destroy();
        }

        private static void BuildBackground()
        {
            if (root == null) return;

            backObject = createChild("JusticeBackground", root.transform, new Vector3(0f, 0f, 7f));

            var group = backObject.AddComponent<SortingGroup>();
            group.sortingOrder = vanillaOrder;

            var mask = Helpers.CreateObject<SpriteMask>("JusticeMask", backObject.transform, Vector3.zero);
            mask.sprite = sprite("MeetingUIMask.png", 100f);
            mask.transform.localScale = new Vector3(1.2f, 1f, 1f);

            background1 = add(backObject.transform, "JusticeBack", sprite("MeetingBack.png", 100f), Vector3.zero, 1f, 1);
            var background2 = add(backObject.transform, "JusticeBackAlpha", sprite("MeetingBackAlpha.png", 100f), new Vector3(0f, 0f, -0.1f), 1f, 2);

            if (background1 != null)
            {
                background1.transform.localScale = new Vector3(BackScaleX, BackScaleY, 1f);
                background1.color = new Color(0.002f, 0.03f, 0.16f);
                background1.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
            }

            if (background2 != null)
            {
                background2.transform.localScale = new Vector3(BackScaleX, BackScaleY, 1f);
                background2.color = new Color(Justice.color.r * 0.21f, Justice.color.g * 0.21f, Justice.color.b * 0.21f, 1f);
                background2.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
            }

            backView = add(backObject.transform, "JusticeBackView", sprite("JusticeMeetingView.png", 100f), new Vector3(0f, 0f, -0.2f), ReticleScale, 3);
            if (backView != null) backView.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;

            var reticle = add(backObject.transform, "JusticeBackReticle", sprite("JusticeMeetingReticle.png", 100f), new Vector3(0f, 0f, -0.2f), ReticleScale, 4);
            if (reticle != null) reticle.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;

            reticleText = new BitmapText("ReticleText", backObject.transform, new Vector3(0f, -1.9f, -0.2f), font(), 6);
            reticleText.FontSize = 0.42f;
            reticleText.Pivot = new Vector2(0.5f, 0.5f);
            reticleText.Alignment = BitmapTextAlignment.Center;
            reticleText.Color = new Color(0.7f, 0.7f, 0.7f, 0.4f);
            reticleText.SetText("");

            var hud = FastDestroyableSingleton<HudManager>.Instance;
            if (hud == null) return;

            var renderers = backObject.GetComponentsInChildren<SpriteRenderer>(true);
            TheOtherRolesPlugin.Logger.LogInfo($"[JusticeMeetingVisual] background layer={backObject.layer} renderers={renderers.Length} orders=[{string.Join(",", renderers.Select(x => x.sortingOrder))}] hasSprite={renderers.All(x => x.sprite != null)}");

            hud.StartCoroutine(CoAnimColorRepeat(background1, new Color(0.002f, 0.03f, 0.16f), new Color(0.002f, 0.1f, 0.1f), 5f).WrapToIl2Cpp());
            hud.StartCoroutine(CoRepeatReticleText().WrapToIl2Cpp());
            hud.StartCoroutine(CoAnimBackView().WrapToIl2Cpp());
            hud.StartCoroutine(CoUpdateGauges().WrapToIl2Cpp());
        }

        private static void SpawnSide(MeetingHud meeting, PlayerControl player, Vector3 localPosition, string holderFile, int sideIndex, int photoIndex)
        {
            if (player == null || root == null) return;

            var hud = FastDestroyableSingleton<HudManager>.Instance;
            if (hud == null) return;

            var holderRoot = createChild("JusticeSide" + sideIndex, root.transform, Vector3.zero);

            var flash = add(holderRoot.transform, "Flash", sprite("JusticeHolderFlash.png", 120f), localPosition + new Vector3(0f, 0f, -19.5f), 1f, vanillaOrder + 20);
            var blur = add(holderRoot.transform, "FlashBlur", sprite("JusticeHolderFlashBlur.png", 120f), localPosition + new Vector3(0f, 0f, -19.5f), 1f, vanillaOrder + 19);

            if (flash != null) flash.color = new Color(1f, 1f, 1f, 0f);
            if (blur != null) blur.color = new Color(1f, 1f, 1f, 0f);

            hud.StartCoroutine(CoFlashHolder(holderRoot, flash, blur).WrapToIl2Cpp());
            hud.StartCoroutine(CoShake(holderRoot, 1.3f).WrapToIl2Cpp());
            hud.StartCoroutine(CoSpawnPlayerArea(meeting, player, localPosition, holderFile, sideIndex, photoIndex).WrapToIl2Cpp());
        }

        private static IEnumerator CoFlashHolder(GameObject holderRoot, SpriteRenderer flash, SpriteRenderer blur)
        {
            float t = 0f;
            while (t < 1.4f)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / 1.4f);

                if (flash != null)
                {
                    flash.color = new Color(1f, 1f, 1f, p);
                    flash.transform.localScale = Vector3.one;
                }
                if (blur != null)
                {
                    blur.color = new Color(1f, 1f, 1f, p * 0.5f);
                    blur.transform.localScale = Vector3.one * (0.6f + p * 0.1f);
                }
                yield return null;
            }

            yield return Effects.Wait(0.1f);

            t = 0f;
            while (t < 0.5f)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / 0.5f);

                if (flash != null) flash.color = new Color(1f, 1f, 1f, 1f - p);
                if (blur != null)
                {
                    blur.color = new Color(1f, 1f, 1f, 0.5f - p * 0.5f);
                    blur.transform.localScale = Vector3.one * (0.7f + p * 0.2f);
                }
                yield return null;
            }

            if (holderRoot != null) UnityEngine.Object.Destroy(holderRoot);
        }

        private static IEnumerator CoShake(GameObject holderRoot, float duration)
        {
            float t = duration;
            while (t > 0f && holderRoot != null)
            {
                float p = t / duration;
                float angle = (float)rnd.NextDouble() * 360f;
                Vector3 direction = Quaternion.Euler(0f, 0f, angle) * Vector3.right;

                holderRoot.transform.localPosition = direction * p * 0.16f * ((float)rnd.NextDouble() * 0.6f + 0.4f);

                float wait = 0.001f + (1f - p) * (1f - p) * 0.2f;
                yield return Effects.Wait(wait);
                t -= wait;
            }

            if (holderRoot != null) holderRoot.transform.localPosition = Vector3.zero;
        }

        private static IEnumerator CoSpawnPlayerArea(MeetingHud meeting, PlayerControl player, Vector3 localPosition, string holderFile, int sideIndex, int photoIndex)
        {
            yield return Effects.Wait(1.3f);

            if (root == null || player == null || meeting == null) yield break;

            byte playerId = player.PlayerId;

            var back = add(root.transform, "JusticePlayerArea" + sideIndex, sprite(holderFile, 120f), localPosition + new Vector3(0f, 0f, 6f), 1f, vanillaOrder);
            if (back == null) yield break;

            var hatManager = DestroyableSingleton<HatManager>.Instance;
            if (hatManager != null)
            {
                back.sharedMaterial = hatManager.PlayerMaterial;
                PlayerMaterial.SetColors(playerId, back);
            }

            var maskObject = createChild("Masked", back.transform, new Vector3(0f, 0f, -0.5f));

            var mask = maskObject.AddComponent<SpriteMask>();
            mask.sprite = sprite("JusticeHolderMask.png", 120f);

            Color32 playerColor = Palette.PlayerColors[playerId];
            Color32 shadowColor = Palette.ShadowColors[playerId];

            Color graphColor = Color.Lerp(Color.Lerp(playerColor, shadowColor, 0.25f), Color.white, 0.28f);
            bool lightColor = (playerColor.r + playerColor.g + playerColor.b) / 3f > 0.6f;
            Color textColor = Color.Lerp(playerColor, lightColor ? new Color(0.18f, 0.18f, 0.18f, 1f) : Color.white, 0.4f);

            for (int i = 0; i < 3; i++)
                BuildGauge(back.transform, new Vector3(-0.6f + 0.28f * i, -0.5f, -0.2f), graphColor);

            BuildBand(back.transform, new Vector3(0.65f, -0.13f, -0.2f), graphColor);

            var aka = new BitmapText("AkaText", back.transform, new Vector3(0.67f, 0.87f, -0.5f), font(), vanillaOrder);
            aka.FontSize = 0.14f;
            aka.Pivot = new Vector2(0.5f, 0.5f);
            aka.Alignment = BitmapTextAlignment.Center;
            aka.Color = new Color(1f, 1f, 1f, 0.8f);
            aka.SetText("a.k.a.");

            lastAltIndex = (lastAltIndex + 1 + (int)(rnd.NextDouble() * (RandomAltTexts.Length - 1))) % RandomAltTexts.Length;

            var alt = new BitmapText("AltText", back.transform, new Vector3(0.67f, 0.65f, -0.5f), font(), vanillaOrder);
            alt.FontSize = 0.27f;
            alt.Pivot = new Vector2(0.5f, 0.5f);
            alt.Alignment = BitmapTextAlignment.Center;
            alt.Color = new Color(1f, 1f, 1f, 0.8f);
            alt.SetText(RandomAltTexts[lastAltIndex]);

            lastNumberIndex = (lastNumberIndex + 1 + (int)(rnd.NextDouble() * 9)) % 10;

            var number = new BitmapText("NumberText", back.transform, new Vector3(-0.8f, 1.2f, -0.4f), font(), vanillaOrder);
            number.FontSize = 1.2f;
            number.Pivot = new Vector2(0.5f, 0.5f);
            number.Alignment = BitmapTextAlignment.Center;
            number.Color = textColor;
            number.SetText(((char)('0' + lastNumberIndex)).ToString());

            BuildPhoto(back.transform, playerId, photoIndex);

            var state = meeting.playerStates.FirstOrDefault(x => x != null && (byte)x.PlayerId == playerId);
            if (state != null)
            {
                state.gameObject.SetActive(true);
                state.gameObject.transform.localPosition = localPosition + new Vector3(0.11f, -1.08f, -0.9f);
                state.gameObject.transform.localScale = Vector3.one;
            }
        }

        private static void BuildGauge(Transform parent, Vector3 localPosition, Color graphColor)
        {
            add(parent, "GaugeBack", sprite("JusticeCircleBackGraph.png", 120f), localPosition, 1f, vanillaOrder);

            var front = add(parent, "GaugeFront", sprite("JusticeCircleGraph.png", 120f), localPosition + new Vector3(0f, 0f, -0.05f), 1f, vanillaOrder);
            if (front == null) return;

            front.transform.localScale = new Vector3(-1f, 1f, 1f);
            front.color = graphColor;

            var shader = Helpers.achievementProgressShader;
            if (shader == null) return;

            var material = new Material(shader);
            material.SetFloat("_Guage", 0.5f);
            material.color = Color.Lerp(graphColor, Color.white, 0.5f);
            front.material = material;

            gauges.Add((material, (float)rnd.NextDouble(), 0f, 1f, 0.2f));
        }

        private static void BuildBand(Transform parent, Vector3 center, Color graphColor)
        {
            var holder = createChild("BandHolder", parent, center);

            var group = new BandGroup();

            for (int i = 0; i < BandCount; i++)
            {
                float x = ((float)i / (BandCount - 1) - 0.5f) * BandWidth;
                var renderer = add(holder.transform, "Band" + i, sprite("JusticeBandGraph.png", 120f), new Vector3(x, 0f, 0f), 1f, vanillaOrder);
                if (renderer == null) continue;

                renderer.color = graphColor;
                renderer.transform.localScale = new Vector3(1f, 0f, 1f);

                group.renderers.Add(renderer);
                group.filter[i] = 0.5f + Mathf.Clamp(0.2f * Mathf.Min(i, BandCount - 1 - i), 0f, 0.5f);
            }

            bandGroups.Add(group);

            var hud = FastDestroyableSingleton<HudManager>.Instance;
            if (hud != null) hud.StartCoroutine(CoUpdateBand(group).WrapToIl2Cpp());
        }

        private static IEnumerator CoUpdateBand(BandGroup group)
        {
            while (root != null && group.renderers.Any(x => x != null))
            {
                for (int wave = 0; wave < 3; wave++)
                {
                    float center = (float)rnd.NextDouble();
                    float height = 0.3f * (float)rnd.NextDouble() * 2f;
                    height *= height;
                    float width = 0.2f * (float)rnd.NextDouble() * 0.45f;
                    if (width < 0.0001f) width = 0.0001f;

                    for (int i = 0; i < BandCount; i++)
                    {
                        float pos = (float)i / BandCount;
                        float x = (pos - center) / width;
                        float denominator = x * x + Mathf.Cos(x);
                        if (denominator <= 0.0001f) continue;

                        float y = height / Mathf.Sqrt(denominator);
                        if (y > group.secret[i]) group.secret[i] = y;
                    }
                }

                for (int i = 0; i < BandCount && i < group.renderers.Count; i++)
                {
                    var renderer = group.renderers[i];
                    if (renderer == null) continue;

                    if (group.displayed[i] < group.secret[i]) group.displayed[i] += delta(group.secret[i] - group.displayed[i], 8f, 0.1f);
                    else group.displayed[i] = group.secret[i];

                    renderer.transform.localScale = new Vector3(1f, group.displayed[i] * group.filter[i] * BandHeightFactor, 1f);

                    group.secret[i] -= Time.deltaTime * 0.5f;
                    if (group.secret[i] < 0f) group.secret[i] = 0f;
                }

                yield return null;
            }
        }

        private static Sprite[] boardingPassPhotos;
        private static bool boardingPassSearched;

        private static Sprite[] BoardingPassPhotos()
        {
            if (boardingPassSearched) return boardingPassPhotos;
            boardingPassSearched = true;

            foreach (var ship in VanillaAsset.MapAsset)
            {
                if (ship == null || ship.CommonTasks == null) continue;

                var game = ship.CommonTasks.FirstOrDefault(p => p != null && p.MinigamePrefab != null
                    && p.MinigamePrefab.name == "BoardingPassGame")?.MinigamePrefab.TryCast<BoardPassGame>();

                if (game?.Photos != null && game.Photos.Length > 0)
                {
                    boardingPassPhotos = game.Photos;
                    break;
                }
            }

            if (boardingPassPhotos == null)
                TheOtherRolesPlugin.Logger.LogInfo("[JusticeMeetingVisual] no boarding pass photo set found");

            return boardingPassPhotos;
        }

        private static void BuildPhoto(Transform parent, byte playerId, int photoIndex)
        {
            var photos = BoardingPassPhotos();
            if (photos == null) return;

            var entry = PhotoData[photoIndex % PhotoData.Length];
            if (entry.photoIndex >= photos.Length) return;

            var photo = add(parent, "JusticeHolderPhoto", photos[entry.photoIndex],
                new Vector3(entry.localPos.x, entry.localPos.y, -0.5f), entry.scale, vanillaOrder);
            if (photo == null) return;

            var hatManager = DestroyableSingleton<HatManager>.Instance;
            if (hatManager != null)
            {
                photo.sharedMaterial = hatManager.PlayerMaterial;
                PlayerMaterial.SetColors(playerId, photo);
            }
        }

        private static IEnumerator CoAnimColor(SpriteRenderer renderer, Color from, Color to, float duration)
        {
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                if (renderer != null) renderer.color = Color.Lerp(from, to, t / duration);
                yield return null;
            }

            if (renderer != null) renderer.color = to;
        }

        private static IEnumerator CoAnimColorRepeat(SpriteRenderer renderer, Color color1, Color color2, float duration)
        {
            while (renderer != null)
            {
                yield return CoAnimColor(renderer, color1, color2, duration);
                yield return CoAnimColor(renderer, color2, color1, duration);
            }
        }

        private static IEnumerator CoRepeatReticleText()
        {
            var queue = new List<string>();
            int index = 0;

            while (reticleText != null && reticleText.GameObject != null)
            {
                if (index >= queue.Count)
                {
                    queue = RandomTexts.OrderBy(_ => Guid.NewGuid()).ToList();
                    index = 0;
                }

                yield return CoTypeText(queue[index++]);
            }
        }

        private static IEnumerator CoTypeText(string text)
        {
            reticleText.SetText("");
            reticleText.SetActive(true);
            yield return null;

            for (int i = 1; i <= text.Length; i++)
            {
                if (reticleText == null || reticleText.GameObject == null) yield break;
                reticleText.SetText(text.Substring(0, i));
                yield return Effects.Wait(0.08f);
            }

            for (int i = 0; i < 3; i++)
            {
                if (reticleText == null || reticleText.GameObject == null) yield break;
                reticleText.SetActive(false);
                yield return Effects.Wait(0.05f);
                reticleText.SetActive(true);
                yield return Effects.Wait(0.05f);
            }

            yield return Effects.Wait(5f + (float)rnd.NextDouble() * 8f);

            for (int i = 0; i < 3; i++)
            {
                if (reticleText == null || reticleText.GameObject == null) yield break;
                reticleText.SetActive(false);
                yield return Effects.Wait(0.05f);
                reticleText.SetActive(true);
                yield return Effects.Wait(0.05f);
            }

            if (reticleText == null || reticleText.GameObject == null) yield break;
            reticleText.SetActive(false);
            yield return Effects.Wait(0.6f + (float)rnd.NextDouble() * 0.6f);
        }

        private static IEnumerator CoAnimBackView()
        {
            var baseColor = new Color(0.5f, 0.5f, 0.5f, 0.4f);
            if (backView != null) backView.color = baseColor;

            while (backView != null)
            {
                yield return Effects.Wait(5.5f + (float)rnd.NextDouble() * 3f);
                if (backView == null) yield break;

                switch (rnd.Next(3))
                {
                    case 0:
                        yield return CoAnimColor(backView, new Color(1f, 0.3f, 0.3f, 0.4f), baseColor, 0.8f);
                        break;
                    case 1:
                        yield return CoAnimColor(backView, new Color(0.9f, 0.3f, 0.3f, 0.4f), baseColor, 0.3f);
                        yield return Effects.Wait(0.2f);
                        yield return CoAnimColor(backView, new Color(1f, 0.3f, 0.3f, 0.4f), baseColor, 1.4f);
                        break;
                    default:
                        yield return CoAnimColor(backView, baseColor, new Color(1f, 1f, 1f, 0.6f), 0.2f);
                        yield return CoAnimColor(backView, new Color(1f, 1f, 1f, 0.6f), baseColor, 0.8f);
                        break;
                }
            }
        }

        private static IEnumerator CoUpdateGauges()
        {
            while (root != null)
            {
                float dt = Time.deltaTime;

                for (int i = 0; i < gauges.Count; i++)
                {
                    var (material, goal, value, bigTimer, smallTimer) = gauges[i];
                    if (material == null) continue;

                    value -= delta(value - goal, 0.8f, 0.001f);
                    bigTimer -= dt;
                    smallTimer -= dt;

                    if (bigTimer < 0f)
                    {
                        goal = Mathf.Clamp01(goal + ((float)rnd.NextDouble() - 0.5f) * 0.35f);
                        bigTimer = 1.4f + (float)rnd.NextDouble() * 6f;
                    }
                    if (smallTimer < 0f)
                    {
                        goal = Mathf.Clamp01(goal + ((float)rnd.NextDouble() - 0.5f) * 0.15f);
                        smallTimer = 0.2f;
                    }

                    material.SetFloat("_Guage", value);
                    gauges[i] = (material, goal, value, bigTimer, smallTimer);
                }

                yield return null;
            }
        }

        public static void Hide()
        {
            BitmapText.DestroyGroup("justice");

            if (root != null) UnityEngine.Object.Destroy(root);
            root = null;
            introRoot = null;
            backObject = null;

            bandGroups.Clear();
            gauges.Clear();

            backView = null;
            background1 = null;
            reticleText = null;
        }
    }
}
