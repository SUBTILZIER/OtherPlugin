using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AutomationStudioWpf.Adapters;

namespace AutomationStudioWpf.Services;

internal sealed class ReleaseVerificationReport
{
    public int SchemaVersion { get; init; } = 1;
    public string Product { get; init; } = "AutomationStudio";
    public string Version { get; init; } = string.Empty;
    public string OsDescription { get; init; } = RuntimeInformation.OSDescription;
    public string Architecture { get; init; } = RuntimeInformation.ProcessArchitecture.ToString();
    public string InstallRoot { get; init; } = string.Empty;
    public string TestRoot { get; init; } = string.Empty;
    public DateTime StartedAtUtc { get; init; }
    public DateTime CompletedAtUtc { get; set; }
    public bool Passed { get; set; }
    public List<ReleaseVerificationCheck> Checks { get; init; } = [];
}

internal sealed class ReleaseVerificationCheck
{
    public required string Id { get; init; }
    public required string Status { get; init; }
    public required long DurationMs { get; init; }
    public required string Message { get; init; }
    public Dictionary<string, object?> Details { get; init; } = [];
}

internal static class ReleaseSelfTestRunner
{
    private static readonly JsonSerializerOptions ReportJsonOptions = new()
    {
        WriteIndented = true,
    };

    internal static async Task<int> RunAsync(ReleaseTestOptions options, CancellationToken ct)
    {
        string testRoot = ApplicationPaths.ReleaseTestRoot
            ?? throw new InvalidOperationException("发布自检尚未配置隔离目录。");
        string reportPath = ReleaseTestEnvironment.ValidateReportPath(options.ReportPath!, testRoot);
        var report = new ReleaseVerificationReport
        {
            Version = GetProductVersion(),
            InstallRoot = ApplicationPaths.InstallRoot,
            TestRoot = testRoot,
            StartedAtUtc = DateTime.UtcNow,
        };

        bool pythonReady = false;
        bool reportWritten = false;
        try
        {
            await RunCheckAsync(report, "path-isolation", () =>
            {
                ValidatePathIsolation(testRoot);
                return Task.CompletedTask;
            });

            pythonReady = await RunCheckAsync(report, "bundled-python", async () =>
            {
                var progress = new Progress<string>(_ => { });
                if (!await PythonEnvironmentService.Shared.EnsureReadyAsync(progress, ct))
                    throw new InvalidOperationException("私有 Python 环境验证失败。");

                string actual = Path.GetFullPath(PythonEnvironmentService.Shared.ValidatedPythonPath);
                string expected = Path.GetFullPath(ApplicationPaths.BundledPythonExecutable);
                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"运行时解释器错误：{actual}");
            });

            if (pythonReady)
            {
                await RunCheckAsync(report, "find-image", () => ValidateFindImageAsync(testRoot, ct));
                await RunCheckAsync(report, "python-cancellation-cleanup", () =>
                    ValidatePythonCancellationAsync(testRoot, ct));
            }
            else
            {
                AddNotRun(report, "find-image", "私有 Python 未通过，找图未执行。");
                AddNotRun(report, "python-cancellation-cleanup", "私有 Python 未通过，取消清理未执行。");
            }
        }
        catch (OperationCanceledException)
        {
            report.Checks.Add(new ReleaseVerificationCheck
            {
                Id = "self-test-cancelled",
                Status = "Failed",
                DurationMs = 0,
                Message = "发布自检被取消。",
            });
        }
        catch (Exception ex)
        {
            report.Checks.Add(new ReleaseVerificationCheck
            {
                Id = "self-test-unhandled",
                Status = "Failed",
                DurationMs = 0,
                Message = ex.ToString(),
            });
        }
        finally
        {
            PythonEnvironmentService.Shared.TerminateAllProcessesImmediately();
            report.CompletedAtUtc = DateTime.UtcNow;
            report.Passed = report.Checks.Count > 0 &&
                            report.Checks.All(check => check.Status == "Passed");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
                File.WriteAllText(
                    reportPath,
                    JsonSerializer.Serialize(report, ReportJsonOptions),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                reportWritten = true;
            }
            catch
            {
            }
        }

        if (!reportWritten)
            return 79;
        return report.Passed ? 0 : 70;
    }

    private static async Task<bool> RunCheckAsync(
        ReleaseVerificationReport report,
        string id,
        Func<Task> action)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await action();
            report.Checks.Add(new ReleaseVerificationCheck
            {
                Id = id,
                Status = "Passed",
                DurationMs = stopwatch.ElapsedMilliseconds,
                Message = "通过",
            });
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            report.Checks.Add(new ReleaseVerificationCheck
            {
                Id = id,
                Status = "Failed",
                DurationMs = stopwatch.ElapsedMilliseconds,
                Message = ex.Message,
                Details = new Dictionary<string, object?>
                {
                    ["exceptionType"] = ex.GetType().FullName,
                },
            });
            return false;
        }
    }

    private static void AddNotRun(ReleaseVerificationReport report, string id, string message)
    {
        report.Checks.Add(new ReleaseVerificationCheck
        {
            Id = id,
            Status = "NotRun",
            DurationMs = 0,
            Message = message,
        });
    }

    private static void ValidatePathIsolation(string testRoot)
    {
        string[] writablePaths =
        [
            ApplicationPaths.UserDataRoot,
            ApplicationPaths.LocalDataRoot,
            ApplicationPaths.LogDirectory,
            ApplicationPaths.CrashReportDirectory,
            ApplicationPaths.AutoScreenshotDirectory,
            ApplicationPaths.PythonRequestDirectory,
        ];
        string prefix = Path.GetFullPath(testRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (string path in writablePaths)
        {
            string fullPath = Path.GetFullPath(path);
            if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"可写目录逃逸测试根：{fullPath}");
        }

        string installPrefix = Path.GetFullPath(ApplicationPaths.InstallRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (writablePaths.Any(path =>
                Path.GetFullPath(path).StartsWith(installPrefix, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("发布自检可写目录不能位于安装目录中。");
        }
    }

    private static Task ValidateFindImageAsync(string testRoot, CancellationToken ct)
    {
        string workingRoot = Path.Combine(testRoot, "SelfTest", "FindImage");
        Directory.CreateDirectory(workingRoot);
        string sourcePath = Path.Combine(workingRoot, "源 图像.png");
        string templatePath = Path.Combine(workingRoot, "模板 图像.png");
        const int matchX = 19;
        const int matchY = 23;
        const int matchWidth = 12;
        const int matchHeight = 10;
        WriteTestImages(sourcePath, templatePath, matchX, matchY, matchWidth, matchHeight);

        string scriptPath = Path.Combine(ApplicationPaths.InstallRoot, "Python", "find_image.py");
        var adapter = new PythonScriptAdapter(PythonEnvironmentService.Shared);
        PythonScriptResult result = adapter.RunJsonScript(
            scriptPath,
            new
            {
                template_path = templatePath,
                source_mode = "ManualImage",
                source_image_path = sourcePath,
                threshold_percent = 99.0,
                use_region = false,
                region_x = 0,
                region_y = 0,
                region_width = 0,
                region_height = 0,
            },
            TimeSpan.FromSeconds(20),
            ct);
        if (!result.Success)
            throw new InvalidOperationException($"正式找图脚本失败：{result.Message}; {result.Stderr}");

        using JsonDocument document = JsonDocument.Parse(result.Stdout);
        JsonElement root = document.RootElement;
        bool found = root.TryGetProperty("found", out JsonElement foundElement) && foundElement.GetBoolean();
        int x = root.TryGetProperty("x", out JsonElement xElement) ? xElement.GetInt32() : -1;
        int y = root.TryGetProperty("y", out JsonElement yElement) ? yElement.GetInt32() : -1;
        if (!found || x != matchX || y != matchY)
            throw new InvalidOperationException($"找图结果错误：found={found}, x={x}, y={y}");

        return Task.CompletedTask;
    }

    private static async Task ValidatePythonCancellationAsync(string testRoot, CancellationToken outerCt)
    {
        string workingRoot = Path.Combine(testRoot, "SelfTest", "Cancellation");
        Directory.CreateDirectory(workingRoot);
        string scriptPath = Path.Combine(workingRoot, "sleep_test.py");
        await File.WriteAllTextAsync(
            scriptPath,
            "import time\ntime.sleep(60)\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            outerCt);

        HashSet<int> baseline = GetPrivatePythonProcessIds();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(outerCt);
        var adapter = new PythonScriptAdapter(PythonEnvironmentService.Shared);
        Task<PythonScriptResult> runTask = Task.Run(() => adapter.RunJsonScript(
            scriptPath,
            new { purpose = "release-cancellation-test" },
            TimeSpan.FromMinutes(2),
            cancellation.Token), CancellationToken.None);

        bool processStarted = await WaitUntilAsync(
            () => GetPrivatePythonProcessIds().Except(baseline).Any(),
            TimeSpan.FromSeconds(8),
            outerCt);
        cancellation.Cancel();
        bool cancelled = false;
        try
        {
            await runTask.WaitAsync(TimeSpan.FromSeconds(12), CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        if (!processStarted)
            throw new InvalidOperationException("取消测试未观察到私有 Python 子进程启动。");
        if (!cancelled)
            throw new InvalidOperationException("Python 任务取消后未返回 OperationCanceledException。");

        bool processesGone = await WaitUntilAsync(
            () => !GetPrivatePythonProcessIds().Except(baseline).Any(),
            TimeSpan.FromSeconds(10),
            CancellationToken.None);
        if (!processesGone)
            throw new InvalidOperationException("取消后仍有私有 Python 子进程残留。");

        string[] requests = Directory.Exists(ApplicationPaths.PythonRequestDirectory)
            ? Directory.GetFiles(ApplicationPaths.PythonRequestDirectory, "automation_studio_*.json")
            : [];
        if (requests.Length > 0)
            throw new InvalidOperationException($"取消后仍有 Python 请求文件残留：{requests[0]}");
    }

    private static async Task<bool> WaitUntilAsync(
        Func<bool> condition,
        TimeSpan timeout,
        CancellationToken ct)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        do
        {
            if (condition())
                return true;
            await Task.Delay(100, ct);
        }
        while (DateTime.UtcNow < deadline);
        return condition();
    }

    private static HashSet<int> GetPrivatePythonProcessIds()
    {
        string expected = Path.GetFullPath(ApplicationPaths.BundledPythonExecutable);
        var ids = new HashSet<int>();
        foreach (Process process in Process.GetProcessesByName("python"))
        {
            using (process)
            {
                try
                {
                    if (string.Equals(Path.GetFullPath(process.MainModule?.FileName ?? string.Empty), expected,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        ids.Add(process.Id);
                    }
                }
                catch
                {
                }
            }
        }

        return ids;
    }

    private static void WriteTestImages(
        string sourcePath,
        string templatePath,
        int templateX,
        int templateY,
        int templateWidth,
        int templateHeight)
    {
        const int width = 64;
        const int height = 64;
        const int stride = width * 4;
        byte[] source = new byte[stride * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int offset = y * stride + x * 4;
                source[offset] = (byte)((x * 17 + y * 7) % 251);
                source[offset + 1] = (byte)((x * 5 + y * 23) % 253);
                source[offset + 2] = (byte)((x * 29 + y * 11) % 247);
                source[offset + 3] = 255;
            }
        }

        byte[] template = new byte[templateWidth * templateHeight * 4];
        for (int y = 0; y < templateHeight; y++)
        {
            Buffer.BlockCopy(
                source,
                (templateY + y) * stride + templateX * 4,
                template,
                y * templateWidth * 4,
                templateWidth * 4);
        }

        WritePng(sourcePath, source, width, height, stride);
        WritePng(templatePath, template, templateWidth, templateHeight, templateWidth * 4);
    }

    private static void WritePng(string path, byte[] pixels, int width, int height, int stride)
    {
        BitmapSource bitmap = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream stream = File.Create(path);
        encoder.Save(stream);
    }

    private static string GetProductVersion()
    {
        Assembly assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
               ?? assembly.GetName().Version?.ToString()
               ?? "unknown";
    }
}
