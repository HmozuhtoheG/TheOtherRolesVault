using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using TheOtherRoles.Patches;
using UnityEngine;

namespace TheOtherRoles.Modules
{
    public static class JusticeAudio
    {
        private class Decoded
        {
            public float[] Data;
            public int Channels;
            public int Rate;
        }

        private static Task<Decoded> introTask;
        private static Task<Decoded> meetingTask;
        private static AudioClip introClip;
        private static AudioClip meetingClip;

        public static void Preload()
        {
            introTask ??= Task.Run(() => Decode("Justice1.wav"));
            meetingTask ??= Task.Run(() => Decode("Justice2.wav"));
        }

        public static void PlayIntro() => Play(ref introClip, introTask, "Justice1");

        public static void PlayMeeting() => Play(ref meetingClip, meetingTask, "Justice2");

        private static void Play(ref AudioClip clip, Task<Decoded> task, string name)
        {
            if (clip == null) clip = Build(task, name);
            if (clip == null) return;
            if (ClientOption.GetValue(ClientOption.ClientOptionType.EnableSoundEffects) == 0) return;
            if (!Constants.ShouldPlaySfx()) return;
            if (SoundManager.Instance == null) return;

            try
            {
                SoundManager.Instance.PlaySound(clip, false, 0.8f);
            }
            catch (Exception ex)
            {
                TheOtherRolesPlugin.Logger.LogWarning($"[JusticeAudio] play {name}: {ex.Message}");
            }
        }

        private static AudioClip Build(Task<Decoded> task, string name)
        {
            if (task == null || !task.IsCompletedSuccessfully) return null;

            var decoded = task.Result;
            if (decoded == null || decoded.Data == null || decoded.Data.Length == 0) return null;
            if (decoded.Channels <= 0 || decoded.Rate <= 0) return null;

            try
            {
                var clip = AudioClip.Create("TORV" + name, decoded.Data.Length / decoded.Channels,
                    decoded.Channels, decoded.Rate, false);
                clip.SetData(decoded.Data, 0);
                return clip;
            }
            catch (Exception ex)
            {
                TheOtherRolesPlugin.Logger.LogWarning($"[JusticeAudio] clip {name}: {ex.Message}");
                return null;
            }
        }

        private static Decoded Decode(string file)
        {
            try
            {
                using var stream = Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream("TheOtherRoles.Resources." + file);
                if (stream == null)
                {
                    TheOtherRolesPlugin.Logger.LogWarning("[JusticeAudio] missing resource " + file);
                    return null;
                }

                var bytes = new byte[stream.Length];
                stream.Read(bytes, 0, bytes.Length);

                if (bytes.Length < 44 || bytes[0] != 'R' || bytes[1] != 'I' || bytes[2] != 'F' || bytes[3] != 'F' ||
                    bytes[8] != 'W' || bytes[9] != 'A' || bytes[10] != 'V' || bytes[11] != 'E')
                {
                    TheOtherRolesPlugin.Logger.LogWarning("[JusticeAudio] " + file + " is not a RIFF WAVE");
                    return null;
                }

                int channels = 0;
                int rate = 0;
                int bits = 0;
                int dataOffset = -1;
                int dataLength = 0;

                int position = 12;
                while (position + 8 <= bytes.Length)
                {
                    string id = System.Text.Encoding.ASCII.GetString(bytes, position, 4);
                    int size = BitConverter.ToInt32(bytes, position + 4);
                    int body = position + 8;

                    if (id == "fmt " && size >= 16)
                    {
                        channels = BitConverter.ToInt16(bytes, body + 2);
                        rate = BitConverter.ToInt32(bytes, body + 4);
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

                if (dataOffset < 0 || bits != 16 || channels < 1 || rate < 1)
                {
                    TheOtherRolesPlugin.Logger.LogWarning(
                        $"[JusticeAudio] {file} unsupported: bits={bits} channels={channels} rate={rate}");
                    return null;
                }

                int sampleCount = dataLength / 2;
                sampleCount -= sampleCount % channels;

                var samples = new float[sampleCount];
                for (int i = 0; i < sampleCount; i++)
                    samples[i] = BitConverter.ToInt16(bytes, dataOffset + i * 2) / 32768f;

                TheOtherRolesPlugin.Logger.LogInfo(
                    $"[JusticeAudio] decoded {file} {sampleCount / channels} frames {rate}Hz {channels}ch");

                return new Decoded { Data = samples, Channels = channels, Rate = rate };
            }
            catch (Exception ex)
            {
                TheOtherRolesPlugin.Logger.LogWarning($"[JusticeAudio] decode {file}: {ex.Message}");
                return null;
            }
        }
    }
}
