using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutomationStudioWpf.Services;

public enum AppThemeMode
{
    Dark,
    Light,
}

public sealed class AppSettings
{
    public AppThemeMode ThemeMode { get; set; } = AppThemeMode.Dark;

    public string AccentColor { get; set; } = "#4FA3FF";

    public bool HighContrastTooltips { get; set; } = true;

    public AppSettings Clone() => new()
    {
        ThemeMode = ThemeMode,
        AccentColor = AccentColor,
        HighContrastTooltips = HighContrastTooltips,
    };

    public void Normalize()
    {
        if (!AppThemeService.TryParseColor(AccentColor, out _))
            AccentColor = ThemeMode == AppThemeMode.Light ? "#7C8DFF" : "#4FA3FF";
    }
}

public sealed class AppSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _settingsPath;

    public AppSettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var folder = Path.Combine(appData, "AutomationStudioWpf");
        Directory.CreateDirectory(folder);
        _settingsPath = Path.Combine(folder, "app-settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return new AppSettings();

            var json = File.ReadAllText(_settingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            settings.Normalize();
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        settings.Normalize();
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_settingsPath, json);
    }
}
