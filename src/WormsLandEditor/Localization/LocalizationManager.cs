using System.Globalization;

namespace WormsLandEditor.Localization;

/// <summary>Selects one of the translations shipped with the application.</summary>
public static class LocalizationManager
{
    public static CultureInfo ResolveCulture(CultureInfo systemUiCulture)
    {
        ArgumentNullException.ThrowIfNull(systemUiCulture);
        return CultureInfo.GetCultureInfo(
            systemUiCulture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase)
                ? "zh-CN"
                : "en");
    }

    public static void Initialize(CultureInfo? systemUiCulture = null)
    {
        CultureInfo selected = ResolveCulture(systemUiCulture ?? CultureInfo.CurrentUICulture);
        CultureInfo.DefaultThreadCurrentUICulture = selected;
        CultureInfo.CurrentUICulture = selected;
        // Keep CurrentCulture unchanged: UI language must not silently change data formatting.
    }
}
