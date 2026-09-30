using System;
using System.IO;
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
            new Vector2Int(1080, 1920)
        };

        private static readonly string[] ResolutionNames =
        {
            "1920 × 1080 (Full HD)", "3840 × 2160 (4K)",
            "1080 × 1080 (方形)", "1080 × 1920 (竖屏)", "自定义"
        };

        private static readonly string[] FilterNames =
        {
            "无", "黑白", "复古胶片", "青橙电影", "暖阳", "冷调", "棕褐老照片"
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
        [SerializeField] private int jpegQuality = 90;
        [SerializeField] private CaptureFilter filter;
        [SerializeField] private string overlayText = "";
        [SerializeField] private Font font;
        [SerializeField] private int fontSize = 64;
        [SerializeField] private Color textColor = Color.white;
        [SerializeField] private TextPlacement placement = TextPlacement.BottomRight;
        [SerializeField] private int margin = 40;
        [SerializeField] private string outputDirectory;
        [SerializeField] private string fileName = "capture";

        private Vector2 scroll;
        private Texture2D preview;
        private string lastSavedPath;
        private string error;

        [MenuItem("工具/相机截图工作室")]
        [MenuItem("Tools/Camera Capture Studio")]
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
        }

        private void OnDisable()
        {
            if (preview != null)
                DestroyImmediate(preview);
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("相机截图工作室", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("选择场景相机，设置输出与后处理，然后点击截图。", MessageType.Info);
            EditorGUILayout.Space(6);

            EditorGUILayout.LabelField("相机与画面", EditorStyles.boldLabel);
            selectedCamera = (Camera)EditorGUILayout.ObjectField("指定相机", selectedCamera, typeof(Camera), true);
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
                    resolutionIndex = ResolutionNames.Length - 1;
                }
                else
                    error = "请先选择有有效像素尺寸的场景相机。";
            }

            int nextResolution = EditorGUILayout.Popup("分辨率", resolutionIndex, ResolutionNames);
            if (nextResolution != resolutionIndex)
            {
                resolutionIndex = nextResolution;
                if (resolutionIndex < ResolutionPresets.Length)
                {
                    width = ResolutionPresets[resolutionIndex].x;
                    height = ResolutionPresets[resolutionIndex].y;
                }
            }
            if (resolutionIndex == ResolutionNames.Length - 1)
            {
                width = EditorGUILayout.IntField("宽度 (px)", width);
                height = EditorGUILayout.IntField("高度 (px)", height);
            }
            EditorGUILayout.LabelField($"输出尺寸：{width} × {height} px", EditorStyles.miniLabel);

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("后处理", EditorStyles.boldLabel);
            filter = (CaptureFilter)EditorGUILayout.Popup("滤镜预设", (int)filter, FilterNames);
            EditorGUILayout.LabelField("嵌字内容");
            overlayText = EditorGUILayout.TextArea(overlayText, GUILayout.MinHeight(64));
            font = (Font)EditorGUILayout.ObjectField("字体", font, typeof(Font), false);
            fontSize = EditorGUILayout.IntSlider("字体大小 (px)", fontSize, 8, 300);
            textColor = EditorGUILayout.ColorField("文字颜色", textColor);
            placement = (TextPlacement)EditorGUILayout.Popup("嵌字位置", (int)placement, PlacementNames);
            margin = EditorGUILayout.IntSlider("边缘留白 (px)", margin, 0, 400);
            if (!string.IsNullOrEmpty(overlayText) && font == null)
                EditorGUILayout.HelpBox("嵌字需要选择一款字体。预置字体仅覆盖其自身包含的字符。", MessageType.Warning);

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("导出", EditorStyles.boldLabel);
            format = (CaptureFormat)EditorGUILayout.Popup("图片格式", (int)format, new[] { "PNG", "JPG" });
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
                    Text = overlayText,
                    Font = font,
                    FontSize = fontSize,
                    TextColor = textColor,
                    Placement = placement,
                    Margin = margin,
                    Format = format,
                    JpegQuality = jpegQuality,
                    Directory = outputDirectory,
                    FileName = fileName
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
    }
}
