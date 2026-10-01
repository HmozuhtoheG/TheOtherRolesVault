using TheOtherRoles.MetaContext;
using TheOtherRoles.Utilities;
using UnityEngine;
using static TheOtherRoles.TheOtherRoles;

namespace TheOtherRoles.Modules.Emotes
{
    public static class EmoteHelpPage
    {
        private const int PerLine = 8;

        private static readonly TextAttribute CellAttr = new(TextAttribute.BoldAttr) { Size = new(0.46f, 0.46f) };
        private static readonly TextAttribute ButtonAttr = new(TextAttribute.BoldAttr) { Size = new(1.5f, 0.24f) };
        private static readonly TextAttribute TextAttr = new(TextAttribute.BoldAttr) { Size = new(6.6f, 0.22f), Alignment = TMPro.TextAlignmentOptions.Center };

        private static readonly Reference<MetaContextOld.ScrollView.InnerScreen> inner = new();

        public static IMetaContextOld Show()
        {
            return new MetaContextOld.ScrollView(new(7.4f, 4.1f), Build())
            {
                Alignment = IMetaContextOld.AlignmentOption.Center,
                ScrollerTag = "HelpEmotes",
                InnerRef = inner
            };
        }

        public static void Refresh()
        {
            if (!(inner?.Value?.IsValid ?? false)) return;
            inner.Value.SetContext(Build());
        }

        private static IMetaContextOld Build()
        {
            var context = new MetaContextOld();

            context.Append(new MetaContextOld.VariableText(TextAttr)
            {
                RawText = ModTranslation.getString("emoteToggleHint"),
                Alignment = IMetaContextOld.AlignmentOption.Center
            });
            context.Append(new MetaContextOld.VerticalMargin(0.12f));

#if WINDOWS
            context.Append(new MetaContextOld.Button(() =>
            {
                if (!EmoteFileDialog.IsBusy) EmoteFileDialog.Open();
            }, ButtonAttr)
            {
                TranslationKey = "emoteImportButton",
                Alignment = IMetaContextOld.AlignmentOption.Center
            });
            context.Append(new MetaContextOld.VerticalMargin(0.12f));
#endif

            context.Append(EmoteCatalog.AllEmotes(), Cell, PerLine, -1, 0, 0.58f);

            return context;
        }

        private static IMetaParallelPlacableOld Cell(EmoteDefinition emote)
        {
            return new MetaContextOld.Button(() =>
            {
                EmotePreference.Toggle(emote.Id);
                EmoteCatalog.RebuildPages();
                Refresh();
            }, CellAttr)
            {
                Text = new RawTextComponent(string.Empty),
                Alignment = IMetaContextOld.AlignmentOption.Center,
                PostBuilder = (_, renderer, _) => BuildIcon(renderer, emote)
            };
        }

        private static void BuildIcon(SpriteRenderer button, EmoteDefinition emote)
        {
            var sprite = emote.Icon;
            if (sprite == null) return;

            var icon = Helpers.CreateObject<SpriteRenderer>("EmoteIcon", button.transform, new Vector3(0f, 0f, -0.05f), LayerMask.NameToLayer("UI"));
            icon.sprite = sprite;
            icon.sortingOrder = 10;
            icon.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;

            float bounds = Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y);
            float scale = bounds > 0f ? CellAttr.Size.x * 0.8f / bounds : 1f;
            icon.transform.localScale = new Vector3(scale, scale, 1f);
            icon.color = EmotePreference.IsEnabled(emote.Id) ? Color.white : new Color(0.22f, 0.22f, 0.22f, 0.9f);
        }
    }
}
