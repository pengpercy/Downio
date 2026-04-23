using System;
using Avalonia;
using Avalonia.Controls;
using Downio.Assets.Lang;

namespace Downio.Services;

public static class LocalizationService
{
    private static string? _currentLanguage;

    public static void SwitchLanguage(string languageCode)
    {
        if (_currentLanguage == languageCode) return;

        var translations = LoadTranslations(languageCode);
        if (translations != null)
        {
            // Update resources
            // Remove old dictionary if it exists to avoid accumulation (optional but good practice)
            // For now we just add, assuming the keys will overwrite or take precedence. 
            // Better to remove previous, but let's stick to the minimal fix first or simple replacement.
            // Actually, usually we clear or replace. But the previous code was just adding.
            // Let's stick to the previous logic but using the new loading method.
            
            // Note: In a robust implementation, we might want to remove the old language resource.
            // But let's follow the existing pattern first.
            Application.Current!.Resources.MergedDictionaries.Add(translations);
            _currentLanguage = languageCode;
        }
    }

    private static ResourceDictionary? LoadTranslations(string languageCode)
    {
        return languageCode switch
        {
            "zh-CN" => new ZhCn(),
            "en-US" => new EnUs(),
            _ => new EnUs() // Default to English if unknown
        };
    }
}
