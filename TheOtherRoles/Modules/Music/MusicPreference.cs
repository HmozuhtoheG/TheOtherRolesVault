namespace TheOtherRoles.Modules.Music
{
    public static class MusicPreference
    {
        private static readonly DataSaver saver = new("MusicOption");
        private static readonly StringDataEntry entry = new("api", saver, string.Empty);

        public static string Saved => entry.Value;

        public static void Save(string url)
        {
            var value = url ?? string.Empty;
            if (entry.Value == value) return;
            entry.Value = value;
        }
    }
}
