# 项目维护上下文

用于在新聊天或换电脑后接续项目。记录来自源码、文档、Git 历史与本机验证资料，后续变更时应更新。

## 当前基线（2026-10-08）

- Unity 相机截图工作室（Camera Capture Studio），编辑器插件；最低 Unity 版本 2022.3。
- GitHub：https://github.com/dungezi/unity-camera-capture-studio
- 包名 `com.camera-capture.studio`，版本 1.2.0，发布标签为 v1.2.0。
- 1.2.0 开发起点为 main 分支的提交 c5fd995：Fix avatar alpha in PNG exports and move menu to Window。
- 阅读开始时工作区干净，缓存的 origin/main 与 main 相同。首次阅读时远端连接失败；发布准备阶段已恢复连接并确认远端 main 与开发起点一致。
- 这是插件包仓库，根目录不是完整的 Unity 游戏项目。

## 已有功能及默认行为

- 入口：Window → 相机截图工作室。
- 指定场景相机、对齐 Scene 视角，支持撤销及透视/正交参数同步。
- PNG/JPG、自定义尺寸、全高清、4K、方形、竖屏、8K UHD、8K DCI、16K UHD。
- 宽高各自受 min(SystemInfo.maxTextureSize, 16384) 限制，高分辨率需要足够内存和显存。
- 实时预览默认开启，每 0.25 秒尝试刷新，按比例缩小到不超过 640 × 360，文字大小及边距随之缩放。
- 原图和八种滤镜：原有六种，以及高对比黑白（默认保留红色）和背景模糊（0–100，默认 40，先处理画面再叠加嵌字）。
- 文字内容、字体、字号、颜色、九宫格位置、边距。使用动态 TTF/OTF，不依赖 TextMesh Pro。
- 预设字体 BRUSHSCI.TTF；中文需另选支持中文字形的字体。根目录 BRUSHSCI SDF.asset 不参与当前文字流程。
- 保存到目标 Unity 项目根目录 Captures，同名追加 _2、_3 等。
- PNG 的“保留透明度”默认关闭，以减少角色材质低 Alpha 导致导出图显淡的问题。
- “离屏蒙皮兼容”默认关闭，开启后仅在渲染期间临时修改符合条件的蒙皮组件，完成后恢复。
- 导出后仅保留缩略图，完整分辨率纹理在处理结束后释放。

## 源码导航

以下编辑器文件位于 Packages/com.camera-capture.studio/Editor：

| 文件 | 职责 |
| --- | --- |
| CameraCaptureWindow.cs | 界面、序列化设置、相机对齐、预览更新、截图操作 |
| CameraCaptureProcessor.cs | 验证、渲染、滤镜和文字合成、编码、文件命名、资源释放 |
| CaptureFilter.shader | 八种滤镜、高斯模糊及画面 Alpha 处理 |
| CaptureText.shader | 字体图集采样、文字混合 |
| CameraCaptureStudio.Editor.asmdef | 程序集限定为 Editor 平台 |

其他入口：package.json 保存包名及版本；tools/build_unitypackage.py 生成和核对安装包；dist 保存历史安装包和当前版本安装包；README.md 和 CHANGELOG.md 保存用户说明及版本记录。

截图流程：CaptureOptions → 验证 → 相机渲染到临时 RenderTexture → 滤镜 Blit → 可选嵌字 → ReadPixels → 编码 → 生成缩略图 → 写入文件。

RenderFrame 在 finally 中恢复相机 targetTexture、活动 RenderTexture、临时修改的蒙皮设置，并释放临时材质和纹理。文字由 TextGenerator 生成顶点，用 GL 绘制。

## 历史改动

- e923c66：初始插件；9dbfb11：稳定的 Unity .meta；b4861a3：本地安装包。
- dcfd7d4 / 1.1.0：实时预览、高分辨率、可选蒙皮兼容、导出缩略图。
- c5fd995 / 1.1.1：调整默认 Alpha 行为、菜单移到 Window、蒙皮兼容默认关闭。

## 安装与发布

UPM 地址：

```text
https://github.com/dungezi/unity-camera-capture-studio.git?path=/Packages/com.camera-capture.studio
```

本地安装包导入到 Assets/CameraCaptureStudio。同一 Unity 项目选择一种安装方式，避免重复安装。

发布前更新包版本、CHANGELOG 和 README 安装包链接，再运行：

```text
python tools/build_unitypackage.py
python tools/build_unitypackage.py --check
```

打包脚本采用固定时间戳，可重复生成；核对源码、路径、GUID 和 .meta。新增发布资源时更新 ASSETS 清单，保留已有资源 GUID。包内文本通过 .gitattributes 统一使用 LF，保持不同系统检出后的安装包一致性。

## 验证结果与边界

- 本次 1.2.0 安装包一致性检查通过，安装包与当前源码及元数据一致。
- 项目重新阅读阶段的独立 .NET 编译因缺少 SDK 失败；新增滤镜后改用 Unity 2022.3.22f1 实际编译与运行检查，已通过。
- 历史 .validation/UnitySmoke/smoke.log 只覆盖 Alpha 输出；当前正式检查见 .validation/FilterSmoke/filter-smoke.log，报告 FILTER_SMOKE_PASS checks=43。
- .validation 被 Git 忽略，里面的历史编译工程及发布辅助文件只存在于本机；当前 FilterSmoke 测试项目可由已保存的 tools/run_filter_smoke.ps1 重建。
- 已增加仓库内的 Unity smoke 检查脚本；未配置 CI。发布准备时已取得历史标签 v1.1.1；当前发布信息以 GitHub Releases 为准。
- 当前使用 Camera.Render、Graphics.Blit 和常规 2D 单眼相机。URP/HDRP、相机堆栈及自定义渲染效果需在目标项目验证。

渲染改动后应在目标 Unity 项目检查截图、滤镜、字体/中文、文字位置、PNG Alpha、JPG、重名、高分辨率、窗口关闭后的资源释放和蒙皮兼容。

## 后续接续

当前任务：按用户两张参考图新增高对比黑白（保留红色点缀）和可调背景模糊，版本升级为 1.2.0。保留原有滤镜枚举顺序，新预设追加在末尾。窗口分别传递 PreserveRed、BlurStrength 给实时预览和导出；ApplyFilter 使用双纹理、三轮水平/垂直高斯处理，随后 DrawText。发布信息以 GitHub Releases 与实际 Git 状态为准。

正式检查入口现为 tools/run_filter_smoke.ps1 与 tools/FilterSmoke.cs，均保存在仓库中，可重建被忽略的临时 Unity 测试项目。本次在 Unity 2022.3.22f1 / Built-in / Direct3D 11 中通过 43 项检查：选择性红色、强对比、模糊 0/20/80、边缘与 1 像素图、透明度和低 Alpha 颜色、预览/较大尺寸的相对模糊强度、文字保持清晰、PNG/JPG 和重名保护，以及相机/活动纹理状态恢复。未实测真实项目的 8K/16K、URP/HDRP 或参考角色场景。

新聊天先读本文件、README、CHANGELOG 及对应源码，再检查实际 Git 状态。上述日期及提交仅代表本次阅读基线。
