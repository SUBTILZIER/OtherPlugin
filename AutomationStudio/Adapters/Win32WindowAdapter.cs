using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace AutomationStudioWpf.Adapters;

public sealed class Win32WindowAdapter : IWindowAdapter
{
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

    private static readonly IntPtr HWND_TOP = IntPtr.Zero;
    private const int SW_RESTORE = 9;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_SHOWWINDOW = 0x0040;
}

