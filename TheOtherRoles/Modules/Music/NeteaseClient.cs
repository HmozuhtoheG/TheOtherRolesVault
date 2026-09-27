using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.Json;

namespace TheOtherRoles.Modules.Music
{
    public static class NeteaseClient
    {
        private const string Host = "https://music.163.com";

        public class Song
        {
            public string Id;
            public string Name;
            public string Artist;

            public string Display => Artist.Length > 0 ? Name + " - " + Artist : Name;
        }

        public static string ApiBase { get; private set; } = string.Empty;

        public static bool HasApi => ApiBase.Length > 0;

        public static void SetApiBase(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                ApiBase = string.Empty;
                return;
            }

            var trimmed = url.Trim();
            if (trimmed == "off" || trimmed == "direct") { ApiBase = string.Empty; return; }
            if (!trimmed.StartsWith("http://") && !trimmed.StartsWith("https://")) trimmed = "http://" + trimmed;
            ApiBase = trimmed.TrimEnd('/');
        }

        public static IEnumerator CoSearch(string keyword, Action<List<Song>> onDone, Action<string> onError)
        {
            string url = HasApi
                ? ApiBase + "/search?keywords=" + Uri.EscapeDataString(keyword) + "&limit=8"
                : Host + "/api/search/get?s=" + Uri.EscapeDataString(keyword) + "&type=1&limit=8&offset=0";

            string text = null;
            string error = null;
            yield return Http.CoGetString(url, (body, err) => { text = body; error = err; });

            if (error != null)
            {
                TheOtherRolesPlugin.Logger.LogWarning($"[Netease] search failed: {error}");
                onError?.Invoke(error);
                yield break;
            }

            if (string.IsNullOrEmpty(text))
            {
                onError?.Invoke(ModTranslation.getString("musicNoResult"));
                yield break;
            }

            var songs = new List<Song>();
            try
            {
                using var document = JsonDocument.Parse(text);
                if (document.RootElement.TryGetProperty("result", out var result)
                    && result.TryGetProperty("songs", out var array)
                    && array.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in array.EnumerateArray())
                    {
                        var song = ReadSong(item);
                        if (song != null) songs.Add(song);
                    }
                }
            }
            catch (Exception ex)
            {
                TheOtherRolesPlugin.Logger.LogWarning($"[Netease] search parse: {ex.Message}");
                onError?.Invoke(ex.Message);
                yield break;
            }

            if (songs.Count == 0) onError?.Invoke(ModTranslation.getString("musicNoResult"));
            else onDone?.Invoke(songs);
        }

        private static Song ReadSong(JsonElement item)
        {
            var song = new Song
            {
                Id = item.TryGetProperty("id", out var id) ? id.ToString() : null,
                Name = item.TryGetProperty("name", out var name) ? name.GetString() : "?"
            };

            var artists = new List<string>();
            foreach (var key in new[] { "ar", "artists" })
            {
                if (!item.TryGetProperty(key, out var array) || array.ValueKind != JsonValueKind.Array) continue;
                foreach (var artist in array.EnumerateArray())
                {
                    if (artist.TryGetProperty("name", out var artistName)) artists.Add(artistName.GetString());
                }
                break;
            }
            song.Artist = string.Join(", ", artists);

            return string.IsNullOrEmpty(song.Id) ? null : song;
        }

        private static readonly int[] Bitrates = { 999000, 320000, 192000, 128000 };

        public static IEnumerator CoGetSongUrl(string songId, Action<string> onDone, Action<string> onError)
        {
            if (HasApi)
            {
                string text = null;
                string error = null;
                yield return Http.CoGetString(ApiBase + "/song/url/v1?id=" + songId + "&level=exhigh", (body, err) => { text = body; error = err; });

                if (error == null)
                {
                    var external = ExtractUrl(text, out _);
                    if (external != null)
                    {
                        TheOtherRolesPlugin.Logger.LogInfo($"[Netease] song url resolved for {songId}");
                        onDone?.Invoke(external);
                        yield break;
                    }
                }

                TheOtherRolesPlugin.Logger.LogWarning($"[Netease] no playable url for {songId} via {ApiBase}");
                onError?.Invoke(error ?? ModTranslation.getString("musicNoUrl"));
                yield break;
            }

            string lastBody = null;
            foreach (var bitrate in Bitrates)
            {
                string text = null;
                string error = null;
                yield return Http.CoGetString(
                    Host + "/api/song/enhance/player/url?ids=[" + songId + "]&br=" + bitrate,
                    (body, err) => { text = body; error = err; });

                if (error != null)
                {
                    TheOtherRolesPlugin.Logger.LogWarning($"[Netease] song url failed (br={bitrate}): {error}");
                    continue;
                }

                lastBody = text;
                var playUrl = ExtractUrl(text, out var actualBitrate);
                if (playUrl != null)
                {
                    TheOtherRolesPlugin.Logger.LogInfo($"[Netease] song url resolved for {songId} (asked {bitrate}, got {actualBitrate})");
                    onDone?.Invoke(playUrl);
                    yield break;
                }
            }

            TheOtherRolesPlugin.Logger.LogWarning($"[Netease] no playable url for {songId}: {(lastBody == null ? "<null>" : lastBody.Substring(0, Math.Min(200, lastBody.Length)))}");
            onError?.Invoke(ModTranslation.getString("musicNoUrl"));
        }

        private static string ExtractUrl(string text, out int bitrate)
        {
            bitrate = 0;
            if (string.IsNullOrEmpty(text)) return null;

            try
            {
                using var document = JsonDocument.Parse(text);
                if (!document.RootElement.TryGetProperty("data", out var data)) return null;

                JsonElement entry = data.ValueKind == JsonValueKind.Array
                    ? (data.GetArrayLength() > 0 ? data[0] : default)
                    : data;

                if (entry.ValueKind != JsonValueKind.Object) return null;

                if (entry.TryGetProperty("br", out var brElement) && brElement.TryGetInt32(out var parsed)) bitrate = parsed;

                if (entry.TryGetProperty("url", out var urlElement) && urlElement.ValueKind == JsonValueKind.String)
                {
                    var value = urlElement.GetString();
                    return string.IsNullOrEmpty(value) ? null : value;
                }
            }
            catch (Exception ex)
            {
                TheOtherRolesPlugin.Logger.LogWarning($"[Netease] url parse: {ex.Message}");
            }
            return null;
        }

        public static IEnumerator CoDownloadAudio(string url, Action<byte[]> onDone, Action<string> onError)
        {
            byte[] data = null;
            string error = null;
            yield return Http.CoGetBytes(url, (body, err) => { data = body; error = err; });

            if (error != null)
            {
                TheOtherRolesPlugin.Logger.LogWarning($"[Netease] audio download failed: {error}");
                onError?.Invoke(error);
                yield break;
            }

            if (data == null || data.Length == 0)
            {
                onError?.Invoke(ModTranslation.getString("musicDownloadFailed"));
                yield break;
            }

            TheOtherRolesPlugin.Logger.LogInfo($"[Netease] downloaded {data.Length / 1024} KB");
            onDone?.Invoke(data);
        }
    }
}
