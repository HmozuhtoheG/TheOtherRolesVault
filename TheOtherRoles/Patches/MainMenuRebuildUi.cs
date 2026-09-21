using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
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

        private const float IconInset = 64f;

        private sealed class LayerDef
        {
            public string Path;
            public string Highlight;
            public Func<MainMenuManager, PassiveButton> Source;
            public Vector2 Center;
            public Vector2 Size;
            public string LabelKey;
            public float TextInset;
        }

        private static readonly LayerDef[] Layers =
        {
            new()
            {
                Path = "TheOtherRoles.Resources.ReBuildUi.MainStart.png",
                Highlight = "TheOtherRoles.Resources.ReBuildUi.HighLightStart.png",
                Source = m => m.playButton,
                Center = new Vector2(812f, 568.5f),
                Size = new Vector2(280f, 117f),
            },
            new()
            {
                Path = "TheOtherRoles.Resources.ReBuildUi.MainInventory.png",
                Highlight = "TheOtherRoles.Resources.ReBuildUi.HighLightInventory.png",
                Source = m => m.inventoryButton,
                Center = new Vector2(1108f, 568.5f),
                Size = new Vector2(280f, 117f),
            },
            new()
            {
                Path = "TheOtherRoles.Resources.ReBuildUi.MainSetting.png",
                Highlight = "TheOtherRoles.Resources.ReBuildUi.HighLightSetting.png",
                Source = m => m.settingsButton,
                Center = new Vector2(811.5f, 680f),
                Size = new Vector2(277f, 72f),
            },
            new()
            {
                Path = "TheOtherRoles.Resources.ReBuildUi.MainAcc.png",
                Highlight = "TheOtherRoles.Resources.ReBuildUi.HighLightAcc.png",
                Source = m => m.myAccountButton,
                Center = new Vector2(1107.5f, 680f),
                Size = new Vector2(277f, 72f),
            },
            new()
            {
                Path = "TheOtherRoles.Resources.ReBuildUi.MainQuit.png",
                Highlight = "TheOtherRoles.Resources.ReBuildUi.HighLightQuit.png",
                Source = m => m.quitButton,
                Center = new Vector2(278f, 812.5f),
                Size = new Vector2(146f, 73f),
            },
            new()
            {
                Path = "TheOtherRoles.Resources.ReBuildUi.MainNews.png",
                Highlight = "TheOtherRoles.Resources.ReBuildUi.HighLightNews.png",
                Source = m => m.newsButton,
                Center = new Vector2(619f, 788f),
                Size = new Vector2(174f, 77f),
                TextInset = IconInset,
            },
            new()
            {
                Path = "TheOtherRoles.Resources.ReBuildUi.MainStore.png",
                Highlight = "TheOtherRoles.Resources.ReBuildUi.HighLightStore.png",
                Source = m => m.shopButton,
                Center = new Vector2(834f, 788f),
                Size = new Vector2(174f, 77f),
                TextInset = IconInset,
            },
            new()
            {
                Path = "TheOtherRoles.Resources.ReBuildUi.MainMod.png",
                Highlight = "TheOtherRoles.Resources.ReBuildUi.HighLightDev.png",
                Source = m => m.creditsButton,
                Center = new Vector2(1060f, 788f),
                Size = new Vector2(174f, 77f),
                LabelKey = "mainMenuDeveloper",
                TextInset = IconInset,
            },
            new()
            {
                Path = "TheOtherRoles.Resources.ReBuildUi.MainDev.png",
                Highlight = "TheOtherRoles.Resources.ReBuildUi.HighLightMod.png",
                Source = ModButton,
                Center = new Vector2(1277f, 788f),
                Size = new Vector2(174f, 77f),
                LabelKey = "mainMenuMod",
                TextInset = IconInset,
            },
        };

        private const string HoverClipResource = "TheOtherRoles.Resources.ReBuildUi.grass";
        private const float HoverClipVolume = 0.8f;

        private static GameObject root;
        private static AudioClip hoverClip;
        private static AudioSource hoverSource;
        private static bool hoverClipFailed;

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

                var highlightRoot = Helpers.CreateObject("Highlight", root.transform,
                    LayerZStep * (Layers.Length + 1));

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
                    WireHover(AddClick(instance, holder.gameObject, layer),
                        CreateHighlight(highlightRoot.transform, layer, scale, index));
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

        private static PassiveButton ModButton(MainMenuManager instance)
        {
            if (!instance) return null;

            var found = MainMenuVanillaHider.FindInChildren(instance.transform, "TORButton");
            if (!found && instance.mainMenuUI)
                found = MainMenuVanillaHider.FindInChildren(instance.mainMenuUI.transform, "TORButton");

            return found ? found.GetComponent<PassiveButton>() : null;
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

            var text = LabelText(layer, sourceText);
            if (text == null) return;

            var holder = new GameObject("Label");
            holder.layer = parent.gameObject.layer;
            holder.transform.SetParent(parent, false);
            holder.transform.localScale = Vector3.one;

            var label = holder.AddComponent<TextMeshPro>();
            label.font = template.font;
            if (template.fontSharedMaterial) label.fontSharedMaterial = template.fontSharedMaterial;

            label.text = text;
            label.alignment = TextAlignmentOptions.Center;
            label.enableWordWrapping = false;
            label.color = Color.white;
            label.raycastTarget = false;

            var rect = label.rectTransform;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(Mathf.Max(1f, layer.Size.x - layer.TextInset), layer.Size.y) / PixelsPerUnit;

            label.enableAutoSizing = true;
            label.fontSizeMin = 0.2f;
            label.fontSizeMax = 10f;

            label.transform.localPosition = new Vector3(
                (layer.Center.x + layer.TextInset * 0.5f - DesignWidth * 0.5f) / PixelsPerUnit,
                (DesignHeight * 0.5f - layer.Center.y) / PixelsPerUnit,
                -0.5f);

            label.ForceMeshUpdate();

            holder.AddComponent<ScriptBehaviour>().UpdateHandler += () =>
            {
                if (!label) return;

                var want = LabelText(layer, sourceText);
                if (want != null && label.text != want) label.text = want;
            };
        }

        private static TextMeshPro FindButtonLabel(PassiveButton button)
        {
            if (button.buttonText) return button.buttonText;
            return button.GetComponentInChildren<TextMeshPro>(true);
        }

        private static string LabelText(LayerDef layer, TextMeshPro source)
        {
            if (!string.IsNullOrEmpty(layer.LabelKey))
            {
                try { return ModTranslation.getString(layer.LabelKey); }
                catch { return layer.LabelKey; }
            }

            return Resolve(source);
        }

        private static string Resolve(TextMeshPro source)
        {
            if (!source) return null;

            var translator = source.GetComponent<TextTranslatorTMP>();
            var name = translator ? translator.TargetText : (StringNames)short.MaxValue;

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
            else if (translator && !string.IsNullOrEmpty(translator.defaultStr))
            {
                try
                {
                    var mod = ModTranslation.getString(translator.defaultStr);
                    if (!string.IsNullOrWhiteSpace(mod)) return mod;
                }
                catch { }
            }

            return source.text;
        }

        private static PassiveButton AddClick(MainMenuManager instance, GameObject target, LayerDef layer)
        {
            var source = layer.Source(instance);
            if (!source) return null;

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

            return button;
        }

        private static SpriteRenderer CreateHighlight(Transform parent, LayerDef layer, float scale, int index)
        {
            var sprite = Helpers.loadSpriteFromResources(layer.Highlight, PixelsPerUnit);
            if (!sprite)
            {
                TheOtherRolesPlugin.Logger.LogWarning("[MainMenuRebuildUi] 高亮图加载失败: " + layer.Highlight);
                return null;
            }

            var renderer = Helpers.CreateObject<SpriteRenderer>("Highlight" + index, parent, Vector3.zero);
            renderer.sprite = sprite;
            renderer.transform.localScale = new Vector3(scale, scale, 1f);
            renderer.enabled = false;

            return renderer;
        }

        private static void WireHover(PassiveButton button, SpriteRenderer highlight)
        {
            if (!button) return;

            button.OnMouseOver.AddListener((UnityEngine.Events.UnityAction)(() =>
            {
                if (highlight) highlight.enabled = true;
                PlayHoverSound();
            }));

            button.OnMouseOut.AddListener((UnityEngine.Events.UnityAction)(() =>
            {
                if (highlight) highlight.enabled = false;
            }));
        }

        private static AudioClip LoadHoverClip()
        {
            var bytes = ReadHoverClipBytes();
            if (bytes == null) return null;

            if (bytes.Length < 44 || bytes[0] != 'R' || bytes[1] != 'I' || bytes[2] != 'F' || bytes[3] != 'F' ||
                bytes[8] != 'W' || bytes[9] != 'A' || bytes[10] != 'V' || bytes[11] != 'E')
            {
                TheOtherRolesPlugin.Logger.LogWarning("[MainMenuRebuildUi] 音效必须是 16-bit PCM 的 WAV");
                return null;
            }

            int channels = 1;
            int sampleRate = 44100;
            int bits = 16;
            int dataOffset = -1;
            int dataLength = 0;

            int position = 12;
            while (position + 8 <= bytes.Length)
            {
                var id = System.Text.Encoding.ASCII.GetString(bytes, position, 4);
                int size = BitConverter.ToInt32(bytes, position + 4);
                int body = position + 8;

                if (id == "fmt " && size >= 16)
                {
                    channels = BitConverter.ToInt16(bytes, body + 2);
                    sampleRate = BitConverter.ToInt32(bytes, body + 4);
                    bits = BitConverter.ToInt16(bytes, body + 14);
                }
                else if (id == "data")
                {
                    dataOffset = body;
                    dataLength = Math.Min(size, bytes.Length - body);
                    break;
                }

                position = body + size + (size & 1);
            }

            if (dataOffset < 0 || bits != 16 || channels < 1)
            {
                TheOtherRolesPlugin.Logger.LogWarning(
                    $"[MainMenuRebuildUi] WAV 解析失败: data={dataOffset} bits={bits} channels={channels}");
                return null;
            }

            var samples = new float[dataLength / 2];
            for (int i = 0; i < samples.Length; i++)
                samples[i] = BitConverter.ToInt16(bytes, dataOffset + i * 2) / 32768f;

            float peak = 0f;
            foreach (var sample in samples) peak = Mathf.Max(peak, Mathf.Abs(sample));

            float threshold = Mathf.Max(0.002f, peak * 0.02f);
            int first = 0;
            while (first < samples.Length && Mathf.Abs(samples[first]) < threshold) first++;
            first -= first % channels;
            if (first >= samples.Length) first = 0;

            var trimmed = new float[samples.Length - first];
            Array.Copy(samples, first, trimmed, 0, trimmed.Length);

            var clip = AudioClip.Create("TORV_MainMenuHover", trimmed.Length / channels, channels, sampleRate, false);
            clip.SetData(trimmed, 0);

            TheOtherRolesPlugin.Logger.LogInfo(
                $"[MainMenuRebuildUi] 音效就绪: {sampleRate}Hz {channels}ch {bits}bit {clip.length:F2}s " +
                $"裁掉起始静音 {(float)first / channels / sampleRate * 1000f:F0}ms");

            return clip;
        }

        private static byte[] ReadHoverClipBytes()
        {
            var assembly = Assembly.GetExecutingAssembly();

            using var stream = assembly.GetManifestResourceStream(HoverClipResource + ".wav")
                            ?? assembly.GetManifestResourceStream(HoverClipResource + ".mp3");
            if (stream == null)
            {
                TheOtherRolesPlugin.Logger.LogWarning("[MainMenuRebuildUi] 找不到音效资源: " + HoverClipResource);
                return null;
            }

            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);

            return buffer.ToArray();
        }

        private static AudioSource HoverSource()
        {
            if (hoverSource) return hoverSource;

            hoverSource = Helpers.CreateObject<AudioSource>("HoverSound", root.transform, Vector3.zero);
            hoverSource.playOnAwake = false;
            hoverSource.loop = false;
            hoverSource.spatialBlend = 0f;

            var manager = SoundManager.Instance;
            if (manager && manager.SfxChannel) hoverSource.outputAudioMixerGroup = manager.SfxChannel;

            return hoverSource;
        }

        private static void PlayHoverSound()
        {
            if (!hoverClip && !hoverClipFailed)
            {
                try { hoverClip = LoadHoverClip(); }
                catch (Exception ex)
                {
                    TheOtherRolesPlugin.Logger.LogWarning("[MainMenuRebuildUi] 音效加载异常: " + ex.Message);
                }

                hoverClipFailed = hoverClip == null;
            }

            if (!hoverClip) return;

            try
            {
                if (!Constants.ShouldPlaySfx()) return;

                var source = HoverSource();
                if (!source) return;

                source.Stop();
                source.clip = hoverClip;
                source.volume = HoverClipVolume;
                source.Play();
            }
            catch { }
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
