using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using AutomationStudioWpf.Logging;

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
    private readonly HashSet<int> _reportedHookErrors = [];

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

        var hookResult = InstallHook();
        if (hookResult.Failed)
        {
            ReportHookFailure(hookResult);
            return;
        }

        ScreenPixelSampler.Begin();
        _lastSample = ScreenPixelSampler.Sample();
        _lastHookX = _lastSample.X;
        _lastHookY = _lastSample.Y;
        _overlay = new MousePickOverlayWindow(_owner);
        _overlay.Update(_lastSample);
        _overlay.Show();

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

    internal void EmergencyStopWithoutUi()
    {
        _disposed = true;
        IsActive = false;
        _leftButtonCaptured = false;
        _isPromptOpen = false;
        _updateQueued = false;
        UninstallHook();
        ScreenPixelSampler.End();
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
                ClipboardHelper.TrySetText(sample.CoordinateText);
                Stop($"已复制坐标并退出鼠标拾取：{sample.CoordinateText}");
                return;
            }

            if (result == MessageBoxResult.No)
            {
                ClipboardHelper.TrySetText(sample.HexText);
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
        {
            var hookResult = InstallHook();
            if (hookResult.Failed)
            {
                Stop("鼠标拾取监听恢复失败，已退出拾取模式。");
                ReportHookFailure(hookResult);
            }
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && IsActive && !_isPromptOpen && !_disposed)
        {
            try
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
                        PostToOwner(() => UpdateSampleAt(_pendingX, _pendingY));
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
                PostToOwner(() => _overlay?.Update(_lastSample));
                return new IntPtr(1);
            }

            if (message == WM_LBUTTONUP && _leftButtonCaptured)
            {
                _leftButtonCaptured = false;
                _lastHookX = hook.pt.X;
                _lastHookY = hook.pt.Y;
                var sample = ScreenPixelSampler.SampleAt(hook.pt.X, hook.pt.Y);
                PostToOwner(() => ShowCopyDialog(sample));
                return new IntPtr(1);
            }

            if (message is WM_RBUTTONDOWN or WM_RBUTTONUP)
            {
                PostToOwner(() => Stop("已退出鼠标拾取。"));
                return new IntPtr(1);
            }
            }
            catch (Exception ex)
            {
                Logger.Error($"鼠标拾取 Hook 回调失败：{ex.Message}");
            }
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private HookEndpointInstallResult InstallHook()
    {
        if (_hook != IntPtr.Zero)
            return new HookEndpointInstallResult(true, true, 0);

        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule;
        var moduleHandle = module is null ? IntPtr.Zero : GetModuleHandle(module.ModuleName);
        _hook = SetWindowsHookEx(WH_MOUSE_LL, _hookProc, moduleHandle, 0);
        int errorCode = _hook == IntPtr.Zero ? Marshal.GetLastWin32Error() : 0;
        return new HookEndpointInstallResult(true, _hook != IntPtr.Zero, errorCode);
    }

    private void PostToOwner(Action action)
    {
        if (_disposed || _owner.Dispatcher.HasShutdownStarted || _owner.Dispatcher.HasShutdownFinished)
            return;

        try
        {
            _owner.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_disposed || _owner.Dispatcher.HasShutdownStarted || _owner.Dispatcher.HasShutdownFinished)
                    return;

                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Logger.Error($"鼠标拾取 UI 回调失败：{ex.Message}");
                    Stop("鼠标拾取已停止。" );
                }
            }));
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void ReportHookFailure(HookEndpointInstallResult result)
    {
        string message = result.FormatFailure("鼠标拾取监听");
        Logger.Error(message);
        _setStatus(message);
        int key = result.ErrorCode == 0 ? -1 : result.ErrorCode;
        if (_reportedHookErrors.Add(key))
            ThemedDialog.Show(_owner, message, "鼠标拾取不可用", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void UninstallHook()
    {
        if (_hook == IntPtr.Zero)
            return;

        if (!UnhookWindowsHookEx(_hook))
            Logger.Warn($"鼠标拾取 Hook 卸载失败，Win32 错误码：{Marshal.GetLastWin32Error()}");
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
