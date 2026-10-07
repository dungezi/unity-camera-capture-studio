using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CameraCaptureStudio
{
    internal enum CaptureFilter
    {
        None,
        BlackAndWhite,
        VintageFilm,
        TealAndOrange,
        WarmSunlight,
        CoolMood,
        Sepia,
        HighContrastBlackAndWhite,
        BackgroundBlur
    }

    internal enum TextPlacement
    {
        TopLeft,
        TopCenter,
        TopRight,
        MiddleLeft,
        Center,
        MiddleRight,
        BottomLeft,
        BottomCenter,
        BottomRight
    }

    internal enum CaptureFormat { Png, Jpg }

    internal sealed class CaptureOptions
    {
        internal Camera Camera;
        internal int Width;
        internal int Height;
        internal CaptureFilter Filter;
        internal bool PreserveRed = true;
        internal float BlurStrength = 40f;
        internal string Text;
        internal Font Font;
        internal int FontSize;
        internal Color TextColor;
        internal TextPlacement Placement;
        internal int Margin;
        internal CaptureFormat Format;
        internal int JpegQuality;
        internal string Directory;
        internal string FileName;
        internal bool KeepSkinnedMeshesUpdated;
        internal bool PreserveTransparency;
    }

    internal static class CameraCaptureProcessor
    {
        internal static string Capture(CaptureOptions options, out Texture2D preview)
        {
            Validate(options);
            preview = null;
            RenderTexture finishedTarget = null;
            Texture2D fullResolution = null;
            RenderTexture previousActive = RenderTexture.active;
            try
            {
                finishedTarget = RenderTexture.GetTemporary(options.Width, options.Height, 0, RenderTextureFormat.ARGB32);
                if (!finishedTarget.IsCreated() && !finishedTarget.Create())
                    throw new InvalidOperationException("无法分配导出纹理；请降低分辨率或释放显存。");
                finishedTarget.filterMode = FilterMode.Bilinear;
                RenderFrame(options, finishedTarget);

                RenderTexture.active = finishedTarget;
                TextureFormat textureFormat = options.Format == CaptureFormat.Png ? TextureFormat.RGBA32 : TextureFormat.RGB24;
                fullResolution = new Texture2D(options.Width, options.Height, textureFormat, false);
                fullResolution.ReadPixels(new Rect(0, 0, options.Width, options.Height), 0, 0);
                fullResolution.Apply(false, false);

                byte[] bytes = options.Format == CaptureFormat.Png
                    ? fullResolution.EncodeToPNG()
                    : fullResolution.EncodeToJPG(options.JpegQuality);
                if (bytes == null || bytes.Length == 0)
                    throw new InvalidOperationException("图片编码失败。");

                preview = CreateSmallPreview(finishedTarget);
                string path = GetAvailablePath(options);
                File.WriteAllBytes(path, bytes);
                if (path.StartsWith(Application.dataPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    AssetDatabase.Refresh();
                return path;
            }
            catch
            {
                if (preview != null)
                {
                    UnityEngine.Object.DestroyImmediate(preview);
                    preview = null;
                }
                throw;
            }
            finally
            {
                RenderTexture.active = previousActive;
                if (fullResolution != null) UnityEngine.Object.DestroyImmediate(fullResolution);
                if (finishedTarget != null) RenderTexture.ReleaseTemporary(finishedTarget);
            }
        }

        internal static void RenderPreview(CaptureOptions options, RenderTexture target)
        {
            if (options.Camera == null || target == null || options.Width < 1 || options.Height < 1) return;
            float scale = Mathf.Min((float)target.width / options.Width, (float)target.height / options.Height);
            var scaled = new CaptureOptions
            {
                Camera = options.Camera,
                Width = target.width,
                Height = target.height,
                Filter = options.Filter,
                PreserveRed = options.PreserveRed,
                BlurStrength = options.BlurStrength,
                Text = options.Font == null ? null : options.Text,
                Font = options.Font,
                FontSize = Mathf.Max(1, Mathf.RoundToInt(options.FontSize * scale)),
                TextColor = options.TextColor,
                Placement = options.Placement,
                Margin = Mathf.RoundToInt(options.Margin * scale),
                KeepSkinnedMeshesUpdated = options.KeepSkinnedMeshesUpdated,
                PreserveTransparency = options.PreserveTransparency
            };
            RenderFrame(scaled, target);
        }

        private static void RenderFrame(CaptureOptions options, RenderTexture target)
        {
            RenderTexture cameraTarget = null;
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture previousCameraTarget = options.Camera.targetTexture;
            var changedRenderers = new List<SkinnedMeshRenderer>();
            try
            {
                if (options.KeepSkinnedMeshesUpdated)
                {
                    foreach (SkinnedMeshRenderer renderer in UnityEngine.Object.FindObjectsOfType<SkinnedMeshRenderer>())
                    {
                        if (!renderer.enabled || renderer.updateWhenOffscreen ||
                            (options.Camera.cullingMask & (1 << renderer.gameObject.layer)) == 0)
                            continue;
                        renderer.updateWhenOffscreen = true;
                        changedRenderers.Add(renderer);
                    }
                }

                cameraTarget = RenderTexture.GetTemporary(options.Width, options.Height, 24, RenderTextureFormat.ARGB32);
                if (!cameraTarget.IsCreated() && !cameraTarget.Create())
                    throw new InvalidOperationException("无法分配相机纹理；请降低分辨率或释放显存。");
                cameraTarget.filterMode = FilterMode.Bilinear;
                cameraTarget.wrapMode = TextureWrapMode.Clamp;
                options.Camera.targetTexture = cameraTarget;
                options.Camera.Render();
                options.Camera.targetTexture = previousCameraTarget;

                ApplyFilter(cameraTarget, target, options);
                if (!string.IsNullOrEmpty(options.Text)) DrawText(target, options);
            }
            finally
            {
                options.Camera.targetTexture = previousCameraTarget;
                RenderTexture.active = previousActive;
                foreach (SkinnedMeshRenderer renderer in changedRenderers)
                    if (renderer != null) renderer.updateWhenOffscreen = false;
                if (cameraTarget != null) RenderTexture.ReleaseTemporary(cameraTarget);
            }
        }

        internal static void ApplyFilter(RenderTexture source, RenderTexture target, CaptureOptions options)
        {
            Shader shader = Shader.Find("Hidden/CameraCaptureStudio/Filter");
            if (shader == null || !shader.isSupported)
                throw new InvalidOperationException("找不到可用的滤镜 Shader，请重新导入插件。");
            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            RenderTexture horizontal = null;
            RenderTexture vertical = null;
            RenderTexture previousActive = RenderTexture.active;
            try
            {
                material.SetFloat("_Preset", (int)options.Filter);
                material.SetFloat("_PreserveAlpha", options.PreserveTransparency ? 1f : 0f);
                material.SetFloat("_PreserveRed", options.PreserveRed ? 1f : 0f);
                float strength = Mathf.Clamp(options.BlurStrength, 0f, 100f);
                if (options.Filter != CaptureFilter.BackgroundBlur || strength <= 0f)
                {
                    Graphics.Blit(source, target, material, 0);
                    return;
                }

                // Sigma is relative to the short edge, so preview and export share the same framing.
                float sigma = strength * 0.00025f * Mathf.Min(source.width, source.height);
                int downsample = Mathf.Max(1, Mathf.FloorToInt(sigma / 2f));
                int blurWidth = Mathf.Max(1, Mathf.CeilToInt((float)source.width / downsample));
                int blurHeight = Mathf.Max(1, Mathf.CeilToInt((float)source.height / downsample));
                // Preserve faint transparent colors across repeated premultiplied passes.
                RenderTextureFormat blurFormat = options.PreserveTransparency &&
                    SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf)
                    ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32;
                horizontal = RenderTexture.GetTemporary(blurWidth, blurHeight, 0, blurFormat);
                vertical = RenderTexture.GetTemporary(blurWidth, blurHeight, 0, blurFormat);
                horizontal.filterMode = vertical.filterMode = FilterMode.Bilinear;
                horizontal.wrapMode = vertical.wrapMode = TextureWrapMode.Clamp;
                if ((!horizontal.IsCreated() && !horizontal.Create()) ||
                    (!vertical.IsCreated() && !vertical.Create()))
                    throw new InvalidOperationException("无法分配模糊纹理；请降低分辨率或释放显存。");

                // Three separable Gaussian iterations; the nine-tap kernel has sigma about 1.689.
                float step = sigma / (1.689f * Mathf.Sqrt(3f));
                for (int iteration = 0; iteration < 3; iteration++)
                {
                    material.SetFloat("_PremultiplyInput", iteration == 0 && options.PreserveTransparency ? 1f : 0f);
                    material.SetVector("_BlurStep", new Vector4(step / source.width, 0f, 0f, 0f));
                    Graphics.Blit(iteration == 0 ? source : vertical, horizontal, material, 1);
                    material.SetFloat("_PremultiplyInput", 0f);
                    material.SetVector("_BlurStep", new Vector4(0f, step / source.height, 0f, 0f));
                    Graphics.Blit(horizontal, vertical, material, 1);
                }
                material.SetFloat("_Unpremultiply", options.PreserveTransparency ? 1f : 0f);
                Graphics.Blit(vertical, target, material, 0);
            }
            finally
            {
                RenderTexture.active = previousActive;
                if (horizontal != null) RenderTexture.ReleaseTemporary(horizontal);
                if (vertical != null) RenderTexture.ReleaseTemporary(vertical);
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        private static Texture2D CreateSmallPreview(RenderTexture source)
        {
            float scale = Mathf.Min(1f, Mathf.Min(640f / source.width, 360f / source.height));
            int width = Mathf.Max(1, Mathf.RoundToInt(source.width * scale));
            int height = Mathf.Max(1, Mathf.RoundToInt(source.height * scale));
            RenderTexture temporary = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            RenderTexture previousActive = RenderTexture.active;
            Texture2D image = null;
            try
            {
                Graphics.Blit(source, temporary);
                RenderTexture.active = temporary;
                image = new Texture2D(width, height, TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply(false, false);
                return image;
            }
            catch
            {
                if (image != null) UnityEngine.Object.DestroyImmediate(image);
                throw;
            }
            finally
            {
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(temporary);
            }
        }

        private static void Validate(CaptureOptions options)
        {
            if (options.Camera == null || EditorUtility.IsPersistent(options.Camera))
                throw new ArgumentException("请选择场景中的相机。");
            int max = Mathf.Min(SystemInfo.maxTextureSize, 16384);
            if (options.Width < 1 || options.Height < 1 || options.Width > max || options.Height > max)
                throw new ArgumentException($"分辨率必须在 1 到 {max} 像素之间。");
            if (!string.IsNullOrEmpty(options.Text) && options.Font == null)
                throw new ArgumentException("嵌字需要选择字体。");
            if (string.IsNullOrWhiteSpace(options.Directory))
                throw new ArgumentException("请指定输出目录。");
        }

        private static string GetAvailablePath(CaptureOptions options)
        {
            string directory = Path.GetFullPath(options.Directory);
            DirectoryInfo info = Directory.CreateDirectory(directory);
            string fileName = string.IsNullOrWhiteSpace(options.FileName) ? "capture" : options.FileName.Trim();
            foreach (char invalid in Path.GetInvalidFileNameChars())
                fileName = fileName.Replace(invalid, '_');
            fileName = Path.GetFileNameWithoutExtension(fileName).TrimEnd(' ', '.');
            if (fileName.Length == 0) fileName = "capture";
            string extension = options.Format == CaptureFormat.Png ? ".png" : ".jpg";
            string path = Path.Combine(info.FullName, fileName + extension);
            for (int suffix = 2; File.Exists(path); suffix++)
                path = Path.Combine(info.FullName, fileName + "_" + suffix + extension);
            return path;
        }

        private static void DrawText(RenderTexture target, CaptureOptions options)
        {
            Shader shader = Shader.Find("Hidden/CameraCaptureStudio/Text");
            if (shader == null)
                throw new InvalidOperationException("找不到嵌字 Shader，请重新导入插件。");

            var settings = new TextGenerationSettings
            {
                font = options.Font,
                color = Color.white,
                fontSize = options.FontSize,
                fontStyle = FontStyle.Normal,
                lineSpacing = 1f,
                richText = false,
                scaleFactor = 1f,
                textAnchor = TextAnchor.UpperLeft,
                alignByGeometry = false,
                resizeTextForBestFit = false,
                updateBounds = true,
                horizontalOverflow = HorizontalWrapMode.Overflow,
                verticalOverflow = VerticalWrapMode.Overflow,
                generationExtents = new Vector2(target.width, target.height),
                pivot = Vector2.zero
            };
            var generator = new TextGenerator();
            if (!generator.Populate(options.Text, settings))
                throw new InvalidOperationException("字体无法生成嵌字。请换用支持这些字符的字体。");

            IList<UIVertex> vertices = generator.verts;
            if (vertices.Count < 4) return;
            float minX = float.PositiveInfinity, minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
            foreach (UIVertex vertex in vertices)
            {
                minX = Mathf.Min(minX, vertex.position.x);
                minY = Mathf.Min(minY, vertex.position.y);
                maxX = Mathf.Max(maxX, vertex.position.x);
                maxY = Mathf.Max(maxY, vertex.position.y);
            }

            float left = options.Margin;
            float bottom = options.Margin;
            float width = maxX - minX;
            float height = maxY - minY;
            switch (options.Placement)
            {
                case TextPlacement.TopCenter:
                case TextPlacement.Center:
                case TextPlacement.BottomCenter: left = (target.width - width) * 0.5f; break;
                case TextPlacement.TopRight:
                case TextPlacement.MiddleRight:
                case TextPlacement.BottomRight: left = target.width - options.Margin - width; break;
            }
            switch (options.Placement)
            {
                case TextPlacement.TopLeft:
                case TextPlacement.TopCenter:
                case TextPlacement.TopRight: bottom = target.height - options.Margin - height; break;
                case TextPlacement.MiddleLeft:
                case TextPlacement.Center:
                case TextPlacement.MiddleRight: bottom = (target.height - height) * 0.5f; break;
            }

            Texture atlas = options.Font.material != null ? options.Font.material.mainTexture : null;
            if (atlas == null)
                throw new InvalidOperationException("无法读取字体图集。请选择动态 TTF/OTF 字体。");
            Material material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                material.SetTexture("_MainTex", atlas);
                material.SetColor("_Color", options.TextColor);
                RenderTexture.active = target;
                GL.PushMatrix();
                try
                {
                    GL.LoadPixelMatrix(0, target.width, 0, target.height);
                    if (!material.SetPass(0))
                        throw new InvalidOperationException("嵌字材质无法渲染。");
                    GL.Begin(GL.TRIANGLES);
                    for (int i = 0; i + 3 < vertices.Count; i += 4)
                    {
                        Emit(vertices[i], left - minX, bottom - minY);
                        Emit(vertices[i + 1], left - minX, bottom - minY);
                        Emit(vertices[i + 2], left - minX, bottom - minY);
                        Emit(vertices[i + 2], left - minX, bottom - minY);
                        Emit(vertices[i + 3], left - minX, bottom - minY);
                        Emit(vertices[i], left - minX, bottom - minY);
                    }
                    GL.End();
                }
                finally { GL.PopMatrix(); }
            }
            finally { UnityEngine.Object.DestroyImmediate(material); }
        }

        private static void Emit(UIVertex vertex, float offsetX, float offsetY)
        {
            GL.TexCoord2(vertex.uv0.x, vertex.uv0.y);
            GL.Vertex3(vertex.position.x + offsetX, vertex.position.y + offsetY, 0);
        }
    }
}
