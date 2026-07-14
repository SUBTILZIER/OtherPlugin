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
        if (!File.Exists(scriptPath))
            return new PythonScriptResult(false, -1, string.Empty, string.Empty, $"Python 脚本不存在：{scriptPath}");

        string requestPath = Path.Combine(Path.GetTempPath(), $"automation_studio_{Guid.NewGuid():N}.json");
        Process? process = null;
        IDisposable? processRegistration = null;
        Task<string>? outputTask = null;
        Task<string>? errorTask = null;
        bool preserveRequestFile = false;
        try
        {
            File.WriteAllText(requestPath, JsonSerializer.Serialize(payload, JsonOptions), Utf8NoBom);
            string pythonExe = _environment.ValidatedPythonPath;
            process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = pythonExe,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
            };
            process.StartInfo.ArgumentList.Add(scriptPath);
            process.StartInfo.ArgumentList.Add(requestPath);

            process.Start();
            processRegistration = _environment.RegisterProcess(process);
            outputTask = process.StandardOutput.ReadToEndAsync();
            errorTask = process.StandardError.ReadToEndAsync();
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

            string stdout = outputTask.GetAwaiter().GetResult();
            string stderr = FilterBenignPythonStderr(errorTask.GetAwaiter().GetResult());
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
            processRegistration?.Dispose();
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
        if (!PythonEnvironmentService.TerminateProcess(process))
            return false;

        DrainOutput(outputTask, errorTask);
        return true;
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

    private static void DrainOutput(Task<string>? outputTask, Task<string>? errorTask)
    {
        try { outputTask?.GetAwaiter().GetResult(); } catch { }
        try { errorTask?.GetAwaiter().GetResult(); } catch { }
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
