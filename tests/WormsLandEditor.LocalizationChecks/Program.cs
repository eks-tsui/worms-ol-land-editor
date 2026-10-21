using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using WormsLandEditor.Localization;

int checks = 0;
void Check(bool condition, string description)
{
    if (!condition)
        throw new InvalidOperationException(description);
    checks++;
}

string[] chineseCultures = ["zh", "zh-CN", "zh-TW", "zh-HK", "zh-Hans", "zh-Hant"];
string[] otherCultures = ["en", "en-US", "en-GB", "fr-FR", "ja-JP", "de-DE", ""];
CultureInfo originalCulture = CultureInfo.CurrentCulture;
CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;
try
{
    foreach (string culture in chineseCultures)
    {
        LocalizationManager.Initialize(CultureInfo.GetCultureInfo(culture));
        Check(CultureInfo.CurrentUICulture.Name == "zh-CN", $"Chinese mapping: {culture}");
        Check(CultureInfo.DefaultThreadCurrentUICulture?.Name == "zh-CN", "Default thread UI culture");
        Check(CultureInfo.CurrentCulture.Equals(originalCulture), "Numeric culture must remain unchanged");
    }
    foreach (string culture in otherCultures)
    {
        LocalizationManager.Initialize(CultureInfo.GetCultureInfo(culture));
        Check(CultureInfo.CurrentUICulture.Name == "en", $"English fallback: {culture}");
    }

    (string Resource, Func<string, object?[], string> Get)[] catalogs =
    [
        ("UiStrings", (key, args) => L10n.Ui(key, args)),
        ("CoreStrings", (key, args) => L10n.Core(key, args)),
        ("CliStrings", (key, args) => L10n.Cli(key, args)),
        ("PackageStrings", (key, args) => L10n.Package(key, args))
    ];
    var resourceKeys = new Dictionary<string, HashSet<string>>();

    foreach (var catalog in catalogs)
    {
        var manager = new ResourceManager($"WormsLandEditor.Resources.{catalog.Resource}", typeof(L10n).Assembly);
        using ResourceSet baseline = manager.GetResourceSet(CultureInfo.GetCultureInfo("en"), true, false)
            ?? throw new InvalidOperationException($"Missing English resource set: {catalog.Resource}");
        using ResourceSet chinese = manager.GetResourceSet(CultureInfo.GetCultureInfo("zh-CN"), true, false)
            ?? throw new InvalidOperationException($"Missing Chinese resource set: {catalog.Resource}");
        var englishKeys = baseline.Cast<DictionaryEntry>().Select(e => (string)e.Key).ToHashSet();
        var chineseKeys = chinese.Cast<DictionaryEntry>().Select(e => (string)e.Key).ToHashSet();
        Check(englishKeys.SetEquals(chineseKeys), $"Resource keys differ: {catalog.Resource}");
        Check(englishKeys.Count > 0, $"Empty resource catalog: {catalog.Resource}");
        resourceKeys[catalog.Resource.Replace("Strings", string.Empty)] = englishKeys;

        foreach (string key in englishKeys)
        {
            string english = baseline.GetString(key) ?? throw new InvalidOperationException(key);
            string translated = chinese.GetString(key) ?? throw new InvalidOperationException(key);
            Check(!Regex.IsMatch(english, @"[\u3400-\u9fff]"), $"Chinese text in English catalog: {catalog.Resource}/{key}");
            if (key.EndsWith("Filter", StringComparison.Ordinal))
            {
                string[] englishPatterns = english.Split('|').Where((_, i) => i % 2 == 1).ToArray();
                string[] chinesePatterns = translated.Split('|').Where((_, i) => i % 2 == 1).ToArray();
                Check(englishPatterns.Length > 0 && englishPatterns.SequenceEqual(chinesePatterns),
                    $"File patterns differ: {catalog.Resource}/{key}");
            }
            int[] Slots(string value) => Regex.Matches(value, @"(?<!\{)\{(\d+)(?:[,:][^}]*)?\}")
                .Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).Order().ToArray();
            int[] englishSlots = Slots(english);
            Check(englishSlots.SequenceEqual(Slots(translated)), $"Format slots differ: {catalog.Resource}/{key}");
            int argumentCount = englishSlots.Length == 0 ? 0 : englishSlots.Max() + 1;
            object?[] arguments = Enumerable.Range(0, argumentCount).Select(i => (object?)i).ToArray();
            foreach (string culture in new[] { "en", "zh-CN" })
            {
                LocalizationManager.Initialize(CultureInfo.GetCultureInfo(culture));
                string value = catalog.Get(key, arguments);
                Check(!string.IsNullOrWhiteSpace(value), $"Empty localized text: {catalog.Resource}/{key}/{culture}");
            }
        }
        Console.WriteLine($"PASS {catalog.Resource}: {englishKeys.Count} English/Chinese keys");
    }

    string sourceDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
        "../../../../../src/WormsLandEditor"));
    Check(Directory.Exists(sourceDirectory), "Run the checks from a source checkout");
    foreach (string path in Directory.EnumerateFiles(sourceDirectory, "*.cs", SearchOption.AllDirectories))
    {
        string relative = Path.GetRelativePath(sourceDirectory, path);
        if (relative.Split(Path.DirectorySeparatorChar).Any(part => part is "obj" or "bin"))
            continue;
        foreach (Match match in Regex.Matches(File.ReadAllText(path), "L10n\\.(Ui|Core|Cli|Package)\\(\\s*\"([^\"]+)\""))
        {
            string catalog = match.Groups[1].Value;
            string key = match.Groups[2].Value;
            Check(resourceKeys[catalog].Contains(key), $"Unknown resource key in {relative}: {catalog}/{key}");
        }
    }
    LocalizationManager.Initialize(CultureInfo.GetCultureInfo("zh-CN"));
    Check(L10n.Ui("ConfirmExportMapPackage") == "是否确定导出本地图资源到 zip？", "Exact Chinese map-package confirmation");
    Console.WriteLine($"PASS {checks} localization checks (headless; not a WinForms UI test)");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"FAIL after {checks} checks: {ex}");
    return 1;
}
finally
{
    CultureInfo.CurrentCulture = originalCulture;
    CultureInfo.CurrentUICulture = originalUiCulture;
    CultureInfo.DefaultThreadCurrentUICulture = null;
}
