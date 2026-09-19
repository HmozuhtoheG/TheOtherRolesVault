using System;
using System.Collections.Generic;
using HarmonyLib;
using TheOtherRoles.MetaContext;
using TheOtherRoles.Utilities;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TheOtherRoles.Patches
{
    public static class MainMenuRebuildUi
    {
        private const float DesignAspect = 16f / 9f;
        private const float UiScale = 1.2f;

        private const float DesignWidth = 1920f;
        private const float DesignHeight = 864f;
        private const float PixelsPerUnit = 100f;

        private static readonly Vector3 LayerZStep = new Vector3(0f, 0f, -0.01f);

        private sealed class LayerDef
        {
            public string Path;
            public Func<MainMenuManager, PassiveButton> Source;
            public Vector2 Center;
            public Vector2 Size;
        }

        private static readonly LayerDef[] Layers =
        {
            new()
            {
                Path = "TheOtherRoles.Resources.ReBuildUi.MainStart.png",
                Source = m => m.playButton,
                Center = new Vector2(812f, 568.5f),
                Size = new Vector2(280f, 117f),
            },
            new()
            {
                Path = "TheOtherRoles.Resources.ReBuildUi.MainInventory.png",
                Source = m => m.inventoryButton,
                Center = new Vector2(1108f, 568.5f),
                Size = new Vector2(280f, 117f),
            },
            new()
            {
                Path = "TheOtherRoles.Resources.ReBuildUi.MainSetting.png",
                Source = m => m.settingsButton,
                Center = new Vector2(811.5f, 680f),
                Size = new Vector2(277f, 72f),
            },
            new()
            {
                Path = "TheOtherRoles.Resources.ReBuildUi.MainAcc.png",
                Source = m => m.myAccountButton,
                Center = new Vector2(1107.5f, 680f),
                Size = new Vector2(277f, 72f),
            },
            new()
            {
                Path = "TheOtherRoles.Resources.ReBuildUi.MainQuit.png",
                Source = m => m.quitButton,
                Center = new Vector2(278f, 812.5f),
                Size = new Vector2(146f, 73f),
            },
        };

        private static GameObject root;

        public static void Apply(MainMenuManager instance)
        {
            try
            {
                if (!instance || !instance.mainMenuUI || root) return;

                var template = FindLabelTemplate(instance);
                if (!template)
                {
                    TheOtherRolesPlugin.Logger.LogWarning("[MainMenuRebuildUi] 找不到原版文字模板");
                    return;
                }

                var cam = Helpers.FindCamera(instance.mainMenuUI.layer);
                if (!cam) cam = Camera.main;

                float screenWidth = cam ? 2f * cam.orthographicSize * DesignAspect : 0f;
                float scale = (screenWidth > 0f ? screenWidth / (DesignWidth / PixelsPerUnit) : 1f) * UiScale;

                root = Helpers.CreateObject("ReBuildUi", instance.mainMenuUI.transform, new Vector3(0f, 0f, 5f));

                int index = 0;
                foreach (var layer in Layers)
                {
                    var sprite = Helpers.loadSpriteFromResources(layer.Path, PixelsPerUnit);
                    if (!sprite)
                    {
                        TheOtherRolesPlugin.Logger.LogWarning("[MainMenuRebuildUi] 图加载失败: " + layer.Path);
                        continue;
                    }

                    var holder = Helpers.CreateObject<SpriteRenderer>("Layer" + index, root.transform,
                        LayerZStep * index);
                    holder.sprite = sprite;
                    holder.transform.localScale = new Vector3(scale, scale, 1f);

                    AddLabel(instance, template, holder.transform, layer);
                    AddClick(instance, holder.gameObject, layer);
                    index++;
                }

                HideVanillaButtons(instance);
            }
            catch (Exception ex)
            {
                try { TheOtherRolesPlugin.Logger.LogWarning("[MainMenuRebuildUi] " + ex.Message); }
                catch { }
            }
        }

        private static TextMeshPro FindLabelTemplate(MainMenuManager instance)
        {
            var play = instance.playButton;
            if (play && play.buttonText) return play.buttonText;
            if (play) return play.GetComponentInChildren<TextMeshPro>(true);
            return null;
        }

        private static void AddLabel(MainMenuManager instance, TextMeshPro template, Transform parent, LayerDef layer)
        {
            var source = layer.Source(instance);
            var sourceText = source ? FindButtonLabel(source) : null;
            if (!sourceText) return;

            var holder = new GameObject("Label");
            holder.layer = parent.gameObject.layer;
            holder.transform.SetParent(parent, false);
            holder.transform.localScale = Vector3.one;

            var label = holder.AddComponent<TextMeshPro>();
            label.font = template.font;
            if (template.fontSharedMaterial) label.fontSharedMaterial = template.fontSharedMaterial;

            var labelName = LabelName(sourceText);
            label.text = Resolve(sourceText, labelName);
            label.alignment = TextAlignmentOptions.Center;
            label.enableWordWrapping = false;
            label.color = Color.white;
            label.raycastTarget = false;

            var rect = label.rectTransform;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = layer.Size / PixelsPerUnit;

            label.enableAutoSizing = true;
            label.fontSizeMin = 0.2f;
            label.fontSizeMax = 10f;

            label.transform.localPosition = new Vector3(
                (layer.Center.x - DesignWidth * 0.5f) / PixelsPerUnit,
                (DesignHeight * 0.5f - layer.Center.y) / PixelsPerUnit,
                -0.5f);

            label.ForceMeshUpdate();

            holder.AddComponent<ScriptBehaviour>().UpdateHandler += () =>
            {
                if (!label) return;

                var want = Resolve(sourceText, labelName);
                if (want != null && label.text != want) label.text = want;
            };
        }

        private static TextMeshPro FindButtonLabel(PassiveButton button)
        {
            if (button.buttonText) return button.buttonText;
            return button.GetComponentInChildren<TextMeshPro>(true);
        }

        private static StringNames LabelName(TextMeshPro source)
        {
            if (!source) return (StringNames)short.MaxValue;

            var translator = source.GetComponent<TextTranslatorTMP>();
            return translator ? translator.TargetText : (StringNames)short.MaxValue;
        }

        private static string Resolve(TextMeshPro source, StringNames name)
        {
            if ((int)name != short.MaxValue)
            {
                try
                {
                    var controller = TranslationController.Instance;
                    if (controller != null)
                    {
                        var translated = controller.GetString(name);
                        if (!string.IsNullOrWhiteSpace(translated)) return translated;
                    }
                }
                catch { }
            }

            return source ? source.text : null;
        }

        private static void AddClick(MainMenuManager instance, GameObject target, LayerDef layer)
        {
            var source = layer.Source(instance);
            if (!source) return;

            var collider = target.AddComponent<BoxCollider2D>();
            collider.isTrigger = true;
            collider.size = layer.Size / PixelsPerUnit;
            collider.offset = new Vector2(
                (layer.Center.x - DesignWidth * 0.5f) / PixelsPerUnit,
                (DesignHeight * 0.5f - layer.Center.y) / PixelsPerUnit);

            var button = target.AddComponent<PassiveButton>();
            button.OnClick = new Button.ButtonClickedEvent();
            button.OnMouseOver = new UnityEngine.Events.UnityEvent();
            button.OnMouseOut = new UnityEngine.Events.UnityEvent();
            button.OnClick.AddListener((UnityEngine.Events.UnityAction)(() =>
            {
                try { source.OnClick.Invoke(); }
                catch (Exception ex) { TheOtherRolesPlugin.Logger.LogWarning("[MainMenuRebuildUi] 点击失败: " + ex.Message); }
            }));
        }

        private static void HideVanillaButtons(MainMenuManager instance)
        {
            foreach (var layer in Layers)
            {
                var button = layer.Source(instance);
                if (!button || !button.gameObject) continue;

                foreach (var renderer in button.GetComponentsInChildren<SpriteRenderer>(true))
                    if (renderer) renderer.enabled = false;

                foreach (var text in button.GetComponentsInChildren<TextMeshPro>(true))
                    if (text) text.enabled = false;

                foreach (var collider in button.GetComponentsInChildren<Collider2D>(true))
                    if (collider) collider.enabled = false;
            }
        }
    }

    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.Start))]
    [HarmonyPriority(Priority.Normal)]
    public static class MainMenuRebuildUiPatch
    {
        public static void Postfix(MainMenuManager __instance)
        {
            MainMenuRebuildUi.Apply(__instance);
        }
    }
}
