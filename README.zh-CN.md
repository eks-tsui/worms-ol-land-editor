# Worms OL Land Editor

[English](README.md) | [简体中文](README.zh-CN.md)

用于制作和编辑二维地形地图的 Windows 桌面工具，基于 C#、.NET 10 和 WinForms。

支持地图 PNG 导入、出生点编辑、水资源预览、地图资源替换，以及 DAT / DIR 文件的读取和保存。

## 功能

- 按 `1–999` 的地图编号加载和编辑地图。
- 导出编号地图 ZIP 包，并加载到当前编辑状态；点击保存后才写入游戏文件。
- 导入固定尺寸的 PNG 地形图，自动生成碰撞和辅助遮罩。
- 编辑出生点，设置开放或封闭地图。
- 选择水资源并预览水面动画。
- 预览、替换、清空和导出以下地图资源：
  - `gradient.img`
  - `back.spr`
  - `debris.spr`
  - `soil.img`
  - `grass.img`
- 将地图保存为 `landNN.dat` 和对应的 `Mfx/TmapNN/Level.Dir`。
- 导出透明背景的地形 PNG；单帧资源导出 PNG，多帧资源导出 GIF。
- 提供 WA、WWP 与海底版相关地图格式的兼容导入入口。兼容性取决于具体文件版本及配套资源，不能保证任意 IMG / DAT / WAM 文件都可导入。

## 下载

在 [GitHub Releases](https://github.com/eks-tsui/worms-ol-land-editor/releases) 获取 Windows x64 便携包。解压到可写目录后运行 `WormsLandEditor.exe`；便携包不包含地图素材。版本从 **1.0.0** 开始。

## 环境要求

- Windows x64。
- 从源码构建需要 [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)。
- 运行框架依赖版本需要 .NET 10 Desktop Runtime；按下文生成的自包含版本带有所需运行时。
- GUI 使用 WinForms 与 Windows 图像处理能力，不提供 macOS 或 Linux 原生运行支持。

应用工程没有额外的 NuGet 包依赖，也不引用其他应用工程。首次还原仍可能需要下载 .NET 目标框架和运行时相关组件。

## 界面语言

启动时自动读取系统 UI 语言：

- 中文系统（`zh`、`zh-CN`、`zh-TW`、`zh-HK` 等）：显示简体中文。当前没有单独的繁体中文翻译。
- 英文及其他系统语言：显示英文。

按钮、窗口标题、文件对话框说明、提示、错误和状态文本使用资源文件管理。文件路径、资源名、扩展名、命令参数和格式标识不会翻译。

新增翻译时，为 `Resources/UiStrings.resx`、`CoreStrings.resx`、`CliStrings.resx`、`PackageStrings.resx` 增加对应 culture 的资源文件，并在 `Localization/LocalizationManager.cs` 中注册语言选择规则。各语言的键和格式占位符应与英文基底一致。

## 准备地图数据

编辑器源码不包含地图模板、水资源、地形图片或字体文件。请使用你有权使用的素材，并先备份工作数据。

GUI 需要选择一个包含 `Data/Mfx` 的目录。也可以直接选择其 `Data` 子目录。界面中的按钮名称为“设置游戏目录”。

一个典型的数据目录如下：

```text
MapWorkspace/
└── Data/
    ├── land01.dat                       # 已有地图，可选
    ├── land42.dat                       # 已有地图，可选
    ├── Mfx/
    │   ├── Tmap01/
    │   │   └── Level.Dir                # 新建或兼容导入所需的默认模板
    │   └── Tmap42/
    │       └── Level.Dir                # 42 号地图的资源包
    └── Water/
        └── Red/
            └── Water.dir               # 对应水资源的预览数据，可选
```

加载地图时，优先使用该编号对应的 `Level.Dir`。该资源包不存在时，尝试使用 `Data/Mfx/Tmap01/Level.Dir` 作为模板。

模板必须是可解析的现有资源包，且包含所需资源条目。当前资源包写入沿用已有目录索引，不支持从空目录创建全新的 DIR 索引。只复制一张 PNG 并不能替代模板。

水资源来自 `Data/Water/<名称>/Water.dir`。缺少水资源时，相应水面预览可能不可用；下拉框中的备用名称不代表这些资源已经存在。

目录选择保存在当前 Windows 用户的 `%APPDATA%/WormsLandEditor/settings.txt` 中。

## 从源码构建和运行

在项目根目录打开 PowerShell。

还原并构建：

```powershell
dotnet restore .\src\WormsLandEditor\WormsLandEditor.csproj
dotnet build .\src\WormsLandEditor\WormsLandEditor.csproj -c Release
```

运行图形界面：

```powershell
dotnet run --project .\src\WormsLandEditor\WormsLandEditor.csproj -c Release
```

保留默认 Windows apphost。GUI 的 PNG 转换通过再次启动同一个可执行程序完成，不要用 `UseAppHost=false` 构建，也不要将直接运行 DLL 的方式作为 GUI 启动方式。

生成 Windows x64 自包含发布目录：

```powershell
dotnet publish .\src\WormsLandEditor\WormsLandEditor.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\artifacts\win-x64
```

运行：

```powershell
.\artifacts\win-x64\WormsLandEditor.exe
```

请保留发布目录中实际生成的配套文件，不要在未经验证时只复制其中一个文件。自包含与单文件发布选项的含义见 [.NET 发布说明](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview)。

## 基本操作

### 1. 选择目录和地图编号

设置地图数据所在目录后，选择编号。已有编号会加载 `Data/landNN.dat`；不存在的编号可以通过画布右下角“导入”按钮导入 PNG 来创建地图。

编号至少使用两位数字：1 对应 `land01.dat` / `Tmap01`，100 对应 `land100.dat` / `Tmap100`。

### 2. 导入主地图 PNG

主地图尺寸必须是 **1920 × 696**，尺寸不符会拒绝导入。

- Alpha 为 `0` 的像素为空白。
- Alpha 为 `1–255` 的像素被视为实体地形。
- 主地图的实体判定不依赖像素亮度。半透明边缘也会成为实体，建议使用全透明背景和不透明地形。
- 导入时会重建调色板及遮罩，不能保证与原图颜色完全一致。

内部地形使用 8 位索引图；碰撞和辅助遮罩为 `1920 × 696` 的 1 位图，低分辨率预览遮罩为 `240 × 87`。当前 PNG 导入流程保持低分辨率预览遮罩为空。

导入后请保留源 PNG，直到地图保存完成。当前 GUI 会在保存时重新读取该文件生成地形与碰撞数据；文件被移动或删除后需要重新导入。

### 3. 编辑出生点和地图选项

- 左键点击地图放置出生点。
- 点击已有出生点将其取消，再次添加时优先复用空出的顺序位置。
- 无法直接放置时，程序会尝试寻找附近可用位置；没有合适落脚点时不新增。
- 最多放置 **32** 个出生点，GUI 保存要求至少 **12** 个。
- “清空出生点”清除全部已放置的点。
- “封闭地图”设置会随 DAT 保存。

这些数量限制主要由 GUI 执行，不能把底层或命令行保存视为完整的可用性验证。

### 4. 替换地图资源

点击底部资源卡片的预览图即可导入替换素材。

- `gradient.img`、`soil.img`、`grass.img`：导入 PNG，尺寸必须与当前资源一致。
- `back.spr`：导入 PNG 或 GIF，宽度必须为 **640**；高度可变化。
- `debris.spr`：导入 PNG 或 GIF，宽高均不得超过 **80**。
- 其他 SPR 条目的兼容处理以其现有画布尺寸为准。

资源会按现有调色板与资源格式转换，因此颜色、透明度和画布表现可能与输入文件不同。请以保存后重新加载的结果为准。

“清空”清除图像像素，不删除该资源条目。“导出”将单帧资源保存为 PNG、多帧资源保存为 GIF。当前 GIF 导出统一使用每帧 **120 毫秒**，不是原动画时序的无损保留。

### 5. 保存和导出

“保存”写入两部分：

```text
Data/landNN.dat
Data/Mfx/TmapNN/Level.Dir
```

目标 DAT 或 `Level.Dir` 已存在时，GUI 会询问是否覆盖。保存会先暂存并校验两个文件，再备份目标并替换；普通写入失败会尝试恢复，但这不是跨文件事务。请始终保留独立备份，并检查地图文件和资源包是否都成功写入。

画布“导出”仅输出地形 PNG，不含出生点标记、水面预览、地图选项或资源包。PNG 不是完整地图工程文件。

“删除”会删除当前编号的 DAT 和整个 `Mfx/TmapNN` 目录，包括该目录中额外放入的文件。不要把唯一副本或无关文件放在地图资源目录中。

## 地图 ZIP 包

使用 **“导出地图包”**，而不是地形 PNG 的“导出”按钮，来传递地图及其资源包。点击后会提示：**“是否确定导出本地图资源到 zip？”**。确定后显示保存对话框，按当前地图编号建议 `land01.zip`、`land100.zip` 等文件名，可以更改文件名和保存位置。

如果地图尚未保存或存在未保存修改，导出前需要先保存当前地图。取消保存或保存失败都会停止导出，不会悄悄用旧的磁盘文件代替当前编辑内容。

地图包严格包含带版本的 `manifest.json`、`Data/landNN.dat` 和 `Data/Mfx/TmapNN/Level.Dir`。其中 `Level.Dir` 完整保留，不限于界面显示的资源卡片。地图目录的其他文件、其他地图及共享水资源不会打包。水资源仍是外部依赖，其引用会记录在清单；目标目录需要对应的 `Data/Water/<name>/Water.dir` 才能预览该水资源。

选择 **“导入地图”** 并打开本工具生成的 ZIP。完整校验后，确认是否替换当前编辑内容。导入只把地图和完整资源包加载到当前所选编号的内存编辑状态，并标记为未保存；**不会创建、暂存或覆盖游戏文件**。取消或导入失败时，原编辑状态和磁盘文件保持不变。

例如当前编号为100时导入 `land01.zip`，编号仍为100，内存中 DAT 的地形资源路径映射为 `data\mfx\Tmap100`，外部水资源引用保持不变。点击 **“保存”** 后才写入 `land100.dat` 和 `Mfx/Tmap100/Level.Dir`，已有目标文件仍需经过正常的保存覆盖确认。保存仍要求至少12个出生点。在保存前切换地图、切换目录、关闭窗口或再次导入，都会按未保存修改流程确认。

刚导入且未进一步编辑时，首次保存直接写入已映射编号的 DAT 和完整原始 DIR，不转换二进制格式，保留隐藏资源以及短 TXT 记录和共享字节引用。后续编辑会使原始字节快照失效，并从编辑后的模型保存；普通编辑可能在原格式内重映射调色板或重建资源偏移。这些检查不能代替目标游戏实测。

只有明确保存时才暂存目标文件、备份和替换，失败时尝试恢复。导入不会按 ZIP 中的任意路径直接解压。断电、进程崩溃或恢复本身失败时，仍无法保证跨文件原子性，请保留独立备份。详见[地图包格式与限制](docs/map-packages.md)。

## 兼容地图导入

使用工具栏“导入地图”选择适用的 IMG、DAT、WAM 或 `Level.dir`。

导入会根据文件头、文件名和周边目录推断类型并查找配套资源，因此只拿到单个文件时不一定能够成功。请尽量保留源素材的相邻目录结构。

兼容导入生成当前编辑格式的地形和资源，并可能调整画面大小、调色板和出生点。它不是完整地图工程或全部配置的无损迁移。导入完成后，请检查地形透明区域、出生点、资源预览与地图选项，再保存到测试目录。

## 常用命令行

以下示例在完成上述发布后使用。命令行写入不会弹出 GUI 覆盖确认；请使用新的输出文件名，并提前创建输出目录。

读取地图结构：

```powershell
.\artifacts\win-x64\WormsLandEditor.exe --inspect ".\work\land42.dat"
```

导出地形 PNG：

```powershell
.\artifacts\win-x64\WormsLandEditor.exe --exportpng ".\work\land42.dat" ".\out\land42.png"
```

替换已有 DAT 中的地形 PNG：

```powershell
.\artifacts\win-x64\WormsLandEditor.exe --replacepng ".\work\land42.dat" ".\art\terrain.png" ".\out\land42-edited.dat"
```

从 PNG 生成基础 DAT：

```powershell
.\artifacts\win-x64\WormsLandEditor.exe --frompng ".\art\terrain.png" ".\out\draft.dat"
```

`--frompng` 可以不依赖 `Level.Dir` 生成基础 DAT，但不会设置出生点、创建资源包或建立完整的编号资源路径。它的输出不是可直接投入使用的完整地图。

导出资源的第一帧 PNG：

```powershell
.\artifacts\win-x64\WormsLandEditor.exe --export-resource-frame ".\work\Level.Dir" "back.spr" ".\out\back.png"
```

当前应用以 Windows GUI 程序构建，终端对标准输出与等待退出的处理可能不同。需要读取检查报告时，应由能够捕获标准输出的调用程序启动。诊断与写入类扩展命令的参数、风险和限制见 `docs/cli.md`。

## 限制与使用提醒

- 当前主地图编辑流程固定为 `1920 × 696`，不是任意尺寸图像编辑器。
- 当前写入会量化和重映射调色板；DAT 的读写往返并不保证字节不变。
- `Level.Dir` 需要已有有效模板或资源包，不支持从零创建目录索引，也不提供任意新增、删除资源条目的 GUI。
- 支持的图片入口是 PNG，以及 SPR 资源使用的 GIF；不提供通用 BMP 文件导入入口。
- 不提供画笔绘图或撤销/重做功能。
- 保存 DAT 和 DIR 当前不是一个事务。如果中途失败，应分别检查两个文件，不要只根据一个成功文件判断保存完整。
- 兼容导入和资源解码只覆盖已实现的变体。未验证素材、损坏文件或不匹配的资源组合可能失败。
- 请仅处理可信来源的素材，并在独立测试目录中检查保存和重新加载的结果。

## 项目结构

```text
src/WormsLandEditor/       WinForms 应用
  Commands/              命令行操作与检查报告
  Controls/              地图画布
  Formats/               地图、图像、动画及资源包读写
  Localization/          系统语言选择与资源读取
  Resources/             英文和简体中文界面、错误、命令文本
  Services/              地图暂存写入与失败恢复
  Properties/            发布配置
tests/                   语言与保存协调回归检查
docs/                    数据布局、格式和命令说明
```

可复现的检查命令、已验证范围及 Windows 验收清单见[验证说明](docs/validation.md)。

## 许可证与素材

项目源码和文档采用 [MIT 许可证](LICENSE)，Copyright (c) 2026 eks-tsui。

你自行提供的地图、模板、图片、动画和字体仍受各自权利及许可约束。请只提交你有权公开分发的素材，并保留适用的版权和许可声明。
