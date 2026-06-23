using System.Runtime.InteropServices;

namespace AutomationStudioWpf.Interaction;

internal readonly record struct ScreenPickSample(int X, int Y, byte R, byte G, byte B)
{
    public string CoordinateText => $"{X},{Y}";

    public string RgbText => $"RGB({R},{G},{B})";

    public string HexText => $"#{R:X2}{G:X2}{B:X2}";
}

internal static class ScreenPixelSampler
{
    private static IntPtr _screenDc;

    public static void Begin()
    {
        End();
        _screenDc = GetDC(IntPtr.Zero);
    }

    public static void End()
    {
        if (_screenDc == IntPtr.Zero)
            return;

        ReleaseDC(IntPtr.Zero, _screenDc);
        _screenDc = IntPtr.Zero;
    }

    public static (int X, int Y) GetCursorPosition()
    {
        return GetCursorPos(out var point)
            ? (point.X, point.Y)
            : (0, 0);
    }

    public static ScreenPickSample Sample()
    {
        var (x, y) = GetCursorPosition();
        return SampleAt(x, y);
    }

    public static ScreenPickSample SampleAt(int x, int y)
    {
        var hdc = _screenDc != IntPtr.Zero ? _screenDc : GetDC(IntPtr.Zero);
        var ownsDc = _screenDc == IntPtr.Zero;
        if (hdc == IntPtr.Zero)
            return new ScreenPickSample(x, y, 0, 0, 0);

        try
        {
            var colorRef = GetPixel(hdc, x, y);
            if (colorRef == uint.MaxValue)
                return new ScreenPickSample(x, y, 0, 0, 0);

            var r = (byte)(colorRef & 0xFF);
            var g = (byte)((colorRef >> 8) & 0xFF);
            var b = (byte)((colorRef >> 16) & 0xFF);
            return new ScreenPickSample(x, y, r, g, b);
        }
        finally
        {
            if (ownsDc)
                ReleaseDC(IntPtr.Zero, hdc);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern uint GetPixel(IntPtr hdc, int nXPos, int nYPos);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }
}
