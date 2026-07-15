using System.Diagnostics;
using System.IO;

namespace AutomationStudioWpf.Services;

internal static class RuntimeFileMaintenance
{
    private static readonly TimeSpan PythonRequestRetention = TimeSpan.FromHours(24);
    private static readonly TimeSpan ScreenshotRetention = TimeSpan.FromDays(7);
    private const long MaxScreenshotBytes = 512L * 1024 * 1024;

    public static void Start()
    {
        _ = Task.Run(Cleanup);
    }

    private static void Cleanup()
    {
        try
        {
            CleanupPythonRequests();
            CleanupAutoScreenshots();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"运行时文件清理失败：{ex.Message}");
        }
    }

    private static void CleanupPythonRequests()
    {
        DateTime cutoffUtc = DateTime.UtcNow - PythonRequestRetention;
        foreach (string path in Directory.EnumerateFiles(
                     Path.GetTempPath(),
                     "automation_studio_*.json",
                     SearchOption.TopDirectoryOnly))
        {
            TryDeleteIfOlderThan(path, cutoffUtc);
        }
    }

    private static void CleanupAutoScreenshots()
    {
        string directory = ApplicationPaths.AutoScreenshotDirectory;
        DateTime cutoffUtc = DateTime.UtcNow - ScreenshotRetention;
        var files = Directory
            .EnumerateFiles(directory, "*.png", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .OrderBy(file => file.LastWriteTimeUtc)
            .ToList();

        foreach (FileInfo file in files.Where(file => file.LastWriteTimeUtc < cutoffUtc))
            TryDelete(file);

        files = files.Where(file => file.Exists).OrderBy(file => file.LastWriteTimeUtc).ToList();
        long totalBytes = files.Sum(SafeLength);
        foreach (FileInfo file in files)
        {
            if (totalBytes <= MaxScreenshotBytes)
                break;

            long length = SafeLength(file);
            if (TryDelete(file))
                totalBytes = Math.Max(0, totalBytes - length);
        }
    }

    private static void TryDeleteIfOlderThan(string path, DateTime cutoffUtc)
    {
        try
        {
            var file = new FileInfo(path);
            if (file.Exists && file.LastWriteTimeUtc < cutoffUtc)
                file.Delete();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"临时文件清理失败：{path}：{ex.Message}");
        }
    }

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

    private static bool TryDelete(FileInfo file)
    {
        try
        {
            file.Delete();
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"自动截图清理失败：{file.FullName}：{ex.Message}");
            return false;
        }
    }
}
