using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace AutomationStudioWpf.Interaction;

internal sealed class MousePickController : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_RBUTTONUP = 0x0205;

    private readonly Window _owner;
    private readonly Action<string> _setStatus;
    private readonly LowLevelMouseProc _hookProc;
    private MousePickOverlayWindow? _overlay;
    private IntPtr _hook;
    private bool _leftButtonCaptured;
    private bool _isPromptOpen;
    private bool _disposed;
    private bool _updateQueued;
    private int _lastHookX = int.MinValue;
    private int _lastHookY = int.MinValue;
    private int _pendingX;
    private int _pendingY;
    private ScreenPickSample _lastSample;

    public MousePickController(Window owner, Action<string> setStatus)
    {
        _owner = owner;
        _setStatus = setStatus;
        _hookProc = HookCallback;
    }

    public bool IsActive { get; private set; }

    public void Toggle()
    {
        if (IsActive)
            Stop("已退出鼠标拾取。");
        else
            Start();
    }

    public void Start()
    {
        if (IsActive || _disposed)
            return;

        ScreenPixelSampler.Begin();
        _lastSample = ScreenPixelSampler.Sample();
        _lastHookX = _lastSample.X;
        _lastHookY = _lastSample.Y;
        _overlay = new MousePickOverlayWindow(_owner);
        _overlay.Update(_lastSample);
        _overlay.Show();

        InstallHook();
        IsActive = true;
        _setStatus("鼠标拾取中：左键复制，右键退出。");
    }

    public void Stop(string? status = null)
    {
        if (!IsActive && _overlay is null && _hook == IntPtr.Zero)
            return;

        IsActive = false;
        _leftButtonCaptured = false;
        _isPromptOpen = false;
        _updateQueued = false;
        UninstallHook();
        CloseOverlay();
        ScreenPixelSampler.End();

        if (!string.IsNullOrWhiteSpace(status))
            _setStatus(status);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Stop();
    }

    private void UpdateSampleAt(int x, int y)
    {
        _updateQueued = false;
        if (!IsActive || _overlay is null || _isPromptOpen)
            return;

        if (x == _lastSample.X && y == _lastSample.Y)
            return;

        _lastSample = ScreenPixelSampler.SampleAt(x, y);
        _overlay.Update(_lastSample);
    }

    private void ShowCopyDialog(ScreenPickSample sample)
    {
        if (!IsActive || _disposed)
            return;

        _isPromptOpen = true;
        UninstallHook();
        _overlay?.Hide();

        try
        {
            MousePickChoiceWindow.ShowAt(_owner, sample, sample.X, sample.Y, result => HandleCopyChoice(sample, result));
        }
        catch
        {
            ResumeAfterCancel();
            throw;
        }
    }

    private void HandleCopyChoice(ScreenPickSample sample, MessageBoxResult result)
    {
        try
        {
            if (result == MessageBoxResult.Yes)
            {
                System.Windows.Clipboard.SetText(sample.CoordinateText);
                Stop($"已复制坐标并退出鼠标拾取：{sample.CoordinateText}");
                return;
            }

            if (result == MessageBoxResult.No)
            {
                System.Windows.Clipboard.SetText(sample.HexText);
                Stop($"已复制颜色并退出鼠标拾取：{sample.HexText}");
                return;
            }

            ResumeAfterCancel();
        }
        catch
        {
            Stop("鼠标拾取已停止。");
            throw;
        }
    }

    private void ResumeAfterCancel()
    {
        if (IsActive && !_disposed)
            _overlay?.Show();

        _isPromptOpen = false;
        if (IsActive && !_disposed)
            InstallHook();
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && IsActive && !_isPromptOpen)
        {
            var message = wParam.ToInt32();
            var hook = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

            if (message == WM_MOUSEMOVE)
            {
                if (hook.pt.X != _lastHookX || hook.pt.Y != _lastHookY)
                {
                    _lastHookX = hook.pt.X;
                    _lastHookY = hook.pt.Y;
                    _pendingX = hook.pt.X;
                    _pendingY = hook.pt.Y;
                    if (!_updateQueued)
                    {
                        _updateQueued = true;
                        _owner.Dispatcher.BeginInvoke(() => UpdateSampleAt(_pendingX, _pendingY));
                    }
                }

                return CallNextHookEx(_hook, nCode, wParam, lParam);
            }

            if (message == WM_LBUTTONDOWN)
            {
                _leftButtonCaptured = true;
                _lastHookX = hook.pt.X;
                _lastHookY = hook.pt.Y;
                _lastSample = ScreenPixelSampler.SampleAt(hook.pt.X, hook.pt.Y);
                _owner.Dispatcher.BeginInvoke(() => _overlay?.Update(_lastSample));
                return new IntPtr(1);
            }

            if (message == WM_LBUTTONUP && _leftButtonCaptured)
            {
                _leftButtonCaptured = false;
                _lastHookX = hook.pt.X;
                _lastHookY = hook.pt.Y;
                var sample = ScreenPixelSampler.SampleAt(hook.pt.X, hook.pt.Y);
                _owner.Dispatcher.BeginInvoke(() => ShowCopyDialog(sample));
                return new IntPtr(1);
            }

            if (message is WM_RBUTTONDOWN or WM_RBUTTONUP)
            {
                _owner.Dispatcher.BeginInvoke(() => Stop("已退出鼠标拾取。"));
                return new IntPtr(1);
            }
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private void InstallHook()
    {
        if (_hook != IntPtr.Zero)
            return;

        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule;
        var moduleHandle = module is null ? IntPtr.Zero : GetModuleHandle(module.ModuleName);
        _hook = SetWindowsHookEx(WH_MOUSE_LL, _hookProc, moduleHandle, 0);
    }

    private void UninstallHook()
    {
        if (_hook == IntPtr.Zero)
            return;

        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private void CloseOverlay()
    {
        if (_overlay is null)
            return;

        _overlay.Close();
        _overlay = null;
    }

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
