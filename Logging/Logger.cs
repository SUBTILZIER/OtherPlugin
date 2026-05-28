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

    public static void Info(string message) => Write("INFO", message);
    public static void Error(string message) => Write("ERROR", message);
    public static void Warn(string message) => Write("WARN", message);

    private static void Write(string level, string message)
    {
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        LogEntry entry = new(timestamp, level, message);

        // Always write to file immediately (safe from any thread).
        lock (_lock)
        {
            try
            {
                string logFile = Path.Combine(LogDir, $"Log_{DateTime.Now:yyyy_MM_dd_HH_mm}.txt");
                File.AppendAllText(logFile, $"[{entry.Timestamp}] [{entry.Level}] {entry.Message}{Environment.NewLine}");
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

public record LogEntry(string Timestamp, string Level, string Message);
