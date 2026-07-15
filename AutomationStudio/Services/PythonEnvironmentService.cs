using System.Diagnostics;
using System.IO;
using AutomationStudioWpf.Adapters;
using AutomationStudioWpf.Logging;

namespace AutomationStudioWpf.Services;

internal sealed record PythonEnvironmentResult(
    bool IsAvailable,
    string PythonPath,
    IReadOnlyList<string> MissingLibraries,
    bool BundledRuntimeExpected = false,
    string FailureReason = "");

internal sealed class PythonEnvironmentService : IDisposable
{
    private static readonly string[] RequiredLibraries = ["cv2", "PIL", "numpy"];
    private const string BundledPythonVersion = "3.14.6";
    private static readonly IReadOnlyDictionary<string, string> BundledPackages =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["opencv-python-headless"] = "4.13.0.92",
            ["numpy"] = "2.4.6",
            ["Pillow"] = "12.2.0",
        };
    private readonly object _cacheGate = new();
    private readonly object _processGate = new();
    private readonly SemaphoreSlim _checkSemaphore = new(1, 1);
    private readonly Dictionary<Process, RegisteredProcess> _activeProcesses = [];
    private ChildProcessJob? _processJob;
    private PythonEnvironmentResult? _cachedResult;
    private bool _missingDialogShown;
    private bool _acceptProcesses = true;
    private bool _disposed;

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

        if (result.BundledRuntimeExpected)
        {
            string reason = string.IsNullOrWhiteSpace(result.FailureReason)
                ? "私有 Python 运行时缺失或损坏。"
                : result.FailureReason;
            Logger.Error($"安装不完整：{reason}");
            if (ShouldShowMissingDialog())
            {
                ShowRepairDialog(reason);
            }

            return false;
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

    internal IDisposable StartOwnedProcess(Process process, string purpose)
    {
        ThrowIfProcessStartBlocked();
        try
        {
            if (!process.Start())
                throw new InvalidOperationException($"无法启动子进程：{purpose}。");
            return RegisterProcess(process, purpose);
        }
        catch
        {
            TerminateProcess(process);
            throw;
        }
    }

    private IDisposable RegisterProcess(Process process, string purpose)
    {
        int processId;
        try
        {
            if (process.HasExited)
                return EmptyRegistration.Instance;
            processId = process.Id;
        }
        catch (InvalidOperationException)
        {
            return EmptyRegistration.Instance;
        }

        ChildProcessJob? processJob = null;
        try
        {
            lock (_processGate)
            {
                if (_acceptProcesses && !_disposed && !RuntimeShutdownGate.IsShutdownStarted)
                    processJob = _processJob ??= new ChildProcessJob();
            }
        }
        catch (Exception ex)
        {
            TerminateProcess(process);
            string message = $"无法建立 Python 子进程安全边界：{purpose}，PID={processId}。";
            Logger.Error($"{message} {ex.Message}");
            throw new InvalidOperationException(message, ex);
        }

        if (processJob is null)
        {
            TerminateProcess(process);
            throw new OperationCanceledException("应用正在退出，Python 子进程已终止。");
        }

        if (!processJob.TryAssign(process, out int jobError))
        {
            TerminateProcess(process);
            if (!CanStartProcess())
                throw new OperationCanceledException("应用正在退出，Python 子进程已终止。");
            string message = $"Python 子进程未能加入安全 Job Object：{purpose}，PID={processId}，Win32={jobError}。";
            Logger.Error(message);
            throw new InvalidOperationException(message);
        }

        bool accepted;
        lock (_processGate)
        {
            accepted = _acceptProcesses && !_disposed && !RuntimeShutdownGate.IsShutdownStarted;
            if (accepted)
                _activeProcesses[process] = new RegisteredProcess(process, processId, purpose);
        }

        if (!accepted)
        {
            TerminateProcess(process);
            throw new OperationCanceledException("应用正在退出，Python 子进程已终止。");
        }

        return new ProcessRegistration(this, process);
    }

    internal void ThrowIfProcessStartBlocked()
    {
        if (!CanStartProcess())
            throw new OperationCanceledException("应用正在退出，已禁止启动 Python 子进程。");
    }

    internal void TerminateAllProcessesImmediately()
    {
        RegisteredProcess[] processes;
        ChildProcessJob? processJob;
        lock (_processGate)
        {
            _acceptProcesses = false;
            processes = _activeProcesses.Values.ToArray();
            processJob = _processJob;
            _processJob = null;
        }

        foreach (RegisteredProcess registered in processes)
        {
            if (!TerminateProcess(registered.Process))
                Logger.Error($"退出时未能确认子进程结束：{registered.Purpose}，PID={registered.ProcessId}。让 Job Object 继续清理。");
        }

        processJob?.Dispose();
        lock (_processGate)
            _activeProcesses.Clear();
    }

    private void UnregisterProcess(Process process)
    {
        lock (_processGate)
            _activeProcesses.Remove(process);
    }

    private bool CanStartProcess()
    {
        lock (_processGate)
            return _acceptProcesses && !_disposed && !RuntimeShutdownGate.IsShutdownStarted;
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

    private PythonEnvironmentResult CheckEnvironment(CancellationToken ct)
    {
        PythonEnvironmentResult bundledResult = CheckBundledEnvironment(ct);
        if (bundledResult.IsAvailable)
            return bundledResult;

#if !DEBUG
        return bundledResult;
#else
        bool bundledFilesPresent = Directory.Exists(ApplicationPaths.BundledPythonRoot) ||
                                   File.Exists(Path.Combine(ApplicationPaths.BundledPythonRoot, "runtime-manifest.json"));
        if (bundledFilesPresent)
            return bundledResult;

        return CheckDevelopmentEnvironment(ct);
#endif
    }

    private PythonEnvironmentResult CheckBundledEnvironment(CancellationToken ct)
    {
        string pythonPath = ApplicationPaths.BundledPythonExecutable;
        string manifestPath = Path.Combine(ApplicationPaths.BundledPythonRoot, "runtime-manifest.json");
        if (!File.Exists(pythonPath))
        {
            return new PythonEnvironmentResult(
                false,
                string.Empty,
                ["BundledPython"],
                true,
                $"缺少 {pythonPath}");
        }

        if (!BundledPythonRuntimeManifest.TryLoad(manifestPath, out var manifest, out string manifestError))
            return new PythonEnvironmentResult(false, pythonPath, ["RuntimeManifest"], true, manifestError);

        if (!string.Equals(manifest.PythonVersion, BundledPythonVersion, StringComparison.Ordinal) ||
            !string.Equals(manifest.Architecture, "win-x64", StringComparison.OrdinalIgnoreCase))
        {
            return new PythonEnvironmentResult(
                false,
                pythonPath,
                ["RuntimeVersion"],
                true,
                $"运行时版本不匹配：Python {manifest.PythonVersion} / {manifest.Architecture}");
        }

        foreach (var expected in BundledPackages)
        {
            if (!manifest.Packages.TryGetValue(expected.Key, out string? actual) ||
                !string.Equals(actual, expected.Value, StringComparison.Ordinal))
            {
                return new PythonEnvironmentResult(
                    false,
                    pythonPath,
                    [expected.Key],
                    true,
                    $"依赖版本不匹配：{expected.Key}，期望 {expected.Value}，实际 {actual ?? "缺失"}");
            }
        }

        const string validationCode =
            "import sys,cv2,numpy,PIL; " +
            "assert sys.version_info[:3]==(3,14,6); " +
            "assert cv2.__version__=='4.13.0'; " +
            "assert numpy.__version__=='2.4.6'; " +
            "assert PIL.__version__=='12.2.0'";
        if (!RunProbe(pythonPath, ["-I", "-c", validationCode], TimeSpan.FromSeconds(15), ct))
        {
            return new PythonEnvironmentResult(
                false,
                pythonPath,
                ["BundledDependencies"],
                true,
                "私有 Python 或图像依赖无法加载");
        }

        return new PythonEnvironmentResult(true, pythonPath, [], true);
    }

    private PythonEnvironmentResult CheckDevelopmentEnvironment(CancellationToken ct)
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

    private string ResolvePythonPath(CancellationToken ct)
    {
        foreach (string candidate in EnumeratePythonCandidates(ct))
        {
            ct.ThrowIfCancellationRequested();
            if (File.Exists(candidate) && RunProbe(candidate, ["--version"], TimeSpan.FromSeconds(3), ct))
                return candidate;
        }

        return string.Empty;
    }

    private IEnumerable<string> EnumeratePythonCandidates(CancellationToken ct)
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
            .Where(path => !IsAppExecutionAlias(path))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private IEnumerable<string> QueryPathPython(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ThrowIfProcessStartBlocked();
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
            using var registration = StartOwnedProcess(process, "Python 路径查找");
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

    private bool RunProbe(
        string executable,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ThrowIfProcessStartBlocked();
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
            using var registration = StartOwnedProcess(
                process,
                $"Python 环境探测：{Path.GetFileName(executable)} {string.Join(' ', arguments)}");
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

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        TerminateAllProcessesImmediately();
    }

    private static bool IsAppExecutionAlias(string path)
    {
        string normalized;
        try
        {
            normalized = Path.GetFullPath(path).Replace('/', '\\');
        }
        catch
        {
            return true;
        }

        return normalized.Contains("\\Microsoft\\WindowsApps\\", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("\\Program Files\\WindowsApps\\", StringComparison.OrdinalIgnoreCase);
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

    private static void ShowRepairDialog(string reason)
    {
        const string instructions = "安装包中的离线 Python 运行时缺失或损坏。请使用安装器执行修复，或重新安装 AutomationStudio。";
        Logger.Error($"{instructions} {reason}");
        if (System.Windows.Application.Current?.MainWindow is { } owner)
        {
            Interaction.ThemedDialog.Show(
                owner,
                $"{instructions}\n\n详细信息：{reason}",
                "安装不完整",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
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

    private sealed record RegisteredProcess(Process Process, int ProcessId, string Purpose);

    private sealed class EmptyRegistration : IDisposable
    {
        public static EmptyRegistration Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
