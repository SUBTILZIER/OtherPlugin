using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Windows.Threading;
using AutomationStudioWpf.Collections;

namespace AutomationStudioWpf.Logging;

public static class Logger
{
    private const int MaxUiEntries = 5000;

    private static readonly string LogDir = Path.Combine(AppContext.BaseDirectory, "saved", "log");
    private static readonly object _lock = new();
    private static readonly object _uiLock = new();
    private static readonly List<LogEntry> _pendingUiEntries = [];
    private static readonly AsyncLocal<LogCaptureScope?> _activeCapture = new();
    private static bool _uiFlushQueued;

    public static ObservableCollection<LogEntry> Entries { get; } = new RangeObservableCollection<LogEntry>();

    static Logger()
    {
        Directory.CreateDirectory(LogDir);
    }

    public static void Info(string message) => Write(LogLevel.Info, message);
    public static void Error(string message) => Write(LogLevel.Error, message);
    public static void Warn(string message) => Write(LogLevel.Warn, message);
    public static void WriteDirect(LogLevel level, string message) => Write(level, message, bypassCapture: true);

    public static LogCaptureScope BeginCapture()
    {
        var scope = new LogCaptureScope(_activeCapture.Value);
        _activeCapture.Value = scope;
        return scope;
    }

    private static string LevelLabel(LogLevel level) => level switch
    {
        LogLevel.Warn => "WARN",
        LogLevel.Error => "ERROR",
        _ => "INFO",
    };

    private static void Write(LogLevel level, string message, bool bypassCapture = false)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss");
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

        // Also add to active capture scope for structured summaries.
        if (!bypassCapture && _activeCapture.Value is { } capture)
            capture.Add(entry);
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

        if (Entries is RangeObservableCollection<LogEntry> rangeEntries)
        {
            rangeEntries.AddRange(entries);
            TrimUiEntries(rangeEntries);
            return;
        }

        foreach (var entry in entries)
            Entries.Add(entry);
        TrimUiEntries(Entries);
    }

    private static void TrimUiEntries(ICollection<LogEntry> entries)
    {
        int overflow = entries.Count - MaxUiEntries;
        if (overflow <= 0)
            return;

        if (entries is RangeObservableCollection<LogEntry> rangeEntries)
        {
            rangeEntries.RemoveFirst(overflow);
            return;
        }

        while (overflow-- > 0 && entries.Count > 0)
            entries.Remove(entries.First());
    }

    public sealed class LogCaptureScope : IDisposable
    {
        private readonly LogCaptureScope? _parent;
        private readonly object _gate = new();
        private readonly List<LogEntry> _entries = [];
        private bool _disposed;

        internal LogCaptureScope(LogCaptureScope? parent)
        {
            _parent = parent;
        }

        public IReadOnlyList<LogEntry> Entries
        {
            get
            {
                lock (_gate)
                {
                    return _entries.ToList();
                }
            }
        }

        internal void Add(LogEntry entry)
        {
            lock (_gate)
            {
                _entries.Add(entry);
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            if (ReferenceEquals(_activeCapture.Value, this))
                _activeCapture.Value = _parent;
        }
    }
}
