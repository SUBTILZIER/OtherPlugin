using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using AutomationStudioWpf.Services;

namespace AutomationStudioWpf.Adapters;

public sealed class PythonScriptAdapter : IPythonScriptAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private readonly PythonEnvironmentService _environment;

    public PythonScriptAdapter()
        : this(PythonEnvironmentService.Shared)
    {
    }

    internal PythonScriptAdapter(PythonEnvironmentService environment)
    {
        _environment = environment;
    }

    public PythonScriptResult RunJsonScript(string scriptPath, object payload, TimeSpan timeout, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _environment.ThrowIfProcessStartBlocked();
        if (!File.Exists(scriptPath))
            return new PythonScriptResult(false, -1, string.Empty, string.Empty, $"Python 脚本不存在：{scriptPath}");

        string requestPath = Path.Combine(
            ApplicationPaths.PythonRequestDirectory,
            $"automation_studio_{Guid.NewGuid():N}.json");
        OwnedProcessHandle? ownedProcess = null;
        Process? process = null;
        Task<string>? outputTask = null;
        Task<string>? errorTask = null;
        bool preserveRequestFile = false;
        try
        {
            File.WriteAllText(requestPath, JsonSerializer.Serialize(payload, JsonOptions), Utf8NoBom);
            string pythonExe = _environment.ValidatedPythonPath;
            var startInfo = new ProcessStartInfo
            {
                FileName = pythonExe,
                WorkingDirectory = Path.GetDirectoryName(pythonExe) ?? AppContext.BaseDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("-I");
            startInfo.ArgumentList.Add(scriptPath);
            startInfo.ArgumentList.Add(requestPath);

            ownedProcess = _environment.StartOwnedProcess(
                startInfo,
                $"Python 脚本：{Path.GetFileName(scriptPath)}");
            process = ownedProcess.Process;
            outputTask = ownedProcess.StandardOutput!.ReadToEndAsync();
            errorTask = ownedProcess.StandardError!.ReadToEndAsync();
            DateTime deadline = DateTime.UtcNow.Add(timeout);

            while (!process.WaitForExit(100))
            {
                ct.ThrowIfCancellationRequested();
                if (DateTime.UtcNow >= deadline)
                {
                    preserveRequestFile = !TerminateAndDrain(process, outputTask, errorTask);
                    return new PythonScriptResult(false, -1, string.Empty, string.Empty, "Python 脚本执行超时。");
                }
            }

            if (!TryDrainOutput(outputTask, errorTask, TimeSpan.FromSeconds(2), out string stdout, out string stderr))
            {
                preserveRequestFile = !TerminateAndDrain(process, outputTask, errorTask);
                return new PythonScriptResult(false, -1, string.Empty, string.Empty,
                    "Python 输出流未能在限定时间内关闭。");
            }

            stderr = FilterBenignPythonStderr(stderr);
            bool success = process.ExitCode == 0;
            return new PythonScriptResult(success, process.ExitCode, stdout, stderr, success ? "Python 脚本执行完成。" : $"Python 脚本退出码 {process.ExitCode}");
        }
        catch (OperationCanceledException)
        {
            if (process is not null)
                preserveRequestFile = !TerminateAndDrain(process, outputTask, errorTask);
            throw;
        }
        catch (Win32Exception)
        {
            if (process is not null)
                preserveRequestFile = !TerminateAndDrain(process, outputTask, errorTask);
            _environment.Invalidate();
            return new PythonScriptResult(false, -1, string.Empty, string.Empty, "未找到 Python 环境。");
        }
        catch (Exception ex)
        {
            if (process is not null)
                preserveRequestFile = !TerminateAndDrain(process, outputTask, errorTask);
            return new PythonScriptResult(false, -1, string.Empty, string.Empty, ex.Message);
        }
        finally
        {
            if (process is not null && !HasExited(process))
                preserveRequestFile = !TerminateAndDrain(process, outputTask, errorTask);
            ownedProcess?.Dispose();
            if (ownedProcess is null)
                process?.Dispose();
            if (!preserveRequestFile)
            {
                try { if (File.Exists(requestPath)) File.Delete(requestPath); } catch { }
            }
            else
            {
                Logging.Logger.Error($"Python 子进程未能终止，已保留请求文件：{requestPath}");
            }
        }
    }

    private static bool TerminateAndDrain(Process process, Task<string>? outputTask, Task<string>? errorTask)
    {
        bool processExited = PythonEnvironmentService.TerminateProcess(process);
        bool outputDrained = WaitForDrain(outputTask, TimeSpan.FromSeconds(2));
        bool errorDrained = WaitForDrain(errorTask, TimeSpan.FromSeconds(2));
        return processExited && outputDrained && errorDrained && HasExited(process);
    }

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryDrainOutput(
        Task<string>? outputTask,
        Task<string>? errorTask,
        TimeSpan timeout,
        out string stdout,
        out string stderr)
    {
        stdout = string.Empty;
        stderr = string.Empty;
        Task<string> output = outputTask ?? Task.FromResult(string.Empty);
        Task<string> error = errorTask ?? Task.FromResult(string.Empty);
        try
        {
            if (!Task.WaitAll([output, error], timeout))
                return false;

            stdout = output.Result;
            stderr = error.Result;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool WaitForDrain(Task<string>? task, TimeSpan timeout)
    {
        if (task is null)
            return true;

        try
        {
            return task.Wait(timeout);
        }
        catch
        {
            return false;
        }
    }

    private static string FilterBenignPythonStderr(string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr))
            return string.Empty;

        var lines = stderr
            .Split(["\r\n", "\n"], StringSplitOptions.None)
            .Where(line => !line.Contains("libpng warning: iCCP: known incorrect sRGB profile", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return string.Join(Environment.NewLine, lines).Trim();
    }
}
