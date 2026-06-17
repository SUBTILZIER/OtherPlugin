using System.Collections.ObjectModel;
using Media = System.Windows.Media;

namespace AutomationStudioWpf.Logging;

/// <summary>
/// Log management module — filter state, color mapping, filtered view.
/// Decouples log display from raw storage.
/// </summary>
public static class LoggingModule
{
    private static readonly Media.Brush InfoBrush = CreateFrozenBrush(208, 215, 226);
    private static readonly Media.Brush WarnBrush = CreateFrozenBrush(255, 215, 0);
    private static readonly Media.Brush ErrorBrush = CreateFrozenBrush(255, 107, 107);

    /// <summary>null = 显示全部 (无)</summary>
    public static LogLevel? FilterLevel { get; set; }

    public static Media.Brush GetLevelBrush(LogLevel level) => level switch
    {
        LogLevel.Warn => WarnBrush,
        LogLevel.Error => ErrorBrush,
        _ => InfoBrush,
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

    private static Media.Brush CreateFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new Media.SolidColorBrush(Media.Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
