using System.Diagnostics;
using System.IO;

namespace AutomationStudioWpf.Services;

internal static class ApplicationPaths
{
    private const string ProductDirectoryName = "AutomationStudioWpf";
    private static readonly Lazy<string> LocalRootValue = new(ResolveLocalRoot);

    public static string InstallRoot => Path.GetFullPath(AppContext.BaseDirectory);

    public static string RoamingDataRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        ProductDirectoryName);

    public static string LocalDataRoot => LocalRootValue.Value;

    public static string LogDirectory => EnsureDirectory(Path.Combine(LocalDataRoot, "Logs"));

    public static string CrashReportDirectory => EnsureDirectory(Path.Combine(LocalDataRoot, "CrashReports"));

    public static string AutoScreenshotDirectory => EnsureDirectory(
        Path.Combine(LocalDataRoot, "Temp", "Screenshots"));

    public static string BundledPythonRoot => Path.Combine(InstallRoot, "Runtime", "Python");

    public static string BundledPythonExecutable => Path.Combine(BundledPythonRoot, "python.exe");

    private static string ResolveLocalRoot()
    {
        string preferred = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ProductDirectoryName);
        try
        {
            return EnsureDirectory(preferred);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LocalAppData 不可写，改用临时目录：{ex.Message}");
            string fallback = Path.Combine(Path.GetTempPath(), ProductDirectoryName);
            return EnsureDirectory(fallback);
        }
    }

    private static string EnsureDirectory(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}
