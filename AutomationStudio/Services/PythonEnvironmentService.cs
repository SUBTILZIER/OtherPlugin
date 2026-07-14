using System.Diagnostics;
using System.IO;
using AutomationStudioWpf.Logging;

namespace AutomationStudioWpf.Services;

internal sealed record PythonEnvironmentResult(
    bool IsAvailable,
    string PythonPath,
    IReadOnlyList<string> MissingLibraries);

internal sealed class PythonEnvironmentService
{
    private static readonly string[] RequiredLibraries = ["cv2", "PIL", "numpy"];
    private readonly object _cacheGate = new();
    private readonly object _processGate = new();
    private readonly SemaphoreSlim _checkSemaphore = new(1, 1);
    private readonly HashSet<Process> _activeProcesses = [];
    private PythonEnvironmentResult? _cachedResult;
    private bool _missingDialogShown;

    public static PythonEnvironmentService Shared { get; } = new();

    public string ValidatedPythonPath
    {
        get
        {
            lock (_cacheGate)
            {
                return _cachedResult is { IsAvailable: true } result
                    ? result.PythonPath
                    : throw new InvalidOperationException("Python 环境尚未通过验证。");
            }
        }
    }

    public async Task<bool> EnsureReadyAsync(IProgress<string> progress, CancellationToken ct)
    {
        var (result, fromCache) = await GetEnvironmentResultAsync(progress, ct);
        if (result.IsAvailable)
        {
            Logger.Info(fromCache
                ? $"Python 环境正常（缓存）: {result.PythonPath}"
                : $"Python 环境正常: {result.PythonPath}");
            return true;
        }

        if (!string.IsNullOrEmpty(result.PythonPath) && result.MissingLibraries.Count > 0)
        {
            Logger.Warn($"Python 已安装但缺少依赖库: {string.Join(", ", result.MissingLibraries)}");
            if (ShouldShowMissingDialog())
            {
                ShowInstallDialog(
                    "依赖库",
                    "打开命令提示符（CMD），执行以下命令：",
                    "pip install opencv-python pillow numpy -i https://mirrors.aliyun.com/pypi/simple/");
            }

            return false;
        }

        Logger.Warn("未检测到 Python 环境");
        if (ShouldShowMissingDialog())
        {
            ShowInstallDialog(
                "Python",
                "安装 Python 3，并勾选 Add Python to PATH。安装完成后执行：",
                "pip install opencv-python pillow numpy -i https://mirrors.aliyun.com/pypi/simple/");
        }

        return false;
    }

    public void Invalidate()
    {
        lock (_cacheGate)
        {
            _cachedResult = null;
            _missingDialogShown = false;
        }
    }

    internal IDisposable RegisterProcess(Process process)
    {
        lock (_processGate)
            _activeProcesses.Add(process);
        return new ProcessRegistration(this, process);
    }

    internal void TerminateAllProcesses()
    {
        Process[] processes;
        lock (_processGate)
            processes = _activeProcesses.ToArray();

        foreach (Process process in processes)
            TerminateProcess(process);
    }

    internal void TerminateAllProcessesImmediately()
    {
        Process[] processes;
        lock (_processGate)
            processes = _activeProcesses.ToArray();

        foreach (Process process in processes)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                Logger.Warn($"退出时终止 Python 进程失败：{ex.Message}");
            }
        }
    }

    private void UnregisterProcess(Process process)
    {
        lock (_processGate)
            _activeProcesses.Remove(process);
    }

    private async Task<(PythonEnvironmentResult Result, bool FromCache)> GetEnvironmentResultAsync(
        IProgress<string> progress,
        CancellationToken ct)
    {
        lock (_cacheGate)
        {
            if (_cachedResult is not null)
                return (_cachedResult, true);
        }

        await _checkSemaphore.WaitAsync(ct);
        try
        {
            lock (_cacheGate)
            {
                if (_cachedResult is not null)
                    return (_cachedResult, true);
            }

            progress.Report("正在检查 Python 环境...");
            PythonEnvironmentResult checkedResult = await Task.Run(() => CheckEnvironment(ct), ct);
            lock (_cacheGate)
            {
                if (checkedResult.IsAvailable)
                    _cachedResult ??= checkedResult;

                return (_cachedResult ?? checkedResult, false);
            }
        }
        finally
        {
            _checkSemaphore.Release();
        }
    }

    private static PythonEnvironmentResult CheckEnvironment(CancellationToken ct)
    {
        string pythonPath = ResolvePythonPath(ct);
        if (string.IsNullOrWhiteSpace(pythonPath))
            return new PythonEnvironmentResult(false, string.Empty, ["Python"]);

        var missingLibraries = new List<string>();
        foreach (string library in RequiredLibraries)
        {
            ct.ThrowIfCancellationRequested();
            if (!RunProbe(pythonPath, ["-c", $"import {library}"], TimeSpan.FromSeconds(5), ct))
                missingLibraries.Add(library);
        }

        return new PythonEnvironmentResult(missingLibraries.Count == 0, pythonPath, missingLibraries);
    }

    private static string ResolvePythonPath(CancellationToken ct)
    {
        foreach (string candidate in EnumeratePythonCandidates(ct))
        {
            ct.ThrowIfCancellationRequested();
            if (File.Exists(candidate) && RunProbe(candidate, ["--version"], TimeSpan.FromSeconds(3), ct))
                return candidate;
        }

        return string.Empty;
    }

    private static IEnumerable<string> EnumeratePythonCandidates(CancellationToken ct)
    {
        var candidates = new List<string>();
        string localPrograms = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "Python");
        if (Directory.Exists(localPrograms))
        {
            candidates.AddRange(Directory
                .GetDirectories(localPrograms, "Python3*")
                .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path => Path.Combine(path, "python.exe")));
        }

        foreach (string root in new[] { @"C:\Program Files", @"C:\" })
        {
            if (!Directory.Exists(root))
                continue;
            candidates.AddRange(Directory
                .GetDirectories(root, "Python3*")
                .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path => Path.Combine(path, "python.exe")));
        }

        candidates.AddRange(QueryPathPython(ct));
        return candidates
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> QueryPathPython(CancellationToken ct)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "where.exe",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };
        process.StartInfo.ArgumentList.Add("python.exe");

        try
        {
            process.Start();
            while (!process.WaitForExit(100))
            {
                if (ct.IsCancellationRequested)
                {
                    TerminateProcess(process);
                    ct.ThrowIfCancellationRequested();
                }
            }

            if (process.ExitCode != 0)
                return [];
            return process.StandardOutput.ReadToEnd()
                .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return [];
        }
    }

    private static bool RunProbe(
        string executable,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken ct)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        foreach (string argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        try
        {
            process.Start();
            long deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
            while (!process.WaitForExit(100))
            {
                if (ct.IsCancellationRequested)
                {
                    TerminateProcess(process);
                    ct.ThrowIfCancellationRequested();
                }

                if (Environment.TickCount64 >= deadline)
                {
                    TerminateProcess(process);
                    return false;
                }
            }

            return process.ExitCode == 0;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            TerminateProcess(process);
            return false;
        }
    }

    internal static bool TerminateProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
        }

        try
        {
            process.WaitForExit(2000);
        }
        catch
        {
        }

        try
        {
            return process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private bool ShouldShowMissingDialog()
    {
        lock (_cacheGate)
        {
            if (_missingDialogShown)
                return false;
            _missingDialogShown = true;
            return true;
        }
    }

    private static void ShowInstallDialog(string title, string instructions, string command)
    {
        Logging.Logger.Warn($"需要安装 {title}：{instructions} {command}");
        if (System.Windows.Application.Current?.MainWindow is { } owner)
        {
            Interaction.ThemedDialog.Show(
                owner,
                $"{instructions}\n\n{command}",
                $"需要安装 {title}",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
    }

    private sealed class ProcessRegistration(PythonEnvironmentService owner, Process process) : IDisposable
    {
        private PythonEnvironmentService? _owner = owner;

        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?.UnregisterProcess(process);
        }
    }
}
