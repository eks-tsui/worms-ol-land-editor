# 验证

## 自动检查

在项目根目录运行：

```powershell
dotnet build WormsLandEditor.sln -c Release
dotnet run --project tests/WormsLandEditor.LocalizationChecks/WormsLandEditor.LocalizationChecks.csproj -c Release
dotnet run --project tests/WormsLandEditor.PersistenceChecks/WormsLandEditor.PersistenceChecks.csproj -c Release
dotnet run --project tests/WormsLandEditor.PackageChecks/WormsLandEditor.PackageChecks.csproj -c Release
dotnet publish src/WormsLandEditor/WormsLandEditor.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o artifacts/win-x64
```

在限制进程间通信的构建环境中，可以给构建命令增加 `-m:1 -p:UseSharedCompilation=false -nodeReuse:false`，使用单节点、非共享编译。

语言检查覆盖系统 culture 选择、298组英文/简体中文键、占位符、实际资源读取、英文资源中的中文残留、文件选择匹配式、源码中的字面量资源键及精确的中文地图包导出确认。

保存协调检查直接使用 `MapSaveService`，但使用测试序列化器，不处理真实地图素材。它覆盖成功写入、序列化失败保留原件、第二文件提交失败后的旧文件恢复或本次新文件清理、无关文件保留及暂存清理。这些检查不是完整的格式往返测试。

地图包检查使用程序生成的 DAT / IMG / SPR / DIR 二进制与真实 ZIP 读写，不使用编号映射的替身序列化器。它覆盖1→100→2编号往返、编号路径之外的字节保留、外部水引用、覆盖、确认取消零写入、第一/第二文件写入前和部分写入后故障恢复、恢复失败保留备份，以及路径、链接、尺寸、校验和、ZIP结构、二进制结构和预览解码预算的异常输入。

## 已验证范围

- .NET SDK 10.0.402，Linux x64 构建环境。
- Release 解决方案编译成功，零警告、零错误。
- 1542项无界面语言检查通过。
- 13项保存协调断言通过。
- 123项地图包检查通过，包括合成格式往返、短 TXT 共享引用及异常输入。
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
12. 检查“导出地图包”的精确确认、默认编号文件名、可改保存路径与取消；未保存地图必须先保存成功，取消或失败不应导出旧文件。
13. 将编号1的地图包导入当前编号100，检查目标文件和内部编号引用，确认取消不会更改磁盘及未保存编辑。
14. 检查水引用使用正斜杠和反斜杠时选择一致；缺失水素材只影响预览，不应改写引用。地图包不应包含共享水资源或无关文件。

已对一个旧 DIR 资源包进行私下字节与结构回归：直接 ZIP 导出和导入保持完整 DIR 不变；调用实际 DIR 保存器后，全部资源载荷与目录哈希前缀保持不变，保存结果也可再次打包。该样本不随源码或发布包分发。

当前尚未完成以上 Windows GUI、GUI 调色板重映射、真实图片渲染及目标游戏运行验收。合成二进制检查与有限 DIR 样本回归不能证明所有素材均兼容。
