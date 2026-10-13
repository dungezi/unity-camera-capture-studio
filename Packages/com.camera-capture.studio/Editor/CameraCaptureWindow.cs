using System;
using System.IO;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CameraCaptureStudio
{
    public sealed class CameraCaptureWindow : EditorWindow
    {
        private static readonly Vector2Int[] ResolutionPresets =
        {
            new Vector2Int(1920, 1080),
            new Vector2Int(3840, 2160),
            new Vector2Int(1080, 1080),
            new Vector2Int(1080, 1920),
            new Vector2Int(7680, 4320),
            new Vector2Int(8192, 4320),
            new Vector2Int(15360, 8640)
        };

        private static readonly string[] ResolutionNames =
        {
            "1920 × 1080 (Full HD)", "3840 × 2160 (4K)",
            "1080 × 1080 (方形)", "1080 × 1920 (竖屏)", "自定义",
            "7680 × 4320 (8K UHD)", "8192 × 4320 (8K DCI)",
            "15360 × 8640 (16K UHD，需硬件支持)"
        };

        private static readonly string[] FilterNames =
        {
            "无", "黑白", "复古胶片", "青橙电影", "暖阳", "冷调", "棕褐老照片",
            "高对比黑白（红色点缀）", "背景模糊"
        };

        private static readonly string[] PlacementNames =
        {
            "左上", "上中", "右上", "左中", "正中", "右中", "左下", "下中", "右下"
        };

        [SerializeField] private Camera selectedCamera;
        [SerializeField] private int resolutionIndex;
        [SerializeField] private int width = 1920;
        [SerializeField] private int height = 1080;
        [SerializeField] private CaptureFormat format = CaptureFormat.Png;
        [SerializeField] private bool preserveTransparency;
        [SerializeField] private int jpegQuality = 90;
        [SerializeField] private CaptureFilter filter;
        [SerializeField] private bool preserveRed = true;
        [SerializeField] private float blurStrength = 40f;
        [SerializeField] private string overlayText = "";
        [SerializeField] private bool useTextSegments;
        [SerializeField] private List<CaptureTextSegment> textSegments = new List<CaptureTextSegment>();
        [SerializeField] private Font font;
        [SerializeField] private int fontSize = 64;
        [SerializeField] private Color textColor = Color.white;
        [SerializeField] private TextPlacement placement = TextPlacement.BottomRight;
        [SerializeField] private int margin = 40;
        [SerializeField] private string outputDirectory;
        [SerializeField] private string fileName = "capture";
        [SerializeField] private bool livePreviewEnabled = true;
        [SerializeField] private bool keepSkinnedMeshesUpdated;

        private Vector2 scroll;
        private Texture2D preview;
        private RenderTexture livePreview;
        private double nextPreviewTime;
        private string previewError;
        private string lastSavedPath;
        private string error;

        [MenuItem("Window/相机截图工作室")]
        private static void Open()
        {
            CameraCaptureWindow window = GetWindow<CameraCaptureWindow>();
            window.titleContent = new GUIContent("相机截图");
            window.minSize = new Vector2(360, 520);
            window.Show();
        }

        private void OnEnable()
        {
            if (string.IsNullOrEmpty(outputDirectory))
            {
                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                outputDirectory = Path.Combine(projectRoot, "Captures");
            }
            if (font == null)
            {
                font = AssetDatabase.LoadAssetAtPath<Font>(
                    "Packages/com.camera-capture.studio/Fonts/BRUSHSCI.TTF");
                if (font == null)
                    font = AssetDatabase.LoadAssetAtPath<Font>(
                        "Assets/CameraCaptureStudio/Fonts/BRUSHSCI.TTF");
                if (font == null)
                {
                    string[] guids = AssetDatabase.FindAssets("BRUSHSCI t:Font");
                    if (guids.Length > 0)
                        font = AssetDatabase.LoadAssetAtPath<Font>(AssetDatabase.GUIDToAssetPath(guids[0]));
                }
            }
            if (selectedCamera == null && Selection.activeGameObject != null)
                selectedCamera = Selection.activeGameObject.GetComponent<Camera>();
            EditorApplication.update += UpdateLivePreview;
        }

        private void OnDisable()
        {
            EditorApplication.update -= UpdateLivePreview;
            if (preview != null)
                DestroyImmediate(preview);
            ReleaseLivePreview();
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("相机截图工作室", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("选择场景相机，设置输出与后处理，然后点击截图。", MessageType.Info);
            EditorGUILayout.Space(6);

            EditorGUILayout.LabelField("相机与画面", EditorStyles.boldLabel);
            selectedCamera = (Camera)EditorGUILayout.ObjectField("指定相机", selectedCamera, typeof(Camera), true);
            keepSkinnedMeshesUpdated = EditorGUILayout.Toggle("离屏蒙皮兼容（可选）", keepSkinnedMeshesUpdated);
            if (selectedCamera != null && !selectedCamera.enabled)
                EditorGUILayout.HelpBox("该相机组件已禁用。插件会手动渲染；如确有蒙皮剔除，可尝试离屏蒙皮兼容。", MessageType.Info);
            using (new EditorGUI.DisabledScope(selectedCamera == null))
            {
                if (GUILayout.Button("将相机对齐到当前场景视角")) AlignToSceneView();
            }
            if (GUILayout.Button("使用相机当前像素尺寸"))
            {
                if (selectedCamera != null && selectedCamera.pixelWidth > 0 && selectedCamera.pixelHeight > 0)
                {
                    width = selectedCamera.pixelWidth;
                    height = selectedCamera.pixelHeight;
                    resolutionIndex = 4;
                }
                else
                    error = "请先选择有有效像素尺寸的场景相机。";
            }

            int nextResolution = EditorGUILayout.Popup("分辨率", resolutionIndex, ResolutionNames);
            if (nextResolution != resolutionIndex)
            {
                resolutionIndex = nextResolution;
                if (resolutionIndex != 4)
                {
                    int presetIndex = resolutionIndex < 4 ? resolutionIndex : resolutionIndex - 1;
                    width = ResolutionPresets[presetIndex].x;
                    height = ResolutionPresets[presetIndex].y;
                }
            }
            if (resolutionIndex == 4)
            {
                width = EditorGUILayout.IntField("宽度 (px)", width);
                height = EditorGUILayout.IntField("高度 (px)", height);
            }
            EditorGUILayout.LabelField($"输出尺寸：{width} × {height} px", EditorStyles.miniLabel);
            int maxTextureSize = Mathf.Min(SystemInfo.maxTextureSize, 16384);
            if (width > maxTextureSize || height > maxTextureSize)
                EditorGUILayout.HelpBox($"当前设备的单张纹理最大边长为 {maxTextureSize} px；请降低尺寸。", MessageType.Warning);
            else if (width >= 7680 || height >= 4320)
                EditorGUILayout.HelpBox("8K 及更高分辨率需要较多显存和内存；实时预览会自动缩小，导出仍使用所选尺寸。", MessageType.Info);

            EditorGUILayout.Space(8);
            livePreviewEnabled = EditorGUILayout.Toggle("实时预览", livePreviewEnabled);
            if (livePreviewEnabled)
            {
                EditorGUILayout.LabelField("相机实时画面（包含滤镜与嵌字）", EditorStyles.boldLabel);
                if (livePreview != null)
                {
                    float previewWidth = Mathf.Max(1, position.width - 36);
                    float previewHeight = Mathf.Min(320, previewWidth * livePreview.height / livePreview.width);
                    Rect previewRect = GUILayoutUtility.GetRect(previewWidth, previewHeight);
                    EditorGUI.DrawPreviewTexture(previewRect, livePreview, null, ScaleMode.ScaleToFit);
                }
                else
                    EditorGUILayout.HelpBox("选择场景相机后将自动显示预览。", MessageType.Info);
                if (!string.IsNullOrEmpty(previewError))
                    EditorGUILayout.HelpBox(previewError, MessageType.Warning);
            }

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("后处理", EditorStyles.boldLabel);
            filter = (CaptureFilter)EditorGUILayout.Popup("滤镜预设", (int)filter, FilterNames);
            if (filter == CaptureFilter.HighContrastBlackAndWhite)
                preserveRed = EditorGUILayout.Toggle("保留红色点缀", preserveRed);
            if (filter == CaptureFilter.BackgroundBlur)
            {
                blurStrength = EditorGUILayout.Slider("模糊程度", blurStrength, 0f, 100f);
                EditorGUILayout.HelpBox("0 为原图。先模糊相机画面，再叠加嵌字，文字保持清晰。", MessageType.Info);
            }
            bool nextTextMode = EditorGUILayout.Toggle("分段设置字体", useTextSegments);
            if (nextTextMode != useTextSegments) SetTextMode(nextTextMode);
            if (useTextSegments) DrawTextSegments();
            else
            {
                EditorGUILayout.LabelField("嵌字内容");
                overlayText = EditorGUILayout.TextArea(overlayText, GUILayout.MinHeight(64));
            }
            font = (Font)EditorGUILayout.ObjectField(useTextSegments ? "默认字体" : "字体", font, typeof(Font), false);
            fontSize = EditorGUILayout.IntSlider(useTextSegments ? "默认字号 (px)" : "字体大小 (px)", fontSize, 8, 300);
            textColor = EditorGUILayout.ColorField("文字颜色", textColor);
            placement = (TextPlacement)EditorGUILayout.Popup(useTextSegments ? "默认位置（拼接）" : "嵌字位置", (int)placement, PlacementNames);
            margin = EditorGUILayout.IntSlider("边缘留白 (px)", margin, 0, 400);
            if (useTextSegments)
            {
                try { CaptureTextRenderer.Validate(new CaptureOptions { TextSegments = textSegments, Font = font }); }
                catch (ArgumentException exception) { EditorGUILayout.HelpBox(exception.Message, MessageType.Warning); }
            }
            else if (!string.IsNullOrEmpty(overlayText) && font == null)
                EditorGUILayout.HelpBox("嵌字需要选择一款字体。预置字体仅覆盖其自身包含的字符。", MessageType.Warning);

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("导出", EditorStyles.boldLabel);
            format = (CaptureFormat)EditorGUILayout.Popup("图片格式", (int)format, new[] { "PNG", "JPG" });
            if (format == CaptureFormat.Png)
            {
                preserveTransparency = EditorGUILayout.Toggle("保留透明度", preserveTransparency);
                if (preserveTransparency)
                    EditorGUILayout.HelpBox("部分角色材质会写入半透明 Alpha，使导出图在图片查看器中显得缺失；关闭此选项可让结果更接近 Unity 相机画面。", MessageType.Info);
            }
            if (format == CaptureFormat.Jpg)
                jpegQuality = EditorGUILayout.IntSlider("JPG 质量", jpegQuality, 1, 100);
            outputDirectory = EditorGUILayout.TextField("保存文件夹", outputDirectory);
            if (GUILayout.Button("选择保存文件夹"))
            {
                string chosen = EditorUtility.OpenFolderPanel("选择截图保存文件夹", outputDirectory, "");
                if (!string.IsNullOrEmpty(chosen)) outputDirectory = chosen;
            }
            fileName = EditorGUILayout.TextField("文件名", fileName);
            EditorGUILayout.LabelField("同名文件将自动添加序号。", EditorStyles.miniLabel);

            EditorGUILayout.Space(8);
            using (new EditorGUI.DisabledScope(selectedCamera == null))
            {
                if (GUILayout.Button("截图并保存", GUILayout.Height(36))) Capture();
            }
            if (!string.IsNullOrEmpty(error))
                EditorGUILayout.HelpBox(error, MessageType.Error);
            if (!string.IsNullOrEmpty(lastSavedPath))
            {
                EditorGUILayout.HelpBox("已保存：" + lastSavedPath, MessageType.Info);
                if (GUILayout.Button("在文件管理器中显示"))
                    EditorUtility.RevealInFinder(lastSavedPath);
            }
            if (preview != null)
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField("最后一张截图", EditorStyles.boldLabel);
                float previewWidth = Mathf.Max(1, position.width - 36);
                float previewHeight = Mathf.Min(360, previewWidth * preview.height / preview.width);
                Rect rect = GUILayoutUtility.GetRect(previewWidth, previewHeight);
                EditorGUI.DrawPreviewTexture(rect, preview, null, ScaleMode.ScaleToFit);
            }
            EditorGUILayout.EndScrollView();
        }

        private void SetTextMode(bool segmented)
        {
            if (textSegments == null) textSegments = new List<CaptureTextSegment>();
            if (segmented)
            {
                if (textSegments.Count == 0 || CaptureTextSegment.Join(textSegments) != overlayText)
                {
                    textSegments.Clear();
                    textSegments.Add(new CaptureTextSegment { Text = overlayText, Placement = placement });
                }
            }
            else overlayText = CaptureTextSegment.Join(textSegments);
            useTextSegments = segmented;
        }

        private void DrawTextSegments()
        {
            if (textSegments == null) textSegments = new List<CaptureTextSegment>();
            EditorGUILayout.HelpBox("每段文字可选不同字体和字号，按顺序拼接；空字体使用默认字体，未开启独立字号时使用全局字号。开启独立位置后可选九宫格并微调；关闭时按顺序拼接，偏移只移动这一段。段内支持换行和按字拆分。", MessageType.Info);
            int action = 0, actionIndex = -1;
            for (int i = 0; i < textSegments.Count; i++)
            {
                if (textSegments[i] == null) textSegments[i] = new CaptureTextSegment();
                CaptureTextSegment segment = textSegments[i];
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"第 {i + 1} 段", EditorStyles.boldLabel);
                using (new EditorGUI.DisabledScope(i == 0))
                    if (GUILayout.Button("上移", GUILayout.Width(40))) { action = -1; actionIndex = i; }
                using (new EditorGUI.DisabledScope(i == textSegments.Count - 1))
                    if (GUILayout.Button("下移", GUILayout.Width(40))) { action = 1; actionIndex = i; }
                if (GUILayout.Button("删除", GUILayout.Width(40))) { action = 2; actionIndex = i; }
                EditorGUILayout.EndHorizontal();
                segment.Text = EditorGUILayout.TextArea(segment.Text ?? "", GUILayout.MinHeight(40));
                segment.Font = (Font)EditorGUILayout.ObjectField("这段的字体", segment.Font, typeof(Font), false);
                bool customSize = EditorGUILayout.Toggle("独立字号", segment.FontSize > 0);
                if (customSize)
                    segment.FontSize = EditorGUILayout.IntSlider("这段的字号 (px)", segment.FontSize > 0 ? segment.FontSize : fontSize, 8, 300);
                else segment.FontSize = 0;
                segment.UseCustomPosition = EditorGUILayout.Toggle("独立位置", segment.UseCustomPosition);
                if (segment.UseCustomPosition)
                    segment.Placement = (TextPlacement)EditorGUILayout.Popup("这段的位置", (int)segment.Placement, PlacementNames);
                segment.OffsetX = EditorGUILayout.IntField("水平偏移 (px，+右)", segment.OffsetX);
                segment.OffsetY = EditorGUILayout.IntField("垂直偏移 (px，+上)", segment.OffsetY);
                if ((segment.OffsetX != 0 || segment.OffsetY != 0) && GUILayout.Button("重置位置微调"))
                    segment.OffsetX = segment.OffsetY = 0;
                int count = StringInfo.ParseCombiningCharacters(segment.Text).Length;
                if (count > 1)
                {
                    EditorGUILayout.BeginHorizontal();
                    segment.SplitAfter = EditorGUILayout.IntSlider("第几个字后拆分", segment.SplitAfter, 1, count - 1);
                    if (GUILayout.Button("拆分", GUILayout.Width(44))) { action = 3; actionIndex = i; }
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndVertical();
            }
            if (actionIndex >= 0)
            {
                if (action == 2) textSegments.RemoveAt(actionIndex);
                else if (action == 3) textSegments.Insert(actionIndex + 1, textSegments[actionIndex].Split(textSegments[actionIndex].SplitAfter));
                else
                {
                    CaptureTextSegment moved = textSegments[actionIndex];
                    textSegments[actionIndex] = textSegments[actionIndex + action];
                    textSegments[actionIndex + action] = moved;
                }
                Repaint();
            }
            if (GUILayout.Button("添加一段文字")) textSegments.Add(new CaptureTextSegment { Placement = placement });
        }

        private void AlignToSceneView()
        {
            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null || sceneView.camera == null)
            {
                error = "请先打开“场景”窗口并调整到需要的视角。";
                return;
            }
            Camera sceneCamera = sceneView.camera;
            Undo.RecordObjects(new UnityEngine.Object[] { selectedCamera, selectedCamera.transform }, "Align camera to Scene view");
            selectedCamera.transform.SetPositionAndRotation(
                sceneCamera.transform.position, sceneCamera.transform.rotation);
            selectedCamera.orthographic = sceneCamera.orthographic;
            selectedCamera.orthographicSize = sceneCamera.orthographicSize;
            selectedCamera.fieldOfView = sceneCamera.fieldOfView;
            if (selectedCamera.gameObject.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(selectedCamera.gameObject.scene);
            error = null;
            SceneView.RepaintAll();
        }

        private void Capture()
        {
            error = null;
            try
            {
                var options = new CaptureOptions
                {
                    Camera = selectedCamera,
                    Width = width,
                    Height = height,
                    Filter = filter,
                    PreserveRed = preserveRed,
                    BlurStrength = blurStrength,
                    Text = overlayText,
                    TextSegments = useTextSegments ? textSegments : null,
                    Font = font,
                    FontSize = fontSize,
                    TextColor = textColor,
                    Placement = placement,
                    Margin = margin,
                    Format = format,
                    JpegQuality = jpegQuality,
                    Directory = outputDirectory,
                    FileName = fileName,
                    KeepSkinnedMeshesUpdated = keepSkinnedMeshesUpdated,
                    PreserveTransparency = format == CaptureFormat.Png && preserveTransparency
                };
                string saved = CameraCaptureProcessor.Capture(options, out Texture2D newPreview);
                if (preview != null) DestroyImmediate(preview);
                preview = newPreview;
                lastSavedPath = saved;
                Repaint();
            }
            catch (Exception exception)
            {
                error = exception.Message;
                Debug.LogException(exception);
            }
        }

        private void UpdateLivePreview()
        {
            if (!livePreviewEnabled || selectedCamera == null)
            {
                if (livePreview != null)
                {
                    ReleaseLivePreview();
                    Repaint();
                }
                return;
            }
            if (width < 1 || height < 1 || EditorApplication.isCompiling ||
                EditorApplication.isUpdating || EditorApplication.timeSinceStartup < nextPreviewTime)
                return;
            nextPreviewTime = EditorApplication.timeSinceStartup + 0.25;
            float scale = Mathf.Min(1f, Mathf.Min(640f / width, 360f / height));
            int previewWidth = Mathf.Max(1, Mathf.RoundToInt(width * scale));
            int previewHeight = Mathf.Max(1, Mathf.RoundToInt(height * scale));
            if (livePreview == null || livePreview.width != previewWidth || livePreview.height != previewHeight)
            {
                ReleaseLivePreview();
                livePreview = new RenderTexture(previewWidth, previewHeight, 0, RenderTextureFormat.ARGB32)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                if (!livePreview.Create())
                {
                    ReleaseLivePreview();
                    previewError = "无法分配实时预览纹理。";
                    Repaint();
                    return;
                }
            }
            try
            {
                CameraCaptureProcessor.RenderPreview(new CaptureOptions
                {
                    Camera = selectedCamera,
                    Width = width,
                    Height = height,
                    Filter = filter,
                    PreserveRed = preserveRed,
                    BlurStrength = blurStrength,
                    Text = overlayText,
                    TextSegments = useTextSegments ? textSegments : null,
                    Font = font,
                    FontSize = fontSize,
                    TextColor = textColor,
                    Placement = placement,
                    Margin = margin,
                    KeepSkinnedMeshesUpdated = keepSkinnedMeshesUpdated,
                    PreserveTransparency = format == CaptureFormat.Png && preserveTransparency
                }, livePreview);
                previewError = null;
            }
            catch (Exception exception)
            {
                previewError = exception.Message;
            }
            Repaint();
        }

        private void ReleaseLivePreview()
        {
            if (livePreview == null) return;
            livePreview.Release();
            DestroyImmediate(livePreview);
            livePreview = null;
        }
    }
}
