# 多语言

## 默认语言

应用启动时先读取系统 UI culture，再选择已提供的语言：

- 所有 `zh` 家族 culture 映射为 `zh-CN`。
- 其他 culture 映射为 `en`。

这意味着繁体中文系统当前显示简体中文，而不是未经提供的繁体翻译。数字与日期的 `CurrentCulture` 保持不变，UI 语言不会偷偷更改数据的区域格式。

## 资源组织

```text
Resources/
├── UiStrings.resx
├── UiStrings.zh-CN.resx
├── CoreStrings.resx
├── CoreStrings.zh-CN.resx
├── CliStrings.resx
├── CliStrings.zh-CN.resx
├── PackageStrings.resx
└── PackageStrings.zh-CN.resx
```

英文 `.resx` 是基底；`.zh-CN.resx` 是简体中文。代码分别通过 `L10n.Ui`、`L10n.Core`、`L10n.Cli`、`L10n.Package` 读取文本，使用稳定键及复合格式占位符。

新增语言时，为四个资源组添加相同键的目标 culture 文件，并更新 `LocalizationManager.ResolveCulture` 的支持规则。不要翻译路径、资源条目名、文件魔数、扩展名或命令参数。

应用自身的提示、错误、报告和状态需要资源化。Windows 系统对话框内置按钮，以及系统或运行时产生的错误细节，可能遵循操作系统或其运行时语言，不属于本项目翻译资源。

## 检查

```powershell
dotnet run --project .\tests\WormsLandEditor.LocalizationChecks\WormsLandEditor.LocalizationChecks.csproj -c Release
```

该无界面检查验证 culture 映射、资源集合、键一致性、占位符及实际格式化。它可以跨平台执行，不替代 Windows GUI 的文字裁切、DPI、系统对话框和交互验收。
