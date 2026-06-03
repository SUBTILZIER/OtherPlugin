using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using AutomationStudioWpf.Logging;

namespace AutomationStudioWpf.Adapters;

public sealed class Win32WindowAdapter : IWindowAdapter
{
    public List<string> GetRunningWindowNames()
    {
        return Process.GetProcesses()
            .Where(p => p.MainWindowHandle != IntPtr.Zero && !string.IsNullOrWhiteSpace(p.MainWindowTitle))
            .Select(p => p.ProcessName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public WindowSelectionResult SelectWindowByProcessName(string processName)
    {
        string normalized = NormalizeProcessName(processName);
        if (string.IsNullOrWhiteSpace(normalized))
            return new WindowSelectionResult(false, string.Empty, "进程名为空。");

        var process = Process.GetProcessesByName(normalized)
            .FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero);

        if (process is null)
            return new WindowSelectionResult(false, normalized, $"未找到进程窗口：{normalized}");

        IntPtr hwnd = process.MainWindowHandle;
        ShowWindow(hwnd, SW_RESTORE);
        SetWindowPos(hwnd, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
        bool foreground = SetForegroundWindow(hwnd);
        string message = foreground ? $"已选中窗口：{normalized}" : $"窗口已置前，但前台焦点可能被系统限制：{normalized}";
        return new WindowSelectionResult(foreground, normalized, message);
    }

    public WindowSelectionResult WaitWindowByProcessName(string processName, int timeoutMs, int intervalMs, CancellationToken ct)
    {
        string normalized = NormalizeProcessName(processName);
        if (string.IsNullOrWhiteSpace(normalized))
            return new WindowSelectionResult(false, string.Empty, "进程名为空。");

        timeoutMs = timeoutMs >= 0 ? timeoutMs : 5000;
        intervalMs = Math.Max(50, intervalMs);
        long start = Environment.TickCount64;
        int attempt = 0;
        Logger.Info($"等待窗口开始：{normalized}，超时={(timeoutMs == 0 ? "不超时" : timeoutMs + "ms")}，间隔={intervalMs}ms");

        while (timeoutMs == 0 || Environment.TickCount64 - start <= timeoutMs)
        {
            ct.ThrowIfCancellationRequested();
            attempt++;
            var exists = WindowExists(normalized);
            if (exists.Success)
                return exists with { Message = $"已等待到窗口：{exists.ProcessName}" };

            long elapsedMs = Math.Max(0, Environment.TickCount64 - start);
            Logger.Info($"等待窗口检查 #{attempt}：未找到 {normalized}，已等待={elapsedMs}ms");
            Thread.Sleep(intervalMs);
        }

        return new WindowSelectionResult(false, normalized, $"等待窗口超时：{normalized}");
    }

    public WindowSelectionResult CloseWindowByProcessName(string processName)
    {
        string normalized = NormalizeProcessName(processName);
        if (string.IsNullOrWhiteSpace(normalized))
            return new WindowSelectionResult(false, string.Empty, "进程名为空。");

        var process = Process.GetProcessesByName(normalized)
            .FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero);
        if (process is null)
            return new WindowSelectionResult(false, normalized, $"未找到进程窗口：{normalized}");

        bool ok = PostMessage(process.MainWindowHandle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        return new WindowSelectionResult(ok, normalized, ok ? $"已发送关闭窗口：{normalized}" : $"关闭窗口失败：{normalized}");
    }

    public WindowSelectionResult WindowExists(string processName)
    {
        string normalized = NormalizeProcessName(processName);
        if (string.IsNullOrWhiteSpace(normalized))
            return new WindowSelectionResult(false, string.Empty, "进程名为空。");

        bool exists = Process.GetProcessesByName(normalized).Any(p => p.MainWindowHandle != IntPtr.Zero);
        return new WindowSelectionResult(exists, normalized, exists ? $"窗口存在：{normalized}" : $"窗口不存在：{normalized}");
    }

    public WindowInfoResult GetForegroundWindowInfo()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return new WindowInfoResult(false, string.Empty, string.Empty, "未找到前台窗口。");

        _ = GetWindowThreadProcessId(hwnd, out uint pid);
        string processName = string.Empty;
        try
        {
            processName = Process.GetProcessById((int)pid).ProcessName;
        }
        catch
        {
            processName = string.Empty;
        }

        var sb = new StringBuilder(512);
        GetWindowText(hwnd, sb, sb.Capacity);
        string title = sb.ToString();
        return new WindowInfoResult(true, processName, title, $"前台窗口：{processName} / {title}");
    }

    private static string NormalizeProcessName(string processName)
    {
        string normalized = processName.Trim();
        return normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(normalized)
            : normalized;
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    private static readonly IntPtr HWND_TOP = IntPtr.Zero;
    private const int SW_RESTORE = 9;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint WM_CLOSE = 0x0010;
}
