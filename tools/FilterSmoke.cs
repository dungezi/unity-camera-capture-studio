using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace CameraCaptureStudio
{
    // Copied into a disposable Unity project by run_filter_smoke.ps1, never shipped in the plugin.
    public static class FilterSmoke
    {
        private static int checks;
        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            checks++;
        }

        private static Texture2D Read(RenderTexture target)
        {
            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                image.Apply();
                return image;
            }
            finally { RenderTexture.active = previous; }
        }

        private static Texture2D Filter(int width, int height, Func<int, int, Color> pixel, CaptureOptions options)
        {
            var image = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++) pixels[y * width + x] = pixel(x, y);
            image.SetPixels(pixels);
            image.Apply();
            image.wrapMode = TextureWrapMode.Clamp;
            var source = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            try
            {
                source.filterMode = FilterMode.Bilinear;
                source.wrapMode = TextureWrapMode.Clamp;
                Graphics.Blit(image, source);
                RenderTexture.active = source;
                CameraCaptureProcessor.ApplyFilter(source, target, options);
                Check(RenderTexture.active == source, "Filter did not restore active render texture");
                return Read(target);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(source);
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(image);
            }
        }

        private static float Difference(Texture2D a, Texture2D b)
        {
            Color32[] left = a.GetPixels32(), right = b.GetPixels32();
            float sum = 0;
            for (int i = 0; i < left.Length; i++)
                sum += Math.Abs(left[i].r - right[i].r) + Math.Abs(left[i].g - right[i].g)
                    + Math.Abs(left[i].b - right[i].b) + Math.Abs(left[i].a - right[i].a);
            return sum / (left.Length * 4f * 255f);
        }

        private static float Detail(Texture2D image)
        {
            Color[] pixels = image.GetPixels();
            float sum = 0;
            for (int y = 1; y < image.height; y++)
                for (int x = 1; x < image.width; x++)
                {
                    int i = y * image.width + x;
                    sum += Mathf.Abs(pixels[i].r - pixels[i - 1].r)
                        + Mathf.Abs(pixels[i].r - pixels[i - image.width].r);
                }
            return sum;
        }

        private static void PaletteChecks()
        {
            Color[] colors = { Color.red, Color.green, Color.blue, Color.yellow, Color.magenta,
                new Color(0.35f, 0.35f, 0.35f, 0.2f), new Color(0.65f, 0.65f, 0.65f, 0.2f), Color.white };
            Func<int, int, Color> palette = (x, y) => colors[x / 16];
            var options = new CaptureOptions { Filter = CaptureFilter.HighContrastBlackAndWhite, PreserveTransparency = true };
            Texture2D accent = Filter(128, 16, palette, options);
            options.PreserveRed = false;
            Texture2D monochrome = Filter(128, 16, palette, options);
            options.Filter = CaptureFilter.BlackAndWhite;
            Texture2D old = Filter(128, 16, palette, options);
            try
            {
                Color red = accent.GetPixel(8, 8);
                Check(red.r > 0.9f && red.g < 0.05f && red.b < 0.05f, "Selective red was lost");
                for (int i = 1; i < colors.Length; i++)
                {
                    Color c = accent.GetPixel(i * 16 + 8, 8);
                    Check(Mathf.Abs(c.r - c.g) < 0.01f && Mathf.Abs(c.g - c.b) < 0.01f,
                        "Non-red color remained saturated: " + i);
                }
                Color disabled = monochrome.GetPixel(8, 8);
                Check(Mathf.Abs(disabled.r - disabled.g) < 0.01f, "Disable-red toggle failed");
                Check(monochrome.GetPixel(88, 8).r < old.GetPixel(88, 8).r - 0.1f
                    && monochrome.GetPixel(104, 8).r > old.GetPixel(104, 8).r + 0.1f, "Contrast did not increase");
                Check(Mathf.Abs(accent.GetPixel(88, 8).a - 0.2f) < 0.015f, "High contrast lost alpha");
            }
            finally { UnityEngine.Object.DestroyImmediate(accent); UnityEngine.Object.DestroyImmediate(monochrome); UnityEngine.Object.DestroyImmediate(old); }
        }

        private static void BlurChecks()
        {
            Func<int, int, Color> checker = (x, y) => ((x / 8 + y / 8) % 2 == 0 ? Color.white : Color.black);
            var options = new CaptureOptions { Filter = CaptureFilter.None };
            Texture2D original = Filter(256, 144, checker, options);
            options.Filter = CaptureFilter.BackgroundBlur;
            options.BlurStrength = 0;
            Texture2D zero = Filter(256, 144, checker, options);
            options.BlurStrength = 20;
            Texture2D mild = Filter(256, 144, checker, options);
            options.BlurStrength = 80;
            Texture2D strong = Filter(256, 144, checker, options);
            try
            {
                Check(Difference(original, zero) < 0.001f, "Zero blur changed the image");
                Check(Detail(mild) < Detail(original) * 0.95f, "Mild blur did not smooth detail");
                Check(Detail(strong) < Detail(mild) * 0.85f, "Increasing strength did not increase blur");
                Check(strong.GetPixel(64, 64).a > 0.99f, "Default blur must be opaque");
                Debug.Log("BLUR_DETAIL original=" + Detail(original) + " mild=" + Detail(mild) + " strong=" + Detail(strong));
            }
            finally { UnityEngine.Object.DestroyImmediate(original); UnityEngine.Object.DestroyImmediate(zero); UnityEngine.Object.DestroyImmediate(mild); UnityEngine.Object.DestroyImmediate(strong); }
            options.PreserveTransparency = true;
            Texture2D alpha = Filter(32, 24, (x, y) => new Color(0.8f, 0.6f, 0.4f, 0.2f), options);
            Texture2D edge = Filter(256, 144, (x, y) => x < 128 ? Color.white : Color.black, options);
            Texture2D tiny = Filter(1, 1, (x, y) => Color.red, options);
            try
            {
                Color c = alpha.GetPixel(10, 10);
                Check(Mathf.Abs(c.a - 0.2f) < 0.02f && Mathf.Abs(c.r - 0.8f) < 0.04f, "Blur alpha or color changed");
                Check(edge.GetPixel(0, 70).r > 0.95f && edge.GetPixel(255, 70).r < 0.05f, "Blur wrapped across image edges");
                Check(tiny.GetPixel(0, 0).r > 0.95f, "One-pixel image failed");
            }
            finally { UnityEngine.Object.DestroyImmediate(alpha); UnityEngine.Object.DestroyImmediate(edge); UnityEngine.Object.DestroyImmediate(tiny); }
        }

        private static void ScaleChecks()
        {
            var options = new CaptureOptions { Filter = CaptureFilter.BackgroundBlur, BlurStrength = 80 };
            Texture2D small = Filter(256, 144, (x, y) => (x / 16 + y / 16) % 2 == 0 ? Color.white : Color.black, options);
            Texture2D large = Filter(1024, 576, (x, y) => (x / 64 + y / 64) % 2 == 0 ? Color.white : Color.black, options);
            var target = RenderTexture.GetTemporary(256, 144, 0);
            Texture2D resized = null;
            RenderTexture previous = RenderTexture.active;
            try
            {
                Graphics.Blit(large, target);
                resized = Read(target);
                float difference = Difference(small, resized);
                Debug.Log("BLUR_SCALE_DIFFERENCE=" + difference);
                Check(difference < 0.04f, "Preview/export blur strength differs excessively");
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(small);
                UnityEngine.Object.DestroyImmediate(large);
                if (resized != null) UnityEngine.Object.DestroyImmediate(resized);
            }
            options.PreserveTransparency = true;
            Texture2D faint = Filter(256, 144, (x, y) => new Color(0.8f, 0.6f, 0.4f, 0.01f), options);
            try
            {
                Color c = faint.GetPixel(100, 100);
                Check(Mathf.Abs(c.r - 0.8f) < 0.04f && Mathf.Abs(c.g - 0.6f) < 0.04f,
                    "Repeated blur lost faint transparent colors");
            }
            finally { UnityEngine.Object.DestroyImmediate(faint); }
        }

        private static void CameraChecks()
        {
            var gameObject = new GameObject("FilterSmokeCamera") { hideFlags = HideFlags.HideAndDontSave };
            Camera camera = gameObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.cullingMask = 0;
            var priorTarget = new RenderTexture(4, 4, 0);
            priorTarget.Create();
            camera.targetTexture = priorTarget;
            var target = RenderTexture.GetTemporary(320, 180, 0, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            Texture2D sharp = null, blurred = null, preview = null;
            string output = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Results");
            Directory.CreateDirectory(output);
            var options = new CaptureOptions
            {
                Camera = camera, Width = 320, Height = 180, Filter = CaptureFilter.BackgroundBlur,
                BlurStrength = 0, Text = "TEST", Font = AssetDatabase.LoadAssetAtPath<Font>("Assets/CameraCaptureStudio/Fonts/BRUSHSCI.TTF"),
                FontSize = 48, TextColor = Color.white, Placement = TextPlacement.Center, Margin = 10,
                Format = CaptureFormat.Png, Directory = output, FileName = "blur-text", JpegQuality = 90
            };
            try
            {
                Check(options.Font != null, "Test font unavailable");
                RenderTexture.active = priorTarget;
                CameraCaptureProcessor.RenderPreview(options, target);
                sharp = Read(target);
                options.BlurStrength = 100;
                CameraCaptureProcessor.RenderPreview(options, target);
                blurred = Read(target);
                Check(camera.targetTexture == priorTarget && RenderTexture.active == priorTarget, "Camera render state was not restored");
                Check(Difference(sharp, blurred) < 0.002f, "Background blur affected overlay text");
                int whitePixels = 0;
                foreach (Color c in blurred.GetPixels()) if (c.r > 0.8f) whitePixels++;
                Check(whitePixels > 40, "Overlay text was not drawn");
                string first = CameraCaptureProcessor.Capture(options, out preview);
                UnityEngine.Object.DestroyImmediate(preview); preview = null;
                string second = CameraCaptureProcessor.Capture(options, out preview);
                UnityEngine.Object.DestroyImmediate(preview); preview = null;
                Check(first != second && File.Exists(first) && File.Exists(second), "Duplicate capture overwrote a file");
                byte[] png = File.ReadAllBytes(first);
                Check(png.Length > 8 && png[0] == 137 && png[1] == 80, "PNG export failed");
                options.Format = CaptureFormat.Jpg;
                options.FileName = "blur-text-jpg";
                string jpg = CameraCaptureProcessor.Capture(options, out preview);
                byte[] jpeg = File.ReadAllBytes(jpg);
                Check(jpeg.Length > 2 && jpeg[0] == 255 && jpeg[1] == 216, "JPG export failed");
                Check(camera.targetTexture == priorTarget && RenderTexture.active == priorTarget, "Export did not restore render state");
            }
            finally
            {
                RenderTexture.active = previous;
                camera.targetTexture = null;
                if (sharp != null) UnityEngine.Object.DestroyImmediate(sharp);
                if (blurred != null) UnityEngine.Object.DestroyImmediate(blurred);
                if (preview != null) UnityEngine.Object.DestroyImmediate(preview);
                RenderTexture.ReleaseTemporary(target);
                priorTarget.Release(); UnityEngine.Object.DestroyImmediate(priorTarget);
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        private static void MixedFontChecks()
        {
            Font script = AssetDatabase.LoadAssetAtPath<Font>("Assets/CameraCaptureStudio/Fonts/BRUSHSCI.TTF");
            Font sans = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Check(script != null && sans != null && script != sans, "Two test fonts unavailable");
            var options = new CaptureOptions { Font = script, FontSize = 48, TextSegments = new List<CaptureTextSegment>
            {
                new CaptureTextSegment { Text = "AB " }, new CaptureTextSegment { Text = "CDE", Font = sans }
            }};
            CaptureTextRenderer.Layout layout = CaptureTextRenderer.BuildLayout(options);
            Check(layout.Runs.Count == 2 && layout.Runs[0].Font == script && layout.Runs[1].Font == sans, "Per-segment font selection/fallback failed");
            Check(layout.Runs[1].Glyphs[0].X > layout.Runs[0].Glyphs[1].X, "Segments did not join in order");
            options.TextSegments = new List<CaptureTextSegment> { new CaptureTextSegment { Text = "A\r" }, new CaptureTextSegment { Text = "\nB", Font = sans } };
            layout = CaptureTextRenderer.BuildLayout(options);
            Check(layout.LineCount == 2 && layout.Runs[1].Glyphs[0].Line == 1 && layout.Runs[1].Glyphs[0].X == 0, "Newline split across segments failed");
            var unicode = new CaptureTextSegment { Text = "A\U0001F600e\u0301中", Font = sans, FontSize = 72, UseCustomPosition = true, Placement = TextPlacement.TopRight, OffsetX = 20, OffsetY = -10 };
            CaptureTextSegment right = unicode.Split(2);
            Check(unicode.Text == "A\U0001F600" && right.Text == "e\u0301中" && right.Font == sans && right.FontSize == 72 && right.UseCustomPosition && right.Placement == TextPlacement.TopRight && right.OffsetX == 20 && right.OffsetY == -10, "Split damaged a Unicode character or font assignment");
            options.Font = null;
            options.TextSegments = new List<CaptureTextSegment> { new CaptureTextSegment { Text = "TEST", Font = script } };
            CaptureTextRenderer.Validate(options);
            options.TextSegments[0].Font = null;
            bool rejected = false;
            try { CaptureTextRenderer.Validate(options); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "Missing segment/default font was not rejected");
            options.TextSegments[0].Text = "";
            CaptureTextRenderer.Validate(options);
            Check(!CaptureTextRenderer.HasText(options), "Empty segment incorrectly required text rendering");

            var gameObject = new GameObject("MixedFontCamera") { hideFlags = HideFlags.HideAndDontSave };
            Camera camera = gameObject.AddComponent<Camera>();
            camera.enabled = false; camera.cullingMask = 0;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            var target = RenderTexture.GetTemporary(640, 360, 0, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            Texture2D mixed = null, uniform = null, oneRun = null, splitRun = null, blurred = null, scaled = null;
            Font chinese = null;
            options.Camera = camera; options.Width = 640; options.Height = 360;
            options.Font = script; options.TextColor = Color.white; options.Placement = TextPlacement.Center;
            options.TextSegments = new List<CaptureTextSegment> { new CaptureTextSegment { Text = "HELLO " }, new CaptureTextSegment { Text = "WORLD", Font = sans } };
            try
            {
                CameraCaptureProcessor.RenderPreview(options, target); mixed = Read(target);
                int visible = 0; foreach (Color c in mixed.GetPixels()) if (c.r > 0.8f) visible++;
                Check(visible > 200, "Mixed-font overlay was not drawn");
                options.TextSegments[1].Font = script;
                CameraCaptureProcessor.RenderPreview(options, target); uniform = Read(target);
                Check(Difference(mixed, uniform) > 0.002f, "Changing a segment font did not affect the image");
                options.TextSegments = new List<CaptureTextSegment> { new CaptureTextSegment { Text = "HELLO WORLD" } };
                CameraCaptureProcessor.RenderPreview(options, target); oneRun = Read(target);
                options.TextSegments = new List<CaptureTextSegment> { new CaptureTextSegment { Text = "HELLO " }, new CaptureTextSegment { Text = "WORLD" } };
                CameraCaptureProcessor.RenderPreview(options, target); splitRun = Read(target);
                Check(Difference(oneRun, splitRun) < 0.001f, "Splitting a same-font line changed spacing or glyphs");
                options.Filter = CaptureFilter.BackgroundBlur; options.BlurStrength = 100;
                CameraCaptureProcessor.RenderPreview(options, target); blurred = Read(target);
                Check(Difference(splitRun, blurred) < 0.001f, "Blur affected mixed-font text");
                options.Font = null;
                options.TextSegments[0].Font = script; options.TextSegments[1].Font = sans;
                options.Width = 1280; options.Height = 720;
                CameraCaptureProcessor.RenderPreview(options, target); scaled = Read(target);
                visible = 0; foreach (Color c in scaled.GetPixels()) if (c.r > 0.8f) visible++;
                Check(visible > 40, "Scaled mixed-font preview disappeared without a default font");
                foreach (string candidate in new[] { "Microsoft YaHei", "Microsoft YaHei UI", "SimSun", "Noto Sans CJK SC" })
                    if (Array.IndexOf(Font.GetOSInstalledFontNames(), candidate) >= 0) { chinese = Font.CreateDynamicFontFromOSFont(candidate, 48); break; }
                options.Width = 640; options.Height = 360;
                if (chinese != null)
                {
                    options.TextSegments = new List<CaptureTextSegment> { new CaptureTextSegment { Text = "中文", Font = chinese }, new CaptureTextSegment { Text = " Unity", Font = script } };
                    CameraCaptureProcessor.RenderPreview(options, target);
                    Texture2D cjk = Read(target);
                    try
                    {
                        visible = 0; foreach (Color c in cjk.GetPixels()) if (c.r > 0.8f) visible++;
                        Check(visible > 200, "Chinese/English mixed-font text did not render");
                        string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Results");
                        Directory.CreateDirectory(folder);
                        File.WriteAllBytes(Path.Combine(folder, "mixed-font-chinese-preview.png"), cjk.EncodeToPNG());
                    }
                    finally { UnityEngine.Object.DestroyImmediate(cjk); }
                }
                options.TextSegments = new List<CaptureTextSegment> { new CaptureTextSegment { Text = "HELLO ", Font = script }, new CaptureTextSegment { Text = "WORLD", Font = sans } };
                options.Directory = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Results");
                options.FileName = "mixed-font-export"; options.Format = CaptureFormat.Png;
                string saved = CameraCaptureProcessor.Capture(options, out Texture2D thumbnail);
                UnityEngine.Object.DestroyImmediate(thumbnail);
                var loaded = new Texture2D(2, 2);
                try
                {
                    Check(loaded.LoadImage(File.ReadAllBytes(saved)) && loaded.width == 640 && loaded.height == 360, "Mixed-font PNG export failed");
                    Check(Difference(mixed, loaded) < 0.001f, "Mixed-font preview and export differ");
                }
                finally { UnityEngine.Object.DestroyImmediate(loaded); }
                Debug.Log("MIXED_FONT_SMOKE_PASS");
            }
            finally
            {
                RenderTexture.active = previous;
                foreach (Texture2D image in new[] { mixed, uniform, oneRun, splitRun, blurred, scaled }) if (image != null) UnityEngine.Object.DestroyImmediate(image);
                if (chinese != null) UnityEngine.Object.DestroyImmediate(chinese);
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(gameObject);
            }

            var window = ScriptableObject.CreateInstance<CameraCaptureWindow>();
            var restored = ScriptableObject.CreateInstance<CameraCaptureWindow>();
            try
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                Type type = typeof(CameraCaptureWindow);
                FieldInfo textField = type.GetField("overlayText", flags), segmentsField = type.GetField("textSegments", flags);
                type.GetField("font", flags).SetValue(window, script);
                textField.SetValue(window, "HELLO WORLD");
                MethodInfo switchMode = type.GetMethod("SetTextMode", flags);
                switchMode.Invoke(window, new object[] { true });
                var segments = (List<CaptureTextSegment>)segmentsField.GetValue(window);
                Check(segments.Count == 1 && segments[0].Text == "HELLO WORLD", "Legacy text did not migrate");
                segments.Add(segments[0].Split(5)); segments[1].Font = sans; segments[1].FontSize = 72; segments[1].UseCustomPosition = true; segments[1].Placement = TextPlacement.TopRight; segments[1].OffsetX = -20; segments[1].OffsetY = 10;
                switchMode.Invoke(window, new object[] { false }); switchMode.Invoke(window, new object[] { true });
                Check(segments.Count == 2 && segments[1].Font == sans && segments[1].FontSize == 72 && segments[1].UseCustomPosition && segments[1].OffsetX == -20, "Mode switch lost font assignments");
                EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(window), restored);
                var restoredSegments = (List<CaptureTextSegment>)segmentsField.GetValue(restored);
                Check(restoredSegments.Count == 2 && restoredSegments[1].Font == sans && restoredSegments[1].FontSize == 72 && restoredSegments[1].UseCustomPosition && restoredSegments[1].Placement == TextPlacement.TopRight && restoredSegments[1].OffsetX == -20 && restoredSegments[1].OffsetY == 10 && CaptureTextSegment.Join(restoredSegments) == "HELLO WORLD", "Window serialization lost segment text/fonts");
                switchMode.Invoke(window, new object[] { false }); textField.SetValue(window, "NEW"); switchMode.Invoke(window, new object[] { true });
                segments = (List<CaptureTextSegment>)segmentsField.GetValue(window);
                Check(segments.Count == 1 && segments[0].Text == "NEW", "Mode switch restored stale text after an edit");
            }
            finally { UnityEngine.Object.DestroyImmediate(window); UnityEngine.Object.DestroyImmediate(restored); }
        }

        private static void SegmentSizeChecks()
        {
            Font font = AssetDatabase.LoadAssetAtPath<Font>("Assets/CameraCaptureStudio/Fonts/BRUSHSCI.TTF");
            var options = new CaptureOptions { FontSize = 48, TextSegments = new List<CaptureTextSegment>
            {
                new CaptureTextSegment { Text = "small ", Font = font, FontSize = 24 },
                new CaptureTextSegment { Text = "BIG", Font = font, FontSize = 96 },
                new CaptureTextSegment { Text = " A", Font = font }
            }};
            var layout = CaptureTextRenderer.BuildLayout(options);
            Check(layout.Runs[0].FontSize == 24 && layout.Runs[1].FontSize == 96 && layout.Runs[2].FontSize == 48, "Size inheritance failed");
            CharacterInfo small = layout.Runs[0].Glyphs[0].Character, large = layout.Runs[1].Glyphs[0].Character;
            Check(large.maxY - large.minY > 2 * (small.maxY - small.minY), "Custom size did not enlarge glyphs");
            var half = CaptureTextRenderer.ScaleSegments(options.TextSegments, 0.5f);
            Check(half[0].FontSize == 12 && half[1].FontSize == 48 && half[2].FontSize == 0 && options.TextSegments[1].FontSize == 96, "Preview mutated sizes or lost inheritance");
            var go = new GameObject("SizeCamera") { hideFlags = HideFlags.HideAndDontSave };
            Camera camera = go.AddComponent<Camera>(); camera.enabled = false; camera.cullingMask = 0;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            var target = RenderTexture.GetTemporary(640, 360, 0);
            RenderTexture previous = RenderTexture.active;
            Texture2D original = null, resized = null, direct = null;
            options.Camera = camera; options.Width = 640; options.Height = 360;
            options.TextColor = Color.white; options.Placement = TextPlacement.Center;
            try
            {
                CameraCaptureProcessor.RenderPreview(options, target); original = Read(target);
                options.Width = 1280; options.Height = 720;
                CameraCaptureProcessor.RenderPreview(options, target); resized = Read(target);
                options.Width = 640; options.Height = 360; options.FontSize = 24; options.TextSegments = half;
                CameraCaptureProcessor.RenderPreview(options, target); direct = Read(target);
                Check(Difference(resized, direct) < 0.001f, "Custom sizes do not scale correctly");
                options.FontSize = 48; options.TextSegments[0].FontSize = 24; options.TextSegments[1].FontSize = 96;
                options.Directory = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Results"); options.FileName = "mixed-size-export";
                foreach (CaptureFormat format in new[] { CaptureFormat.Png, CaptureFormat.Jpg })
                {
                    options.Format = format; options.JpegQuality = 95;
                    string saved = CameraCaptureProcessor.Capture(options, out Texture2D thumbnail);
                    UnityEngine.Object.DestroyImmediate(thumbnail);
                    var image = new Texture2D(2, 2);
                    try { Check(image.LoadImage(File.ReadAllBytes(saved)) && Difference(original, image) < 0.015f, "Custom-size export differs: " + format); }
                    finally { UnityEngine.Object.DestroyImmediate(image); }
                }
                Debug.Log("SEGMENT_SIZE_SMOKE_PASS");
            }
            finally
            {
                RenderTexture.active = previous;
                foreach (Texture2D image in new[] { original, resized, direct }) if (image != null) UnityEngine.Object.DestroyImmediate(image);
                RenderTexture.ReleaseTemporary(target); UnityEngine.Object.DestroyImmediate(go);
            }
        }

        private static RectInt InkBounds(Texture2D image)
        {
            Color32[] pixels = image.GetPixels32();
            int minX = image.width, minY = image.height, maxX = -1, maxY = -1;
            for (int y = 0; y < image.height; y++) for (int x = 0; x < image.width; x++)
                if (pixels[y * image.width + x].r > 40)
                { minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y); }
            return maxX < 0 ? new RectInt() : new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        private static void SegmentPositionChecks()
        {
            Font font = AssetDatabase.LoadAssetAtPath<Font>("Assets/CameraCaptureStudio/Fonts/BRUSHSCI.TTF");
            var first = new CaptureTextSegment { Text = "BASE", Font = font, UseCustomPosition = true, Placement = TextPlacement.Center };
            var options = new CaptureOptions { FontSize = 48, TextColor = Color.white, Margin = 40,
                Placement = TextPlacement.Center, Width = 640, Height = 360, TextSegments = new List<CaptureTextSegment> { first } };
            var go = new GameObject("PositionCamera") { hideFlags = HideFlags.HideAndDontSave };
            Camera camera = go.AddComponent<Camera>(); camera.enabled = false; camera.cullingMask = 0;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            options.Camera = camera;
            var target = RenderTexture.GetTemporary(640, 360, 0);
            RenderTexture previous = RenderTexture.active;
            Texture2D original = null, shifted = null, combined = null, scaled = null, direct = null;
            try
            {
                // Exercise every grid location with only independently positioned text.
                for (int placement = 0; placement < 9; placement++)
                {
                    first.Placement = (TextPlacement)placement;
                    CameraCaptureProcessor.RenderPreview(options, target);
                    Texture2D image = Read(target);
                    try
                    {
                        RectInt bounds = InkBounds(image); int col = placement % 3, row = placement / 3;
                        bool horizontal = col == 0 ? bounds.xMax < 320 : col == 2 ? bounds.xMin > 320 : Mathf.Abs(bounds.center.x - 320) < 6;
                        bool vertical = row == 0 ? bounds.yMin > 180 : row == 2 ? bounds.yMax < 180 : Mathf.Abs(bounds.center.y - 180) < 6;
                        Check(bounds.width > 0 && horizontal && vertical, "Independent grid position failed: " + placement);
                    }
                    finally { UnityEngine.Object.DestroyImmediate(image); }
                }
                first.Placement = TextPlacement.Center;
                CameraCaptureProcessor.RenderPreview(options, target); original = Read(target);
                first.OffsetX = 17; first.OffsetY = -11;
                CameraCaptureProcessor.RenderPreview(options, target); shifted = Read(target);
                RectInt originalBounds = InkBounds(original), shiftedBounds = InkBounds(shifted);
                Check(shiftedBounds.xMin == originalBounds.xMin + 17 && shiftedBounds.yMin == originalBounds.yMin - 11,
                    "Pixel offset direction/amount is incorrect");
                first.OffsetX = first.OffsetY = 0; first.UseCustomPosition = false;
                float inlineWidth = CaptureTextRenderer.BuildLayout(options).Bounds.width;
                var corner = new CaptureTextSegment { Text = "CORNER", Font = font, FontSize = 32,
                    UseCustomPosition = true, Placement = TextPlacement.TopRight, OffsetX = -20, OffsetY = -12 };
                options.TextSegments.Add(corner);
                var layout = CaptureTextRenderer.BuildLayout(options);
                Check(layout.Runs.Count == 2 && layout.Bounds.width == inlineWidth, "Independent text changed inline spacing");
                CameraCaptureProcessor.RenderPreview(options, target); combined = Read(target);
                Color32[] a = original.GetPixels32(), b = combined.GetPixels32(); bool unchanged = true;
                for (int y = 100; y < 240 && unchanged; y++) for (int x = 160; x < 480; x++)
                    if (!a[y * 640 + x].Equals(b[y * 640 + x])) { unchanged = false; break; }
                Check(unchanged && Difference(original, combined) > 0.001f, "Moving a separate segment affected existing text or was not drawn");
                List<CaptureTextSegment> half = CaptureTextRenderer.ScaleSegments(options.TextSegments, 0.5f);
                Check(half[1].UseCustomPosition && half[1].Placement == TextPlacement.TopRight && half[1].OffsetX == -10 && half[1].OffsetY == -6
                    && corner.OffsetX == -20 && corner.OffsetY == -12, "Preview scaling changed position settings");
                options.Width = 1280; options.Height = 720;
                CameraCaptureProcessor.RenderPreview(options, target); scaled = Read(target);
                options.Width = 640; options.Height = 360; options.FontSize = 24; options.Margin = 20; options.TextSegments = half;
                CameraCaptureProcessor.RenderPreview(options, target); direct = Read(target);
                Check(Difference(scaled, direct) < 0.001f, "Position offsets do not scale with preview resolution");
                options.FontSize = 48; options.Margin = 40; options.TextSegments = new List<CaptureTextSegment> { first, corner };
                options.Directory = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Results"); options.FileName = "segment-position-export";
                foreach (CaptureFormat format in new[] { CaptureFormat.Png, CaptureFormat.Jpg })
                {
                    options.Format = format; options.JpegQuality = 95;
                    string saved = CameraCaptureProcessor.Capture(options, out Texture2D thumbnail);
                    UnityEngine.Object.DestroyImmediate(thumbnail);
                    var image = new Texture2D(2, 2);
                    try { Check(image.LoadImage(File.ReadAllBytes(saved)) && Difference(combined, image) < 0.015f, "Position export differs: " + format); }
                    finally { UnityEngine.Object.DestroyImmediate(image); }
                }
                Debug.Log("SEGMENT_POSITION_SMOKE_PASS");
            }
            finally
            {
                RenderTexture.active = previous;
                foreach (Texture2D image in new[] { original, shifted, combined, scaled, direct }) if (image != null) UnityEngine.Object.DestroyImmediate(image);
                RenderTexture.ReleaseTemporary(target); UnityEngine.Object.DestroyImmediate(go);
            }
        }

        public static void Run()
        {
            try
            {
                Check(Shader.Find("Hidden/CameraCaptureStudio/Filter").isSupported, "Filter shader unsupported");
                Check(Shader.Find("Hidden/CameraCaptureStudio/Text").isSupported, "Text shader unsupported");
                PaletteChecks();
                BlurChecks();
                ScaleChecks();
                CameraChecks();
                MixedFontChecks();
                SegmentSizeChecks();
                SegmentPositionChecks();
                Debug.Log("FILTER_SMOKE_PASS checks=" + checks);
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }
    }
}
