using System.Diagnostics;
using System.IO;

namespace AutomationStudioWpf.Services;

internal static class ApplicationPaths
{
    private const string ProductDirectoryName = "AutomationStudioWpf";
    private static readonly object ConfigurationGate = new();
    private static readonly Lazy<string> LocalRootValue = new(ResolveLocalRoot);
    private static readonly Lazy<string> UserDataRootValue = new(ResolveUserDataRoot);
    private static string? _releaseTestRoot;

    public static string InstallRoot => Path.GetFullPath(AppContext.BaseDirectory);

    public static string RoamingDataRoot => _releaseTestRoot is null
        ? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ProductDirectoryName)
        : Path.Combine(_releaseTestRoot, "UserData");

    public static string UserDataRoot => UserDataRootValue.Value;

    public static string LocalDataRoot => LocalRootValue.Value;

    public static string LogDirectory => EnsureDirectory(Path.Combine(LocalDataRoot, "Logs"));

    public static string CrashReportDirectory => EnsureDirectory(Path.Combine(LocalDataRoot, "CrashReports"));

    public static string AutoScreenshotDirectory => EnsureDirectory(
        Path.Combine(LocalDataRoot, "Temp", "Screenshots"));

    public static string PythonRequestDirectory => EnsureDirectory(
        Path.Combine(LocalDataRoot, "Temp", "PythonRequests"));

    public static string BundledPythonRoot => Path.Combine(InstallRoot, "Runtime", "Python");

    public static string BundledPythonExecutable => Path.Combine(BundledPythonRoot, "python.exe");

    internal static bool IsReleaseTestMode => _releaseTestRoot is not null;

    internal static string? ReleaseTestRoot => _releaseTestRoot;

    internal static void ConfigureReleaseTestRoot(string path)
    {
        string validated = ReleaseTestEnvironment.ValidateRoot(path);
        lock (ConfigurationGate)
        {
            if (LocalRootValue.IsValueCreated || UserDataRootValue.IsValueCreated)
                throw new InvalidOperationException("发布测试路径必须在应用数据目录首次访问前配置。");
            if (_releaseTestRoot is not null &&
                !string.Equals(_releaseTestRoot, validated, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("发布测试路径在同一进程中只能配置一次。");
            }

            _releaseTestRoot = validated;
        }
    }

    private static string ResolveLocalRoot()
    {
        if (_releaseTestRoot is not null)
            return EnsureDirectory(Path.Combine(_releaseTestRoot, "LocalData"));

        var candidates = new List<string>();
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
            candidates.Add(Path.Combine(localAppData, ProductDirectoryName));
        try
        {
            candidates.Add(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ProductDirectoryName));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"无法解析用户目录：{ex.Message}");
        }

        try
        {
            candidates.Add(Path.Combine(Path.GetTempPath(), ProductDirectoryName));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"无法解析临时目录：{ex.Message}");
        }

        foreach (string candidate in candidates
                     .Where(Path.IsPathRooted)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (TryEnsureWritableDirectory(candidate))
                return candidate;
        }

        throw new IOException("Unable to create a writable AutomationStudio local-data directory.");
    }

    private static string ResolveUserDataRoot()
    {
        string roaming = RoamingDataRoot;
        if (TryEnsureWritableDirectory(roaming))
            return roaming;

        var candidates = new List<string>();
        try
        {
            candidates.Add(Path.Combine(LocalDataRoot, "Data"));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"无法解析本地数据目录：{ex.Message}");
        }

        try
        {
            candidates.Add(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ProductDirectoryName));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"无法解析用户数据备用目录：{ex.Message}");
        }

        try
        {
            candidates.Add(Path.Combine(Path.GetTempPath(), ProductDirectoryName, "Data"));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"无法解析临时数据备用目录：{ex.Message}");
        }

        foreach (string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (TryEnsureWritableDirectory(candidate))
                return candidate;
        }

        throw new IOException("Unable to create a writable AutomationStudio user-data directory.");
    }

    private static bool TryEnsureWritableDirectory(string path)
    {
        if (!Path.IsPathRooted(path))
            return false;

        try
        {
            Directory.CreateDirectory(path);
            string probe = Path.Combine(path, $".write-test-{Guid.NewGuid():N}");
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            {
            }
            File.Delete(probe);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"User-data directory is not writable: {path}; {ex.Message}");
            return false;
        }
    }

    private static string EnsureDirectory(string path)
    {
        if (!Path.IsPathRooted(path))
            throw new IOException($"Refusing to write to a relative application path: {path}");

        Directory.CreateDirectory(path);
        return path;
    }
}
