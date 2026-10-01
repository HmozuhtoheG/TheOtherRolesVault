using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using TheOtherRoles.MetaContext;
using UnityEngine;

namespace TheOtherRoles.Modules
{
    public static class PresetIO
    {
        public const string RootName = "TORVPresetIO";
        private const string FolderName = "TORVPresets";
        private const float RootZ = -30f;
        private const float DialogWidth = 4.6f;
        private const float DialogHeight = 3.8f;
        private const float RowHeight = 0.34f;

        private static GameObject root;
        private static MetaScreen dialog;
        private static MetaScreen toast;

        private static readonly TextAttribute rowAttr = new(TextAttribute.BoldAttr)
        {
            Size = new Vector2(DialogWidth - 0.8f, RowHeight),
            FontSize = 1.4f,
            FontMaxSize = 1.4f,
            FontMinSize = 0.7f
        };

        private static readonly TextAttribute titleAttr = new(TextAttribute.BoldAttr)
        {
            Size = new Vector2(DialogWidth - 0.8f, RowHeight),
            FontSize = 1.5f,
            FontMaxSize = 1.5f,
            FontMinSize = 0.8f
        };

        public static string Directory =>
            Path.Combine(Path.GetDirectoryName(TheOtherRolesPlugin.Instance.Config.ConfigFilePath), FolderName);

        public static string CurrentPresetName()
        {
            int index = CustomOption.preset;
            string[] keys = CustomOptionHolder.presets;
            if (keys == null || index < 0 || index >= keys.Length) return "Preset" + index;

            string key = keys[index];
            string localized = ModTranslation.getString(key, tryFind: true);
            return string.IsNullOrEmpty(localized) ? key : localized;
        }

        public static string Export()
        {
            try
            {
                System.IO.Directory.CreateDirectory(Directory);

                string baseName = Sanitize(CurrentPresetName());
                if (string.IsNullOrEmpty(baseName)) baseName = "preset";

                string path = Path.Combine(Directory, baseName + ".txt");
                int suffix = 1;
                while (File.Exists(path))
                    path = Path.Combine(Directory, $"{baseName} ({suffix++}).txt");

                string content = $"# TORV preset: {CurrentPresetName()} @ {DateTime.Now:yyyy-MM-dd HH:mm:ss}"
                                 + Environment.NewLine
                                 + CustomOption.BuildSettingsPayload()
                                 + Environment.NewLine;
                File.WriteAllText(path, content, new UTF8Encoding(true));

                TheOtherRolesPlugin.Logger.LogMessage($"[{RootName}] exported -> {path}");
                ShowToast(string.Format(ModTranslation.getString("presetExportDone"), Path.GetFileName(path)));
                return path;
            }
            catch (Exception e)
            {
                TheOtherRolesPlugin.Logger.LogWarning($"[{RootName}] export failed: {e}");
                ShowToast(ModTranslation.getString("presetImportFailed"));
                return null;
            }
        }

        public static int Import(string path)
        {
            try
            {
                string payload = File.ReadAllLines(path)
                    .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("#"));
                if (string.IsNullOrEmpty(payload)) throw new InvalidDataException("no payload line");

                int result = CustomOption.applySettingsPayload(payload, "preset file");
                TheOtherRolesPlugin.Logger.LogMessage($"[{RootName}] imported {Path.GetFileName(path)} -> {result}");
                ShowToast(result > 0
                    ? string.Format(ModTranslation.getString("presetImportDone"), Path.GetFileNameWithoutExtension(path))
                    : ModTranslation.getString("presetImportFailed"));
                return result;
            }
            catch (Exception e)
            {
                TheOtherRolesPlugin.Logger.LogWarning($"[{RootName}] import failed: {e}");
                ShowToast(ModTranslation.getString("presetImportFailed"));
                return 0;
            }
        }

        public static List<string> ListFiles()
        {
            try
            {
                if (!System.IO.Directory.Exists(Directory)) return new List<string>();
                return System.IO.Directory.GetFiles(Directory, "*.txt")
                    .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception e)
            {
                TheOtherRolesPlugin.Logger.LogWarning($"[{RootName}] list failed: {e}");
                return new List<string>();
            }
        }

        private static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            char[] invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
                sb.Append(invalid.Contains(c) ? '_' : c);
            return sb.ToString().Trim();
        }

        private static void EnsureRoot()
        {
            if (root) return;
            if (Camera.main == null) return;

            root = Helpers.CreateObject(RootName, Camera.main.transform, new Vector3(0f, 0f, RootZ));
            root.layer = LayerMask.NameToLayer("UI");
        }

        private static void CloseScreen(ref MetaScreen screen)
        {
            if (!screen) return;
            screen.CloseScreen();
            screen = null;
        }

        public static void ShowImportDialog()
        {
            EnsureRoot();
            if (!root) return;
            CloseScreen(ref dialog);

            List<string> files = ListFiles();

            var inner = new MetaContextOld();
            inner.Append(new MetaContextOld.Text(titleAttr)
            {
                RawText = ModTranslation.getString("presetImportTitle"),
                Alignment = IMetaContextOld.AlignmentOption.Center
            });
            inner.Append(new MetaContextOld.VerticalMargin(0.15f));

            if (files.Count == 0)
            {
                inner.Append(new MetaContextOld.Text(rowAttr)
                {
                    RawText = Helpers.cs(Color.gray, string.Format(ModTranslation.getString("presetImportNoFiles"), Directory)),
                    Alignment = IMetaContextOld.AlignmentOption.Center
                });
            }
            else
            {
                foreach (string file in files)
                {
                    string captured = file;
                    inner.Append(new MetaContextOld.Button(() =>
                    {
                        Import(captured);
                        CloseScreen(ref dialog);
                    }, rowAttr)
                    {
                        RawText = Path.GetFileNameWithoutExtension(file),
                        Alignment = IMetaContextOld.AlignmentOption.Center
                    });
                    inner.Append(new MetaContextOld.VerticalMargin(0.04f));
                }
            }

            dialog = MetaScreen.GenerateWindow(new Vector2(DialogWidth, DialogHeight), root.transform, Vector3.zero,
                true, true, false, BackgroundSetting.Old);
            dialog.SetContext(new MetaContextOld.ScrollView(new Vector2(DialogWidth, DialogHeight), inner, true)
            {
                Alignment = IMetaContextOld.AlignmentOption.Center
            });
        }

        private static void ShowToast(string message)
        {
            EnsureRoot();
            if (!root) return;
            CloseScreen(ref toast);

            var inner = new MetaContextOld();
            inner.Append(new MetaContextOld.Text(titleAttr)
            {
                RawText = message,
                Alignment = IMetaContextOld.AlignmentOption.Center
            });

            toast = MetaScreen.GenerateScreen(new Vector2(DialogWidth - 0.6f, 0.6f), root.transform, Vector3.zero, true, false, false);
            toast.SetContext(inner);

            TORGUIManager.Instance.StartCoroutine(CoCloseToast(toast).WrapToIl2Cpp());
        }

        private static System.Collections.IEnumerator CoCloseToast(MetaScreen target)
        {
            yield return new WaitForSeconds(2.6f);
            if (toast == target) CloseScreen(ref toast);
        }
    }
}
