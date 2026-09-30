# Unity 相机截图工作室

一个 Unity 编辑器插件：从指定的场景相机按设定分辨率截图，叠加滤镜与文字，导出 PNG 或 JPG。所有设置、截图和预览都在 Unity 的编辑器窗口完成。

## 安装

需要 Unity 2022.3 或更新版本。任选下面一种方式安装，同一项目不要重复安装。

### 方法一：Git URL（方便获取更新）

在 Unity **Window → Package Manager → + → Add package from git URL** 中输入：

```text
https://github.com/dungezi/unity-camera-capture-studio.git?path=/Packages/com.camera-capture.studio
```

如果仓库设为私有，需要在本机 Git 中配置对此 GitHub 仓库的访问权限。也可把 `Packages/com.camera-capture.studio` 文件夹复制到 Unity 项目的 `Packages` 目录。

### 方法二：本地 `.unitypackage`

1. 从仓库下载 [CameraCaptureStudio-1.0.0.unitypackage](dist/CameraCaptureStudio-1.0.0.unitypackage) 到本机。
2. 在 Unity 中选择 **Assets → Import Package → Custom Package…**，打开下载的文件。
3. 保持导入项目全选，点击 **Import**。插件会安装到 `Assets/CameraCaptureStudio`，之后从顶栏 **工具 → 相机截图工作室** 打开。

此文件包含脚本、Shader、预设字体和对应的 Unity `.meta`，不需要联网安装。维护者在修改插件后可运行 `python tools/build_unitypackage.py` 重新生成，并用 `python tools/build_unitypackage.py --check` 核对发布文件。[Unity 官方本地资源包导入说明](https://docs.unity3d.com/2022.3/Documentation/Manual/AssetPackagesImport.html)。

本插件只在 Unity 编辑器运行，不向游戏构建添加运行时代码。无需 TextMesh Pro、URP/HDRP 专属后处理包。

## 使用

1. 在 Unity 顶栏打开 **工具 → 相机截图工作室**，或 **Tools → Camera Capture Studio**。
2. 选择场景中的相机。需要让它对准当前“场景”窗口时，点击 **将相机对齐到当前场景视角**；此操作支持 Unity 撤销，并同步透视/正交投影参数。
3. 选择分辨率预设或填入自定义宽高。也可读取相机当前像素尺寸。
4. 选择滤镜；如需嵌字，填写文字，选择字体、字号、颜色和九宫格位置。
5. 填写保存文件夹与文件名，选择 PNG 或 JPG，点击 **截图并保存**。窗口下方显示最后一张图片的预览与保存位置。同名文件会自动追加 `_2`、`_3` 等序号。

默认保存到 Unity 项目根目录下的 `Captures`。当前包内的 `BRUSHSCI.TTF` 是用户提供的预设字体；字体字段也接受 Unity 已导入的其他 TTF/OTF 字体。所选字体必须包含待嵌入的字符，例如中文内容需要支持中文字形的字体。原文件夹中的 `BRUSHSCI SDF.asset` 是 TextMesh Pro 字体资源；本插件使用 TTF 原字体进行文字生成，因此不依赖 TMP。

## 滤镜预设

| 预设 | 效果 |
| --- | --- |
| None | 原图 |
| BlackAndWhite | 对比度略增强的黑白 |
| VintageFilm | 低饱和、暖色和轻微颗粒 |
| TealAndOrange | 青色阴影与橙色高光 |
| WarmSunlight | 暖色、略提高饱和度 |
| CoolMood | 冷色、略降低饱和度 |
| Sepia | 棕褐色老照片 |

这些是插件内实现的原创参数预设，不包含第三方 LUT 或收费滤镜素材。风格选择参考了常见照片预设中的 Vintage、Cinematic、Black & White，以及青橙电影调色趋势。参考资料：[Darkroom 社区预设分类](https://darkroom.co/presets)、[Unity Camera.Render](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Camera.Render.html)、[Unity Graphics.Blit](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Graphics.Blit.html)。

## 维护与反馈

问题和功能建议请在 GitHub 仓库的 Issues 中提交，最好附上 Unity 版本、渲染管线（Built-in/URP/HDRP）、复现步骤和 Console 报错。改动请通过 Pull Request 提交；版本记录见 [CHANGELOG.md](CHANGELOG.md)。

当前实现针对 Unity 2022.3+ 的常规 2D 单眼相机截图。不同项目的 SRP、相机堆栈和自定义渲染效果可能影响最终截图；发布前应在目标 Unity 项目中实测。
