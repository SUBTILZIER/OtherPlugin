using System.Diagnostics;
using System.IO;
using System.Windows;
using AutomationStudioWpf.Logging;
using MessageBox = System.Windows.MessageBox;

namespace AutomationStudioWpf.Services;

/// <summary>
/// Python 环境检测与提示
/// </summary>
public static class PythonAutoInstaller
{
    private static readonly object CacheLock = new();
    private static readonly SemaphoreSlim CheckSemaphore = new(1, 1);
    private static PythonEnvironmentResult? _cachedResult;
    private static bool _missingDialogShown;

    private sealed record PythonEnvironmentResult(bool IsAvailable, string PythonPath, List<string> MissingLibraries);

    /// <summary>
    /// 检查 Python 环境，如缺失则提示用户手动安装
    /// </summary>
    public static async Task<bool> EnsurePythonAsync(IProgress<string> progress)
    {
        var (result, fromCache) = await GetEnvironmentResultAsync(progress);
        
        if (result.IsAvailable)
        {
            Logger.Info(fromCache
                ? $"Python 环境正常（缓存）: {result.PythonPath}"
                : $"Python 环境正常: {result.PythonPath}");
            return true;
        }

        // Python 未安装或缺少依赖
        if (!string.IsNullOrEmpty(result.PythonPath) && File.Exists(result.PythonPath) && result.MissingLibraries.Count > 0)
        {
            // Python 已安装但缺少依赖
            Logger.Warn($"Python 已安装但缺少依赖库: {string.Join(", ", result.MissingLibraries)}");
            
            if (ShouldShowMissingDialog())
            {
                ShowInstallDialog("依赖库", """
                    1. 打开命令提示符（CMD）
                    2. 执行以下命令：
                    """, "pip install opencv-python pillow numpy -i https://mirrors.aliyun.com/pypi/simple/");
            }
            
            return false;
        }

        // Python 未安装
        Logger.Warn("未检测到 Python 环境");
        
        if (ShouldShowMissingDialog())
        {
            ShowInstallDialog("Python", """
                1. 下载 Python 3.11
                   https://www.python.org/downloads/release/python-3118/
                   下载 Windows installer (64-bit)

                2. 运行安装程序，勾选 "Add Python to PATH"

                3. 安装依赖库，打开 CMD 执行：
                """, "pip install opencv-python pillow numpy -i https://mirrors.aliyun.com/pypi/simple/");
        }

        return false;
    }

    private static async Task<(PythonEnvironmentResult Result, bool FromCache)> GetEnvironmentResultAsync(IProgress<string> progress)
    {
        lock (CacheLock)
        {
            if (_cachedResult is not null)
                return (_cachedResult, true);
        }

        await CheckSemaphore.WaitAsync();
        try
        {
            lock (CacheLock)
            {
                if (_cachedResult is not null)
                    return (_cachedResult, true);
            }

            progress.Report("正在检查 Python 环境...");
            var (isAvailable, pythonPath, missingLibs) = await Task.Run(CheckEnvironmentDetailed);
            var checkedResult = new PythonEnvironmentResult(isAvailable, pythonPath, missingLibs);

            lock (CacheLock)
            {
                _cachedResult ??= checkedResult;
                return (_cachedResult, !ReferenceEquals(_cachedResult, checkedResult));
            }
        }
        finally
        {
            CheckSemaphore.Release();
        }
    }

    private static bool ShouldShowMissingDialog()
    {
        lock (CacheLock)
        {
            if (_missingDialogShown)
                return false;

            _missingDialogShown = true;
            return true;
        }
    }

    /// <summary>
    /// 详细检查 Python 环境
    /// </summary>
    private static (bool IsAvailable, string PythonPath, List<string> MissingLibraries) CheckEnvironmentDetailed()
    {
        var pythonPath = ResolvePythonPath();

        if (string.IsNullOrEmpty(pythonPath) || (!File.Exists(pythonPath) && pythonPath == "python"))
        {
            return (false, "", new List<string> { "Python" });
        }

        // 检查必要库
        var missingLibs = CheckLibraries(pythonPath);
        if (missingLibs.Count > 0)
        {
            return (false, pythonPath, missingLibs);
        }

        return (true, pythonPath, new List<string>());
    }

    private static string ResolvePythonPath()
    {
        // 检查常见路径
        var commonPaths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python"),
            @"C:\Program Files\Python311",
            @"C:\Python311",
        };

        foreach (var basePath in commonPaths)
        {
            if (!Directory.Exists(basePath)) continue;
            var exe = Directory.GetFiles(basePath, "python.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (exe != null) return exe;
        }

        // 尝试 PATH
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "where",
                Arguments = "python",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process != null)
            {
                if (!process.WaitForExit(3000))
                {
                    TryKill(process);
                    return "python";
                }

                var output = process.StandardOutput.ReadToEnd();
                if (process.ExitCode == 0)
                {
                    var path = output.Trim().Split('\n')[0].Trim();
                    if (File.Exists(path)) return path;
                }
            }
        }
        catch { }

        return "python";
    }

    private static List<string> CheckLibraries(string pythonPath)
    {
        var missing = new List<string>();
        var libs = new[] { "cv2", "PIL", "numpy" };

        foreach (var lib in libs)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = pythonPath,
                    Arguments = $"-c \"import {lib}\"",
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = Process.Start(psi);
                if (process != null)
                {
                    if (!process.WaitForExit(5000))
                    {
                        TryKill(process);
                        missing.Add(lib);
                        continue;
                    }

                    if (process.ExitCode != 0) missing.Add(lib);
                }
            }
            catch { missing.Add(lib); }
        }

        return missing;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best-effort cleanup for timed-out environment probes.
        }
    }

    /// <summary>
    /// 显示可复制的安装对话框（使用 WinForms）
    /// </summary>
    private static void ShowInstallDialog(string title, string instructions, string command)
    {
        using var form = new System.Windows.Forms.Form
        {
            Text = $"需要安装 {title}",
            Width = 550,
            Height = 320,
            StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen,
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false
        };

        var y = 20;

        // 标题
        var titleLabel = new System.Windows.Forms.Label
        {
            Text = $"未检测到 {title}",
            Font = new System.Drawing.Font("Segoe UI", 12, System.Drawing.FontStyle.Bold),
            Location = new System.Drawing.Point(20, y),
            Size = new System.Drawing.Size(500, 25)
        };
        form.Controls.Add(titleLabel);
        y += 35;

        // 说明文字
        var instructionLabel = new System.Windows.Forms.Label
        {
            Text = instructions.Trim(),
            Location = new System.Drawing.Point(20, y),
            Size = new System.Drawing.Size(500, 60)
        };
        form.Controls.Add(instructionLabel);
        y += 70;

        // 可复制的命令文本框
        var commandBox = new System.Windows.Forms.TextBox
        {
            Text = command,
            ReadOnly = true,
            Font = new System.Drawing.Font("Consolas", 10),
            Location = new System.Drawing.Point(20, y),
            Size = new System.Drawing.Size(500, 60),
            Multiline = true,
            ScrollBars = System.Windows.Forms.ScrollBars.Vertical,
            BackColor = System.Drawing.Color.FromArgb(240, 240, 240)
        };
        form.Controls.Add(commandBox);
        y += 70;

        // 复制按钮
        var copyButton = new System.Windows.Forms.Button
        {
            Text = "复制命令",
            Location = new System.Drawing.Point(340, y),
            Size = new System.Drawing.Size(80, 30)
        };
        copyButton.Click += (s, e) =>
        {
            System.Windows.Forms.Clipboard.SetText(command);
            copyButton.Text = "已复制!";
        };
        form.Controls.Add(copyButton);

        // 确定按钮
        var okButton = new System.Windows.Forms.Button
        {
            Text = "确定",
            Location = new System.Drawing.Point(440, y),
            Size = new System.Drawing.Size(80, 30),
            DialogResult = System.Windows.Forms.DialogResult.OK
        };
        form.Controls.Add(okButton);

        form.AcceptButton = okButton;
        form.ShowDialog();
    }
}
