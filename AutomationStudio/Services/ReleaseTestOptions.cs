using System.IO;

namespace AutomationStudioWpf.Services;

internal sealed record ReleaseTestOptions(
    bool IsSelfTest,
    string? TestRoot,
    string? ReportPath)
{
    public static bool TryParse(
        IReadOnlyList<string> arguments,
        out ReleaseTestOptions options,
        out string error)
    {
        bool selfTest = false;
        string? testRoot = null;
        string? reportPath = null;

        for (int index = 0; index < arguments.Count; index++)
        {
            string argument = arguments[index];
            if (string.Equals(argument, "--release-self-test", StringComparison.OrdinalIgnoreCase))
            {
                selfTest = true;
                continue;
            }

            if (TryReadValue(arguments, ref index, argument, "--release-test-root", out string? root, out error))
            {
                if (!string.IsNullOrEmpty(error))
                {
                    options = new ReleaseTestOptions(selfTest, testRoot, reportPath);
                    return false;
                }
                testRoot = root;
                continue;
            }

            if (TryReadValue(arguments, ref index, argument, "--report", out string? report, out error))
            {
                if (!string.IsNullOrEmpty(error))
                {
                    options = new ReleaseTestOptions(selfTest, testRoot, reportPath);
                    return false;
                }
                reportPath = report;
                continue;
            }
        }

        if (!selfTest && reportPath is not null)
        {
            options = new ReleaseTestOptions(false, testRoot, reportPath);
            error = "--report 只能与 --release-self-test 一起使用。";
            return false;
        }

        if (selfTest && (string.IsNullOrWhiteSpace(testRoot) || string.IsNullOrWhiteSpace(reportPath)))
        {
            options = new ReleaseTestOptions(true, testRoot, reportPath);
            error = "--release-self-test 必须同时提供 --release-test-root 和 --report。";
            return false;
        }

        options = new ReleaseTestOptions(selfTest, testRoot, reportPath);
        error = string.Empty;
        return true;
    }

    private static bool TryReadValue(
        IReadOnlyList<string> arguments,
        ref int index,
        string argument,
        string name,
        out string? value,
        out string error)
    {
        value = null;
        error = string.Empty;
        if (argument.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
        {
            value = argument[(name.Length + 1)..];
            return true;
        }

        if (!string.Equals(argument, name, StringComparison.OrdinalIgnoreCase))
            return false;

        if (index + 1 >= arguments.Count || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            error = $"{name} 缺少参数值。";
            return true;
        }

        value = arguments[++index];
        return true;
    }
}

internal static class ReleaseTestEnvironment
{
    internal const string MarkerFileName = ".automationstudio-release-test";
    internal const string MarkerContent = "AutomationStudio.ReleaseVerify.v1";

    internal static string AllowedRoot => Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "AutomationStudio.ReleaseVerify"));

    internal static string ValidateRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new InvalidOperationException("发布测试路径必须是绝对路径。");

        string root = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string allowed = AllowedRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string prefix = allowed + Path.DirectorySeparatorChar;
        if (!root.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"发布测试路径必须位于：{allowed}");

        string marker = Path.Combine(root, MarkerFileName);
        if (!File.Exists(marker) ||
            !string.Equals(File.ReadAllText(marker).Trim(), MarkerContent, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("发布测试路径缺少有效安全标记，拒绝访问。");
        }

        return root;
    }

    internal static string ValidateReportPath(string reportPath, string testRoot)
    {
        if (string.IsNullOrWhiteSpace(reportPath) || !Path.IsPathFullyQualified(reportPath))
            throw new InvalidOperationException("发布自检报告路径必须是绝对路径。");

        string report = Path.GetFullPath(reportPath);
        string rootPrefix = Path.GetFullPath(testRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!report.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("发布自检报告必须写入发布测试目录。");

        return report;
    }
}
