using System;
using System.IO;
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
