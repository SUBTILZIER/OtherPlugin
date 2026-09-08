using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Windows.Threading;
using System.Diagnostics;
using AutomationStudioWpf.Collections;
using AutomationStudioWpf.Services;

namespace AutomationStudioWpf.Logging;

public static class Logger
{
    private const int MaxUiEntries = 5000;
    private const int LogRetentionDays = 5;
    private const long MaxLogDirectoryBytes = 128L * 1024 * 1024;

    private static readonly object _lock = new();
    private static readonly object _uiLock = new();
    private static readonly List<LogEntry> _pendingUiEntries = [];
    private static readonly AsyncLocal<LogCaptureScope?> _activeCapture = new();
    private static readonly Channel<FileLogEntry> _fileQueue = Channel.CreateBounded<FileLogEntry>(
        new BoundedChannelOptions(8192)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    private static bool _uiFlushQueued;

    public static ObservableCollection<LogEntry> Entries { get; } = new RangeObservableCollection<LogEntry>();

    static Logger()
    {
        _ = Task.Run(WriteFileLoop);
        _ = Task.Run(CleanupOldLogs);
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
        QueueFileEntry(level, entry);

        // Captured node-internal entries feed the structured node block only.
        // They still reach the log file above, but must not duplicate in the UI.
        if (!bypassCapture && _activeCapture.Value is { } capture)
        {
            capture.Add(entry);
            return;
        }

        QueueEntryForUi(entry);
    }

    public static string GetLogDirectory() => LogDirectory;

    private static string LogDirectory => ApplicationPaths.LogDirectory;

    private static string CurrentLogFilePath(DateTime localTime) =>
        Path.Combine(LogDirectory, $"Log_{localTime:yyyy_MM_dd_HH}.txt");

    private static void QueueFileEntry(LogLevel level, LogEntry entry)
    {
        var fileEntry = new FileLogEntry(
            DateTime.Now,
            $"[{entry.Timestamp}] [{LevelLabel(entry.Level)}] {entry.Message}{Environment.NewLine}");
        if (_fileQueue.Writer.TryWrite(fileEntry))
            return;

        // Preserve errors if the bounded queue is under pressure.
        if (level == LogLevel.Error)
            WriteFileEntries([fileEntry]);
    }

    private static async Task WriteFileLoop()
    {
        try
        {
            await foreach (FileLogEntry first in _fileQueue.Reader.ReadAllAsync())
            {
                var batch = new List<FileLogEntry> { first };
                while (batch.Count < 256 && _fileQueue.Reader.TryRead(out FileLogEntry next))
                    batch.Add(next);
                WriteFileEntries(batch);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"日志写入线程退出：{ex.Message}");
        }
    }

    private static void WriteFileEntries(IReadOnlyList<FileLogEntry> entries)
    {
        lock (_lock)
        {
            try
            {
                var grouped = entries
                    .GroupBy(entry => CurrentLogFilePath(entry.LocalTime), StringComparer.OrdinalIgnoreCase);
                foreach (var group in grouped)
                {
                    Directory.CreateDirectory(LogDirectory);
                    var builder = new StringBuilder();
                    foreach (FileLogEntry entry in group)
                        builder.Append(entry.Line);
                    File.AppendAllText(group.Key, builder.ToString());
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"日志写入失败：{ex.Message}");
            }
        }
    }

    private static void CleanupOldLogs()
    {
        lock (_lock)
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                string currentLogPath = Path.GetFullPath(CurrentLogFilePath(DateTime.Now));
                DateTime cutoffUtc = DateTime.UtcNow.AddDays(-LogRetentionDays);
                var files = Directory
                    .EnumerateFiles(LogDirectory, "Log_*.txt", SearchOption.TopDirectoryOnly)
                    .Select(path => new FileInfo(path))
                    .OrderBy(file => file.LastWriteTimeUtc)
                    .ToList();

                foreach (FileInfo file in files.Where(file =>
                             !IsCurrentLog(file, currentLogPath) &&
                             file.LastWriteTimeUtc < cutoffUtc))
                {
                    TryDeleteLog(file);
                }

                files = files.Where(file => file.Exists).OrderBy(file => file.LastWriteTimeUtc).ToList();
                long totalBytes = files.Sum(file => SafeLength(file));
                foreach (FileInfo file in files)
                {
                    if (totalBytes <= MaxLogDirectoryBytes)
                        break;
                    if (IsCurrentLog(file, currentLogPath))
                        continue;

                    long length = SafeLength(file);
                    if (TryDeleteLog(file))
                        totalBytes = Math.Max(0, totalBytes - length);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"日志清理失败：{ex.Message}");
            }
        }
    }

    private static bool IsCurrentLog(FileInfo file, string currentLogPath) =>
        string.Equals(file.FullName, currentLogPath, StringComparison.OrdinalIgnoreCase);

    private static long SafeLength(FileInfo file)
    {
        try
        {
            return file.Exists ? file.Length : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static bool TryDeleteLog(FileInfo file)
    {
        try
        {
            file.Delete();
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"日志文件清理失败：{file.FullName}：{ex.Message}");
            return false;
        }
    }

    private readonly record struct FileLogEntry(DateTime LocalTime, string Line);

    public static void ClearUiEntries()
    {
        lock (_uiLock)
            _pendingUiEntries.Clear();
        Entries.Clear();
    }

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
        private bool _summarized;

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

        public void MarkSummarized()
        {
            lock (_gate)
                _summarized = true;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            if (ReferenceEquals(_activeCapture.Value, this))
                _activeCapture.Value = _parent;

            List<LogEntry> entriesToReplay;
            lock (_gate)
                entriesToReplay = _summarized ? [] : [.. _entries];
            foreach (LogEntry entry in entriesToReplay)
                QueueEntryForUi(entry);
        }
    }
}
