using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using TheOtherRoles.MetaContext;
using TheOtherRoles.Patches;
using TheOtherRoles.Roles;
using TheOtherRoles.Utilities;
using UnityEngine;
using UnityEngine.Rendering;
using static TheOtherRoles.TheOtherRoles;

namespace TheOtherRoles.Objects
{
    public static class HigurumaTrial
    {
        private const float VerdictHold = 3.5f;
        private const int TextOrder = 970;

        private static GameObject root;
        private static TMPro.TextMeshPro crimesText;
        private static TMPro.TextMeshPro timerText;
        private static BitmapText verdictIntro;
        private static BitmapText verdictText;
        private static MetaScreen choiceScreen;
        private static bool smashing;
        private static bool collapsing;

        private const float SmashTopY = 3.4f;
        private const float SmashHitY = -1.3f;

        public static void OpenChoice(byte defendantId)
        {
            Close();

            var meeting = MeetingHud.Instance;
            if (meeting == null) return;

            root = new GameObject("HigurumaTrial");
            root.transform.SetParent(meeting.transform, false);
            root.transform.localPosition = Vector3.zero;
            root.layer = meeting.gameObject.layer;

            var defendant = Helpers.playerById(defendantId);
            string name = defendant?.Data?.PlayerName ?? "";

            var lines = HiromiHiguruma.DescribeCrimes(defendantId);
            int score = HiromiHiguruma.GetScore(defendantId);

            string body = string.Format(ModTranslation.getString("hiromiCrimeHeader"),
                Helpers.cs(defendant != null ? defendant.Data.Color : Color.white, name));
            body += "\n";

            if (lines.Count == 0)
                body += ModTranslation.getString("hiromiCrimeNone");
            else
                body += string.Join("\n", lines.ToArray());


            crimesText = HigurumaDomain.CreateText(root.transform, body, 3.4f, Color.white, new Vector3(0f, 0.35f, -2f), TextOrder);
            if (crimesText != null) crimesText.gameObject.SetActive(true);

            timerText = HigurumaDomain.CreateText(root.transform, "", 3f, new Color(1f, 0.85f, 0.4f), new Vector3(0f, 2.2f, -2f), TextOrder);
            if (timerText != null) timerText.gameObject.SetActive(true);

            bool isDefendant = defendantId == PlayerControl.LocalPlayer.PlayerId;
            TheOtherRolesPlugin.Logger.LogInfo($"[Higuruma] choice UI: defendant={defendantId} local={PlayerControl.LocalPlayer.PlayerId} isDefendant={isDefendant}");

            if (isDefendant)
                OpenChoiceButtons(defendantId);
            else
                OpenWaitingHint();
        }

        private static void OpenChoiceButtons(byte defendantId)
        {
            var gui = TORGUIContextEngine.API;
            var attr = gui.GetAttribute(AttributeAsset.CenteredBoldFixed);

            choiceScreen = MetaScreen.GenerateWindow(new Vector2(6.8f, 1.8f), MeetingHud.Instance.transform, new Vector3(0f, -2.9f, -1f), false, false);
            RaiseWindowAboveDomain(choiceScreen);

            choiceScreen.SetContext(gui.HorizontalHolder(GUIAlignment.Center,
                gui.LocalizedButton(GUIAlignment.Center, attr, "hiromiChoiceConfess", () => Choose(defendantId, HigurumaChoice.Confess)),
                gui.HorizontalMargin(0.25f),
                gui.LocalizedButton(GUIAlignment.Center, attr, "hiromiChoiceDeny", () => Choose(defendantId, HigurumaChoice.Deny)),
                gui.HorizontalMargin(0.25f),
                gui.LocalizedButton(GUIAlignment.Center, attr, "hiromiChoiceSilence", () => Choose(defendantId, HigurumaChoice.Silence))), out _);
        }

        private static void OpenWaitingHint()
        {
            var hint = HigurumaDomain.CreateText(root.transform, ModTranslation.getString("hiromiWaitingChoice"), 3f, new Color(0.75f, 0.75f, 0.75f), new Vector3(0f, -1.6f, -2f), TextOrder);
            if (hint != null) hint.gameObject.SetActive(true);
        }

        private static void RaiseWindowAboveDomain(MetaScreen screen)
        {
            if (screen == null) return;

            var root = screen.transform.parent != null ? screen.transform.parent.gameObject : null;
            if (root == null) return;

            var group = root.GetComponent<SortingGroup>();
            if (group == null) group = root.AddComponent<SortingGroup>();
            group.sortingOrder = 1000;

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = MeetingHud.Instance.gameObject.layer;

            TheOtherRolesPlugin.Logger.LogInfo($"[Higuruma] choice window raised: layer={root.layer} order={group.sortingOrder}");
        }

        private static void Choose(byte defendantId, HigurumaChoice choice)
        {
            if (!HiromiHiguruma.awaitingChoice) return;
            HiromiHiguruma.SubmitChoice.Invoke((defendantId, (byte)choice));
        }

        public static void CloseChoiceUI()
        {
            if (choiceScreen != null) choiceScreen.CloseScreen();
            choiceScreen = null;
        }

        public static void UpdateTimer(float remaining)
        {
            if (timerText == null) return;

            timerText.text = string.Format(ModTranslation.getString("hiromiTrialTimeLeft"), Mathf.Max(0, Mathf.CeilToInt(remaining)));
        }

        public static void ShowVerdict(PlayerControl defendant, HigurumaChoice choice, HigurumaVerdict verdict, List<string> crimes, int score)
        {
            CloseChoiceUI();

            if (crimesText != null) crimesText.gameObject.SetActive(false);
            if (timerText != null) timerText.gameObject.SetActive(false);

            if (root == null)
            {
                var meeting = MeetingHud.Instance;
                if (meeting == null) return;

                root = new GameObject("HigurumaTrial");
                root.transform.SetParent(meeting.transform, false);
                root.transform.localPosition = Vector3.zero;
                root.layer = meeting.gameObject.layer;
            }

            verdictIntro = HigurumaDomain.CreateBitmapText(root.transform, "最終判決は", HigurumaDomain.GothicFont(), 0.95f, Color.white, new Vector3(0f, 1.3f, -2f), TextOrder);

            string japanese = verdict switch
            {
                HigurumaVerdict.Death => "有罪、死刑",
                HigurumaVerdict.Forfeit => "有罪、没収",
                _ => "無罪"
            };

            bool guilty = verdict != HigurumaVerdict.Innocent;
            var font = guilty ? HigurumaDomain.MinchoFont() : HigurumaDomain.GothicFont();
            Color verdictColor = guilty ? new Color(0.86f, 0.06f, 0.06f) : Color.white;

            verdictText = HigurumaDomain.CreateBitmapText(root.transform, japanese, font, guilty ? 1.9f : 1.7f, verdictColor, new Vector3(0f, -0.2f, -2f), TextOrder + 1);

            if (verdict != HigurumaVerdict.Innocent) SmashScreen();
        }

        private static void SmashScreen()
        {
            if (smashing) return;

            var meeting = MeetingHud.Instance;
            var hud = FastDestroyableSingleton<HudManager>.Instance;
            if (meeting == null || hud == null) return;

            smashing = true;
            logSoundLibrary();
            hud.StartCoroutine(CoSmash(meeting).WrapToIl2Cpp());
        }

        private static void logSoundLibrary()
        {
            try
            {
                var names = Modules.AssetLoader.AudioClips.Keys.ToArray();
                TheOtherRolesPlugin.Logger.LogInfo($"[Higuruma] SE library ({names.Length}): {string.Join(",", names).Replace("assets/audio/", "")}");
            }
            catch (Exception ex)
            {
                TheOtherRolesPlugin.Logger.LogWarning("[Higuruma] SE list: " + ex.Message);
            }
        }

        private static IEnumerator CoSmash(MeetingHud meeting)
        {
            var gavel = spawnGavel(meeting);

            for (int i = 0; i < 3; i++)
            {
                yield return CoGavelMove(gavel, SmashTopY, SmashHitY, 0.13f);
                Impact(meeting, i);
                yield return Effects.Wait(0.07f);
                yield return CoGavelMove(gavel, SmashHitY, SmashTopY, 0.22f);
                yield return Effects.Wait(0.06f);
            }

            if (gavel != null) UnityEngine.Object.Destroy(gavel);

            SettleVerdictScale();
        }

        private static GameObject spawnGavel(MeetingHud meeting)
        {
            if (meeting.judgeGavelPrefab == null)
            {
                TheOtherRolesPlugin.Logger.LogWarning("[Higuruma] judgeGavelPrefab is null");
                return null;
            }

            var gavel = UnityEngine.Object.Instantiate(meeting.judgeGavelPrefab, meeting.transform);
            gavel.name = "HigurumaGavel";
            gavel.transform.localPosition = new Vector3(0f, SmashTopY, -1f);
            gavel.transform.localScale = Vector3.one * 1.4f;

            foreach (var t in gavel.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = meeting.gameObject.layer;

            foreach (var renderer in gavel.GetComponentsInChildren<SpriteRenderer>(true))
                renderer.sortingOrder = TextOrder + 5;

            gavel.SetActive(true);

            TheOtherRolesPlugin.Logger.LogInfo($"[Higuruma] gavel spawned scale={(gavel.transform.localScale.ToString())} renderers={gavel.GetComponentsInChildren<SpriteRenderer>(true).Length}");
            return gavel;
        }

        private static IEnumerator CoGavelMove(GameObject gavel, float fromY, float toY, float duration)
        {
            if (gavel == null) yield break;

            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                if (gavel == null) yield break;

                float y = Mathf.Lerp(fromY, toY, Mathf.Clamp01(t / duration));
                gavel.transform.localPosition = new Vector3(0f, y, -1f);
                yield return null;
            }

            if (gavel != null) gavel.transform.localPosition = new Vector3(0f, toY, -1f);
        }

        private static void Impact(MeetingHud meeting, int index)
        {
            bool first = index == 0;

            HigurumaDomain.PlaySmashSound();

            try
            {
                var camera = Camera.main;
                if (camera != null)
                {
                    var follower = camera.GetComponent<FollowerCamera>();
                    if (follower != null) follower.ShakeScreen(0.32f, first ? 0.75f : 0.5f);
                }

                var hud = FastDestroyableSingleton<HudManager>.Instance;
                if (hud != null)
                    hud.StartCoroutine(CoImpactFlash(index).WrapToIl2Cpp());

                HigurumaDomain.SpawnShockwave(new Vector3(0f, -1.3f, -0.6f), 5.5f, new Color(1f, 0.18f, 0.12f), 0.42f);

                if (verdictText?.GameObject != null)
                {
                    var t = verdictText.GameObject.transform;
                    t.localScale = Vector3.one * 1.32f;
                }
                if (verdictIntro?.GameObject != null)
                {
                    var t = verdictIntro.GameObject.transform;
                    t.localScale = Vector3.one * 1.22f;
                }

                if (first && root != null)
                    HigurumaDomain.SpawnVerdictSweep(root.transform, -0.2f, 9.5f, TextOrder + 3);

                if (first && meeting != null && meeting.Glass != null && meeting.CrackedGlass != null)
                {
                    meeting.Glass.sprite = meeting.CrackedGlass;
                    meeting.Glass.color = new Color(1f, 1f, 1f, 0.65f);
                    meeting.Glass.gameObject.SetActive(true);

                    var renderer = meeting.Glass.GetComponent<SpriteRenderer>();
                    if (renderer != null) renderer.sortingOrder = TextOrder + 2;
                }
            }
            catch (Exception ex)
            {
                TheOtherRolesPlugin.Logger.LogWarning("[Higuruma] impact: " + ex.Message);
            }
        }

        private static IEnumerator CoImpactFlash(int index)
        {
            var hud = FastDestroyableSingleton<HudManager>.Instance;
            if (hud == null || hud.FullScreen == null) yield break;

            var flash = UnityEngine.Object.Instantiate(hud.FullScreen, hud.transform);
            flash.enabled = true;
            flash.gameObject.SetActive(true);

            var color = index == 0 ? new Color(1f, 0.85f, 0.85f) : new Color(1f, 0.15f, 0.1f);
            float peak = index == 0 ? 0.75f : 0.4f;
            float duration = index == 0 ? 0.22f : 0.16f;

            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                if (flash == null) yield break;

                float p = t / duration;
                flash.color = new Color(color.r, color.g, color.b, peak * (1f - p) * (1f - p));
                yield return null;
            }

            if (flash != null) UnityEngine.Object.Destroy(flash.gameObject);
        }

        private static void SettleVerdictScale()
        {
            if (verdictText?.GameObject != null) verdictText.GameObject.transform.localScale = Vector3.one;
            if (verdictIntro?.GameObject != null) verdictIntro.GameObject.transform.localScale = Vector3.one;
        }

        public static void UpdateVerdict(float elapsed)
        {
            if (!HiromiHiguruma.showingVerdict) return;
            if (collapsing || elapsed < VerdictHold) return;

            collapsing = true;
            HigurumaDomain.Collapse(() => HiromiHiguruma.FinishTrial());
        }

        public static void Announce(string message)
        {
            var hud = FastDestroyableSingleton<HudManager>.Instance;
            if (hud == null || hud.Chat == null) return;
            if (PlayerControl.LocalPlayer == null) return;

            hud.Chat.AddChat(PlayerControl.LocalPlayer, message, false);
        }

        public static void Close()
        {
            CloseChoiceUI();

            if (root != null) UnityEngine.Object.Destroy(root);
            root = null;
            crimesText = null;
            timerText = null;
            verdictIntro = null;
            verdictText = null;
            smashing = false;
            collapsing = false;
        }
    }
}
