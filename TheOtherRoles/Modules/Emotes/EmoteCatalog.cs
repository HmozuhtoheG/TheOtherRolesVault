using System.Collections.Generic;
using System.Linq;

namespace TheOtherRoles.Modules.Emotes
{
    public static class EmoteCatalog
    {
        public const int SlotsPerPage = 8;
        private const int IconCount = 8;

        private static readonly List<EmoteDefinition> icons = new();
        private static readonly List<EmoteDefinition> animated = new();
        private static readonly List<EmoteDefinition> received = new();
        private static readonly Dictionary<string, EmoteDefinition> byId = new();
        private static readonly List<List<EmoteDefinition>> pages = new();

        public static IReadOnlyList<List<EmoteDefinition>> Pages => pages;

        public static int PageCount => pages.Count;

        public static void Load()
        {
            EmoteImport.Scan();

            icons.Clear();
            animated.Clear();
            byId.Clear();

            for (int i = 0; i < IconCount; i++)
            {
                var sprite = Helpers.loadSpriteFromResources($"TheOtherRoles.Resources.expression{i + 1}.png", 100f);
                icons.Add(new IconEmote(i, sprite));
            }

            animated.Add(new HandEmote(HandEmote.HandKind.Good));
            animated.Add(new HandEmote(HandEmote.HandKind.Bad));
            animated.Add(new HandEmote(HandEmote.HandKind.Wave));
            animated.Add(new DanceEmote());

            foreach (var emote in icons.Concat(animated)) Register(emote);
            foreach (var emote in EmoteImport.Imported) Register(emote);
            foreach (var emote in received) Register(emote);

            RebuildPages();
        }

        public static void Reload()
        {
            Load();
        }

        public static void Register(EmoteDefinition emote)
        {
            if (emote == null || emote.Id == null) return;
            byId[emote.Id] = emote;
        }

        public static EmoteDefinition Get(string id)
        {
            if (id == null) return null;
            return byId.TryGetValue(id, out var emote) ? emote : null;
        }

        public static EmoteDefinition RegisterImported(string id, byte[] payload)
        {
            if (id == null || payload == null || payload.Length == 0) return null;

            var existing = Get(id);
            if (existing != null) return existing;

            var decoded = EmotePayload.Decode(payload);
            if (decoded == null) return null;

            var sprites = new List<UnityEngine.Sprite>();
            var delays = new List<float>();
            foreach (var frame in decoded.Frames)
            {
                var sprite = EmoteImport.MakeSprite(frame.Pixels, decoded.Width, decoded.Height);
                if (sprite == null) return null;
                sprites.Add(sprite);
                delays.Add(UnityEngine.Mathf.Max(0.02f, frame.DelayMs / 1000f));
            }

            var seconds = delays.Count > 0 ? delays.Average() : 0.1f;
            var emote = new ImportedEmote(id, sprites.ToArray(), seconds, payload, null, delays.ToArray());
            received.Add(emote);
            Register(emote);
            RebuildPages();
            TheOtherRolesPlugin.Logger.LogInfo($"[EmoteCatalog] received emote {id} ({decoded.Width}x{decoded.Height}, {decoded.Frames.Count} frame(s))");
            return emote;
        }

        public static void ClearReceived()
        {
            received.Clear();
            RebuildPages();
        }

        public static void RebuildPages()
        {
            pages.Clear();

            if (icons.Any(Enabled)) pages.Add(icons.Where(Enabled).ToList());
            if (animated.Any(Enabled)) pages.Add(animated.Where(Enabled).ToList());

            var imported = new List<EmoteDefinition>();
            imported.AddRange(EmoteImport.Imported);
            imported.AddRange(received);

            var seen = new HashSet<string>();
            var unique = new List<EmoteDefinition>();
            foreach (var emote in imported)
            {
                if (emote == null || !seen.Add(emote.Id) || !Enabled(emote)) continue;
                unique.Add(emote);
            }

            for (int i = 0; i < unique.Count; i += SlotsPerPage)
            {
                pages.Add(unique.Skip(i).Take(SlotsPerPage).ToList());
            }
        }

        private static bool Enabled(EmoteDefinition emote) => EmotePreference.IsEnabled(emote.Id);

        public static List<EmoteDefinition> AllEmotes()
        {
            var all = new List<EmoteDefinition>();
            var seen = new HashSet<string>();

            foreach (var emote in icons) if (emote != null && seen.Add(emote.Id)) all.Add(emote);
            foreach (var emote in animated) if (emote != null && seen.Add(emote.Id)) all.Add(emote);
            foreach (var emote in EmoteImport.Imported) if (emote != null && seen.Add(emote.Id)) all.Add(emote);
            foreach (var emote in received) if (emote != null && seen.Add(emote.Id)) all.Add(emote);

            return all;
        }
    }
}
