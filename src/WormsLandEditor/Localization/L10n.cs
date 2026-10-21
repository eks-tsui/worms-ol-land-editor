using System.Globalization;
using System.Resources;

namespace WormsLandEditor.Localization;

/// <summary>Localized UI, file-format diagnostics, and command-line messages.</summary>
public static class L10n
{
    private static readonly ResourceManager UiResources = CreateManager("UiStrings");
    private static readonly ResourceManager CoreResources = CreateManager("CoreStrings");
    private static readonly ResourceManager CliResources = CreateManager("CliStrings");

    public static string Ui(string key, params object?[] args) => Get(UiResources, key, args);
    public static string Core(string key, params object?[] args) => Get(CoreResources, key, args);
    public static string Cli(string key, params object?[] args) => Get(CliResources, key, args);

    private static ResourceManager CreateManager(string name) =>
        new($"WormsLandEditor.Resources.{name}", typeof(L10n).Assembly);

    private static string Get(ResourceManager manager, string key, object?[] args)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        string value = manager.GetString(key, CultureInfo.CurrentUICulture)
            ?? throw new MissingManifestResourceException($"Missing localization key: {key}");
        return args.Length == 0 ? value : string.Format(CultureInfo.CurrentCulture, value, args);
    }
}
