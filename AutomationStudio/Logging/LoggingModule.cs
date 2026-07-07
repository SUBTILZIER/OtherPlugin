using System.Collections.ObjectModel;
using System.Windows;
using Media = System.Windows.Media;
using WpfApplication = System.Windows.Application;

namespace AutomationStudioWpf.Logging;

/// <summary>
/// Log management module — filter state, color mapping, filtered view.
/// Decouples log display from raw storage.
/// </summary>
public static class LoggingModule
{
    /// <summary>null = 显示全部 (无)</summary>
    public static LogLevel? FilterLevel { get; set; }

    public static Media.Brush GetLevelBrush(LogLevel level) => level switch
    {
        LogLevel.Warn => ResourceBrush("CompileDirtyBorderBrush"),
        LogLevel.Error => ResourceBrush("LogErrorBrush"),
        _ => ResourceBrush("EditorTextBrush"),
    };

    /// <summary>Filter label for display (WARN/ERROR/INFO)</summary>
    public static string LevelLabel(LogLevel level) => level switch
    {
        LogLevel.Warn => "WARN",
        LogLevel.Error => "ERROR",
        _ => "INFO",
    };

    public static IEnumerable<LogEntry> Filter(ObservableCollection<LogEntry> source)
    {
        if (FilterLevel is null)
            return source;

        return source.Where(e => e.Level == FilterLevel.Value);
    }

    private static Media.Brush ResourceBrush(string key) =>
        WpfApplication.Current?.TryFindResource(key) as Media.Brush ?? Media.Brushes.White;
}
