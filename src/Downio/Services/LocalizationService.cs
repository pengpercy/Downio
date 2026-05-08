using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Downio.Assets.Lang;

namespace Downio.Services;

public static class LocalizationService
{
    private static string? _currentLanguage;
    private static ResourceDictionary? _currentTranslations;

    public static void Initialize(string requestedLanguage)
    {
        var languageCode = ResolveLanguageCode(requestedLanguage);
        ApplyCulture(languageCode);
        SwitchLanguage(languageCode);
    }

    public static void SwitchLanguage(string languageCode)
    {
        var resolvedLanguage = ResolveLanguageCode(languageCode);
        var resources = Application.Current?.Resources;
        var hasCurrentTranslations = _currentTranslations != null &&
                                     resources?.MergedDictionaries.Contains(_currentTranslations) == true;
        if (_currentLanguage == resolvedLanguage && hasCurrentTranslations) return;

        ApplyCulture(resolvedLanguage);

        var translations = LoadTranslations(resolvedLanguage);
        if (translations != null)
        {
            if (_currentTranslations != null)
            {
                resources?.MergedDictionaries.Remove(_currentTranslations);
            }

            resources!.MergedDictionaries.Add(translations);
            _currentTranslations = translations;
            _currentLanguage = resolvedLanguage;
        }
    }

    public static string ResolveLanguageCode(string requestedLanguage)
    {
        if (!string.Equals(requestedLanguage, "System", System.StringComparison.OrdinalIgnoreCase))
        {
            return requestedLanguage;
        }

        var uiCulture = CultureInfo.CurrentUICulture;
        var cultureName = uiCulture.Name;
        var twoLetterName = uiCulture.TwoLetterISOLanguageName;

        return cultureName.StartsWith("zh", System.StringComparison.OrdinalIgnoreCase) ||
               string.Equals(twoLetterName, "zh", System.StringComparison.OrdinalIgnoreCase)
            ? "zh-CN"
            : "en-US";
    }

    private static void ApplyCulture(string languageCode)
    {
        var culture = languageCode switch
        {
            "zh-CN" => new CultureInfo("zh-CN"),
            _ => new CultureInfo("en-US")
        };

        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    private static ResourceDictionary? LoadTranslations(string languageCode)
    {
        return languageCode switch
        {
            "zh-CN" => new ZhCn(),
            _ => new EnUs()
        };
    }
}
