using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Logging;
using AutomationStudioWpf.Runtime;
using MouseButton = AutomationStudioWpf.Graph.MouseButton;

namespace AutomationStudioWpf.Adapters;

public sealed class Win32MouseAdapter : IMouseAdapter, IExecutionScopedInputAdapter
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, HashSet<MouseButton>> _pressedButtonsByExecution = [];
    private readonly Dictionary<MouseButton, int> _buttonHoldCounts = [];

    public void MoveTo(Point point)
    {
        RuntimeShutdownGate.ThrowIfShutdownStarted();
        if (!SetCursorPos(point.X, point.Y))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetCursorPos failed.");
    }

    public void ExecuteButton(MouseButton button, PressReleaseMode mode)
    {
        if (mode != PressReleaseMode.Release)
            RuntimeShutdownGate.ThrowIfShutdownStarted();

        (uint downFlag, uint upFlag, uint xButtonData) = GetMouseEventFlags(button);

        switch (mode)
        {
            case PressReleaseMode.Click:
                ClickButton(button, downFlag, upFlag, xButtonData);
                break;
            case PressReleaseMode.Press:
                PressButton(RuntimeExecutionInputContext.CurrentExecutionId, button, downFlag, xButtonData);
                break;
            case PressReleaseMode.Release:
                ReleaseButton(RuntimeExecutionInputContext.CurrentExecutionId, button, upFlag, xButtonData);
                break;
        }
    }

    public Point GetPosition()
    {
        if (!TryGetPosition(out Point point))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetCursorPos failed.");
        return point;
    }

    public bool TryGetPosition(out Point point)
    {
        if (GetCursorPos(out POINT nativePoint))
        {
            point = new Point(nativePoint.X, nativePoint.Y);
            return true;
        }
        point = default;
        return false;
    }

    public void ExecuteScroll(ScrollWheelAction action, int speed, int intervalMs, int durationMs, CancellationToken ct)
    {
        if (action != ScrollWheelAction.Release)
            RuntimeShutdownGate.ThrowIfShutdownStarted();

        switch (action)
        {
            case ScrollWheelAction.Press:
                PressButton(RuntimeExecutionInputContext.CurrentExecutionId, MouseButton.Middle, MOUSEEVENTF_MIDDLEDOWN, 0);
                break;
            case ScrollWheelAction.Release:
                ReleaseButton(RuntimeExecutionInputContext.CurrentExecutionId, MouseButton.Middle, MOUSEEVENTF_MIDDLEUP, 0);
                break;
            case ScrollWheelAction.ScrollForward:
            case ScrollWheelAction.ScrollBackward:
            {
                int delta = action == ScrollWheelAction.ScrollForward ? speed : -speed;
                int elapsed = 0;
                int lastLog = 0;
                string direction = action == ScrollWheelAction.ScrollForward ? "前滚" : "后滚";
                Logger.Info($"滚轮开始：{direction}，速度={speed}，间隔={intervalMs}ms，持续={durationMs}ms");

                while (durationMs == 0 || elapsed < durationMs)
                {
                    ct.ThrowIfCancellationRequested();
                    RuntimeShutdownGate.ThrowIfShutdownStarted();
                    SendMouseInput(MOUSEEVENTF_WHEEL, unchecked((uint)delta));
                    CancellationWait.WaitOrThrow(intervalMs, ct);
                    if (durationMs > 0)
                        elapsed += intervalMs;
                    if (elapsed - lastLog >= 500 || (durationMs > 0 && elapsed >= durationMs))
                    {
                        Logger.Info($"滚轮滚动中：{direction}，已滚动 {elapsed}ms / {durationMs}ms");
                        lastLog = elapsed;
                    }
                }
                Logger.Info($"滚轮完成：{direction}，总耗时 {elapsed}ms");
                break;
            }
        }
    }

    void IExecutionScopedInputAdapter.ReleaseExecution(Guid executionId) => ReleaseExecution(executionId);

    void IExecutionScopedInputAdapter.ReleaseAllExecutions() => ReleaseAllExecutions();

    private void ClickButton(MouseButton button, uint downFlag, uint upFlag, uint xButtonData)
    {
        lock (_gate)
        {
            RuntimeShutdownGate.ThrowIfShutdownStarted();
            SendMouseInput(downFlag, xButtonData);
            Thread.Sleep(50);
            SendMouseInput(upFlag, xButtonData);
            if (!RuntimeShutdownGate.IsShutdownStarted && _buttonHoldCounts.GetValueOrDefault(button) > 0)
                SendMouseInput(downFlag, xButtonData);
        }
    }

    private void PressButton(Guid executionId, MouseButton button, uint downFlag, uint xButtonData)
    {
        lock (_gate)
        {
            RuntimeShutdownGate.ThrowIfShutdownStarted();
            if (!_pressedButtonsByExecution.TryGetValue(executionId, out var pressedButtons))
            {
                pressedButtons = [];
                _pressedButtonsByExecution[executionId] = pressedButtons;
            }

            if (!pressedButtons.Add(button))
            {
                SendMouseInput(downFlag, xButtonData);
                return;
            }

            int holdCount = _buttonHoldCounts.GetValueOrDefault(button);
            if (holdCount == 0)
                SendMouseInput(downFlag, xButtonData);
            _buttonHoldCounts[button] = holdCount + 1;
        }
    }

    private void ReleaseButton(Guid executionId, MouseButton button, uint upFlag, uint xButtonData)
    {
        lock (_gate)
        {
            if (_pressedButtonsByExecution.TryGetValue(executionId, out var pressedButtons) && pressedButtons.Remove(button))
            {
                if (pressedButtons.Count == 0)
                    _pressedButtonsByExecution.Remove(executionId);
                DecrementHold(button);
                return;
            }

            if (_buttonHoldCounts.GetValueOrDefault(button) == 0)
                SendMouseInput(upFlag, xButtonData);
        }
    }

    private void ReleaseExecution(Guid executionId)
    {
        lock (_gate)
        {
            if (!_pressedButtonsByExecution.Remove(executionId, out var pressedButtons))
                return;

            foreach (MouseButton button in pressedButtons)
                DecrementHold(button);
        }
    }

    private void ReleaseAllExecutions()
    {
        lock (_gate)
        {
            foreach (MouseButton button in _buttonHoldCounts.Keys.ToList())
            {
                var (_, upFlag, xButtonData) = GetMouseEventFlags(button);
                SendMouseInput(upFlag, xButtonData);
            }

            _pressedButtonsByExecution.Clear();
            _buttonHoldCounts.Clear();
        }
    }

    private void DecrementHold(MouseButton button)
    {
        int holdCount = _buttonHoldCounts.GetValueOrDefault(button);
        if (holdCount <= 1)
        {
            _buttonHoldCounts.Remove(button);
            var (_, upFlag, xButtonData) = GetMouseEventFlags(button);
            SendMouseInput(upFlag, xButtonData);
            return;
        }

        _buttonHoldCounts[button] = holdCount - 1;
    }

    private static (uint downFlag, uint upFlag, uint xButtonData) GetMouseEventFlags(MouseButton button)
    {
        return button switch
        {
            MouseButton.Left => (MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP, 0),
            MouseButton.Right => (MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP, 0),
            MouseButton.Middle => (MOUSEEVENTF_MIDDLEDOWN, MOUSEEVENTF_MIDDLEUP, 0),
            MouseButton.XButton1 => (MOUSEEVENTF_XDOWN, MOUSEEVENTF_XUP, XBUTTON1),
            MouseButton.XButton2 => (MOUSEEVENTF_XDOWN, MOUSEEVENTF_XUP, XBUTTON2),
            _ => (MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP, 0),
        };
    }

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    private static void SendMouseInput(uint flags, uint data)
    {
        var input = new INPUT64
        {
            type = INPUT_MOUSE,
            mi = new MOUSEINPUT
            {
                dx = 0,
                dy = 0,
                mouseData = data,
                dwFlags = flags,
                time = 0,
                dwExtraInfo = IntPtr.Zero,
            },
        };
        if (SendInput(1, new[] { input }, Marshal.SizeOf<INPUT64>()) != 1)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SendInput mouse event failed.");
    }

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
    private const uint INPUT_MOUSE = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct INPUT64
    {
        [FieldOffset(0)]
        public uint type;
        [FieldOffset(8)]
        public MOUSEINPUT mi;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT64[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }
}
