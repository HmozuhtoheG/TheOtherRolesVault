using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace TheOtherRoles.Patches
{
    [HarmonyPriority(Priority.HigherThanNormal)]
    [HarmonyPatch(typeof(SplashManager), nameof(SplashManager.Update))]
    public static class LoadPatch
    {
        private const float MinLoadTime = 6f;
        private const float FirstRowGap = 0.9f;
        private const float RowGap = 0.32f;
        private const float FadeInDuration = 0.25f;
        private const float CompletedHold = 0.15f;
        private const float MoveDuration = 0.15f;
        private const float SuccessFadeIn = 0.3f;
        private const float SuccessHold = 0.8f;
        private const float TipInterval = 2f;
        private const float TipFade = 0.25f;

        private const float LogoFadeIn = 0.6f;
        private const float LogoFloatCycle = 2f;
        private const float LogoFloatAmp = 0.025f;

        private const float ClickGlowCycle = 1.5f;

        private const int BlobCount = 4;
        private const float BlobMinCycle = 20f;
        private const float BlobMaxCycle = 40f;
        private const float BlobAlpha = 0.14f;
        private const float SplitMinInterval = 5f;
        private const float SplitMaxInterval = 9f;
        private const float SplitDuration = 3f;

        private static readonly Color BlobColorA = new(0.96f, 0.83f, 0.54f, 1f);
        private static readonly Color BlobColorB = new(1.00f, 0.91f, 0.69f, 1f);
        private static readonly Color ProgressGray = new(0.72f, 0.72f, 0.74f, 1f);
        private static readonly Color ProgressGreen = new(0.486f, 0.988f, 0f, 1f);
        private static readonly Color ClickGold = new(0.96f, 0.83f, 0.54f, 1f);

        private static readonly Vector3 LogoPos = new(0f, 0.5f, -5f);
        private static readonly Vector3 StepAnchor = new(0f, -0.55f, -10f);
        private static readonly Vector3 TipPos = new(0f, -3.6f, -10f);
        private static readonly Vector3 VersionPos = new(4.5f, -3.2f, -10f);
        private static readonly Vector3 ClickPos = new(0f, -1.0f, -10f);

        private const string LogoResource = "TheOtherRoles.Resources.ReBuildUi.MainMenuLogo.png";

        private static readonly string[] LoadStepTexts =
        [
            "正在加载资源...",
            "正在解压资源包...",
            "正在初始化模组核心...",
            "正在注册组件...",
            "正在配置 Harmony 补丁...",
            "正在加载语言数据...",
            "正在准备游戏环境...",
            "正在校准模组参数...",
            "正在建立通信管道...",
        ];

        private static readonly string[] TipTexts =
        [
            "提示：本模组与大多数模组不兼容",
            "提示：客户端选项可以在游戏内设置界面里调整",
            "提示：对局中的帮助菜单可以查看全部角色介绍",
            "感谢您选择 The Other Roles Vault！",
            "不要相信内鬼的话",
            "祝你好运！",
            "如果玩AU就是你的力量的话，失去了他你又能干什么",
            "imp11快女装!",
            "load界面由hvt协助完成",
            "提示:TORV是GMIA的分支",
        ];

        private const string LoadSuccessTextCN = "加载成功！";
        private const string ClickToEnterTextCN = "-- 点击任意处进入游戏 --";

        private static SpriteRenderer logo;
        private static readonly List<SpriteRenderer> blobs = new();
        private static readonly List<SpriteRenderer> subBlobs = new();
        private static readonly List<BlobAnim> blobAnims = new();

        private static TextMeshPro currentStepText;
        private static TextMeshPro tipText;
        private static TextMeshPro successText;
        private static TextMeshPro clickText;
        private static TextMeshPro versionText;
        private static readonly List<TextMeshPro> completedSteps = new();
        private static readonly List<Tweener> moveTweens = new();

        private static bool ambientRunning;
        private static int tipState;
        private static float tipFadeElapsed;
        private static float tipTimer;
        private static int lastTipIndex;

        private static bool loaded;
        private static bool cachedDoneLoadingRefData;

        private sealed class Tweener
        {
            public float From;
            public float To;
            public float Duration;
            public float Elapsed;
            public Action<float> Set;
            public Action<float> OnDone;

            public bool Update()
            {
                Elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(Elapsed / Mathf.Max(0.0001f, Duration));
                float eased = 0.5f - 0.5f * Mathf.Cos(Mathf.PI * t);
                float v = Mathf.Lerp(From, To, eased);
                Set?.Invoke(v);
                if (t >= 1f)
                {
                    OnDone?.Invoke(v);
                    return true;
                }
                return false;
            }
        }

        private static void CleanupTweens()
        {
            for (int i = moveTweens.Count - 1; i >= 0; i--)
            {
                if (moveTweens[i].Update())
                    moveTweens.RemoveAt(i);
            }
        }

        public static bool Prefix(SplashManager __instance)
        {
            try
            {
                cachedDoneLoadingRefData |= __instance.doneLoadingRefdata;
                if (!cachedDoneLoadingRefData && Time.time - __instance.startTime > 15f)
                    cachedDoneLoadingRefData = true;
                __instance.doneLoadingRefdata = false;

                if (cachedDoneLoadingRefData
                    && !__instance.startedSceneLoad
                    && Time.time - __instance.startTime > Mathf.Max(__instance.minimumSecondsBeforeSceneChange, 1f)
                    && !loaded)
                {
                    loaded = true;
                    __instance.StartCoroutine(CoLoadTorg(__instance).WrapToIl2Cpp());
                }

                return false;
            }
            catch (Exception ex)
            {
                TheOtherRolesPlugin.Logger.LogError("[LoadPatch.Prefix] " + ex.Message);
                return false;
            }
        }

        public static void Postfix(SplashManager __instance)
        {
            try
            {
                __instance.doneLoadingRefdata = cachedDoneLoadingRefData;
            }
            catch (Exception ex)
            {
                TheOtherRolesPlugin.Logger.LogError("[LoadPatch.Postfix] " + ex.Message);
            }
        }

        private static IEnumerator CoAmbient()
        {
            while (ambientRunning)
            {
                if (logo != null)
                {
                    logo.transform.localPosition = LogoPos + new Vector3(
                        0f,
                        Mathf.Sin(Time.time / LogoFloatCycle * Mathf.PI * 2f) * LogoFloatAmp,
                        0f);
                }

                for (int b = 0; b < blobAnims.Count; b++)
                {
                    var a = blobAnims[b];
                    float t = Time.time / a.cycle * Mathf.PI * 2f + a.phase;
                    float scaleT = Time.time / a.scaleCycle * Mathf.PI * 2f + a.scalePhase;

                    var pos = a.origin + new Vector3(
                        Mathf.Sin(t) * 0.8f + Mathf.Sin(t * 1.7f + a.phase2) * 0.3f,
                        Mathf.Cos(t * 1.13f) * 0.5f + Mathf.Cos(t * 2.3f + a.phase2 * 2f) * 0.25f,
                        0f);
                    blobs[b].transform.localPosition = pos;
                    float s = a.scaleBase * (1f + Mathf.Sin(scaleT) * 0.15f);
                    blobs[b].transform.localScale = Vector3.one * s;
                    blobs[b].transform.localEulerAngles = new Vector3(0f, 0f, Mathf.Sin(t * 0.7f) * 14f + Mathf.Sin(t * 1.9f + a.phase2) * 6f);

                    a.splitTimer += Time.deltaTime;
                    if (!a.splitting && a.splitTimer >= a.splitInterval)
                    {
                        a.splitTimer = 0f;
                        a.splitting = true;
                        a.splitElapsed = 0f;
                        a.splitStart = pos;
                        var dir = UnityEngine.Random.insideUnitCircle;
                        if (dir.sqrMagnitude < 0.01f) dir = new Vector2(1f, 0.2f);
                        a.splitDir = dir.normalized;
                        a.splitDist = UnityEngine.Random.Range(0.8f, 1.6f);
                    }

                    if (a.splitting && subBlobs[b] != null)
                    {
                        a.splitElapsed += Time.deltaTime;
                        float k = Mathf.Clamp01(a.splitElapsed / SplitDuration);
                        var sp = a.splitStart + (Vector3)a.splitDir * (a.splitDist * Mathf.Sin(k * Mathf.PI * 0.5f));
                        subBlobs[b].transform.localPosition = sp;
                        subBlobs[b].transform.localScale = Vector3.one * (0.6f + k * 0.8f);
                        float sa = Mathf.Sin(k * Mathf.PI);
                        subBlobs[b].color = new Color(a.color.r, a.color.g, a.color.b, a.color.a * sa * 0.8f);
                        if (k >= 1f) a.splitting = false;
                    }
                }

                if (tipText != null)
                {
                    tipTimer += Time.deltaTime;
                    if (tipState == 0 && tipTimer >= TipInterval && TipTexts.Length > 1)
                    {
                        tipTimer = 0f;
                        tipState = 1;
                        tipFadeElapsed = 0f;
                    }
                    if (tipState == 1)
                    {
                        tipFadeElapsed += Time.deltaTime;
                        float k = Mathf.Clamp01(tipFadeElapsed / TipFade);
                        tipText.alpha = 0.8f * (1f - k);
                        if (k >= 1f)
                        {
                            int newIdx;
                            do { newIdx = UnityEngine.Random.Range(0, TipTexts.Length); } while (newIdx == lastTipIndex && TipTexts.Length > 1);
                            lastTipIndex = newIdx;
                            tipText.text = TipTexts[newIdx];
                            tipState = 2;
                            tipFadeElapsed = 0f;
                        }
                    }
                    else if (tipState == 2)
                    {
                        tipFadeElapsed += Time.deltaTime;
                        float k = Mathf.Clamp01(tipFadeElapsed / TipFade);
                        tipText.alpha = 0.8f * k;
                        if (k >= 1f) tipState = 0;
                    }
                }

                yield return null;
            }
        }

        private static IEnumerator CoLoadTorg(SplashManager instance)
        {
            float startTime = Time.time;

            var blobSprite = CreateBlobSprite(256);
            for (int i = 0; i < BlobCount; i++)
            {
                var sr = Helpers.CreateObject<SpriteRenderer>($"LightBlob{i}", null,
                    new Vector3(
                        UnityEngine.Random.Range(-4.4f, -1.6f),
                        UnityEngine.Random.Range(0.6f, 2.8f),
                        -8f));
                sr.sprite = blobSprite;
                var blobColor = UnityEngine.Random.value < 0.5f ? BlobColorA : BlobColorB;
                float a = UnityEngine.Random.Range(0.12f, BlobAlpha);
                sr.color = new Color(
                    blobColor.r * UnityEngine.Random.Range(0.9f, 1f),
                    blobColor.g * UnityEngine.Random.Range(0.9f, 1f),
                    blobColor.b * UnityEngine.Random.Range(0.9f, 1f),
                    a);
                float scale = UnityEngine.Random.Range(3.2f, 5.5f);
                sr.transform.localScale = Vector3.one * scale;
                blobs.Add(sr);
                blobAnims.Add(new BlobAnim
                {
                    origin = sr.transform.localPosition,
                    cycle = UnityEngine.Random.Range(BlobMinCycle, BlobMaxCycle),
                    phase = UnityEngine.Random.Range(0f, Mathf.PI * 2f),
                    phase2 = UnityEngine.Random.Range(0f, Mathf.PI * 2f),
                    scaleBase = scale,
                    scaleCycle = UnityEngine.Random.Range(18f, 34f),
                    scalePhase = UnityEngine.Random.Range(0f, Mathf.PI * 2f),
                    color = sr.color,
                    splitInterval = UnityEngine.Random.Range(SplitMinInterval, SplitMaxInterval),
                    splitTimer = UnityEngine.Random.Range(0f, SplitMaxInterval),
                });

                var sub = Helpers.CreateObject<SpriteRenderer>($"LightBlobSub{i}", null, sr.transform.localPosition + new Vector3(0f, 0f, -0.05f));
                sub.sprite = blobSprite;
                sub.transform.localScale = Vector3.one * 0.6f;
                sub.color = new Color(sr.color.r, sr.color.g, sr.color.b, 0f);
                subBlobs.Add(sub);
            }

            logo = Helpers.CreateObject<SpriteRenderer>("TORVLogo", null, LogoPos);
            logo.transform.localScale = Vector3.one * 0.5f;
            logo.sprite = Helpers.loadSpriteFromResources(LogoResource, 100f);
            logo.color = new Color(1f, 1f, 1f, 0f);

            ambientRunning = true;
            instance.StartCoroutine(CoAmbient().WrapToIl2Cpp());

            yield return FadeAlpha(logo, 0f, 1f, LogoFadeIn);
            logo.color = Color.white;

            currentStepText = CreateText(instance, "LoadStepText", StepAnchor, FontStyles.Bold, 1f, TextAlignmentOptions.Center);
            currentStepText.text = "";
            currentStepText.color = new Color(0f, 0f, 0f, 0f);

            tipText = CreateText(instance, "LoadTipText", TipPos, FontStyles.Italic, 0.7f, TextAlignmentOptions.Center);
            tipText.color = new Color(0.6f, 0.6f, 0.6f, 0f);

            versionText = CreateText(instance, "LoadVersionText", VersionPos, FontStyles.Italic, 0.55f, TextAlignmentOptions.BottomRight);
            versionText.color = new Color(0.5f, 0.5f, 0.5f, 0.6f);
            versionText.text = TheOtherRolesPlugin.Version.ToString() + (TheOtherRolesPlugin.betaDays > 0 ? "-BETA" : "");

            lastTipIndex = UnityEngine.Random.Range(0, TipTexts.Length);
            tipText.text = TipTexts[lastTipIndex];
            tipText.alpha = 0f;
            tipState = 2;
            tipFadeElapsed = 0f;
            tipTimer = TipInterval;

            for (int stepIdx = 0; stepIdx < LoadStepTexts.Length; stepIdx++)
            {
                float stepDuration = StepDuration(stepIdx);
                currentStepText.text = LoadStepTexts[stepIdx];
                currentStepText.color = ProgressGray;
                yield return FadeAlpha(currentStepText, 0f, 1f, FadeInDuration);

                float elapsed = 0f;
                while (elapsed < stepDuration)
                {
                    elapsed += Time.deltaTime;
                    float localProgress = Mathf.Clamp01(elapsed / stepDuration);
                    int percent = Mathf.FloorToInt(localProgress * 100f);

                    currentStepText.color = Color.Lerp(ProgressGray, ProgressGreen, localProgress);
                    currentStepText.text = $"{LoadStepTexts[stepIdx]} ({percent}%)";

                    CleanupTweens();
                    yield return null;
                }

                yield return new WaitForSeconds(CompletedHold);

                completedSteps.Add(currentStepText);
                AnimateCompletedStep(currentStepText);
                float moveElapsed = 0f;
                while (moveElapsed < MoveDuration)
                {
                    moveElapsed += Time.deltaTime;
                    CleanupTweens();
                    yield return null;
                }
                CleanupTweens();

                if (stepIdx < LoadStepTexts.Length - 1)
                {
                    currentStepText = CreateText(instance, "LoadStepText", StepAnchor, FontStyles.Bold, 1f, TextAlignmentOptions.Center);
                    currentStepText.text = "";
                    currentStepText.color = new Color(0f, 0f, 0f, 0f);
                }
            }

            while (!cachedDoneLoadingRefData || Time.time - startTime < MinLoadTime)
                yield return null;

            Vector3 vanishPos = StepAnchor;
            if (completedSteps.Count > 0)
            {
                Vector3[] froms = new Vector3[completedSteps.Count];
                float[] scales = new float[completedSteps.Count];
                for (int i = 0; i < completedSteps.Count; i++)
                {
                    froms[i] = completedSteps[i].transform.localPosition;
                    scales[i] = completedSteps[i].transform.localScale.x;
                }
                float up = 0.9f;
                float duration = 0.25f;
                float elapsed = 0f;
                while (elapsed < duration)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / duration);
                    for (int i = 0; i < completedSteps.Count; i++)
                    {
                        if (completedSteps[i] == null) continue;
                        completedSteps[i].transform.localPosition = Vector3.Lerp(froms[i], froms[i] + new Vector3(0f, up, 0f), t);
                        completedSteps[i].transform.localScale = Vector3.one * Mathf.Lerp(scales[i], 0.1f, t);
                        completedSteps[i].alpha = Mathf.Lerp(1f, 0f, t);
                    }
                    yield return null;
                }
                if (completedSteps.Count > 0 && completedSteps[completedSteps.Count - 1] != null)
                    vanishPos = completedSteps[completedSteps.Count - 1].transform.localPosition;
                foreach (var c in completedSteps) if (c != null) UnityEngine.Object.Destroy(c.gameObject);
                completedSteps.Clear();
            }

            successText = CreateText(instance, "LoadSuccessText", vanishPos, FontStyles.Bold, 1.15f, TextAlignmentOptions.Center);
            successText.text = LoadSuccessTextCN;
            successText.color = ProgressGreen;
            successText.alpha = 0f;
            yield return FadeAlpha(successText, 0f, 1f, SuccessFadeIn);
            yield return new WaitForSeconds(SuccessHold);
            yield return MoveText(successText, vanishPos, ClickPos, 0.5f);
            yield return FadeAlpha(successText, 1f, 0f, 0.4f);
            UnityEngine.Object.Destroy(successText.gameObject);
            successText = null;

            clickText = CreateText(instance, "LoadClickText", ClickPos, FontStyles.Bold, 0.9f, TextAlignmentOptions.Center);
            clickText.text = ClickToEnterTextCN;
            clickText.color = new Color(ClickGold.r, ClickGold.g, ClickGold.b, 0f);

            bool entered = false;
            while (!entered)
            {
                float breathe = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * Time.time / ClickGlowCycle);
                clickText.alpha = 0.4f + breathe * 0.6f;

                if (Input.GetMouseButtonDown(0) || Input.touchCount > 0)
                {
                    entered = true;
                    break;
                }
                yield return null;
            }

            ambientRunning = false;
            yield return null;

            for (int i = 0; i < blobs.Count; i++)
                yield return FadeAlpha(blobs[i], blobs[i].color.a, 0f, 0.4f);
            yield return FadeAlpha(tipText, tipText.alpha, 0f, 0.4f);
            yield return FadeAlpha(versionText, versionText.alpha, 0f, 0.4f);
            yield return FadeAlpha(clickText, clickText.alpha, 0f, 0.4f);
            yield return FadeAlpha(logo, 1f, 0f, 0.4f);

            foreach (var b in blobs) if (b != null) UnityEngine.Object.Destroy(b.gameObject);
            foreach (var s in subBlobs) if (s != null) UnityEngine.Object.Destroy(s.gameObject);
            blobs.Clear(); subBlobs.Clear(); blobAnims.Clear();
            if (logo != null) UnityEngine.Object.Destroy(logo.gameObject);
            if (tipText != null) UnityEngine.Object.Destroy(tipText.gameObject);
            if (versionText != null) UnityEngine.Object.Destroy(versionText.gameObject);
            if (clickText != null) UnityEngine.Object.Destroy(clickText.gameObject);
            foreach (var t in completedSteps) if (t != null) UnityEngine.Object.Destroy(t.gameObject);
            completedSteps.Clear();
            if (currentStepText != null) UnityEngine.Object.Destroy(currentStepText.gameObject);
            logo = null; tipText = null; versionText = null; clickText = null; currentStepText = null;

            instance.sceneChanger.AllowFinishLoadingScene();
            instance.startedSceneLoad = true;
        }

        private sealed class BlobAnim
        {
            public Vector3 origin;
            public float cycle;
            public float phase;
            public float phase2;
            public float scaleBase;
            public float scaleCycle;
            public float scalePhase;
            public Color color;
            public float splitInterval;
            public float splitTimer;
            public bool splitting;
            public float splitElapsed;
            public Vector3 splitStart;
            public Vector2 splitDir;
            public float splitDist;
        }

        private static int LoadStepCount => LoadStepTexts.Length;

        private static float StepDuration(int stepIdx)
        {
            float w = stepIdx == LoadStepCount - 1 ? 1.2f : 1f;
            return (w / (8f + 1.2f)) * MinLoadTime;
        }

        private static TextMeshPro CreateText(SplashManager instance, string name, Vector3 pos, FontStyles style, float fontSizeScale, TextAlignmentOptions align)
        {
            var tmp = UnityEngine.Object.Instantiate(instance.errorPopup.InfoText, null);
            tmp.name = name;
            tmp.transform.localPosition = pos;
            tmp.fontStyle = style;
            tmp.fontSize *= fontSizeScale;
            tmp.alignment = align;
            tmp.gameObject.SetActive(true);
            return tmp;
        }

        private static Sprite CreateBlobSprite(int size)
        {
            try
            {
                var tex = new Texture2D(size, size);
                float r = size * 0.5f;
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dx = (x + 0.5f - r) / r;
                        float dy = (y + 0.5f - r) / r;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        if (d >= 1f) { tex.SetPixel(x, y, new Color(1f, 1f, 1f, 0f)); continue; }
                        float a = 1f - d;
                        a = a * a * (3f - 2f * a);
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                    }
                }
                tex.Apply();
                return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            }
            catch (Exception ex)
            {
                TheOtherRolesPlugin.Logger.LogError("[LoadPatch.CreateBlobSprite] " + ex.Message);
                return null;
            }
        }

        private static IEnumerator FadeAlpha(Component target, float from, float to, float duration)
        {
            if (target == null) yield break;
            var r = target as TextMeshPro;
            var s = target as SpriteRenderer;
            if (r != null)
            {
                r.alpha = from;
                float elapsed = 0f;
                while (elapsed < duration)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / duration);
                    r.alpha = Mathf.Lerp(from, to, t);
                    yield return null;
                }
                r.alpha = to;
            }
            else if (s != null)
            {
                var c = s.color;
                c.a = from;
                s.color = c;
                float elapsed = 0f;
                while (elapsed < duration)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / duration);
                    var cc = s.color;
                    cc.a = Mathf.Lerp(from, to, t);
                    s.color = cc;
                    yield return null;
                }
                var fc = s.color; fc.a = to; s.color = fc;
            }
        }

        private static IEnumerator MoveText(TextMeshPro tmp, Vector3 from, Vector3 to, float duration)
        {
            if (tmp == null) yield break;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float e = 0.5f - 0.5f * Mathf.Cos(Mathf.PI * t);
                tmp.transform.localPosition = Vector3.Lerp(from, to, e);
                yield return null;
            }
            tmp.transform.localPosition = to;
        }

        private static void AnimateCompletedStep(TextMeshPro done)
        {
            AddTextMove(done, done.transform.localPosition + new Vector3(0f, -FirstRowGap, 0f));
            AddTextScale(done, 0.5f);

            for (int i = 0; i < completedSteps.Count - 1; i++)
            {
                var old = completedSteps[i];
                AddTextMove(old, old.transform.localPosition + new Vector3(0f, -RowGap, 0f));
            }
        }

        private static void AddTextMove(TextMeshPro tmp, Vector3 target)
        {
            if (tmp == null) return;
            Vector3 from = tmp.transform.localPosition;
            moveTweens.Add(new Tweener
            {
                From = 0f, To = 1f, Duration = MoveDuration,
                Set = v => tmp.transform.localPosition = Vector3.Lerp(from, target, v),
                OnDone = _ => tmp.transform.localPosition = target,
            });
        }

        private static void AddTextScale(TextMeshPro tmp, float targetScale)
        {
            if (tmp == null) return;
            float from = tmp.transform.localScale.x;
            moveTweens.Add(new Tweener
            {
                From = 0f, To = 1f, Duration = MoveDuration,
                Set = v => tmp.transform.localScale = Vector3.one * Mathf.Lerp(from, targetScale, v),
                OnDone = _ => tmp.transform.localScale = Vector3.one * targetScale,
            });
        }
    }
}
