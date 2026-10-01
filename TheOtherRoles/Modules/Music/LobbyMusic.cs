using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using TheOtherRoles.MetaContext;
using TheOtherRoles.Patches;
using TheOtherRoles.Utilities;
using UnityEngine;

namespace TheOtherRoles.Modules.Music
{
    [TORRPCHolder]
    public static class LobbyMusic
    {
        private const float ResyncInterval = 20f;

        public static RemoteProcess<(byte hostId, string songId, string title, float position)> PlaySong =
            new("LobbyMusicPlay", (message, _) =>
            {
                var host = Helpers.playerById(message.hostId);
                if (host == null) return;
                if (!Enabled) return;

                if (message.songId == currentSongId)
                {
                    MusicPlayer.SeekTo(message.position);
                    return;
                }

                currentSongId = message.songId;
                currentTitle = message.title ?? string.Empty;
                Notify(string.Format(ModTranslation.getString("musicNowPlaying"), currentTitle));
                FetchAndPlay(message.songId, currentTitle, message.position);
            });

        private static int lastPlayerCount = -1;
        private static float lastResyncTime;

        public static void HostBroadcast()
        {
            if (!InLobby || !Enabled) return;
            if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
            if (string.IsNullOrEmpty(currentSongId)) return;

            int count = GameData.Instance != null ? GameData.Instance.PlayerCount : -1;
            bool joined = count > 0 && count != lastPlayerCount;
            lastPlayerCount = count;

            bool due = Time.unscaledTime - lastResyncTime >= ResyncInterval;
            if (!joined && !due) return;

            lastResyncTime = Time.unscaledTime;
            PlaySong.Invoke((PlayerControl.LocalPlayer.PlayerId, currentSongId, currentTitle, MusicPlayer.PositionSeconds));
        }

        public static RemoteProcess<byte> StopSong = RemotePrimitiveProcess.OfByte("LobbyMusicStop", (message, _) =>
        {
            currentSongId = null;
            currentTitle = string.Empty;
            MusicPlayer.Stop();
        });

        public static RemoteProcess<(byte hostId, string api)> SyncApi = new("LobbyMusicSyncApi", (message, _) =>
        {
            NeteaseClient.SetApiBase(message.api);
            MusicPreference.Save(NeteaseClient.ApiBase);
            Notify(string.Format(ModTranslation.getString("musicApiSynced"), NeteaseClient.ApiBase));
        });

        private static string currentSongId;
        private static string currentTitle = string.Empty;
        private static bool fetching;

        public static bool InLobby => LobbyBehaviour.Instance != null && ShipStatus.Instance == null;

        public static bool Enabled => InLobby && ClientOption.GetValue(ClientOption.ClientOptionType.HostMusic) == 1;

        public static void Notify(string text)
        {
            try
            {
                if (string.IsNullOrEmpty(text)) return;
                if (PlayerControl.LocalPlayer == null || PlayerControl.LocalPlayer.Data == null) return;

                var chat = FastDestroyableSingleton<HudManager>.Instance?.Chat;
                if (chat == null) return;

                chat.AddChat(PlayerControl.LocalPlayer, text);
            }
            catch (Exception ex)
            {
                TheOtherRolesPlugin.Logger.LogWarning($"[LobbyMusic] notify failed: {ex.Message}");
            }
        }

        public static void HostPlay(string songId, string title)
        {
            if (!AmongUsClient.Instance.AmHost) return;
            if (!Enabled) return;

            currentSongId = songId;
            currentTitle = title ?? string.Empty;
            lastResyncTime = Time.unscaledTime;
            FetchAndPlay(songId, title, 0f);
            PlaySong.Invoke((PlayerControl.LocalPlayer.PlayerId, songId, currentTitle, 0f));
        }

        public static void HostStop()
        {
            if (!AmongUsClient.Instance.AmHost) return;
            currentSongId = null;
            currentTitle = string.Empty;
            MusicPlayer.Stop();
            StopSong.Invoke(PlayerControl.LocalPlayer.PlayerId);
        }

        public static void Search(string keyword)
        {
            if (!InLobby) return;
            if (searching) return;

            var host = FastDestroyableSingleton<HudManager>.Instance;
            if (host == null) return;

            searching = true;
            host.StartCoroutine(CoSearch(keyword).WrapToIl2Cpp());
        }

        private static bool searching;

        private static IEnumerator CoSearch(string keyword)
        {
            var results = new List<NeteaseClient.Song>();
            string error = null;

            yield return NeteaseClient.CoSearch(keyword, songs => results = songs, err => error = err);

            searching = false;

            if (error != null)
            {
                Notify(error);
                yield break;
            }

            lastResults = results;
            Notify(string.Format(ModTranslation.getString("musicSearchResult"), keyword));
            string vipTag = "<color=#FF4D4D>" + ModTranslation.getString("musicVipRequired") + "</color>";
            for (int i = 0; i < results.Count; i++)
            {
                var song = results[i];
                Notify($"<color=#FFC864>[{i + 1}]</color> {song.Display}{(song.RequiresVip ? " " + vipTag : "")}");
            }
            Notify(ModTranslation.getString("musicPickHint"));
        }

        private static List<NeteaseClient.Song> lastResults = new();

        public static List<NeteaseClient.Song> LastResults => lastResults;

        private static void FetchAndPlay(string songId, string title, float elapsed)
        {
            if (fetching) return;
            var host = FastDestroyableSingleton<HudManager>.Instance;
            if (host == null) return;

            fetching = true;
            host.StartCoroutine(CoFetchAndPlay(songId, title, elapsed).WrapToIl2Cpp());
        }

        private static IEnumerator CoFetchAndPlay(string songId, string title, float elapsed)
        {
            string url = null;
            string error = null;
            yield return NeteaseClient.CoGetSongUrl(songId, value => url = value, err => error = err);

            if (url == null)
            {
                fetching = false;
                Notify(error ?? ModTranslation.getString("musicNoUrl"));
                yield break;
            }

            byte[] audio = null;
            yield return NeteaseClient.CoDownloadAudio(url, data => audio = data, err => error = err);

            fetching = false;

            if (audio == null || audio.Length == 0)
            {
                Notify(error ?? ModTranslation.getString("musicDownloadFailed"));
                yield break;
            }

            MusicPlayer.Play(audio, title, elapsed);
            Notify(string.Format(ModTranslation.getString("musicLoading"), title));
        }

        public static void StopIfDisabled()
        {
            if (Enabled) return;
            if (!MusicPlayer.IsPlaying && !MusicPlayer.IsWorking) return;
            currentSongId = null;
            MusicPlayer.Stop();
        }

        public static void Reset()
        {

            currentSongId = null;
            currentTitle = string.Empty;
            searching = false;
            fetching = false;
            lastPlayerCount = -1;
            lastResyncTime = 0f;
            lastResults = new List<NeteaseClient.Song>();
            MusicPlayer.Stop();
        }
    }

    [HarmonyPatch]
    public static class LobbyMusicPatch
    {
        [HarmonyPatch(typeof(TORGUIManager), nameof(TORGUIManager.Update))]
        [HarmonyPostfix]
        public static void UpdatePostfix()
        {
            LobbyMusic.StopIfDisabled();
            MusicPlayer.Tick();
            LobbyMusic.HostBroadcast();
        }

        [HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.Start))]
        [HarmonyPostfix]
        public static void ShipStatusStartPostfix()
        {
            LobbyMusic.Reset();
        }

        [HarmonyPatch(typeof(LobbyBehaviour), nameof(LobbyBehaviour.Start))]
        [HarmonyPostfix]
        public static void LobbyStartPostfix()
        {
            LobbyMusic.Reset();
        }

        [HarmonyPatch(typeof(LobbyBehaviour), nameof(LobbyBehaviour.OnDestroy))]
        [HarmonyPostfix]
        public static void LobbyDestroyPostfix()
        {
            LobbyMusic.Reset();
        }
    }
}
