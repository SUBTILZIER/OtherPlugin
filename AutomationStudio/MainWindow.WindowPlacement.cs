using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace AutomationStudioWpf;

public partial class MainWindow
{
    private const int WmGetMinMaxInfo = 0x0024;
    private const uint MonitorDefaultToNearest = 2;
    private HwndSource? _windowPlacementSource;

    private void InstallWindowPlacementHook()
    {
        if (_windowPlacementSource is not null)
            return;

        IntPtr handle = new WindowInteropHelper(this).Handle;
        _windowPlacementSource = HwndSource.FromHwnd(handle);
        _windowPlacementSource?.AddHook(WindowPlacementWndProc);
    }

    private void UninstallWindowPlacementHook()
    {
        if (_windowPlacementSource is null)
            return;

        _windowPlacementSource.RemoveHook(WindowPlacementWndProc);
        _windowPlacementSource = null;
    }

    private static IntPtr WindowPlacementWndProc(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message != WmGetMinMaxInfo || lParam == IntPtr.Zero)
            return IntPtr.Zero;

        IntPtr monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
            return IntPtr.Zero;

        var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref monitorInfo))
            return IntPtr.Zero;

        MinMaxInfo minMaxInfo = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        WindowMaximizedBounds bounds = CalculateMaximizedBounds(monitorInfo.Monitor, monitorInfo.Work);
        minMaxInfo.MaxPosition = new PointI(bounds.X, bounds.Y);
        minMaxInfo.MaxSize = new SizeI(bounds.Width, bounds.Height);
        Marshal.StructureToPtr(minMaxInfo, lParam, false);
        handled = true;
        return IntPtr.Zero;
    }

    internal static WindowMaximizedBounds CalculateMaximizedBounds(RectI monitor, RectI work)
    {
        return new WindowMaximizedBounds(
            work.Left - monitor.Left,
            work.Top - monitor.Top,
            work.Right - work.Left,
            work.Bottom - work.Top);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct PointI
    {
        public int X;
        public int Y;

        public PointI(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SizeI
    {
        public int Width;
        public int Height;

        public SizeI(int width, int height)
        {
            Width = width;
            Height = height;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RectI
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public RectI Monitor;
        public RectI Work;
        public int Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public PointI Reserved;
        public SizeI MaxSize;
        public PointI MaxPosition;
        public SizeI MinTrackSize;
        public SizeI MaxTrackSize;
    }

    internal readonly record struct WindowMaximizedBounds(int X, int Y, int Width, int Height);
}
