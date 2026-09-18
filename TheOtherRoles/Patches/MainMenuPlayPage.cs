using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using TMPro;
using TheOtherRoles.MetaContext;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TheOtherRoles.Patches
{
    public static class MainMenuPlayPage
    {
        public const bool Enabled = true;

        public const string PageName = "TORVPlayPage";
        public const string OnlinePageName = "TORVOnlinePage";

        private static readonly string[] OnlineButtons =
        {
            "Create Lobby Button",
            "Enter Code Button",
            "Find Game Button",
        };

        private static readonly string[][] Rows =
        {
            new[] { "PlayLocalButton", "PlayOnlineButton" },
            new[] { "HowToPlayButton", "FreePlayButton" },
        };

        private static readonly string[] ModeButtons =
        {
            "PlayLocalButton",
            "PlayOnlineButton",
            "HowToPlayButton",
            "FreePlayButton",
        };

        private static readonly string[] HiddenMainMenu =
        {
            "ReBuildUi",
            "TORMenuLogo",
        };

        private static readonly string[] Furniture =
        {
            "MaskedBlackScreen",
            "WindowShine",
            "CreditsSizer",
            "CreditsScreen",
        };

        private static readonly string[] PanelRoots =
        {
            PageName,
            OnlinePageName,
            "enterCodeContainer",
            "enterCodeHeader",
            "enterCodeButtons",
        };

        private const float GapX = 0.35f;
        private const float GapY = 0.3f;
        private const float PageZ = 4f;
        private const float FrontZ = -2f;

        private static readonly Vector2 FineTune = new Vector2(0f, 0f);
        private static readonly Vector2 PlayOffset = new Vector2(0f, -0.3f);

        private static readonly Vector2 ExitAnchor = new Vector2(0.08f, 0.79f);

        private static MainMenuManager _menu;
        private static bool _enterCodeOpen;
        private static int _enterCodeFrame = -9999;
        private const int EnterCodeGrace = 5;
        private static GameObject _page;
        private static GameObject _onlinePage;
        private static GameObject _exit;
        private static GameObject _onlineExit;
        private static GameObject _panelFor;
        private static readonly List<SpriteRenderer> _panelRenderers = new List<SpriteRenderer>();
        private static readonly List<TextMeshPro> _panelTexts = new List<TextMeshPro>();

        public static void Open(MainMenuManager instance, bool reset = true)
        {
            try
            {
                if (!instance) return;

                _menu = instance;
                if (reset) _menu.ResetScreen();
                Build();

                _page.SetActive(true);

                EnsureSpawned();
                Layout();
            }
            catch (Exception ex)
            {
                Warn("打开失败: " + ex.Message);
            }
        }

        public static void OpenOnline(MainMenuManager instance, bool reset = true)
        {
            try
            {
                if (!instance) return;

                _menu = instance;
                if (reset) _menu.ResetScreen();
                BuildOnline();

                _onlinePage.SetActive(true);

                LayoutOnline();
            }
            catch (Exception ex)
            {
                Warn("打开在线页失败: " + ex.Message);
            }
        }

        public static void Close()
        {
            if (_page) _page.SetActive(false);
            if (_onlinePage) _onlinePage.SetActive(false);

            if (Time.frameCount - _enterCodeFrame <= EnterCodeGrace) return;

            _enterCodeOpen = false;
        }

        public static void PrepareEnterCode(MainMenuManager instance)
        {
            try
            {
                if (!instance) return;

                _menu = instance;
                _enterCodeFrame = Time.frameCount;
                _enterCodeOpen = true;

                Close();
                ShowEnterCode();
            }
            catch (Exception ex)
            {
                Warn("输入代码页准备失败: " + ex.Message);
            }
        }

        public static void ReportEnterCode(MainMenuManager instance, string phase)
        {
            try
            {
                if (!instance) return;

                _menu = instance;

                if (phase == "post") ShowEnterCode();
            }
            catch (Exception ex)
            {
                Warn("输入代码页回报失败: " + ex.Message);
            }
        }

        private static bool EnterCodeOpen => _menu && _enterCodeOpen;

        private static Transform PlayRoot =>
            _menu && _menu.gameModeButtons ? _menu.gameModeButtons.transform : (_page ? _page.transform : null);

        private static Transform OnlineRoot =>
            _menu && _menu.onlineButtonsContainer ? _menu.onlineButtonsContainer : (_onlinePage ? _onlinePage.transform : null);

        private static void ShowEnterCode()
        {
            if (!_menu) return;

            RestoreVisuals(_menu.enterCodeContainer ? _menu.enterCodeContainer.gameObject : null);
            RestoreVisuals(_menu.enterCodeHeader);
            RestoreVisuals(_menu.enterCodeButtons);

            if (EnterCodeOpen) ShowPanel();

            HideStaleCodeInfo();
        }

        private static void HideStaleCodeInfo()
        {
            if (!_menu || !_menu.entercodeField) return;

            var typed = FindDeep(_menu.entercodeField.transform, "Text_TMP");
            var text = typed ? typed.GetComponent<TextMeshPro>() : null;
            if (text && !string.IsNullOrEmpty(text.text)) return;

            foreach (var root in new[] { _menu.enterCodeContainer, _menu.enterCodeButtons ? _menu.enterCodeButtons.transform : null })
            {
                if (!root) continue;

                var fields = FindDeep(root, "FieldsContainer");
                if (!fields) continue;

                for (int i = 0; i < fields.childCount; i++)
                {
                    var row = fields.GetChild(i);
                    if (!row) continue;

                    for (int j = 0; j < row.childCount; j++)
                    {
                        var value = row.GetChild(j);
                        if (!value) continue;
                        if (value.name != "Text_TMP" && value.name != "Container") continue;

                        if (value.gameObject.activeSelf) value.gameObject.SetActive(false);
                    }
                }
            }
        }

        private static int RestoreVisuals(GameObject root)
        {
            if (!root) return 0;

            int fixedCount = 0;

            foreach (var renderer in root.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (!renderer) continue;
                if (!renderer.enabled)
                {
                    renderer.enabled = true;
                    if (!renderer.enabled) fixedCount++;
                }
                if (renderer.maskInteraction != SpriteMaskInteraction.None)
                    renderer.maskInteraction = SpriteMaskInteraction.None;
            }

            foreach (var text in root.GetComponentsInChildren<TextMeshPro>(true))
            {
                if (text && !text.gameObject.activeSelf) text.gameObject.SetActive(true);
            }

            foreach (var mask in root.GetComponentsInChildren<SpriteMask>(true))
                if (mask && !mask.enabled) mask.enabled = true;

            foreach (var mask in root.GetComponentsInChildren<Mask>(true))
                if (mask && !mask.enabled) mask.enabled = true;

            return fixedCount;
        }





        private static void Build()
        {
            if (_page) return;

            _page = Helpers.CreateObject(PageName, _menu.mainMenuUI.transform, new Vector3(0f, 0f, PageZ));

            MoveButtons();
            SetupHover(_page, ModeButtons);
            AddExit();
            HideChrome();

            _panelFor = null;
        }

        private static void MoveButtons()
        {
            foreach (var name in ModeButtons)
            {
                var node = FindDeep(_menu.gameModeButtons ? _menu.gameModeButtons.transform : null, name);
                if (!node) continue;

                if (!node.gameObject.activeSelf) node.gameObject.SetActive(true);
            }
        }

        private static void AddExit()
        {
            _exit = MakeExit(_page.transform, "PlayPageExit");
        }

        private static GameObject MakeExit(Transform parent, string name)
        {
            if (!_menu.backButtonOnline) return null;

            var source = _menu.backButtonOnline.transform;
            Sprite arrow = null;
            Sprite plate = null;
            Sprite highlight = null;
            TextMeshPro label = null;
            TextMeshPro font = null;

            foreach (var renderer in source.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (!renderer) continue;
                if (renderer.name == "BackArrow") arrow = renderer.sprite;
                else if (renderer.name == "Inactive") plate = renderer.sprite;
                else if (renderer.name == "Highlight") highlight = renderer.sprite;
            }
            label = source.GetComponentInChildren<TextMeshPro>(true);

            if (_menu.playButton)
                font = _menu.playButton.buttonText
                    ? _menu.playButton.buttonText
                    : _menu.playButton.GetComponentInChildren<TextMeshPro>(true);
            if (!font) font = label;

            var go = Helpers.CreateObject(name, parent, Vector3.zero);
            PlaceExit(go);

            var size = new Vector2(1.5f, 0.55f);
            var normal = AddSprite(go.transform, "Inactive", plate, Vector2.zero, size);
            var hover = AddSprite(go.transform, "Highlight", highlight, Vector2.zero, size);
            if (hover) hover.SetActive(false);

            AddSprite(go.transform, "Arrow", arrow, new Vector2(-0.45f, 0f), new Vector2(0.3f, 0.3f));

            if (label) AddLabel(go.transform, label, font, new Vector2(0.2f, 0f), new Vector2(0.9f, 0.35f));

            var collider = go.AddComponent<BoxCollider2D>();
            collider.isTrigger = true;
            collider.size = size;

            var button = go.AddComponent<PassiveButton>();
            button.OnClick = new Button.ButtonClickedEvent();
            button.OnMouseOver = new UnityEngine.Events.UnityEvent();
            button.OnMouseOut = new UnityEngine.Events.UnityEvent();
            button.inactiveSprites = normal;
            button.activeSprites = hover;
            button.ClickSound = _menu.backButtonOnline ? _menu.backButtonOnline.ClickSound : null;
            button.HoverSound = _menu.backButtonOnline ? _menu.backButtonOnline.HoverSound : null;
            button.OnClick.AddListener((UnityEngine.Events.UnityAction)(() =>
            {
                try { _menu.ResetScreen(); }
                catch (Exception ex) { Warn("返回失败: " + ex.Message); }
            }));

            var sourceHighlight = FindDeep(source, "Highlight");
            if (sourceHighlight)
            {
                var copy = Object.Instantiate(sourceHighlight.gameObject, go.transform);
                copy.name = "Highlight";
                copy.transform.localPosition = Vector3.zero;

                var bounds = Collect(sourceHighlight.gameObject);
                if (bounds.HasValue && bounds.Value.size.x > 0.0001f)
                    copy.transform.localScale *= size.x / bounds.Value.size.x;

                foreach (var renderer in copy.GetComponentsInChildren<SpriteRenderer>(true))
                    if (renderer) renderer.maskInteraction = SpriteMaskInteraction.None;

                button.activeSprites = copy;
                if (hover) hover.SetActive(false);
            }

            return go;
        }

        private static GameObject AddSprite(Transform parent, string name, Sprite sprite, Vector2 offset, Vector2 size)
        {
            if (!sprite) return null;

            var renderer = Helpers.CreateObject<SpriteRenderer>(name, parent, offset);
            renderer.sprite = sprite;
            renderer.drawMode = SpriteDrawMode.Sliced;
            renderer.size = size;
            renderer.maskInteraction = SpriteMaskInteraction.None;
            renderer.transform.localScale = Vector3.one;

            return renderer.gameObject;
        }

        private static void AddLabel(Transform parent, TextMeshPro source, TextMeshPro font, Vector2 offset, Vector2 size)
        {
            var go = new GameObject("Label");
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = offset;

            var text = go.AddComponent<TextMeshPro>();
            text.font = font.font;
            if (font.fontSharedMaterial) text.fontSharedMaterial = font.fontSharedMaterial;

            var translator = source.GetComponent<TextTranslatorTMP>();
            var controller = TranslationController.Instance;

            text.text = translator && controller != null
                ? controller.GetString(translator.TargetText)
                : source.text;

            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = false;
            text.color = Color.white;
            text.raycastTarget = false;

            var rect = text.rectTransform;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.localScale = Vector3.one;

            text.enableAutoSizing = true;
            text.fontSizeMin = 0.2f;
            text.fontSizeMax = 3f;
            text.ForceMeshUpdate();
        }

        private static void BuildOnline()
        {
            if (_onlinePage) return;

            _onlinePage = Helpers.CreateObject(OnlinePageName, _menu.mainMenuUI.transform, new Vector3(0f, 0f, PageZ));

            foreach (var name in OnlineButtons)
            {
                var node = FindDeep(OnlineRoot, name);
                if (!node) continue;

                if (!node.gameObject.activeSelf) node.gameObject.SetActive(true);
            }

            SetupHover(_onlinePage, OnlineButtons);
            _onlineExit = MakeExit(_onlinePage.transform, "OnlinePageExit");

            _panelFor = null;
        }

        private static void LayoutOnline()
        {
            if (_onlinePage) return;

            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            var found = false;

            foreach (var name in OnlineButtons)
            {
                var node = FindDeep(OnlineRoot, name);
                if (!node) continue;

                found = true;
                var pivot = node.position;
                min = new Vector2(Mathf.Min(min.x, pivot.x), Mathf.Min(min.y, pivot.y));
                max = new Vector2(Mathf.Max(max.x, pivot.x), Mathf.Max(max.y, pivot.y));
            }

            if (!found) return;

            var center = (min + max) * 0.5f;
            var delta = new Vector3(-center.x + FineTune.x, -center.y + FineTune.y, 0f);
            if (delta.sqrMagnitude < 0.0001f) return;

            foreach (var name in OnlineButtons)
            {
                var node = FindDeep(OnlineRoot, name);
                if (!node) continue;

                var position = node.position + delta;
                node.position = new Vector3(position.x, position.y, FrontZ);
            }
        }

        private static void HideChrome()
        {
            if (!_menu.gameModeButtons) return;

            foreach (var name in new[] { "Header", "Divider" })
            {
                var node = FindDeep(_menu.gameModeButtons.transform, name);
                if (node) node.gameObject.SetActive(false);
            }
        }

        public static void SyncMainMenu(MainMenuManager instance)
        {
            if (!instance || !instance.mainMenuUI) return;

            _menu = instance;
            if (!_menu.gameModeButtons || !_menu.mainMenuUI) return;

            bool online = _onlinePage && _onlinePage.activeSelf;
            bool subPage = (_page && _page.activeSelf) || online ||
                           (_menu.screenTint && _menu.screenTint.enabled) || EnterCodeOpen;

            if (_menu.onlineHeader)
            {
                var header = FindDeep(_menu.onlineHeader.transform, "Header");
                if (header && header.gameObject.activeSelf == online) header.gameObject.SetActive(!online);
            }

            foreach (var name in HiddenMainMenu)
            {
                var node = FindDeep(_menu.mainMenuUI.transform, name);
                if (node && node.gameObject.activeSelf == subPage) node.gameObject.SetActive(!subPage);
            }

            var backing = FindDeep(_menu.mainMenuUI.transform, "MaskedBlackScreen");
            if (backing)
            {
                var backingRenderer = backing.GetComponent<SpriteRenderer>();
                if (backingRenderer && backingRenderer.enabled) backingRenderer.enabled = false;
            }

            var tint = FindDeep(_menu.mainMenuUI.transform, "Tint");
            if (tint)
            {
                var renderer = tint.GetComponent<SpriteRenderer>();
                if (renderer && renderer.enabled) renderer.enabled = false;
            }

            PlaceExit(_exit);
            PlaceExit(_onlineExit);

            if (EnterCodeOpen) ShowEnterCode();

            if (subPage)
            {
                SetFurniture();

                if (_menu.screenTint && _menu.screenTint.enabled)
                {
                    Activate(_menu.enterCodeContainer ? _menu.enterCodeContainer.gameObject : null);
                    Activate(_menu.enterCodeHeader);
                    Activate(_menu.enterCodeButtons);
                }

                EnableButtons(_page, ModeButtons);
                EnableButtons(_onlinePage, OnlineButtons);
                ShowPanel();
            }

            SetFurniture();

            if (online) LayoutOnline();
        }

        private static void Layout()
        {
            var view = ViewSize();
            var report = new StringBuilder("[MainMenuPlayPage] ");

            var first = Measure(Rows[0]);
            var second = Measure(Rows[1]);

            float h1 = first.HasValue ? first.Value.y : 0f;
            float h2 = second.HasValue ? second.Value.y : 0f;
            float top = (h1 + GapY + h2) * 0.5f;

            PlaceRow(Rows[0], h1, top - h1 * 0.5f, report);
            top -= h1 + GapY;
            PlaceRow(Rows[1], h2, top - h2 * 0.5f, report);

            PlaceExit(view, report);

        }

        private static void PlaceRow(string[] row, float rowHeight, float y, StringBuilder report)
        {
            if (rowHeight <= 0f) return;

            float width = 0f;
            for (int i = 0; i < row.Length; i++) width += Measure(new[] { row[i] })?.x ?? 0f;
            width += GapX * (row.Length - 1);

            float x = -width * 0.5f;
            foreach (var name in row)
            {
                var button = FindDeep(PlayRoot, name);
                var buttonSize = Measure(new[] { name });
                if (!button || !buttonSize.HasValue) continue;

                Place(button.gameObject, new Vector3(
                    x + buttonSize.Value.x * 0.5f + FineTune.x + PlayOffset.x,
                    y + FineTune.y + PlayOffset.y,
                    0f));

                report.Append(name).Append('=').Append(buttonSize.Value.x.ToString("F2"))
                      .Append('x').Append(buttonSize.Value.y.ToString("F2"))
                      .Append('@').Append(button.position.x.ToString("F2")).Append(',').Append(button.position.y.ToString("F2"))
                      .Append("  ");

                x += buttonSize.Value.x + GapX;
            }
        }

        private static void PlaceExit(Vector2 view, StringBuilder report)
        {
            if (!_exit) return;

            var size = Measure(new[] { _exit.name }) ?? Vector2.zero;
            report.Append("exit=").Append(size.x.ToString("F2")).Append('x').Append(size.y.ToString("F2"))
                  .Append('@').Append(_exit.transform.position.x.ToString("F2")).Append(',')
                  .Append(_exit.transform.position.y.ToString("F2"));
        }

        private static void Place(GameObject target, Vector3 offset)
        {
            var current = BoundsCenter(target);
            if (!current.HasValue) return;

            target.transform.position += new Vector3(
                offset.x - current.Value.x,
                offset.y - current.Value.y,
                0f);

            var position = target.transform.position;
            target.transform.position = new Vector3(position.x, position.y, FrontZ);
        }

        private static Vector3? BoundsCenter(GameObject target)
        {
            var bounds = Collect(target);
            return bounds.HasValue ? bounds.Value.center : (Vector3?)null;
        }

        private static Vector2? Measure(string[] names)
        {
            Vector2? result = null;

            foreach (var name in names)
            {
                var node = PlayRoot ? FindDeep(PlayRoot, name) : null;
                if (node) result = Merge(result, Measure(node.gameObject));
            }

            return result;
        }

        private static Vector2? Measure(GameObject target)
        {
            var bounds = Collect(target);
            if (!bounds.HasValue) return null;

            return new Vector2(bounds.Value.size.x, bounds.Value.size.y);
        }

        private static Bounds? Collect(GameObject target)
        {
            Bounds? result = null;

            foreach (var renderer in target.GetComponentsInChildren<SpriteRenderer>(false))
            {
                if (!renderer || !renderer.sprite) continue;

                if (result.HasValue) result.Value.Encapsulate(renderer.bounds);
                else result = renderer.bounds;
            }

            return result;
        }

        private static Vector2? Merge(Vector2? a, Vector2? b)
        {
            if (!a.HasValue) return b;
            if (!b.HasValue) return a;

            return new Vector2(Mathf.Max(a.Value.x, b.Value.x), Mathf.Max(a.Value.y, b.Value.y));
        }

        private static Vector2 ViewSize()
        {
            var cam = Helpers.FindCamera(_page ? _page.layer : 0);
            if (!cam) cam = Camera.main;
            if (!cam) return new Vector2(10.67f, 6f);

            float height = 2f * cam.orthographicSize;
            return new Vector2(height * cam.aspect, height);
        }

        private static void EnsureSpawned()
        {
            if (!_page || !_page.activeInHierarchy) return;

            if (_exit && !_exit.activeSelf) _exit.SetActive(true);

            foreach (var name in ModeButtons)
            {
                var node = FindDeep(PlayRoot, name);
                if (node && !node.gameObject.activeSelf) node.gameObject.SetActive(true);
            }
        }

        private static void PlaceExit(GameObject exit)
        {
            if (!exit) return;

            var view = ViewSize();
            exit.transform.localPosition = new Vector3(
                (ExitAnchor.x - 0.5f) * view.x,
                (ExitAnchor.y - 0.5f) * view.y,
                -1f);
        }

        public static void DumpReport()
        {
            var b = new StringBuilder("[MainMenuPlayPage] ===== 按钮诊断 =====");

            ReportPage(b, _page, ModeButtons);
            ReportPage(b, _onlinePage, OnlineButtons);
            ReportChain(b, "enterCodeContainer", _menu ? _menu.enterCodeContainer : null);
            ReportChain(b, "enterCodeHeader", _menu && _menu.enterCodeHeader ? _menu.enterCodeHeader.transform : null);
            ReportChain(b, "page", _page ? _page.transform : null);
            ReportChain(b, "onlinePage", _onlinePage ? _onlinePage.transform : null);

            Log(b.ToString());
        }

        private static void ReportPage(StringBuilder b, GameObject page, string[] names)
        {
            if (!page)
            {
                b.Append("\n-- page 不存在");
                return;
            }

            b.Append("\n-- ").Append(page.name).Append(" active=").Append(page.activeInHierarchy);

            foreach (var name in names)
            {
                var node = FindDeep(page.transform, name);
                if (!node)
                {
                    b.Append("\n   [缺] ").Append(name);
                    continue;
                }

                var button = node.GetComponent<PassiveButton>();
                b.Append("\n   ").Append(name)
                 .Append(" active=").Append(node.gameObject.activeInHierarchy)
                 .Append(" btn=").Append(button ? "y" : "n");

                if (!button) continue;

                b.Append(" hover=").Append(ObjectName(button.activeSprites))
                 .Append(" idle=").Append(ObjectName(button.inactiveSprites))
                 .Append(" click=").Append(ObjectName(button.onClickSprites));
            }
        }

        private static string ObjectName(GameObject go)
        {
            if (!go) return "null";
            return go.name + (go.activeSelf ? "+" : "-");
        }

        private static void ReportChain(StringBuilder b, string label, Transform node)
        {
            b.Append("\n-- ").Append(label);

            if (node == null)
            {
                b.Append(" = null");
                return;
            }

            for (var t = node; t; t = t.parent)
                b.Append("\n     ").Append(t.name).Append(" active=").Append(t.gameObject.activeSelf);
        }

        private static readonly List<Transform> _playFlowRoots = new List<Transform>();

        private static void MarkPlayFlowRoots()
        {
            _playFlowRoots.Clear();
            if (!_menu) return;

            AddRoot(_menu.enterCodeContainer);
            AddRoot(_menu.enterCodeHeader);
            AddRoot(_menu.enterCodeButtons);
            AddRoot(_menu.onlineButtonsContainer);
            AddRoot(_menu.onlineHeader);
            AddRoot(_menu.onlineButtons);
            AddRoot(_menu.gameModeButtons);
            AddRoot(_menu.accountButtons);
            AddRoot(_menu.createGameScreen);
            if (_page) _playFlowRoots.Add(_page.transform);
            if (_onlinePage) _playFlowRoots.Add(_onlinePage.transform);
        }

        private static void AddRoot(Component root)
        {
            if (root) _playFlowRoots.Add(root.transform);
        }

        private static void AddRoot(GameObject root)
        {
            if (root) _playFlowRoots.Add(root.transform);
        }

        private static bool InPlayFlow(Transform node)
        {
            for (int i = 0; i < _playFlowRoots.Count; i++)
            {
                var root = _playFlowRoots[i];
                if (!root) continue;
                if (node == root || node.IsChildOf(root)) return true;
            }
            return false;
        }

        private static void SetFurniture()
        {
            if (!_menu || !_menu.mainMenuUI) return;

            MarkPlayFlowRoots();

            foreach (var name in Furniture)
            {
                var node = FindDeep(_menu.mainMenuUI.transform, name);
                if (!node) continue;

                foreach (var renderer in node.GetComponentsInChildren<SpriteRenderer>(true))
                    if (renderer && renderer.enabled && !InPlayFlow(renderer.transform))
                        renderer.enabled = false;

                foreach (var mask in node.GetComponentsInChildren<SpriteMask>(true))
                    if (mask && mask.enabled && !InPlayFlow(mask.transform))
                        mask.enabled = false;

                foreach (var mask in node.GetComponentsInChildren<Mask>(true))
                    if (mask && mask.enabled && !InPlayFlow(mask.transform))
                        mask.enabled = false;
            }
        }

        private static void Activate(GameObject root)
        {
            if (!root) return;

            if (!root.activeSelf) root.SetActive(true);

            foreach (var text in root.GetComponentsInChildren<TextMeshPro>(true))
                if (text && !text.gameObject.activeSelf) text.gameObject.SetActive(true);

            foreach (var button in root.GetComponentsInChildren<PassiveButton>(true))
                if (button && !button.gameObject.activeSelf) button.gameObject.SetActive(true);
        }

        private static void SetupHover(GameObject page, string[] names)
        {
            if (!page) return;

            foreach (var name in names)
            {
                var node = FindDeep(page.transform, name);
                if (!node) continue;

                var button = node.GetComponent<PassiveButton>();
                if (!button) continue;

                var inactive = FindDeep(node, "Inactive");
                var highlight = FindDeep(node, "Highlight");

                if (inactive) button.inactiveSprites = inactive.gameObject;
                if (!highlight) continue;

                button.activeSprites = highlight.gameObject;
                if (highlight.gameObject.activeSelf) highlight.gameObject.SetActive(false);
            }
        }

        private static void EnableButtons(GameObject page, string[] names)
        {
            if (!page) return;

            foreach (var name in names)
            {
                var node = FindDeep(page.transform, name);
                if (!node) continue;

                var button = node.GetComponent<PassiveButton>();
                if (button) button.SetButtonEnableState(true);
            }
        }

        private static void CollectPanel()
        {
            if (_panelFor == _menu.mainMenuUI) return;

            _panelFor = _menu.mainMenuUI;
            _panelRenderers.Clear();
            _panelTexts.Clear();

            foreach (var name in PanelRoots)
            {
                var root = FindDeep(_menu.mainMenuUI.transform, name);
                if (!root) continue;

                _panelRenderers.AddRange(root.GetComponentsInChildren<SpriteRenderer>(true));
                _panelTexts.AddRange(root.GetComponentsInChildren<TextMeshPro>(true));
            }
        }

        private static void ShowPanel()
        {
            if (!_menu || !_menu.mainMenuUI) return;

            CollectPanel();

            foreach (var renderer in _panelRenderers)
            {
                if (!renderer) continue;
                if (renderer.name == "AspectSizeBounds") continue;

                if (!renderer.enabled) renderer.enabled = true;
                if (renderer.maskInteraction != SpriteMaskInteraction.None)
                    renderer.maskInteraction = SpriteMaskInteraction.None;
            }

            foreach (var text in _panelTexts)
            {
                if (!text || text.gameObject.activeSelf) continue;

                text.gameObject.SetActive(true);
            }
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;

            for (int i = 0; i < root.childCount; i++)
            {
                var found = FindDeep(root.GetChild(i), name);
                if (found) return found;
            }
            return null;
        }

        private static void Kill(GameObject target)
        {
            if (target && target.activeSelf) target.SetActive(false);
        }

        private static void Kill(Component target)
        {
            if (target) Kill(target.gameObject);
        }

        private static void Log(string message)
        {
            try { TheOtherRolesPlugin.Logger.LogInfo(message); }
            catch { }
        }

        private static void Warn(string message)
        {
            try { TheOtherRolesPlugin.Logger.LogWarning("[MainMenuPlayPage] " + message); }
            catch { }
        }
    }

    [HarmonyPatch(typeof(KeyboardJoystick), nameof(KeyboardJoystick.Update))]
    public static class MainMenuPlayPageDumpHotkeyPatch
    {
        public static void Postfix()
        {
            try
            {
                if (Input.GetKeyDown(KeyCode.F10)) MainMenuPlayPage.DumpReport();
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.OpenGameModeMenu))]
    public static class MainMenuPlayPageEntryPatch
    {
        public static void Postfix(MainMenuManager __instance)
        {
            
            MainMenuPlayPage.Open(__instance, false);
        }
    }

    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.LateUpdate))]
    public static class MainMenuPlayPageSyncPatch
    {
        public static void Postfix(MainMenuManager __instance)
        {
            
            MainMenuPlayPage.SyncMainMenu(__instance);
        }
    }

    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.OpenOnlineMenu))]
    public static class MainMenuOnlinePageEntryPatch
    {
        public static void Postfix(MainMenuManager __instance)
        {
            
            MainMenuPlayPage.OpenOnline(__instance, false);
        }
    }

    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.OpenEnterCodeMenu))]
    public static class MainMenuEnterCodeEntryPatch
    {
        public static void Prefix(MainMenuManager __instance)
        {
            
            MainMenuPlayPage.PrepareEnterCode(__instance);
        }

        public static void Postfix(MainMenuManager __instance)
        {
            
            MainMenuPlayPage.ReportEnterCode(__instance, "post");
        }
    }

    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.ClickBackEnterCode))]
    public static class MainMenuOnlinePageBackPatch
    {
        public static void Postfix(MainMenuManager __instance)
        {
            
            MainMenuPlayPage.OpenOnline(__instance, false);
        }
    }

    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.ResetScreen))]
    public static class MainMenuPlayPageResetPatch
    {
        public static void Postfix()
        {
            MainMenuPlayPage.Close();
        }
    }
}
