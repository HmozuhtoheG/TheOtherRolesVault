using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TheOtherRoles.Utilities;
using UnityEngine;

namespace TheOtherRoles.Modules.Music
{
    public static class MusicPlayer
    {
        private const float MaxSeconds = 600f;
        private const float Volume = 0.6f;
        private const float ResyncTolerance = 1.5f;

        private const int ChunkFrames = 8192;
        private const int MaxQueuedChunks = 16;
        private const int SourceChunkSamples = 32768;

        private static AudioSource source;
        private static AudioClip clip;
        private static string title = string.Empty;

        private static int generation;
        private static float pendingSeek;
        private static string lastError;
        private static Job job;

        private static volatile bool decoding;

        public static bool IsWorking => decoding;
        public static bool IsPlaying => source != null && source.isPlaying;
        public static string CurrentTitle => title;
        public static string LastError => lastError;

        public static float PositionSeconds => source != null && source.isPlaying ? source.time : 0f;

        private sealed class Job
        {
            public int Generation;
            public int Channels;
            public int Rate;
            public int TotalFrames;
            public int WrittenFrames;
            public bool Started;
            public volatile bool Cancelled;
            public volatile bool Done;
            public volatile string Error;
            public readonly ConcurrentQueue<float[]> Chunks = new();
            public readonly SemaphoreSlim Slots = new(MaxQueuedChunks);
        }

        public static void Play(byte[] mp3, string songTitle, float startAt)
        {
            Stop();

            if (mp3 == null || mp3.Length == 0) return;

            title = songTitle ?? string.Empty;
            pendingSeek = startAt < 0f ? 0f : startAt;
            lastError = null;

            if (!TryReadHeader(mp3, out var channels, out var rate, out var seconds))
            {
                lastError = "bad mp3 header";
                TheOtherRolesPlugin.Logger.LogWarning("[MusicPlayer] bad mp3 header");
                return;
            }

            if (FastDestroyableSingleton<HudManager>.Instance == null) return;

            int totalFrames = (int)Math.Min(MaxSeconds * rate, Math.Max(1.0, seconds * rate * 1.05 + rate));

            try
            {
                clip = AudioClip.Create("TORVMusic", totalFrames, channels, rate, false);
            }
            catch (Exception ex)
            {
                clip = null;
                lastError = ex.Message;
                TheOtherRolesPlugin.Logger.LogWarning($"[MusicPlayer] clip create failed: {ex.Message}");
                return;
            }

            var current = new Job
            {
                Generation = ++generation,
                Channels = channels,
                Rate = rate,
                TotalFrames = totalFrames
            };

            job = current;
            decoding = true;

            TheOtherRolesPlugin.Logger.LogInfo(
                $"[MusicPlayer] {rate}Hz {channels}ch, ~{seconds:0}s -> clip {totalFrames / (float)rate:0}s ({totalFrames * channels * 4L / 1048576} MB)");

            var bytes = mp3;
            Task.Run(() => DecodeLoop(bytes, current));
        }

        public static void Stop()
        {
            generation++;
            decoding = false;
            lastError = null;
            title = string.Empty;

            var current = job;
            job = null;
            if (current != null) current.Cancelled = true;

            if (source != null)
            {
                source.Stop();
                source.clip = null;
            }

            if (clip != null)
            {
                UnityEngine.Object.Destroy(clip);
                clip = null;
            }

            if (current != null)
            {
                current.Chunks.Clear();
                GC.Collect();
            }
        }

        public static void SeekTo(float seconds)
        {
            if (source == null || clip == null || !source.isPlaying) return;

            float length = clip.length;
            if (length <= 0.5f) return;

            float target = seconds % length;
            if (target < 0f) target += length;

            float raw = source.time - target;
            if (raw < 0f) raw = -raw;
            float wrapped = length - raw;
            float distance = raw < wrapped ? raw : wrapped;

            if (distance < ResyncTolerance) return;
            source.time = target;
        }

        public static void Tick()
        {
            var current = job;
            if (current == null) return;

            while (current.Chunks.TryDequeue(out var chunk))
            {
                current.Slots.Release();

                if (clip == null || current.WrittenFrames >= current.TotalFrames) continue;

                int frames = chunk.Length / current.Channels;
                if (frames <= 0) continue;
                if (current.WrittenFrames + frames > current.TotalFrames) frames = current.TotalFrames - current.WrittenFrames;
                if (frames <= 0) continue;

                try
                {
                    clip.SetData(chunk, current.WrittenFrames);
                    current.WrittenFrames += frames;
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                    TheOtherRolesPlugin.Logger.LogWarning($"[MusicPlayer] SetData failed: {ex.Message}");
                }
            }

            if (current.Started || !current.Done) return;

            current.Started = true;
            decoding = false;

            if (current.WrittenFrames <= 0)
            {
                lastError = current.Error ?? "no audio";
                TheOtherRolesPlugin.Logger.LogWarning($"[MusicPlayer] decode failed: {lastError}");
                return;
            }

            if (current.Error != null) TheOtherRolesPlugin.Logger.LogWarning($"[MusicPlayer] decode stopped: {current.Error}");

            var hud = FastDestroyableSingleton<HudManager>.Instance;
            if (hud == null || clip == null) return;

            if (source == null)
            {
                source = Helpers.CreateObject<AudioSource>("TORVMusicSource", hud.transform, Vector3.zero);
                source.playOnAwake = false;
                source.loop = true;
                source.spatialBlend = 0f;
                source.volume = Volume;
                if (SoundManager.Instance != null && SoundManager.Instance.MusicChannel != null)
                    source.outputAudioMixerGroup = SoundManager.Instance.MusicChannel;
            }

            source.clip = clip;
            source.Play();

            if (pendingSeek > 0.2f && pendingSeek < clip.length - 0.5f) source.time = pendingSeek;

            TheOtherRolesPlugin.Logger.LogInfo($"[MusicPlayer] playing {current.WrittenFrames / (float)current.Rate:0.0}s");
        }

        private static bool TryReadHeader(byte[] mp3, out int channels, out int rate, out double seconds)
        {
            channels = 0;
            rate = 0;
            seconds = 0;

            try
            {
                using var stream = new MemoryStream(mp3);
                using var mpeg = new NLayer.MpegFile(stream);
                channels = mpeg.Channels;
                rate = mpeg.SampleRate;
                seconds = mpeg.Duration.TotalSeconds;
                return channels > 0 && rate > 0;
            }
            catch (Exception ex)
            {
                TheOtherRolesPlugin.Logger.LogWarning($"[MusicPlayer] header: {ex.Message}");
                return false;
            }
        }

        private static void DecodeLoop(byte[] mp3, Job current)
        {
            try
            {
                using var stream = new MemoryStream(mp3);
                using var mpeg = new NLayer.MpegFile(stream);

                int channels = current.Channels;
                int scratchLength = SourceChunkSamples - SourceChunkSamples % channels;
                if (scratchLength <= 0) scratchLength = channels;

                var scratch = new float[scratchLength];
                var chunk = new float[ChunkFrames * channels];
                int filled = 0;

                int read;
                while (!current.Cancelled && (read = mpeg.ReadSamples(scratch, 0, scratchLength)) > 0)
                {
                    int index = 0;
                    while (index < read)
                    {
                        int take = Math.Min(chunk.Length - filled, read - index);
                        Array.Copy(scratch, index, chunk, filled, take);
                        filled += take;
                        index += take;

                        if (filled < chunk.Length) continue;
                        if (!Enqueue(current, chunk)) return;
                        chunk = new float[chunk.Length];
                        filled = 0;
                    }
                }

                if (filled > 0 && !current.Cancelled)
                {
                    var tail = new float[filled];
                    Array.Copy(chunk, tail, filled);
                    Enqueue(current, tail);
                }
            }
            catch (Exception ex)
            {
                current.Error = ex.Message;
            }
            finally
            {
                current.Done = true;
            }
        }

        private static bool Enqueue(Job current, float[] chunk)
        {
            while (!current.Cancelled)
            {
                if (current.Slots.Wait(100))
                {
                    current.Chunks.Enqueue(chunk);
                    return true;
                }
            }
            return false;
        }
    }
}
