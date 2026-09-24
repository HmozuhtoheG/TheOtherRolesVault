using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using MainMenuSetUpPatch = TheOtherRoles.Modules.MainMenuSetUpPatch;

namespace TheOtherRoles.Patches
{
    public static class MainMenuVanillaHider
    {
        private const string CustomLogoResource = "TheOtherRoles.Resources.ReBuildUi.MainMenuLogo.png";
        private const float CustomLogoWidth = 0.486f;
        private static readonly Vector2 CustomLogoAnchor = new Vector2(0.4992f, 0.6867f);

        private static MainMenuManager _menu;
        private static GameObject _customLogo;
        private static SpriteRenderer _logoRenderer;
        private static Transform _logoParent;
        private static Vector3 _logoParentPos;
        private static float _logoParentScale;
        private static bool _dumped;
        private static bool _hooked;
        private static bool _lateDumped;
        private static int _popupTick;
        private static float _enforceUntil;
        private static float _lateDumpAt;

        private const string FriendCodeBar = "AccountManager/AccountTab";

        private static readonly HashSet<string> Visible = new HashSet<string>
        {
            "TORMainMenuBackground",
            "TORMenuLogo",
            "ReBuildUi",
            "TORVPlayPage",
            "TORVOnlinePage",
            "PlayButton",
        };

        private static readonly HashSet<string> SkipRoots = new HashSet<string>
        {
            "TransitionFadeManager",
            "DisconnectPopup_Real",
            "WaitingForHostPopup",
        };

        private static readonly HashSet<string> SweepRoots = new HashSet<string>
        {
            "MainMenuManager",
            "ModManager",
            "ConsoleManager",
            "MouseCursorForConsole(Clone)",
            "ReactorVersion",
        };

        private static readonly HashSet<string> FriendCodeBackgrounds = new HashSet<string>
        {
            "BarSprite",
        };

        private static readonly List<SpriteRenderer> _disabledRenderers = new List<SpriteRenderer>();
        private static readonly List<Transform> _keptRoots = new List<Transform>();
        private static readonly List<GameObject> _disabledObjects = new List<GameObject>();

        private static void RepairPlayFlow()
        {
            if (_menu && _menu.rightPanelMask && !_menu.rightPanelMask.activeSelf)
                _menu.rightPanelMask.SetActive(true);
        }

        public static void Hide(MainMenuManager instance)
        {
            _menu = instance;
            _enforceUntil = Time.time + 10f;
            _lateDumpAt = Time.time + 6f;

            HookSceneChange();

            Step("ClearBackground", ClearBackground);
            Step("ReplaceLogo", ReplaceLogo);
            Step("ClearPanels", ClearPanels);
            Step("ClearDivider", ClearDivider);
            Step("MarkKeptPlayFlow", MarkKeptPlayFlow);
            Step("ClearButtons", ClearButtons);
            Step("ClearContainers", ClearContainers);
            Step("ClearSubScreens", ClearSubScreens);
            Step("ClearFriendCodeBar", ClearFriendCodeBar);
            Step("DestroyModBanner", DestroyModBanner);
            Step("DestroyModLobbyText", DestroyModLobbyText);
            Step("ClearModButton", ClearModButton);
            Step("ClearModPanels", ClearModPanels);
            Step("ClearModUpdateButton", ClearModUpdateButton);
            Step("ClearVersionText", ClearVersionText);
            Step("SweepVisible", SweepMenu);
            Step("RepairPlayFlow", RepairPlayFlow);

            if (!_dumped)
            {
                _dumped = true;
                Step("Dump", Dump);
            }
        }

        public static void Enforce(MainMenuManager instance)
        {
            if (!instance) return;

            if (!_lateDumped && Time.time > _lateDumpAt)
            {
                _lateDumped = true;
                Step("DumpLate", Dump);
            }

            Step("WatchPopup", WatchPopup);

            if (Time.time > _enforceUntil) return;

            Step("DestroyModBanner", DestroyModBanner);
            Step("ClearModButton", ClearModButton);
            Step("DestroyModLobbyText", DestroyModLobbyText);

            if (_logoParent && _logoRenderer &&
                (_logoParent.position != _logoParentPos || _logoParent.lossyScale.x != _logoParentScale))
                Step("PlaceLogo", () => PlaceLogo(false));
        }

        public static void DumpNow()
        {
            Step("DumpHotkey", Dump);
        }

        // ────────────────────────────────────────────────────────────
        //  目标对象
        // ────────────────────────────────────────────────────────────

        private static void ClearBackground()
        {
            Kill("BackgroundTexture");
            Kill("Ambience");
        }

        private static void ReplaceLogo()
        {
            var logo = LeftPanel?.FindChild("Sizer")?.FindChild("LOGO-AU");
            if (!logo || _customLogo) return;

            var old = logo.GetComponent<SpriteRenderer>();
            var sprite = Helpers.loadSpriteFromResources(CustomLogoResource, 100f);

            logo.gameObject.SetActive(false);
            if (!sprite) return;

            var renderer = Helpers.CreateObject<SpriteRenderer>("TORMenuLogo", logo.parent, logo.localPosition);
            renderer.sprite = sprite;

            if (old)
            {
                renderer.sortingLayerID = old.sortingLayerID;
                renderer.sortingOrder = old.sortingOrder;
            }

            _logoRenderer = renderer;
            _logoParent = logo.parent;
            _customLogo = renderer.gameObject;

            PlaceLogo(true);
        }

        private static bool PlaceLogo(bool log)
        {
            if (!_logoRenderer || !_logoRenderer.sprite || !_logoParent) return false;

            var cam = Helpers.FindCamera(_logoRenderer.gameObject.layer);
            if (!cam) cam = Camera.main;
            if (!cam) return false;

            float screenHeight = 2f * cam.orthographicSize;
            float screenWidth = screenHeight * cam.aspect;
            float parentScale = Mathf.Max(Mathf.Abs(_logoParent.lossyScale.x), 0.0001f);

            float worldScale = screenWidth * CustomLogoWidth / _logoRenderer.sprite.bounds.size.x;
            float localScale = worldScale / parentScale;
            _logoRenderer.transform.localScale = new Vector3(localScale, localScale, 1f);

            _logoRenderer.transform.position = new Vector3(
                (CustomLogoAnchor.x - 0.5f) * screenWidth,
                (CustomLogoAnchor.y - 0.5f) * screenHeight,
                _logoRenderer.transform.position.z);

            _logoParentPos = _logoParent.position;
            _logoParentScale = _logoParent.lossyScale.x;

            if (log)
            {
                TheOtherRolesPlugin.Logger.LogInfo(
                    $"[MainMenuVanillaHider] logo cam={cam.name} ortho={cam.orthographicSize} aspect={cam.aspect} " +
                    $"screen={screenWidth:F2}x{screenHeight:F2} parentScale={parentScale:F4} localScale={localScale:F4} " +
                    $"spriteW={_logoRenderer.sprite.bounds.size.x:F2} screenPos={_logoRenderer.transform.position}");
            }

            return true;
        }

        private static void ClearPanels()
        {
            var leftPanel = LeftPanel;
            if (leftPanel)
            {
                var vanilla = leftPanel.GetComponent<SpriteRenderer>();
                if (vanilla) vanilla.enabled = false;

                var reworked = leftPanel.FindChild("ReworkedLeftPanel");
                if (reworked) reworked.gameObject.SetActive(false);
            }

            var rightPanel = FindByName("RightPanel");
            if (rightPanel)
            {
                var renderer = rightPanel.GetComponent<SpriteRenderer>();
                if (renderer) renderer.enabled = false;
            }

            if (_menu && _menu.rightPanelMask) _menu.rightPanelMask.SetActive(true);
        }

        private static void ClearDivider()
        {
            var dividers = new List<Transform>();
            CollectDividers(_menu ? _menu.transform : null, dividers);
            foreach (var divider in dividers) divider.gameObject.SetActive(false);
        }

        private static void CollectDividers(Transform node, List<Transform> found)
        {
            if (node == null) return;

            for (int i = 0; i < node.childCount; i++)
            {
                var child = node.GetChild(i);
                if (!child) continue;
                if (child.name == "Divider") found.Add(child);
                CollectDividers(child, found);
            }
        }

        private static void MarkKeptPlayFlow()
        {
            _keptRoots.Clear();
            if (_menu == null) return;

            Component[] kept =
            {
                _menu.PlayOnlineButton, _menu.playLocalButton, _menu.findGameButton,
                _menu.createGameButton, _menu.backButtonOnline, _menu.entercodeField,
                _menu.onlineButtonsContainer, _menu.enterCodeContainer,
                _menu.screenTint,
            };

            foreach (var component in kept)
                if (component) _keptRoots.Add(component.transform);

            GameObject[] keptObjects =
            {
                _menu.onlineButtons, _menu.onlineHeader, _menu.gameModeButtons,
                _menu.enterCodeButtons, _menu.enterCodeHeader,
                _menu.accountButtons, _menu.creditsScreen,
            };

            Component[] keptComponents = { _menu.createGameScreen };

            foreach (var go in keptObjects)
                if (go) _keptRoots.Add(go.transform);

            foreach (var component in keptComponents)
                if (component) _keptRoots.Add(component.transform);
        }

        private static void ClearButtons()
        {
            if (_menu == null) return;

            Component[] fields =
            {
                _menu.playButton, _menu.inventoryButton, _menu.shopButton,
                _menu.newsButton, _menu.myAccountButton, _menu.settingsButton, _menu.creditsButton,
                _menu.quitButton, _menu.freePlayButton, _menu.howToPlayButton,
                _menu.accountCTAButton, _menu.accountStatsButton,
            };

            foreach (var field in fields)
            {
                if (field && IsVisibleKept(field.gameObject)) continue;
                if (field && KeptByPlayFlow(field.gameObject)) continue;
                Kill(field);
            }

            if (_menu.mainButtons != null)
                foreach (var button in _menu.mainButtons)
                    if (button && button.gameObject && !Visible.Contains(button.gameObject.name))
                        button.gameObject.SetActive(false);
        }

        private static void ClearContainers()
        {
            if (_menu == null) return;

            GameObject[] containers = { _menu.adsMenu };

            foreach (var container in containers) Kill(container);
        }

        private static void ClearSubScreens()
        {
            if (_menu == null) return;

            Kill(_menu.createGameScreen);
            Kill(_menu.ejectMenu);
        }

        private static void DestroyModBanner()
        {
            var banner = FindByName("bannerLogo_TOR");
            if (!banner && CredentialsPatch.LogoPatch.motdText)
                banner = CredentialsPatch.LogoPatch.motdText.transform.parent.gameObject;
            if (banner) Object.Destroy(banner);
        }

        private static void DestroyModLobbyText()
        {
            Kill("LobbyText");
        }

        private static void ClearModButton()
        {
            if (!_menu) return;

            var button = FindInChildren(_menu.transform, "TORButton");
            if (button) button.SetActive(false);
        }

        private static void ClearModPanels()
        {
            if (MainMenuSetUpPatch.modScreen) MainMenuSetUpPatch.modScreen.SetActive(false);
            if (MainMenuSetUpPatch.aboutScreen) MainMenuSetUpPatch.aboutScreen.SetActive(false);
        }

        private static void ClearModUpdateButton()
        {
            Kill("ExitGameButton(Clone)");
        }

        private static void ClearVersionText()
        {
            var shower = Object.FindObjectOfType<VersionShower>();
            if (shower && shower.text) shower.text.gameObject.SetActive(false);
        }

        private static void ClearFriendCodeBar()
        {
            var root = GameObject.Find("AccountManager");
            if (!root) return;

            var bar = FindInChildren(root.transform, "BarSprite");
            if (!bar) return;

            foreach (var renderer in bar.GetComponentsInChildren<SpriteRenderer>(true))
                if (renderer) renderer.enabled = false;
        }

        // ────────────────────────────────────────────────────────────
        //  全局清扫
        // ────────────────────────────────────────────────────────────

        private static void SweepMenu()
        {
            SweepAll<SpriteRenderer>(component =>
            {
                if (!component.enabled) return;
                if (!component.gameObject.activeInHierarchy) return;
                _disabledRenderers.Add(component);
                component.enabled = false;
            }, keepRenderers: false);

            SweepAll<TMPro.TextMeshPro>(component =>
            {
                var go = component.gameObject;
                if (!go.activeInHierarchy) return;
                _disabledObjects.Add(go);
                go.SetActive(false);
            }, keepRenderers: true);
        }

        private static void SweepAll<T>(Action<T> hide, bool keepRenderers) where T : Component
        {
            var type = Il2CppInterop.Runtime.Il2CppType.Of<T>();

            foreach (var entry in Object.FindObjectsOfTypeIncludingAssets(type))
            {
                var component = entry ? entry.TryCast<T>() : null;
                if (!component || !component.gameObject) continue;
                if (!IsSweepable(component.gameObject)) continue;
                if (IsKept(component.gameObject, keepRenderers)) continue;

                hide(component);
            }
        }

        private static bool IsSweepable(GameObject target)
        {
            var root = RootOf(target.transform);
            if (SkipRoots.Contains(root)) return false;

            return SweepRoots.Contains(root);
        }

        private static bool IsVisibleKept(GameObject target)
        {
            for (var node = target.transform; node; node = node.parent)
                if (Visible.Contains(node.name)) return true;

            return false;
        }

        private static readonly string[] PlayFlowNames =
        {
            "onlineButtonsContainer",
            "onlineHeader",
            "onlineButtons",
            "enterCodeContainer",
            "enterCodeHeader",
            "enterCodeButtons",
            "gameModeButtons",
            "accountButtons",
            "createGameScreen",
        };

        private static bool KeptPlayFlow(Transform node)
        {
            for (var current = node; current; current = current.parent)
            {
                var name = current.name;
                for (int i = 0; i < PlayFlowNames.Length; i++)
                    if (name == PlayFlowNames[i]) return true;
            }
            return false;
        }

        private static bool IsKept(GameObject target, bool keepRenderers)
        {
            if (IsVisibleKept(target)) return true;
            if (KeptPlayFlow(target.transform)) return true;

            foreach (var kept in _keptRoots)
            {
                if (!kept) continue;
                if (target.transform == kept || target.transform.IsChildOf(kept)) return true;
            }

            if (!PathOf(target.transform).StartsWith(FriendCodeBar, StringComparison.Ordinal)) return false;

            if (keepRenderers) return true;
            return !FriendCodeBackgrounds.Contains(target.name);
        }

        private static bool KeptByPlayFlow(GameObject target)
        {
            foreach (var kept in _keptRoots)
            {
                if (!kept) continue;
                if (target.transform == kept || target.transform.IsChildOf(kept)) return true;
            }
            return false;
        }

        private static void HookSceneChange()
        {
            if (_hooked) return;
            _hooked = true;

            SceneManager.add_sceneLoaded((Action<Scene, LoadSceneMode>)((scene, _) =>
            {
                if (scene.name == "MainMenu") return;
                RestoreAll();
            }));
        }

        private static void RestoreAll()
        {
            foreach (var renderer in _disabledRenderers)
                if (renderer) renderer.enabled = true;
            _disabledRenderers.Clear();

            foreach (var target in _disabledObjects)
                if (target) target.SetActive(true);
            _disabledObjects.Clear();

            _menu = null;
            _customLogo = null;
            _logoRenderer = null;
            _logoParent = null;
            _dumped = false;
            _lateDumped = false;
            _popupTick = 0;
            _enforceUntil = 0f;
            _lateDumpAt = 0f;
        }

        // ────────────────────────────────────────────────────────────
        //  诊断
        // ────────────────────────────────────────────────────────────

        private static void Dump()
        {
            var builder = new StringBuilder("[MainMenuVanillaHider] ===== 仍可见 =====");

            Step("DumpTree", () => DumpTree(_menu ? _menu.transform.root : null, 0, builder));
            Step("DumpAllText", () => DumpAll<TMPro.TextMeshPro>(builder, "[全局TMP]",
                text => string.IsNullOrWhiteSpace(text.text) ? null : Short(text.text)));
            Step("DumpAllSprite", () => DumpAll<SpriteRenderer>(builder, "[全局SR]",
                renderer => renderer.enabled ? "" : null));
            Step("DumpPlayFlow", () => DumpPlayFlow(builder));

            Step("DumpLog", () => TheOtherRolesPlugin.Logger.LogInfo(builder.ToString()));
        }

        private static void WatchPopup()
        {
            if (!_menu) return;

            if (++_popupTick % 15 != 0) return;
            if (!PopupOpen()) return;

            Step("CleanPopupImages", CleanPopupImages);
        }

        private static void CleanPopupImages()
        {
            if (!_menu || !_menu.creditsScreen) return;

            foreach (var image in _menu.creditsScreen.GetComponentsInChildren<UnityEngine.UI.Image>(true))
            {
                if (!image || !image.gameObject) continue;
                if (!IsBigWhite(image.rectTransform, image.color, image.sprite)) continue;

                image.color = new Vector4(image.color.r, image.color.g, image.color.b, 0f);
            }

            foreach (var raw in _menu.creditsScreen.GetComponentsInChildren<UnityEngine.UI.RawImage>(true))
            {
                if (!raw || !raw.gameObject) continue;
                if (!IsBigWhite(raw.rectTransform, raw.color, raw.texture)) continue;

                raw.color = new Vector4(raw.color.r, raw.color.g, raw.color.b, 0f);
            }
        }

        private static bool IsBigWhite(RectTransform rect, Color color, UnityEngine.Object asset)
        {
            if (asset && asset.name != "blank" && asset.name != "White") return false;

            var size = rect ? rect.rect.size : Vector2.zero;
            if (size.x * size.y < 200000f) return false;

            return color.r > 0.9f && color.g > 0.9f && color.b > 0.9f && color.a > 0.5f;
        }

        private static bool PopupOpen()
        {
            if (_menu.screenTint && _menu.screenTint.enabled) return true;
            if (FindActive<CreditsScreenPopUp>()) return true;
            if (FindActive<AnnouncementPopUp>()) return true;
            if (MainMenuSetUpPatch.modScreen && MainMenuSetUpPatch.modScreen.activeInHierarchy) return true;

            return false;
        }

        private static bool FindActive<T>() where T : Component
        {
            var type = Il2CppInterop.Runtime.Il2CppType.Of<T>();

            foreach (var entry in Object.FindObjectsOfTypeIncludingAssets(type))
            {
                var component = entry ? entry.TryCast<T>() : null;
                if (component && component.gameObject && component.gameObject.activeInHierarchy) return true;
            }

            return false;
        }

        private static void DumpPlayFlow(StringBuilder builder)
        {
            if (!_menu) return;

            builder.Append("\n===== 原版开局流程层级 =====");

            DumpSubtree(_menu.onlineButtonsContainer, "onlineButtonsContainer", builder, 0);
            DumpSubtree(_menu.onlineHeader ? _menu.onlineHeader.transform : null, "onlineHeader", builder, 0);
            DumpSubtree(_menu.onlineButtons ? _menu.onlineButtons.transform : null, "onlineButtons", builder, 0);
            DumpSubtree(_menu.enterCodeContainer, "enterCodeContainer", builder, 0);
            DumpSubtree(_menu.enterCodeHeader ? _menu.enterCodeHeader.transform : null, "enterCodeHeader", builder, 0);
            DumpSubtree(_menu.enterCodeButtons ? _menu.enterCodeButtons.transform : null, "enterCodeButtons", builder, 0);
            DumpSubtree(_menu.gameModeButtons ? _menu.gameModeButtons.transform : null, "gameModeButtons", builder, 0);
            DumpSubtree(_menu.accountButtons ? _menu.accountButtons.transform : null, "accountButtons", builder, 0);

            var accountRoot = GameObject.Find("AccountManager");
            DumpSubtree(accountRoot ? accountRoot.transform : null, "AccountManager", builder, 0);
        }

        private static void DumpSubtree(Transform node, string label, StringBuilder builder, int depth)
        {
            if (node == null)
            {
                builder.Append("\n  [缺] ").Append(label);
                return;
            }

            builder.Append("\n-- ").Append(label).Append(" --");
            DumpNodeRaw(node, depth, builder, 0);
        }

        private static void DumpNodeRaw(Transform node, int depth, StringBuilder builder, int budget)
        {
            if (node == null || depth > 4 || budget > 120) return;

            var go = node.gameObject;
            var renderer = go.GetComponent<SpriteRenderer>();
            var text = go.GetComponent<TMPro.TextMeshPro>();
            var button = go.GetComponent<PassiveButton>();
            var aspectPos = go.GetComponent<AspectPosition>();

            var kinds = new List<string>();
            if (renderer) kinds.Add("SR" + (renderer.enabled ? "" : "(off)"));
            if (text) kinds.Add("TMP:" + Short(text.text));
            if (button) kinds.Add("Btn");
            if (aspectPos) kinds.Add("AspectPos");

            var pos = node.localPosition;
            builder.Append('\n').Append(new string(' ', depth * 2 + 2))
                .Append(go.activeSelf ? "[o] " : "[x] ").Append(go.name)
                .Append("  (").Append(pos.x.ToString("F2")).Append(',').Append(pos.y.ToString("F2")).Append(')');

            if (kinds.Count > 0) builder.Append("  ").Append(string.Join(" | ", kinds));

            for (int i = 0; i < node.childCount; i++)
                DumpNodeRaw(node.GetChild(i), depth + 1, builder, budget + 1);
        }

        private static void DumpTree(Transform node, int depth, StringBuilder builder)
        {
            if (node == null || depth > 14) return;

            var go = node.gameObject;
            if (!go.activeInHierarchy) return;

            var renderer = go.GetComponent<SpriteRenderer>();
            var text = go.GetComponent<TMPro.TextMeshPro>();

            bool hasRenderer = renderer && renderer.enabled;
            bool hasText = text && !string.IsNullOrWhiteSpace(text.text);

            if (hasRenderer || hasText)
            {
                builder.Append('\n').Append(PathOf(node));
                if (hasRenderer) builder.Append("  [SR]");
                if (hasText) builder.Append("  [TMP:").Append(Short(text.text)).Append(']');
            }

            for (int i = 0; i < node.childCount; i++)
                DumpTree(node.GetChild(i), depth + 1, builder);
        }

        private static void DumpAll<T>(StringBuilder builder, string tag, Func<T, string> describe)
            where T : Component
        {
            var type = Il2CppInterop.Runtime.Il2CppType.Of<T>();

            foreach (var entry in Object.FindObjectsOfTypeIncludingAssets(type))
            {
                var component = entry ? entry.TryCast<T>() : null;
                if (!component || !component.gameObject) continue;
                if (!component.gameObject.activeInHierarchy) continue;

                var detail = describe(component);
                if (detail == null) continue;

                builder.Append('\n').Append(tag).Append(' ').Append(PathOf(component.transform));
                if (detail.Length > 0) builder.Append(" : ").Append(detail);
            }
        }

        // ────────────────────────────────────────────────────────────
        //  工具
        // ────────────────────────────────────────────────────────────

        private static string PathOf(Transform node)
        {
            if (!node) return "?";

            var path = node.name;
            var parent = node.parent;

            for (int guard = 0; parent && guard < 12; guard++)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            return path;
        }

        private static string RootOf(Transform node)
        {
            var path = PathOf(node);
            int slash = path.IndexOf('/');
            return slash < 0 ? path : path.Substring(0, slash);
        }

        private static string Short(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            value = value.Replace("\n", " ");
            return value.Length > 32 ? value.Substring(0, 32) : value;
        }

        private static Transform LeftPanel
        {
            get
            {
                try
                {
                    if (!_menu || !_menu.mainMenuUI) return null;
                    return _menu.mainMenuUI.transform.FindChild("AspectScaler")?.FindChild("LeftPanel");
                }
                catch { return null; }
            }
        }

        private static void Kill(Component target)
        {
            if (!target) return;
            if (target.gameObject) target.gameObject.SetActive(false);
        }

        private static void Kill(GameObject target)
        {
            if (target) target.SetActive(false);
        }

        private static void Kill(string name)
        {
            var go = FindByName(name);
            if (go) go.SetActive(false);
        }

        private static GameObject FindByName(string name)
        {
            var direct = GameObject.Find(name);
            if (direct) return direct;

            if (!_menu) return null;

            var found = FindInChildren(_menu.transform, name);
            if (found) return found;

            if (_menu.mainMenuUI)
            {
                found = FindInChildren(_menu.mainMenuUI.transform, name);
                if (found) return found;
            }
            return null;
        }

        public static GameObject FindInChildren(Transform root, string name)
        {
            if (root == null) return null;

            for (int i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                if (child == null) continue;
                if (child.name == name) return child.gameObject;

                var found = FindInChildren(child, name);
                if (found != null) return found;
            }
            return null;
        }

        private static void Step(string label, Action action)
        {
            try { action(); }
            catch (Exception ex)
            {
                try { TheOtherRolesPlugin.Logger.LogWarning($"[MainMenuVanillaHider] {label} 失败: {ex.Message}"); }
                catch { }
            }
        }
    }

    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.Start))]
    [HarmonyPriority(Priority.Low)]
    public static class MainMenuClearPatch
    {
        public static void Postfix(MainMenuManager __instance)
        {
            MainMenuVanillaHider.Hide(__instance);
        }
    }

    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.ActivateMainMenuUI))]
    public static class MainMenuReactivatePatch
    {
        public static void Postfix(MainMenuManager __instance)
        {
            MainMenuVanillaHider.Hide(__instance);
        }
    }

    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.LateUpdate))]
    public static class MainMenuEnforcePatch
    {
        public static void Postfix(MainMenuManager __instance)
        {
            MainMenuVanillaHider.Enforce(__instance);
        }
    }

    [HarmonyPatch(typeof(KeyboardJoystick), nameof(KeyboardJoystick.Update))]
    public static class MainMenuDumpHotkeyPatch
    {
        public static void Postfix()
        {
            try
            {
                if (Input.GetKeyDown(KeyCode.F9)) MainMenuVanillaHider.DumpNow();
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(PingTracker), nameof(PingTracker.Update))]
    public static class MainMenuPingTrackerPatch
    {
        public static void Postfix(PingTracker __instance)
        {
            if (!__instance || !__instance.text) return;

            try
            {
                bool inMainMenu = SceneManager.GetActiveScene().name == "MainMenu";
                var textObject = __instance.text.gameObject;
                if (textObject.activeSelf == inMainMenu)
                    textObject.SetActive(!inMainMenu);
            }
            catch { }
        }
    }
}
