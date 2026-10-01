using System;
using System.Collections.Generic;

namespace TheOtherRoles.Modules.Emotes
{
    public static class EmotePreference
    {
        private static readonly DataSaver saver = new("EmoteOption");
        private static readonly StringDataEntry entry = new("disabled", saver, string.Empty);
        private static readonly HashSet<string> disabled = new();

        static EmotePreference()
        {
            foreach (var id in entry.Value.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                disabled.Add(id);
            }
        }

        public static bool IsEnabled(string id) => id != null && !disabled.Contains(id);

        public static void SetEnabled(string id, bool enabled)
        {
            if (id == null) return;
            if (enabled) disabled.Remove(id);
            else if (!disabled.Add(id)) return;
            entry.Value = string.Join(",", disabled);
        }

        public static void Toggle(string id) => SetEnabled(id, !IsEnabled(id));

        public static bool IsDisabledAny => disabled.Count > 0;
    }
}
