using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace CameraCaptureStudio
{
    [Serializable]
    internal sealed class CaptureTextSegment
    {
        public string Text = "";
        public Font Font;
        public int FontSize; // 0 inherits the global size.
        public int SplitAfter = 1;

        internal CaptureTextSegment Split(int characters)
        {
            int[] starts = StringInfo.ParseCombiningCharacters(Text ?? "");
            if (characters < 1 || characters >= starts.Length)
                throw new ArgumentOutOfRangeException(nameof(characters));
            int offset = starts[characters];
            var remainder = new CaptureTextSegment { Text = Text.Substring(offset), Font = Font, FontSize = FontSize };
            Text = Text.Substring(0, offset);
            return remainder;
        }

        internal static string Join(IList<CaptureTextSegment> segments)
        {
            var text = new StringBuilder();
            if (segments != null)
                foreach (CaptureTextSegment segment in segments)
                    if (segment != null) text.Append(segment.Text);
            return text.ToString();
        }
    }

    internal static class CaptureTextRenderer
    {
        internal sealed class Glyph
        {
            internal CharacterInfo Character;
            internal float X;
            internal int Line;
        }

        internal sealed class Run
        {
            internal Font Font;
            internal int FontSize;
            internal readonly List<Glyph> Glyphs = new List<Glyph>();
        }

        internal sealed class Layout
        {
            internal readonly List<Run> Runs = new List<Run>();
            internal Rect Bounds;
            internal float LineHeight;
            internal int LineCount;
        }

        internal static bool HasText(CaptureOptions options)
        {
            if (options.TextSegments == null) return !string.IsNullOrEmpty(options.Text);
            foreach (CaptureTextSegment segment in options.TextSegments)
                if (segment != null && !string.IsNullOrEmpty(segment.Text)) return true;
            return false;
        }

        internal static void Validate(CaptureOptions options)
        {
            if (options.TextSegments == null)
            {
                if (!string.IsNullOrEmpty(options.Text) && options.Font == null)
                    throw new ArgumentException("嵌字需要选择字体。");
                return;
            }
            for (int i = 0; i < options.TextSegments.Count; i++)
            {
                CaptureTextSegment segment = options.TextSegments[i];
                if (segment == null || string.IsNullOrEmpty(segment.Text)) continue;
                Font resolved = segment.Font != null ? segment.Font : options.Font;
                if (resolved == null)
                    throw new ArgumentException($"第 {i + 1} 段文字需要选择字体，或设置默认字体。");
                if (segment.FontSize < 0)
                    throw new ArgumentException($"第 {i + 1} 段字号不能为负数。");
                if (!resolved.dynamic)
                    throw new ArgumentException($"第 {i + 1} 段请使用动态 TTF/OTF 字体。");
            }
        }

        internal static List<CaptureTextSegment> ScaleSegments(List<CaptureTextSegment> segments, float scale)
        {
            if (segments == null) return null;
            var result = new List<CaptureTextSegment>(segments.Count);
            foreach (CaptureTextSegment segment in segments)
                result.Add(segment == null ? null : new CaptureTextSegment
                {
                    Text = segment.Text, Font = segment.Font, SplitAfter = segment.SplitAfter,
                    FontSize = segment.FontSize > 0 ? Mathf.Max(1, Mathf.RoundToInt(segment.FontSize * scale)) : 0
                });
            return result;
        }

        internal static Layout BuildLayout(CaptureOptions options)
        {
            Validate(options);
            var layout = new Layout { LineCount = 1, LineHeight = 1f };
            var requested = new Dictionary<(Font Font, int Size), StringBuilder>();
            foreach (CaptureTextSegment segment in options.TextSegments)
            {
                if (segment == null || string.IsNullOrEmpty(segment.Text)) continue;
                Font font = segment.Font != null ? segment.Font : options.Font;
                int size = segment.FontSize > 0 ? segment.FontSize : Mathf.Max(1, options.FontSize);
                var key = (font, size);
                if (!requested.TryGetValue(key, out StringBuilder text))
                {
                    text = new StringBuilder(" ");
                    requested.Add(key, text);
                }
                text.Append(segment.Text);
                float scale = (float)size / Mathf.Max(1, font.fontSize);
                layout.LineHeight = Mathf.Max(layout.LineHeight, Mathf.Max(size, font.lineHeight * scale));
            }
            // Request every character before reading any UVs: a later request can rebuild the same atlas.
            foreach (var pair in requested)
                pair.Key.Font.RequestCharactersInTexture(pair.Value.ToString(), pair.Key.Size, FontStyle.Normal);

            float x = 0f, width = 0f, minX = 0f, ascent = 0f, descent = 0f;
            int line = 0;
            bool previousWasCR = false, anyGlyph = false;
            foreach (CaptureTextSegment segment in options.TextSegments)
            {
                if (segment == null || string.IsNullOrEmpty(segment.Text)) continue;
                Font font = segment.Font != null ? segment.Font : options.Font;
                int size = segment.FontSize > 0 ? segment.FontSize : Mathf.Max(1, options.FontSize);
                var run = new Run { Font = font, FontSize = size };
                layout.Runs.Add(run);
                foreach (char character in segment.Text)
                {
                    if (character == '\n' && previousWasCR) { previousWasCR = false; continue; }
                    previousWasCR = character == '\r';
                    if (character == '\r' || character == '\n')
                    {
                        width = Mathf.Max(width, x);
                        x = 0f;
                        line++;
                        continue;
                    }
                    if (character == '\t')
                    {
                        float space = font.GetCharacterInfo(' ', out CharacterInfo spaceInfo, size)
                            ? Mathf.Max(1, spaceInfo.advance) : Mathf.Max(1f, size * 0.25f);
                        float tab = space * 4f;
                        x = (Mathf.Floor(x / tab) + 1f) * tab;
                        continue;
                    }
                    if (char.IsControl(character)) continue;
                    if (!font.GetCharacterInfo(character, out CharacterInfo info, size, FontStyle.Normal))
                        throw new ArgumentException($"字体“{font.name}”无法显示字符“{character}”（U+{(int)character:X4}），请为这段文字选择支持该字符的字体。");
                    if (info.maxX > info.minX && info.maxY > info.minY)
                    {
                        run.Glyphs.Add(new Glyph { Character = info, X = x, Line = line });
                        ascent = Mathf.Max(ascent, info.maxY);
                        descent = Mathf.Min(descent, info.minY);
                        minX = Mathf.Min(minX, x + info.minX);
                        width = Mathf.Max(width, x + info.maxX);
                        anyGlyph = true;
                    }
                    x += info.advance;
                }
            }
            width = Mathf.Max(width, x);
            layout.LineCount = line + 1;
            layout.LineHeight = Mathf.Max(layout.LineHeight, ascent - descent);
            if (anyGlyph)
                layout.Bounds = new Rect(minX, descent - line * layout.LineHeight,
                    width - minX, ascent - descent + line * layout.LineHeight);
            return layout;
        }

        internal static void Draw(RenderTexture target, CaptureOptions options)
        {
            Layout layout = BuildLayout(options);
            if (layout.Bounds.height <= 0f) return;
            Shader shader = Shader.Find("Hidden/CameraCaptureStudio/Text");
            if (shader == null) throw new InvalidOperationException("找不到嵌字 Shader，请重新导入插件。");
            float left = options.Margin, bottom = options.Margin;
            switch (options.Placement)
            {
                case TextPlacement.TopCenter: case TextPlacement.Center: case TextPlacement.BottomCenter:
                    left = (target.width - layout.Bounds.width) * 0.5f; break;
                case TextPlacement.TopRight: case TextPlacement.MiddleRight: case TextPlacement.BottomRight:
                    left = target.width - options.Margin - layout.Bounds.width; break;
            }
            switch (options.Placement)
            {
                case TextPlacement.TopLeft: case TextPlacement.TopCenter: case TextPlacement.TopRight:
                    bottom = target.height - options.Margin - layout.Bounds.height; break;
                case TextPlacement.MiddleLeft: case TextPlacement.Center: case TextPlacement.MiddleRight:
                    bottom = (target.height - layout.Bounds.height) * 0.5f; break;
            }
            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            RenderTexture previous = RenderTexture.active;
            try
            {
                material.SetColor("_Color", options.TextColor);
                RenderTexture.active = target;
                GL.PushMatrix();
                try
                {
                    GL.LoadPixelMatrix(0, target.width, 0, target.height);
                    foreach (Run run in layout.Runs)
                    {
                        if (run.Glyphs.Count == 0) continue;
                        Texture atlas = run.Font.material != null ? run.Font.material.mainTexture : null;
                        if (atlas == null) throw new InvalidOperationException($"无法读取字体“{run.Font.name}”的图集。");
                        material.SetTexture("_MainTex", atlas);
                        if (!material.SetPass(0)) throw new InvalidOperationException("嵌字材质无法渲染。");
                        GL.Begin(GL.TRIANGLES);
                        try
                        {
                            foreach (Glyph glyph in run.Glyphs)
                            {
                                CharacterInfo c = glyph.Character;
                                float px = left + glyph.X - layout.Bounds.xMin;
                                float py = bottom - glyph.Line * layout.LineHeight - layout.Bounds.yMin;
                                Emit(px + c.minX, py + c.minY, c.uvBottomLeft);
                                Emit(px + c.minX, py + c.maxY, c.uvTopLeft);
                                Emit(px + c.maxX, py + c.maxY, c.uvTopRight);
                                Emit(px + c.maxX, py + c.maxY, c.uvTopRight);
                                Emit(px + c.maxX, py + c.minY, c.uvBottomRight);
                                Emit(px + c.minX, py + c.minY, c.uvBottomLeft);
                            }
                        }
                        finally { GL.End(); }
                    }
                }
                finally { GL.PopMatrix(); }
            }
            finally
            {
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        private static void Emit(float x, float y, Vector2 uv)
        {
            GL.TexCoord2(uv.x, uv.y);
            GL.Vertex3(x, y, 0f);
        }
    }
}
