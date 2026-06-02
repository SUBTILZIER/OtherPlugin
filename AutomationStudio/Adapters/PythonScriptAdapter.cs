using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace AutomationStudioWpf.Adapters;

public sealed class PythonScriptAdapter : IPythonScriptAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public PythonScriptResult RunJsonScript(string scriptPath, object payload, TimeSpan timeout, CancellationToken ct)
    {
        if (!File.Exists(scriptPath))
            return new PythonScriptResult(false, -1, string.Empty, string.Empty, $"Python 脚本不存在：{scriptPath}");

        string requestPath = Path.Combine(Path.GetTempPath(), $"automation_studio_{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(requestPath, JsonSerializer.Serialize(payload, JsonOptions), Utf8NoBom);
            string pythonExe = ResolvePythonPath();
            using Process process = new()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = pythonExe,
                    Arguments = $"\"{scriptPath}\" \"{requestPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
            };

            process.Start();
            Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
            Task<string> errorTask = process.StandardError.ReadToEndAsync();
            DateTime deadline = DateTime.UtcNow.Add(timeout);

            while (!process.WaitForExit(100))
            {
                ct.ThrowIfCancellationRequested();
                if (DateTime.UtcNow >= deadline)
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                    return new PythonScriptResult(false, -1, string.Empty, string.Empty, "Python 脚本执行超时。");
                }
            }

            string stdout = outputTask.GetAwaiter().GetResult();
            string stderr = errorTask.GetAwaiter().GetResult();
            bool success = process.ExitCode == 0;
            return new PythonScriptResult(success, process.ExitCode, stdout, stderr, success ? "Python 脚本执行完成。" : $"Python 脚本退出码 {process.ExitCode}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex.Message.Contains("系统找不到指定的文件"))
        {
            return new PythonScriptResult(false, -1, string.Empty, string.Empty, "未找到 Python 环境。");
        }
        catch (Exception ex)
        {
            return new PythonScriptResult(false, -1, string.Empty, string.Empty, ex.Message);
        }
        finally
        {
            try { if (File.Exists(requestPath)) File.Delete(requestPath); } catch { }
        }
    }

    private static string ResolvePythonPath()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string programsPython = Path.Combine(localAppData, "Programs", "Python");
        if (Directory.Exists(programsPython))
        {
            string[] dirs = Directory.GetDirectories(programsPython, "Python3*");
            if (dirs.Length > 0)
            {
                string exe = Path.Combine(dirs[0], "python.exe");
                if (File.Exists(exe))
                    return exe;
            }
        }

        return "python";
    }
}

