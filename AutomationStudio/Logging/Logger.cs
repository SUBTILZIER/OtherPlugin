using System.Collections.ObjectModel;
using System.IO;

namespace AutomationStudioWpf.Logging;

public static class Logger
{
    private static readonly string LogDir = Path.Combine(AppContext.BaseDirectory, "saved", "log");
    private static readonly object _lock = new();

    public static ObservableCollection<LogEntry> Entries { get; } = [];

    static Logger()
    {
        Directory.CreateDirectory(LogDir);
    }

    public static void Info(string message) => Write(LogLevel.Info, message);
    public static void Error(string message) => Write(LogLevel.Error, message);
    public static void Warn(string message) => Write(LogLevel.Warn, message);

    private static string LevelLabel(LogLevel level) => level switch
    {
        LogLevel.Warn => "WARN",
        LogLevel.Error => "ERROR",
        _ => "INFO",
    };

    private static void Write(LogLevel level, string message)
    {
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        LogEntry entry = new(timestamp, level, message);

        // Always write to file immediately (safe from any thread).
        lock (_lock)
        {
            try
            {
                string logFile = Path.Combine(LogDir, $"Log_{DateTime.Now:yyyy_MM_dd_HH_mm}.txt");
                File.AppendAllText(logFile, $"[{entry.Timestamp}] [{LevelLabel(entry.Level)}] {entry.Message}{Environment.NewLine}");
            }
            catch
            {
                // Best effort; don't crash.
            }
        }

        // Marshal collection write to UI thread.
        System.Windows.Application.Current.Dispatcher.InvokeAsync(() => Entries.Add(entry));
    }

    public static string GetLogDirectory() => LogDir;
}
