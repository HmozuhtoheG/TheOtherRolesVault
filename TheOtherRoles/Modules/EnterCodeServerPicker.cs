using System.Collections.Generic;
using System.Linq;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using TheOtherRoles.MetaContext;
using UnityEngine;
using Object = Il2CppSystem.Object;

namespace TheOtherRoles.Modules
{
    public static class EnterCodeServerPicker
    {
        public const string RootName = "TORVEnterCodeServerPicker";

        private const float ViewAnchorX = -0.30f;
        private const float ViewAnchorY = 0.10f;
        private const float RootZ = -30f;

        private const float ButtonWidth = 2.6f;
        private const float ButtonHeight = 0.44f;
        private const float ListWidth = 3.6f;
        private const float ListHeight = 3.6f;
        private const float RowHeight = 0.36f;

        private static GameObject root;
        private static MetaScreen buttonScreen;
        private static MetaScreen listScreen;

        private static readonly TextAttribute buttonAttr = new(TextAttribute.BoldAttr)
        {
            Size = new Vector2(ButtonWidth, ButtonHeight),
            FontSize = 1.5f,
            FontMaxSize = 1.5f,
            FontMinSize = 0.8f
        };

        private static readonly TextAttribute rowAttr = new(TextAttribute.BoldAttr)
        {
            Size = new Vector2(ListWidth - 0.6f, RowHeight),
            FontSize = 1.4f,
            FontMaxSize = 1.4f,
            FontMinSize = 0.7f
        };

        private static readonly TextAttribute titleAttr = new(TextAttribute.BoldAttr)
        {
            Size = new Vector2(ListWidth - 0.6f, RowHeight),
            FontSize = 1.3f,
            FontMaxSize = 1.3f,
            FontMinSize = 0.7f
        };

        private static Vector2 ViewSize()
        {
            Camera cam = Camera.main;
            if (cam == null) return new Vector2(8f, 4.5f);
            float halfHeight = cam.orthographicSize;
            return new Vector2(halfHeight * cam.aspect * 2f, halfHeight * 2f);
        }

        private static ServerManager Servers => DestroyableSingleton<ServerManager>.Instance;

        private static string RegionName(IRegionInfo region)
        {
            if (region == null) return "?";
            return DestroyableSingleton<TranslationController>.Instance
                .GetStringWithDefault(region.TranslateName, region.Name, new Il2CppReferenceArray<Object>(0));
        }

        private static bool IsOfficial(IRegionInfo region) =>
            ServerManager.DefaultRegions != null && ServerManager.DefaultRegions.Contains(region);

        private static List<IRegionInfo> OrderedRegions() =>
            Servers.AvailableRegions
                .Where(r => r != null)
                .OrderBy(IsOfficial)
                .ToList();

        public static void Show()
        {
            if (root) return;
            if (Camera.main == null) return;

            ServerManager mgr = Servers;
            if (mgr == null || mgr.AvailableRegions == null || mgr.AvailableRegions.Length == 0) return;

            Vector2 view = ViewSize();
            root = Helpers.CreateObject(RootName, Camera.main.transform,
                new Vector3(view.x * ViewAnchorX, view.y * ViewAnchorY, RootZ));
            root.layer = LayerMask.NameToLayer("UI");

            buttonScreen = MetaScreen.GenerateScreen(new Vector2(ButtonWidth, ButtonHeight), root.transform, Vector3.zero, false, false, false);
            RefreshButton();
        }

        public static void Hide()
        {
            if (listScreen) { listScreen.CloseScreen(); listScreen = null; }
            if (buttonScreen) { buttonScreen.CloseScreen(); buttonScreen = null; }
            if (root) UnityEngine.Object.Destroy(root);
            root = null;
        }

        private static void RefreshButton()
        {
            if (!buttonScreen) return;

            var ctx = new MetaContextOld();
            ctx.Append(new MetaContextOld.Button(ToggleList, buttonAttr)
            {
                RawText = ModTranslation.getString("enterCodeServer") + ": " + RegionName(Servers?.CurrentRegion),
                Alignment = IMetaContextOld.AlignmentOption.Center
            });
            buttonScreen.SetContext(ctx);
        }

        private static void ToggleList()
        {
            if (listScreen)
            {
                listScreen.CloseScreen();
                listScreen = null;
                return;
            }
            if (!buttonScreen) return;

            listScreen = MetaScreen.GenerateWindow(new Vector2(ListWidth, ListHeight), root.transform,
                new Vector3(0f, -0.1f, -0.1f), true, true, false, BackgroundSetting.Old);
            listScreen.SetContext(BuildList());
        }

        private static IMetaContextOld BuildList()
        {
            var inner = new MetaContextOld();

            inner.Append(new MetaContextOld.Text(titleAttr)
            {
                RawText = ModTranslation.getString("enterCodeServerTitle"),
                Alignment = IMetaContextOld.AlignmentOption.Center
            });
            inner.Append(new MetaContextOld.VerticalMargin(0.15f));

            bool officialShown = false;
            foreach (IRegionInfo region in OrderedRegions())
            {
                bool official = IsOfficial(region);

                if (official && !officialShown)
                {
                    officialShown = true;
                    inner.Append(new MetaContextOld.VerticalMargin(0.12f));
                    inner.Append(new MetaContextOld.Text(titleAttr)
                    {
                        RawText = Helpers.cs(Color.gray, ModTranslation.getString("enterCodeServerOfficialGroup")),
                        Alignment = IMetaContextOld.AlignmentOption.Center
                    });
                }

                IRegionInfo captured = region;
                bool isCurrent = Servers.CurrentRegion != null && Servers.CurrentRegion.Name == region.Name;

                inner.Append(new MetaContextOld.Button(() => Pick(captured), rowAttr)
                {
                    RawText = official
                        ? RegionName(region) + "  " + ModTranslation.getString("enterCodeServerIncompatible")
                        : RegionName(region),
                    Alignment = IMetaContextOld.AlignmentOption.Center,
                    Color = official ? Color.gray : (isCurrent ? Color.white : new Color(0.85f, 0.85f, 0.85f))
                });
                inner.Append(new MetaContextOld.VerticalMargin(0.04f));
            }

            inner.Append(new MetaContextOld.VerticalMargin(0.1f));
            inner.Append(new MetaContextOld.Text(titleAttr)
            {
                RawText = Helpers.cs(Color.gray, ModTranslation.getString("enterCodeServerHint")),
                Alignment = IMetaContextOld.AlignmentOption.Center
            });

            return new MetaContextOld.ScrollView(new Vector2(ListWidth, ListHeight), inner, true)
            {
                Alignment = IMetaContextOld.AlignmentOption.Center
            };
        }

        private static void Pick(IRegionInfo region)
        {
            if (region != null) Servers.SetRegion(region);

            RefreshButton();

            if (listScreen)
            {
                listScreen.CloseScreen();
                listScreen = null;
            }
        }
    }
}
