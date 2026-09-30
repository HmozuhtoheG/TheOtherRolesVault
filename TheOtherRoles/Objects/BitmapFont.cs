using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using TheOtherRoles.MetaContext;
using UnityEngine;

namespace TheOtherRoles.Objects
{
    public class BitmapFont
    {
        public class Glyph
        {
            public float top;
            public float bottom;
            public float left;
            public float right;
            public float extraWidth;

            public bool IsWhitespace => top < 0f;
        }

        private readonly Dictionary<char, Glyph> glyphs = new();

        public float LineHeight { get; private set; } = 1f;
        public Texture2D Texture { get; private set; }
        public bool IsReady => Texture != null && glyphs.Count > 0;

        public Glyph GetGlyph(char c) => glyphs.TryGetValue(c, out var glyph) ? glyph : null;

        public BitmapFont(string jsonResource, string textureResource)
        {
            Texture = GraphicsHelper.LoadTextureFromResources(textureResource);
            if (Texture == null)
            {
                TheOtherRolesPlugin.Logger.LogError("[BitmapFont] texture missing: " + textureResource);
                return;
            }

            string json = ReadResourceText(jsonResource);
            if (json == null)
            {
                TheOtherRolesPlugin.Logger.LogError("[BitmapFont] json missing: " + jsonResource);
                return;
            }

            Build(json);
        }

        private static string ReadResourceText(string path)
        {
            using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(path);
            if (stream == null) return null;
            using StreamReader reader = new(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        private void Build(string json)
        {
            int columns = int.Parse(ExtractField(json, "X", 0), CultureInfo.InvariantCulture);
            int rows = int.Parse(ExtractField(json, "Y", 0), CultureInfo.InvariantCulture);
            if (columns <= 0 || rows <= 0)
            {
                TheOtherRolesPlugin.Logger.LogError("[BitmapFont] invalid grid");
                return;
            }

            LineHeight = float.Parse(ExtractField(json, "DefaultHeight", 0), CultureInfo.InvariantCulture);

            string whitespace = ExtractField(json, "WhitespaceWidth", 0);
            if (whitespace != null)
                Register(' ', -1f, -1f, -1f, -1f, float.Parse(whitespace, CultureInfo.InvariantCulture));

            float halfWidth = 1f / columns * 0.5f;
            float halfHeight = 1f / rows * 0.5f;

            void RegisterAt(int index, float ratio, char character)
            {
                if (index < 0 || index >= columns * rows) return;

                int cellX = index % columns;
                int cellY = index / columns;
                float centerX = (cellX + 0.5f) / columns;
                float centerY = 1f - (cellY + 0.5f) / rows;

                Register(character,
                    centerY + halfHeight, centerY - halfHeight,
                    centerX - halfWidth * ratio, centerX + halfWidth * ratio, 0f);
            }

            void RegisterSequence(string field, char first, int count)
            {
                string beginText = ExtractField(json, field, 0);
                if (beginText == null) return;

                int begin = int.Parse(beginText, CultureInfo.InvariantCulture);
                for (int i = 0; i < count; i++) RegisterAt(begin + i, 1f, (char)(first + i));
            }

            RegisterSequence("LowercaseSequenceBegin", 'a', 26);
            RegisterSequence("UppercaseSequenceBegin", 'A', 26);
            RegisterSequence("NumberSequenceBegin", '0', 10);

            int charactersAt = json.IndexOf("\"Characters\"", StringComparison.Ordinal);
            if (charactersAt < 0) return;
            int arrayStart = json.IndexOf('[', charactersAt);
            if (arrayStart < 0) return;

            foreach (string block in SplitBlocks(json.Substring(arrayStart)))
            {
                string character = ExtractField(block, "Character", 0);
                string indexText = ExtractField(block, "Index", 0);
                if (character == null || indexText == null) continue;

                int index = int.Parse(indexText, CultureInfo.InvariantCulture);
                string ratioText = ExtractField(block, "Ratio", 0);
                float ratio = ratioText == null ? 1f : float.Parse(ratioText, CultureInfo.InvariantCulture);

                RegisterAt(index, ratio, DecodeCharacter(character));
            }
        }

        private void Register(char character, float top, float bottom, float left, float right, float extraWidth)
        {
            glyphs[character] = new Glyph { top = top, bottom = bottom, left = left, right = right, extraWidth = extraWidth };
        }

        private static char DecodeCharacter(string token)
        {
            if (token.Length == 3 && token[0] == '%'
                && int.TryParse(token.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                return (char)code;

            return token[0];
        }

        private static IEnumerable<string> SplitBlocks(string text)
        {
            int depth = 0;
            int start = -1;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '{')
                {
                    if (depth == 0) start = i;
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0 && start >= 0)
                    {
                        yield return text.Substring(start, i - start + 1);
                        start = -1;
                    }
                }
                else if (c == ']' && depth == 0)
                {
                    yield break;
                }
            }
        }

        private static string ExtractField(string text, string field, int from)
        {
            int at = text.IndexOf("\"" + field + "\"", from, StringComparison.Ordinal);
            if (at < 0) return null;

            int colon = text.IndexOf(':', at);
            if (colon < 0) return null;

            int end = colon + 1;
            while (end < text.Length && char.IsWhiteSpace(text[end])) end++;

            int stop = end;
            while (stop < text.Length && text[stop] != ',' && text[stop] != '}' && text[stop] != '\n' && text[stop] != '\r')
                stop++;

            string value = text.Substring(end, stop - end).Trim();
            return value.Length == 0 ? null : value;
        }
    }

    public enum BitmapTextAlignment
    {
        Left,
        Center,
        Right
    }

    public class BitmapText
    {
        private static Shader cachedShader;
        private static bool shaderResolved;

        private static readonly Dictionary<string, List<BitmapText>> groups = new();

        public const string DefaultGroup = "default";

        private readonly string group;

        private readonly GameObject gameObject;
        private readonly MeshRenderer renderer;
        private readonly MeshFilter filter;
        private readonly Mesh mesh;

        private string current = "";
        private Color color = Color.white;

        public BitmapFont Font { get; }
        public float FontSize { get; set; } = 1f;
        public Vector2 Pivot { get; set; } = new(0.5f, 0.5f);
        public BitmapTextAlignment Alignment { get; set; } = BitmapTextAlignment.Left;

        public GameObject GameObject => gameObject;
        public bool Enabled
        {
            get => renderer.enabled;
            set => renderer.enabled = value;
        }

        public Color Color
        {
            get => color;
            set
            {
                color = value;
                Rebuild();
            }
        }

        public int SortingOrder
        {
            get => renderer.sortingOrder;
            set => renderer.sortingOrder = value;
        }

        public static Material CreateSharedMaterial(BitmapFont font, Color color)
        {
            var material = new Material(ResolveShader());
            if (font?.Texture != null) material.mainTexture = font.Texture;
            material.color = color;
            return material;
        }

        public BitmapText(string name, Transform parent, Vector3 localPosition, BitmapFont font, int sortingOrder, string group = null, Material sharedMaterial = null)
        {
            Font = font;
            this.group = group ?? DefaultGroup;

            gameObject = Helpers.CreateObject(name, parent, localPosition);
            renderer = gameObject.AddComponent<MeshRenderer>();
            filter = gameObject.AddComponent<MeshFilter>();

            mesh = new Mesh();
            filter.mesh = mesh;

            renderer.sharedMaterial = sharedMaterial ?? CreateSharedMaterial(font, Color.white);
            renderer.sortingOrder = sortingOrder;

            if (!groups.TryGetValue(this.group, out var list))
                groups[this.group] = list = new List<BitmapText>();

            list.Add(this);
        }

        public static void DestroyGroup(string group)
        {
            if (!groups.TryGetValue(group ?? DefaultGroup, out var list)) return;

            foreach (var text in list.ToArray()) text.Destroy();
            list.Clear();
        }

        public static IEnumerable<BitmapText> Group(string group)
        {
            return groups.TryGetValue(group ?? DefaultGroup, out var list) ? list.ToArray() : Array.Empty<BitmapText>();
        }

        private static Shader ResolveShader()
        {
            if (shaderResolved) return cachedShader;
            shaderResolved = true;

            cachedShader = Shader.Find("Unlit/Transparent");
            if (cachedShader == null) cachedShader = Helpers.achievementMaterialShader;
            if (cachedShader == null) cachedShader = Shader.Find("Sprites/Default");

            TheOtherRolesPlugin.Logger.LogInfo("[BitmapText] shader = " + (cachedShader == null ? "NULL" : cachedShader.name));
            return cachedShader ?? Shader.Find("Unlit/Texture");
        }

        public void SetText(string text)
        {
            if (current == text) return;
            current = text ?? "";
            Rebuild();
        }

        public void SetActive(bool active) => gameObject.SetActive(active);

        public void Destroy()
        {
            if (groups.TryGetValue(group, out var list)) list.Remove(this);
            if (mesh != null) UnityEngine.Object.Destroy(mesh);
            if (gameObject != null) UnityEngine.Object.Destroy(gameObject);
        }

        private void Rebuild()
        {
            if (Font == null || !Font.IsReady) return;

            string text = current.Replace("<br>", "\n").Replace("\r\n", "\n").Replace("\r", "");

            List<List<Vector3>> lines = new();
            List<Vector2> uvs = new();
            List<int> indices = new();

            List<Vector3> line = new();
            lines.Add(line);

            float cursor = 0f;
            float texRatio = (float)Font.Texture.width / Font.Texture.height;

            foreach (char c in text)
            {
                if (c == '\n')
                {
                    cursor = 0f;
                    line = new List<Vector3>();
                    lines.Add(line);
                    continue;
                }

                var glyph = Font.GetGlyph(c);
                if (glyph != null)
                {
                    if (glyph.IsWhitespace)
                    {
                        cursor += glyph.extraWidth * FontSize;
                    }
                    else
                    {
                        int offset = line.Count;

                        float height = Font.LineHeight * FontSize;
                        float width = (glyph.right - glyph.left) / (glyph.top - glyph.bottom) * texRatio * Font.LineHeight * FontSize;

                        line.Add(new Vector3(cursor, 0f));
                        line.Add(new Vector3(cursor + width, 0f));
                        line.Add(new Vector3(cursor + width, -height));
                        line.Add(new Vector3(cursor, -height));

                        uvs.Add(new Vector2(glyph.left, glyph.top));
                        uvs.Add(new Vector2(glyph.right, glyph.top));
                        uvs.Add(new Vector2(glyph.right, glyph.bottom));
                        uvs.Add(new Vector2(glyph.left, glyph.bottom));

                        indices.Add(offset);
                        indices.Add(offset + 1);
                        indices.Add(offset + 2);
                        indices.Add(offset + 2);
                        indices.Add(offset + 3);
                        indices.Add(offset);

                        cursor += width;
                    }
                }
            }

            while (lines.Count > 0 && lines[lines.Count - 1].Count == 0) lines.RemoveAt(lines.Count - 1);

            if (lines.Count == 0)
            {
                renderer.enabled = false;
                return;
            }
            renderer.enabled = true;

            float widest = 0f;
            foreach (var l in lines)
                if (l.Count > 0) widest = Mathf.Max(widest, l[l.Count - 2].x);

            float totalHeight = Font.LineHeight * FontSize * lines.Count;

            Vector3 origin = new(-Pivot.x * widest, (1f - Pivot.y) * totalHeight);

            int row = 0;
            foreach (var l in lines)
            {
                if (l.Count == 0)
                {
                    row++;
                    continue;
                }

                float lineWidth = l[l.Count - 2].x;
                float alignOffset = Alignment switch
                {
                    BitmapTextAlignment.Center => (widest - lineWidth) * 0.5f,
                    BitmapTextAlignment.Right => widest - lineWidth,
                    _ => 0f
                };

                for (int i = 0; i < l.Count; i++)
                    l[i] += new Vector3(origin.x + alignOffset, origin.y + row * (Font.LineHeight * FontSize));

                row++;
            }

            List<Vector3> vertices = new();
            foreach (var l in lines) vertices.AddRange(l);

            var vertexColors = new Color32[vertices.Count];
            var packed = (Color32)color;
            for (int i = 0; i < vertexColors.Length; i++) vertexColors[i] = packed;

            mesh.Clear();
            mesh.SetVertices(vertices.ToArray());
            mesh.SetUVs(0, uvs.ToArray());
            mesh.SetColors(vertexColors);
            mesh.SetTriangles(indices.ToArray(), 0);
            mesh.RecalculateBounds();
        }
    }
}
