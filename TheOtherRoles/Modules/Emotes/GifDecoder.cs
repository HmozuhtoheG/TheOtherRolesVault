using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheOtherRoles.Modules.Emotes
{
    public static class GifDecoder
    {
        public const int MaxFrames = 64;
        private const long MaxPixels = 4000000;
        private const long MaxFrameBytes = 64000000;

        public class Result
        {
            public int Width;
            public int Height;
            public List<Color32[]> Frames = new();
            public List<ushort> Delays = new();
        }

        public static Result Decode(byte[] data)
        {
            if (data == null || data.Length < 13) return null;
            if (data[0] != 'G' || data[1] != 'I' || data[2] != 'F') return null;

            int pos = 6;
            int width = ReadUInt16(data, pos);
            int height = ReadUInt16(data, pos + 2);
            byte screenPacked = data[pos + 4];
            pos += 7;

            if (width <= 0 || height <= 0) return null;
            if ((long)width * height > MaxPixels)
            {
                TheOtherRolesPlugin.Logger.LogWarning($"[GifDecoder] image too large: {width}x{height}");
                return null;
            }

            Color32[] globalTable = null;
            if ((screenPacked & 0x80) != 0)
            {
                globalTable = ReadColorTable(data, ref pos, 2 << (screenPacked & 0x07));
                if (globalTable == null) return null;
            }

            var result = new Result { Width = width, Height = height };
            var canvas = new Color32[width * height];

            int maxFrames = (int)Math.Clamp(MaxFrameBytes / ((long)width * height * 4), 1, MaxFrames);

            int delay = 0;
            int transparentIndex = -1;
            int disposal = 0;

            while (pos < data.Length)
            {
                byte block = data[pos++];
                if (block == 0x3B) break;

                if (block == 0x21)
                {
                    if (pos >= data.Length) break;
                    byte label = data[pos++];

                    if (label == 0xF9)
                    {
                        if (pos >= data.Length) break;
                        int size = data[pos++];
                        if (size >= 4 && pos + size <= data.Length)
                        {
                            byte gcePacked = data[pos];
                            disposal = (gcePacked >> 2) & 0x07;
                            delay = ReadUInt16(data, pos + 1);
                            transparentIndex = (gcePacked & 0x01) != 0 ? data[pos + 3] : -1;
                        }
                        pos += size;
                    }

                    ReadSubBlocks(data, ref pos);
                    continue;
                }

                if (block != 0x2C) break;

                if (pos + 9 > data.Length) break;
                int left = ReadUInt16(data, pos);
                int top = ReadUInt16(data, pos + 2);
                int frameWidth = ReadUInt16(data, pos + 4);
                int frameHeight = ReadUInt16(data, pos + 6);
                byte imagePacked = data[pos + 8];
                pos += 9;

                var table = globalTable;
                if ((imagePacked & 0x80) != 0)
                {
                    table = ReadColorTable(data, ref pos, 2 << (imagePacked & 0x07));
                }
                if (table == null || frameWidth <= 0 || frameHeight <= 0) break;

                if (pos >= data.Length) break;
                int minCodeSize = data[pos++];

                var lzw = ReadSubBlocks(data, ref pos);
                var indices = DecodeLzw(lzw, minCodeSize, frameWidth * frameHeight);
                if (indices == null) break;

                var previous = disposal == 3 ? (Color32[])canvas.Clone() : null;

                DrawFrame(canvas, width, height, table, indices, left, top, frameWidth, frameHeight,
                    (imagePacked & 0x40) != 0, transparentIndex);

                result.Frames.Add((Color32[])canvas.Clone());
                result.Delays.Add((ushort)Mathf.Clamp(delay <= 0 ? 100 : delay * 10, 20, 1000));

                if (disposal == 2) ClearRect(canvas, width, height, left, top, frameWidth, frameHeight);
                else if (previous != null) canvas = previous;

                disposal = 0;
                transparentIndex = -1;
                delay = 0;

                if (result.Frames.Count >= maxFrames)
                {
                    if (maxFrames < MaxFrames)
                    {
                        TheOtherRolesPlugin.Logger.LogInfo($"[GifDecoder] {width}x{height}: kept first {maxFrames} frame(s)");
                    }
                    break;
                }
            }

            return result.Frames.Count > 0 ? result : null;
        }

        private static int ReadUInt16(byte[] data, int pos) => data[pos] | (data[pos + 1] << 8);

        private static Color32[] ReadColorTable(byte[] data, ref int pos, int count)
        {
            if (count <= 0 || pos + count * 3 > data.Length) return null;
            var table = new Color32[count];
            for (int i = 0; i < count; i++)
            {
                table[i] = new Color32(data[pos], data[pos + 1], data[pos + 2], 255);
                pos += 3;
            }
            return table;
        }

        private static byte[] ReadSubBlocks(byte[] data, ref int pos)
        {
            int total = 0;
            int scan = pos;
            while (scan < data.Length)
            {
                int size = data[scan++];
                if (size == 0) break;
                total += size;
                scan += size;
            }

            var result = new byte[total];
            int read = 0;
            int cursor = pos;
            while (cursor < data.Length)
            {
                int size = data[cursor++];
                if (size == 0) break;
                if (cursor + size > data.Length) size = data.Length - cursor;
                if (size <= 0) break;
                Array.Copy(data, cursor, result, read, size);
                read += size;
                cursor += size;
            }

            pos = scan;
            return result;
        }

        private static int[] RowOrder(int height, bool interlaced)
        {
            var order = new int[height];
            if (!interlaced)
            {
                for (int i = 0; i < height; i++) order[i] = i;
                return order;
            }

            int index = 0;
            int[] starts = { 0, 4, 2, 1 };
            int[] steps = { 8, 8, 4, 2 };
            for (int pass = 0; pass < 4; pass++)
            {
                for (int y = starts[pass]; y < height; y += steps[pass]) order[index++] = y;
            }
            return order;
        }

        private static void DrawFrame(Color32[] canvas, int canvasWidth, int canvasHeight, Color32[] table,
            byte[] indices, int left, int top, int frameWidth, int frameHeight, bool interlaced, int transparentIndex)
        {
            var order = RowOrder(frameHeight, interlaced);
            int source = 0;

            for (int i = 0; i < order.Length; i++)
            {
                int y = top + order[i];
                if (y >= 0 && y < canvasHeight)
                {
                    int rowBase = y * canvasWidth;
                    for (int x = 0; x < frameWidth; x++)
                    {
                        int index = indices[source + x];
                        if (index == transparentIndex || index >= table.Length) continue;
                        int canvasX = left + x;
                        if (canvasX < 0 || canvasX >= canvasWidth) continue;
                        canvas[rowBase + canvasX] = table[index];
                    }
                }
                source += frameWidth;
            }
        }

        private static void ClearRect(Color32[] canvas, int canvasWidth, int canvasHeight, int left, int top, int width, int height)
        {
            for (int y = top; y < top + height; y++)
            {
                if (y < 0 || y >= canvasHeight) continue;
                int rowBase = y * canvasWidth;
                for (int x = left; x < left + width; x++)
                {
                    if (x < 0 || x >= canvasWidth) continue;
                    canvas[rowBase + x] = new Color32(0, 0, 0, 0);
                }
            }
        }

        private static byte[] DecodeLzw(byte[] data, int minCodeSize, int pixelCount)
        {
            if (data.Length == 0 || pixelCount <= 0) return null;
            if (minCodeSize < 2 || minCodeSize > 8) return null;

            int clearCode = 1 << minCodeSize;
            int endCode = clearCode + 1;
            int codeSize = minCodeSize + 1;
            int nextCode = endCode + 1;

            int[] prefix = new int[4096];
            byte[] suffix = new byte[4096];
            byte[] stack = new byte[4096];
            for (int i = 0; i < clearCode; i++)
            {
                prefix[i] = -1;
                suffix[i] = (byte)i;
            }

            var output = new byte[pixelCount];
            int outputPos = 0;

            int bitBuffer = 0;
            int bitCount = 0;
            int dataPos = 0;
            int oldCode = -1;
            int first = 0;

            while (outputPos < pixelCount)
            {
                while (bitCount < codeSize)
                {
                    if (dataPos >= data.Length) return outputPos > 0 ? output : null;
                    bitBuffer |= data[dataPos++] << bitCount;
                    bitCount += 8;
                }

                int code = bitBuffer & ((1 << codeSize) - 1);
                bitBuffer >>= codeSize;
                bitCount -= codeSize;

                if (code == clearCode)
                {
                    codeSize = minCodeSize + 1;
                    nextCode = endCode + 1;
                    oldCode = -1;
                    continue;
                }

                if (code == endCode) break;

                if (oldCode == -1)
                {
                    if (code >= clearCode) break;
                    output[outputPos++] = suffix[code];
                    first = code;
                    oldCode = code;
                    continue;
                }

                int inCode = code;
                int stackPos = 0;

                if (code >= nextCode)
                {
                    stack[stackPos++] = (byte)first;
                    code = oldCode;
                }

                int guard = 0;
                while (code >= clearCode && guard++ < 4096)
                {
                    stack[stackPos++] = suffix[code];
                    code = prefix[code];
                    if (code < 0) break;
                }
                if (code < 0 || code >= clearCode) break;

                first = suffix[code];
                stack[stackPos++] = (byte)first;

                if (nextCode < 4096)
                {
                    prefix[nextCode] = oldCode;
                    suffix[nextCode] = (byte)first;
                    nextCode++;
                    if (nextCode == (1 << codeSize) && codeSize < 12) codeSize++;
                }

                while (stackPos > 0 && outputPos < pixelCount) output[outputPos++] = stack[--stackPos];

                oldCode = inCode;
            }

            return outputPos > 0 ? output : null;
        }
    }
}
