using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AutomationStudioWpf.Logging;

namespace AutomationStudioWpf.Services;

public enum AppThemeMode
{
    Dark,
    Light,
}

public enum AppWindowCloseAction
{
    MinimizeToTray,
    ExitApplication,
}

public sealed class AppSettings
{
    public AppThemeMode ThemeMode { get; set; } = AppThemeMode.Dark;

    public string AccentColor { get; set; } = "#4FA3FF";

    public double AccentOpacity { get; set; } = 0.62;

    public AppWindowCloseAction WindowCloseAction { get; set; } = AppWindowCloseAction.MinimizeToTray;

    public bool HighContrastTooltips { get; set; } = true;

    public AppSettings Clone() => new()
    {
        ThemeMode = ThemeMode,
        AccentColor = AccentColor,
        AccentOpacity = AccentOpacity,
        WindowCloseAction = WindowCloseAction,
        HighContrastTooltips = HighContrastTooltips,
    };

    public void Normalize()
    {
        if (!AppThemeService.TryParseColor(AccentColor, out _))
            AccentColor = "#4FA3FF";
        AccentOpacity = Math.Clamp(AccentOpacity, 0.18, 1.0);
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
    private bool _writesBlockedByLoadFailure;

    public AppSettingsService()
    {
        string folder = ApplicationPaths.RoamingDataRoot;
        Directory.CreateDirectory(folder);
        _settingsPath = Path.Combine(folder, "app-settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath) && !File.Exists(_settingsPath + ".bak"))
                return new AppSettings();

            var readResult = AtomicJsonFileStore.Read<AppSettings>(_settingsPath, JsonOptions);
            var settings = readResult.Value;
            _writesBlockedByLoadFailure = readResult.RepairError is not null;
            if (readResult.RecoveredFromBackup && readResult.PrimaryFileRepaired)
                Logger.Warn($"设置已从备份恢复，并修复主文件：{_settingsPath}");
            else if (readResult.RepairError is not null)
                Logger.Error($"设置已从备份读取，但主文件修复失败，本次运行禁止覆盖：{readResult.RepairError.Message}");
            settings.Normalize();
            return settings;
        }
        catch (Exception ex)
        {
            _writesBlockedByLoadFailure = true;
            Logger.Error($"设置读取失败，已阻止覆盖原文件：{ex.Message}");
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        settings.Normalize();
        if (_writesBlockedByLoadFailure)
            throw new InvalidOperationException($"设置主文件无法安全恢复。为避免覆盖原数据，本次运行已禁止保存：{_settingsPath}");
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        AtomicJsonFileStore.Write(_settingsPath, settings, JsonOptions);
    }
}
