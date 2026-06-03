using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using AutomationStudioWpf.Graph;
using MouseButton = AutomationStudioWpf.Graph.MouseButton;

namespace AutomationStudioWpf.Adapters;

public sealed class Win32MouseAdapter : IMouseAdapter
{
    public void MoveTo(Point point)
    {
        SetCursorPos(point.X, point.Y);
    }

    public void ExecuteButton(MouseButton button, PressReleaseMode mode)
    {
        (uint downFlag, uint upFlag, uint xButtonData) = GetMouseEventFlags(button);

        switch (mode)
        {
            case PressReleaseMode.Click:
                mouse_event(downFlag, 0, 0, xButtonData, UIntPtr.Zero);
                Thread.Sleep(50);
                mouse_event(upFlag, 0, 0, xButtonData, UIntPtr.Zero);
                break;
            case PressReleaseMode.Press:
                mouse_event(downFlag, 0, 0, xButtonData, UIntPtr.Zero);
                break;
            case PressReleaseMode.Release:
                mouse_event(upFlag, 0, 0, xButtonData, UIntPtr.Zero);
                break;
        }
    }

    public void DoubleClick(MouseButton button)
    {
        ExecuteButton(button, PressReleaseMode.Click);
        Thread.Sleep(80);
        ExecuteButton(button, PressReleaseMode.Click);
    }

    public Point GetPosition()
    {
        return GetCursorPos(out POINT point)
            ? new Point(point.X, point.Y)
            : new Point(0, 0);
    }

    public void ExecuteScroll(ScrollWheelAction action, int speed, int intervalMs, int durationMs, CancellationToken ct)
    {
        switch (action)
        {
            case ScrollWheelAction.Press:
                mouse_event(MOUSEEVENTF_MIDDLEDOWN, 0, 0, 0, UIntPtr.Zero);
                break;
            case ScrollWheelAction.Release:
                mouse_event(MOUSEEVENTF_MIDDLEUP, 0, 0, 0, UIntPtr.Zero);
                break;
            case ScrollWheelAction.ScrollForward:
            case ScrollWheelAction.ScrollBackward:
            {
                int delta = action == ScrollWheelAction.ScrollForward ? speed : -speed;
                int elapsed = 0;

                while (durationMs == 0 || elapsed < durationMs)
                {
                    ct.ThrowIfCancellationRequested();
                    mouse_event(MOUSEEVENTF_WHEEL, 0, 0, unchecked((uint)delta), UIntPtr.Zero);
                    Thread.Sleep(intervalMs);
                    if (durationMs > 0)
                        elapsed += intervalMs;
                }
                break;
            }
        }
    }

    private static (uint downFlag, uint upFlag, uint xButtonData) GetMouseEventFlags(MouseButton button)
    {
        return button switch
        {
            MouseButton.Left => (MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP, 0),
            MouseButton.Right => (MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP, 0),
            MouseButton.XButton1 => (MOUSEEVENTF_XDOWN, MOUSEEVENTF_XUP, XBUTTON1),
            MouseButton.XButton2 => (MOUSEEVENTF_XDOWN, MOUSEEVENTF_XUP, XBUTTON2),
            _ => (MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP, 0),
        };
    }

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_XDOWN = 0x0080;
    private const uint MOUSEEVENTF_XUP = 0x0100;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;
    private const uint XBUTTON1 = 0x0001;
    private const uint XBUTTON2 = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }
}
