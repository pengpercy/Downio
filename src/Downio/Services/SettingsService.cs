using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Downio.Models;

namespace Downio.Services;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal partial class SettingsJsonContext : JsonSerializerContext
{
}

public class SettingsService
{
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Downio",
        "settings.json");

    public AppSettings Settings { get; private set; }

    public SettingsService()
    {
        Settings = Load();
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings) ?? new AppSettings();
            }
        }
        catch
        {
            // Ignore errors and return default
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir!);
            }

            var json = JsonSerializer.Serialize(Settings, SettingsJsonContext.Default.AppSettings);
            File.WriteAllText(ConfigPath, json);
        }
        catch
        {
            // Ignore save errors
        }
    }

    public void ReplaceSettings(AppSettings settings)
    {
        Settings = settings ?? new AppSettings();
        Save();
    }

    public void ResetToDefaults()
    {
        Settings = new AppSettings();
        Save();
    }
}
