using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;

namespace AutomationStudioWpf.Logging;

public static class Logger
{
    private static readonly string LogDir = Path.Combine(AppContext.BaseDirectory, "saved", "log");
    private static readonly object _lock = new();
    private static readonly object _uiLock = new();
    private static readonly List<LogEntry> _pendingUiEntries = [];
    private static bool _uiFlushQueued;

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

        QueueEntryForUi(entry);
    }

    public static string GetLogDirectory() => LogDir;

    private static void QueueEntryForUi(LogEntry entry)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            return;

        lock (_uiLock)
        {
            _pendingUiEntries.Add(entry);
            if (_uiFlushQueued)
                return;

            _uiFlushQueued = true;
        }

        dispatcher.BeginInvoke(FlushPendingUiEntries, DispatcherPriority.Background);
    }

    private static void FlushPendingUiEntries()
    {
        List<LogEntry> entries;
        lock (_uiLock)
        {
            entries = [.. _pendingUiEntries];
            _pendingUiEntries.Clear();
            _uiFlushQueued = false;
        }

        foreach (var entry in entries)
            Entries.Add(entry);
    }
}
