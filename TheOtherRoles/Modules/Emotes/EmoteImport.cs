using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace TheOtherRoles.Modules.Emotes
{
    public class ImportedEmote : BubbleEmote
    {
        public override string Id { get; }

        public override Sprite Icon => Frames != null && Frames.Length > 0 ? Frames[0] : null;

        public override Sprite[] Frames { get; }

        public override float FrameSeconds { get; }

        public override float[] FrameDelays { get; }

        public override byte[] Payload { get; }

        public string FileName { get; }

        public ImportedEmote(string id, Sprite[] frames, float frameSeconds, byte[] payload, string fileName, float[] frameDelays = null)
        {
            Id = id;
            Frames = frames;
            FrameSeconds = frameSeconds;
            FrameDelays = frameDelays;
            Payload = payload;
            FileName = fileName;
        }
    }

    public static class EmoteImport
    {
        private const string FolderName = "TheOtherEmotes";
        private const int MaxCount = 48;
        private const int StaticMaxSide = 128;
        private const int AnimatedMaxSide = 64;
        private const int MaxInputFrames = 32;
        private const int MinSide = 48;

        private static readonly List<ImportedEmote> imported = new();

        public static IReadOnlyList<ImportedEmote> Imported => imported;

        public static string Directory
        {
            get
            {
#if WINDOWS
                return Path.Combine(Path.GetDirectoryName(Application.dataPath)!, FolderName);
#else
                return Path.Combine(Application.persistentDataPath, FolderName);
#endif
            }
        }

        public static void Scan()
        {
            imported.Clear();
            try
            {
                if (!System.IO.Directory.Exists(Directory)) System.IO.Directory.CreateDirectory(Directory);

                var files = new List<string>();
                foreach (var pattern in new[] { "*.png", "*.jpg", "*.jpeg", "*.gif" })
                {
                    files.AddRange(System.IO.Directory.GetFiles(Directory, pattern));
                }
                files.Sort(StringComparer.OrdinalIgnoreCase);

                var seen = new HashSet<string>();
                foreach (var file in files)
                {
                    if (imported.Count >= MaxCount) break;
                    var emote = TryLoad(file);
                    if (emote == null) continue;
                    if (!seen.Add(emote.Id)) continue;
                    imported.Add(emote);
                }
            }
            catch (Exception ex)
            {
                TheOtherRolesPlugin.Logger.LogWarning($"[EmoteImport] scan failed: {ex.Message}");
            }

            TheOtherRolesPlugin.Logger.LogInfo($"[EmoteImport] {imported.Count} emote(s) loaded from {Directory}");
        }

        private static ImportedEmote TryLoad(string file)
        {
            try
            {
                var extension = Path.GetExtension(file).ToLowerInvariant();
                int width;
                int height;
                List<Color32[]> frames;
                List<ushort> delays;
                bool animated;

                if (extension == ".gif")
                {
                    var decoded = GifDecoder.Decode(System.IO.File.ReadAllBytes(file));
                    if (decoded == null || decoded.Frames.Count == 0) return null;
                    width = decoded.Width;
                    height = decoded.Height;
                    frames = decoded.Frames;
                    delays = decoded.Delays;
                    animated = frames.Count > 1;
                }
                else
                {
                    var texture = Helpers.loadTextureFromDisk(file);
                    if (texture == null) return null;
                    width = texture.width;
                    height = texture.height;
                    frames = new List<Color32[]> { texture.GetPixels32() };
                    delays = new List<ushort> { 100 };
                    animated = false;
                }

                if (width <= 0 || height <= 0) return null;

                var built = Build(frames, delays, width, height, animated, out var payload, out var outFrames, out var outDelays);
                if (!built) return null;

                var id = "l." + Hash(payload);
                var seconds = Mathf.Max(0.02f, outDelays.Average() / 1000f);
                return new ImportedEmote(id, outFrames, seconds, payload, Path.GetFileName(file), outDelays);
            }
            catch (Exception ex)
            {
                TheOtherRolesPlugin.Logger.LogWarning($"[EmoteImport] {Path.GetFileName(file)} failed: {ex.Message}");
                return null;
            }
        }

        private static bool Build(List<Color32[]> frames, List<ushort> delays, int width, int height, bool animated,
            out byte[] payload, out Sprite[] sprites, out float[] frameDelays)
        {
            payload = null;
            sprites = null;
            frameDelays = null;

            int side = animated ? AnimatedMaxSide : StaticMaxSide;
            int stride = Mathf.Max(1, Mathf.CeilToInt(frames.Count / (float)MaxInputFrames));

            for (int attempt = 0; attempt < 24; attempt++)
            {
                var usedPixels = new List<Color32[]>();
                var usedDelays = new List<ushort>();
                for (int i = 0; i < frames.Count; i += stride)
                {
                    usedPixels.Add(frames[i]);
                    usedDelays.Add(i < delays.Count ? delays[i] : (ushort)100);
                }
                if (usedPixels.Count == 0) return false;

                var (outWidth, outHeight) = Fit(width, height, side);
                var resized = usedPixels.Select(pixels => Resize(pixels, width, height, outWidth, outHeight)).ToList();

                var encoded = new List<EmotePayload.Frame>();
                for (int i = 0; i < resized.Count; i++)
                {
                    encoded.Add(new EmotePayload.Frame { Pixels = resized[i], DelayMs = usedDelays[i] });
                }

                payload = EmotePayload.Encode(outWidth, outHeight, encoded);
                if (payload.Length <= EmotePayload.MaxBytes)
                {
                    sprites = resized.Select(pixels => MakeSprite(pixels, outWidth, outHeight)).ToArray();
                    if (!sprites.All(sprite => sprite != null)) return false;

                    frameDelays = usedDelays.Select(delay => Mathf.Max(0.02f, delay / 1000f)).ToArray();
                    return true;
                }

                if (side > MinSide) side = Mathf.Max(MinSide, side * 3 / 4);
                else if (stride * 2 < frames.Count) stride *= 2;
                else if (side > 16) side = side * 3 / 4;
                else return false;
            }

            return false;
        }

        private static (int, int) Fit(int width, int height, int maxSide)
        {
            if (width <= maxSide && height <= maxSide) return (width, height);
            float scale = Mathf.Min((float)maxSide / width, (float)maxSide / height);
            return (Mathf.Max(1, Mathf.RoundToInt(width * scale)), Mathf.Max(1, Mathf.RoundToInt(height * scale)));
        }

        private static Color32[] Resize(Color32[] source, int sourceWidth, int sourceHeight, int width, int height)
        {
            if (sourceWidth == width && sourceHeight == height) return source;

            var result = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                float fy = (y + 0.5f) * sourceHeight / height - 0.5f;
                int y0 = Mathf.Clamp(Mathf.FloorToInt(fy), 0, sourceHeight - 1);
                int y1 = Mathf.Clamp(y0 + 1, 0, sourceHeight - 1);
                float ty = Mathf.Clamp01(fy - y0);

                for (int x = 0; x < width; x++)
                {
                    float fx = (x + 0.5f) * sourceWidth / width - 0.5f;
                    int x0 = Mathf.Clamp(Mathf.FloorToInt(fx), 0, sourceWidth - 1);
                    int x1 = Mathf.Clamp(x0 + 1, 0, sourceWidth - 1);
                    float tx = Mathf.Clamp01(fx - x0);

                    var p00 = source[y0 * sourceWidth + x0];
                    var p10 = source[y0 * sourceWidth + x1];
                    var p01 = source[y1 * sourceWidth + x0];
                    var p11 = source[y1 * sourceWidth + x1];

                    float w00 = (1f - tx) * (1f - ty);
                    float w10 = tx * (1f - ty);
                    float w01 = (1f - tx) * ty;
                    float w11 = tx * ty;

                    float alpha = p00.a * w00 + p10.a * w10 + p01.a * w01 + p11.a * w11;
                    if (alpha <= 0.5f)
                    {
                        result[y * width + x] = new Color32(0, 0, 0, 0);
                        continue;
                    }

                    float r = (p00.r * p00.a * w00 + p10.r * p10.a * w10 + p01.r * p01.a * w01 + p11.r * p11.a * w11) / alpha;
                    float g = (p00.g * p00.a * w00 + p10.g * p10.a * w10 + p01.g * p01.a * w01 + p11.g * p11.a * w11) / alpha;
                    float b = (p00.b * p00.a * w00 + p10.b * p10.a * w10 + p01.b * p01.a * w01 + p11.b * p11.a * w11) / alpha;

                    result[y * width + x] = new Color32(
                        (byte)Mathf.Clamp(Mathf.RoundToInt(r), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(g), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(b), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(alpha), 0, 255));
                }
            }

            return result;
        }

        public static Sprite MakeSprite(Color32[] pixels, int width, int height)
        {
            try
            {
                var texture = new Texture2D(width, height, TextureFormat.ARGB32, false);
                texture.SetPixels32(pixels);
                texture.Apply();
                texture.hideFlags |= HideFlags.HideAndDontSave | HideFlags.DontSaveInEditor;
                var sprite = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f);
                sprite.hideFlags |= HideFlags.HideAndDontSave | HideFlags.DontSaveInEditor;
                return sprite;
            }
            catch (Exception ex)
            {
                TheOtherRolesPlugin.Logger.LogWarning($"[EmoteImport] sprite build failed: {ex.Message}");
                return null;
            }
        }

        public static int ImportFiles(IEnumerable<string> paths)
        {
            int count = 0;
            try
            {
                if (!System.IO.Directory.Exists(Directory)) System.IO.Directory.CreateDirectory(Directory);

                foreach (var path in paths)
                {
                    try
                    {
                        var extension = Path.GetExtension(path).ToLowerInvariant();
                        if (extension != ".png" && extension != ".gif") continue;
                        if (!File.Exists(path)) continue;

                        File.Copy(path, UniquePath(Path.GetFileName(path)), false);
                        count++;
                    }
                    catch (Exception ex)
                    {
                        TheOtherRolesPlugin.Logger.LogWarning($"[EmoteImport] import {path} failed: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                TheOtherRolesPlugin.Logger.LogWarning($"[EmoteImport] import failed: {ex.Message}");
            }

            if (count > 0) TheOtherRolesPlugin.Logger.LogInfo($"[EmoteImport] imported {count} file(s) into {Directory}");
            return count;
        }

        private static string UniquePath(string fileName)
        {
            var target = Path.Combine(Directory, fileName);
            if (!File.Exists(target)) return target;

            var stem = Path.GetFileNameWithoutExtension(fileName);
            var extension = Path.GetExtension(fileName);
            for (int i = 1; i < 1000; i++)
            {
                target = Path.Combine(Directory, $"{stem} ({i}){extension}");
                if (!File.Exists(target)) return target;
            }
            return Path.Combine(Directory, $"{stem}_{Guid.NewGuid():N}{extension}");
        }

        public static string Hash(byte[] data)
        {
            using var md5 = System.Security.Cryptography.MD5.Create();
            var hash = md5.ComputeHash(data);
            return BitConverter.ToString(hash, 0, 8).Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}
