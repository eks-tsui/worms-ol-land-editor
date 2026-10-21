# 验证

## 自动检查

在项目根目录运行：

```powershell
dotnet build WormsLandEditor.sln -c Release
dotnet run --project tests/WormsLandEditor.LocalizationChecks/WormsLandEditor.LocalizationChecks.csproj -c Release
dotnet run --project tests/WormsLandEditor.PersistenceChecks/WormsLandEditor.PersistenceChecks.csproj -c Release
dotnet publish src/WormsLandEditor/WormsLandEditor.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o artifacts/win-x64
```

在限制进程间通信的构建环境中，可以给构建命令增加 `-m:1 -p:UseSharedCompilation=false -nodeReuse:false`，使用单节点、非共享编译。

语言检查覆盖系统 culture 选择、261组英文/简体中文键、占位符、实际资源读取、英文资源中的中文残留、文件选择匹配式及源码中的字面量资源键。

保存协调检查直接使用 `MapSaveService`，但使用测试序列化器，不处理真实地图素材。它覆盖成功写入、序列化失败保留原件、第二文件提交失败后的旧文件恢复或本次新文件清理、无关文件保留及暂存清理。这些检查不是完整的格式往返测试。

## 已验证范围

- .NET SDK 10.0.402，Linux x64 构建环境。
- Release 解决方案编译成功，零警告、零错误。
- 1353项无界面语言检查通过。
- 13项保存协调与故障注入检查通过。
- Windows x64 自包含单文件产物构建成功。

交叉编译成功不代表已经运行 Windows GUI。自包含产物及其界面仍需在 Windows 上验收。

## Windows 验收清单

仅使用备份或独立测试目录，不要用唯一素材副本执行破坏性测试。

1. 分别使用英文、简体中文及繁体中文系统 UI culture 启动，检查语言选择、文字裁切、窗口缩放和系统字体。
2. 首次目录选择取消、无效目录、目录不可读时，确认不会循环弹窗或意外关闭。
3. 地图有未保存修改时切换编号、切换目录和清空画布，确认取消会保留当前内容。
4. 让另一个编号的 DAT、已有 `Level.Dir` 或水资源目录读取失败，确认旧编号、模型、资源及水选项保持一致，且不会用模板覆盖失败的已有资源包。缺少当前水资源素材时，应保留原路径而不是自动改成其他水资源。
5. 导入1920×696 PNG，检查 alpha 为0、1、255的像素与碰撞遮罩；检查错误尺寸、缺失文件和转换超时。
6. 检查12/32出生点边界、不可用位置、删除后顺序复用以及封闭地图选项。
7. 分别替换和导出 IMG、单帧SPR、多帧SPR/GIF，检查尺寸、调色板和透明区域。
8. 保存后重新打开 DAT 与 `Level.Dir`，检查图像、出生点和资源一致性；使用真实合法模板做往返检查。
9. 在独立副本上检查删除确认，确认明确涉及 DAT 和整个 Tmap 目录。
10. 检查发布产物启动和GUI调用自身完成PNG转换的退出码、标准输出及标准错误。
11. 检查文件占用、写权限不足与恢复失败场景；确认恢复提示保留了所需备份。

当前尚未完成以上 Windows GUI、真实图片/模板与实际格式往返验收。兼容输入仅覆盖已实现的变体，不承诺任意素材可用。
