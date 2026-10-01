using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace TheOtherRoles.Modules.Emotes
{
    public static class EmotePayload
    {
        public const int MaxBytes = 24000;

        private const byte Version = 1;

        public class Frame
        {
            public Color32[] Pixels;
            public ushort DelayMs;
        }

        public class Decoded
        {
            public int Width;
            public int Height;
            public List<Frame> Frames;
        }

        public static byte[] Encode(int width, int height, List<Frame> frames)
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write((byte)'P');
                writer.Write((byte)'T');
                writer.Write((byte)'E');
                writer.Write(Version);
                writer.Write((byte)(frames.Count > 1 ? 1 : 0));
                writer.Write((ushort)width);
                writer.Write((ushort)height);
                writer.Write((ushort)frames.Count);

                foreach (var frame in frames)
                {
                    var packed = Deflate(ToRgba(frame.Pixels));
                    writer.Write(frame.DelayMs);
                    writer.Write(packed.Length);
                    writer.Write(packed);
                }
            }
            return stream.ToArray();
        }

        public static Decoded Decode(byte[] data)
        {
            try
            {
                using var stream = new MemoryStream(data);
                using var reader = new BinaryReader(stream);

                if (reader.ReadByte() != 'P' || reader.ReadByte() != 'T' || reader.ReadByte() != 'E') return null;
                if (reader.ReadByte() != Version) return null;
                reader.ReadByte();
                int width = reader.ReadUInt16();
                int height = reader.ReadUInt16();
                int count = reader.ReadUInt16();

                if (width <= 0 || height <= 0 || count <= 0 || count > 64) return null;
                int pixelCount = width * height;
                if (pixelCount > 256 * 256) return null;

                var decoded = new Decoded { Width = width, Height = height, Frames = new List<Frame>(count) };
                for (int i = 0; i < count; i++)
                {
                    ushort frameDelay = reader.ReadUInt16();
                    int length = reader.ReadInt32();
                    if (length <= 0 || length > MaxBytes) return null;

                    var packed = reader.ReadBytes(length);
                    if (packed.Length != length) return null;

                    var raw = Inflate(packed, pixelCount * 4);
                    if (raw == null || raw.Length != pixelCount * 4) return null;

                    decoded.Frames.Add(new Frame { Pixels = FromRgba(raw), DelayMs = frameDelay });
                }

                return decoded;
            }
            catch
            {
                return null;
            }
        }

        private static byte[] ToRgba(Color32[] pixels)
        {
            var bytes = new byte[pixels.Length * 4];
            for (int i = 0; i < pixels.Length; i++)
            {
                bytes[i * 4] = pixels[i].r;
                bytes[i * 4 + 1] = pixels[i].g;
                bytes[i * 4 + 2] = pixels[i].b;
                bytes[i * 4 + 3] = pixels[i].a;
            }
            return bytes;
        }

        private static Color32[] FromRgba(byte[] bytes)
        {
            var pixels = new Color32[bytes.Length / 4];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color32(bytes[i * 4], bytes[i * 4 + 1], bytes[i * 4 + 2], bytes[i * 4 + 3]);
            }
            return pixels;
        }

        private static byte[] Deflate(byte[] data)
        {
            using var output = new MemoryStream();
            using (var deflate = new DeflateStream(output, System.IO.Compression.CompressionLevel.Optimal, true))
            {
                deflate.Write(data, 0, data.Length);
            }
            return output.ToArray();
        }

        private static byte[] Inflate(byte[] data, int expectedLength)
        {
            using var input = new MemoryStream(data);
            using var deflate = new DeflateStream(input, CompressionMode.Decompress);
            var result = new byte[expectedLength];
            int offset = 0;
            while (offset < expectedLength)
            {
                int read = deflate.Read(result, offset, expectedLength - offset);
                if (read <= 0) break;
                offset += read;
            }
            if (offset != expectedLength) return null;
            return result;
        }
    }
}
