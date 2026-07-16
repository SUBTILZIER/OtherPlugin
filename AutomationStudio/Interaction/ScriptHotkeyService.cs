using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using AutomationStudioWpf.Services;

namespace AutomationStudioWpf.Interaction;

public enum ScriptHotkeyAction
{
    Start,
    Stop,
}

public sealed record ScriptHotkeyTrigger(ContentAssetViewModel Asset, ScriptHotkeyAction Action);

internal sealed class ScriptHotkeyService : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL = 14;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_XBUTTONDOWN = 0x020B;
    private const int WM_MOUSEWHEEL = 0x020A;

    private readonly Window _owner;
    private readonly Action<ScriptHotkeyTrigger> _onTrigger;
    private readonly LowLevelKeyboardProc _keyboardProc;
    private readonly LowLevelMouseProc _mouseProc;
    private readonly Dictionary<ScriptHotkeyMatchKey, ScriptHotkeyBinding> _bindings = [];
    private readonly Dictionary<ScriptHotkeyPressKey, PressState> _pressStates = [];
    private readonly Dictionary<ScriptHotkeyPressKey, int> _pressWindows = [];
    private readonly HashSet<int> _pressedKeys = [];
    private readonly DispatcherTimer _flushTimer;
    private IntPtr _keyboardHook;
    private IntPtr _mouseHook;
    private int _triggerSuspendCount;
    private bool _disposed;

    public ScriptHotkeyService(Window owner, Action<ScriptHotkeyTrigger> onTrigger)
    {
        _owner = owner;
        _onTrigger = onTrigger;
        _keyboardProc = KeyboardHookCallback;
        _mouseProc = MouseHookCallback;
        _flushTimer = new DispatcherTimer(DispatcherPriority.Background, owner.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(80),
        };
        _flushTimer.Tick += (_, _) => FlushReadyPresses();
    }

    public ScriptHotkeyRefreshResult Refresh(
        IEnumerable<ContentAssetViewModel> assets,
        Func<ContentAssetViewModel, bool> isStopHotkeyActive)
    {
        var assetList = assets.ToList();
        var conflicts = Validate(assetList);
        _bindings.Clear();
        _pressStates.Clear();
        _pressWindows.Clear();
        foreach (var asset in assetList.Where(asset => asset.Kind == ContentAssetKind.Script && asset.IsScriptEnabled))
        {
            asset.RunSettings.Normalize();
            AddBinding(asset, ScriptHotkeyAction.Start, asset.RunSettings.StartHotkey);
            if (isStopHotkeyActive(asset))
                AddBinding(asset, ScriptHotkeyAction.Stop, asset.RunSettings.StopHotkey);
        }

        if (_bindings.Count == 0)
        {
            UninstallHooks();
            _flushTimer.Stop();
            return new ScriptHotkeyRefreshResult(conflicts, HookInstallResult.None);
        }

        bool keyboardRequired = _bindings.Keys.Any(key => key.InputKind == ScriptHotkeyInputKind.Keyboard);
        bool mouseRequired = _bindings.Keys.Any(key => key.InputKind == ScriptHotkeyInputKind.Mouse);
        var hooks = InstallHooks(keyboardRequired, mouseRequired);
        if (hooks.Keyboard.Installed || hooks.Mouse.Installed)
        {
            if (!_flushTimer.IsEnabled)
                _flushTimer.Start();
        }
        else
        {
            _flushTimer.Stop();
        }

        return new ScriptHotkeyRefreshResult(conflicts, hooks);
    }

    public IReadOnlyList<string> Validate(IEnumerable<ContentAssetViewModel> assets, ContentAssetViewModel? editingAsset = null, ScriptRunSettings? editingSettings = null)
    {
        var errors = new List<string>();
        var seen = new Dictionary<ScriptHotkeyMatchKey, string>();
        foreach (var asset in assets.Where(asset => asset.Kind == ContentAssetKind.Script && asset.IsScriptEnabled))
        {
            var settings = ReferenceEquals(asset, editingAsset) && editingSettings is not null
                ? editingSettings
                : asset.RunSettings;
            settings.Normalize();
            Check(asset.Name, "启动", settings.StartHotkey);
            Check(asset.Name, "终止", settings.StopHotkey);
        }

        return errors;

        void Check(string assetName, string actionName, ScriptHotkeySettings hotkey)
        {
            if (!hotkey.IsConfigured)
                return;

            var key = ToMatchKey(hotkey);
            string label = $"{assetName}/{actionName}";
            if (seen.TryGetValue(key, out var existing))
                errors.Add($"热键冲突：{label} 与 {existing} 都使用 {hotkey}。");
            else
                seen[key] = label;
        }
    }

    public static bool SameHotkey(ScriptHotkeySettings left, ScriptHotkeySettings right)
    {
        if (!left.IsConfigured || !right.IsConfigured)
            return false;

        return ToMatchKey(left).Equals(ToMatchKey(right));
    }

    public IDisposable SuspendTriggers()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _triggerSuspendCount++;
        if (_triggerSuspendCount == 1)
        {
            _pressStates.Clear();
            _pressedKeys.Clear();
        }

        return new TriggerSuspension(this);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _flushTimer.Stop();
        _bindings.Clear();
        UninstallHooks();
    }

    internal void EmergencyStopWithoutUi()
    {
        _disposed = true;
        _bindings.Clear();
        _pressStates.Clear();
        _pressWindows.Clear();
        _pressedKeys.Clear();
        UninstallHooks();
    }

    private void AddBinding(ContentAssetViewModel asset, ScriptHotkeyAction action, ScriptHotkeySettings hotkey)
    {
        if (!hotkey.IsConfigured)
            return;

        var matchKey = ToMatchKey(hotkey);
        if (!_bindings.TryAdd(matchKey, new ScriptHotkeyBinding(asset, action, Math.Max(1, hotkey.PressCount), hotkey.TriggerWindowMs)))
            return;

        var pressKey = new ScriptHotkeyPressKey(hotkey.InputKind, hotkey.Key);
        int window = hotkey.TriggerWindowMs > 0 ? hotkey.TriggerWindowMs : 1000;
        if (!_pressWindows.TryGetValue(pressKey, out int existing) || window > existing)
            _pressWindows[pressKey] = window;
    }

    private void HandlePress(ScriptHotkeyPressKey key)
    {
        if (_disposed || _triggerSuspendCount > 0)
            return;

        var now = DateTime.UtcNow;
        var candidates = _bindings
            .Where(pair => pair.Key.InputKind == key.InputKind && pair.Key.Key == key.Key)
            .ToArray();
        if (candidates.Length == 0)
            return;

        int windowMs = candidates.Max(pair => Math.Max(100, pair.Value.TriggerWindowMs));
        var window = TimeSpan.FromMilliseconds(windowMs);

        if (_pressStates.TryGetValue(key, out var state))
        {
            var pendingHigherCounts = candidates
                .Where(pair => pair.Key.PressCount > state.Count)
                .ToArray();
            if (pendingHigherCounts.Length > 0)
            {
                int pendingWindowMs = pendingHigherCounts.Max(pair => Math.Max(100, pair.Value.TriggerWindowMs));
                if (now - state.FirstPressAt >= TimeSpan.FromMilliseconds(pendingWindowMs))
                {
                    TryTriggerPressState(key, state, candidates, now, ignoreBindingWindow: true);
                    state = new PressState(now, 0);
                }
            }
            else if (now - state.FirstPressAt > window)
            {
                state = new PressState(now, 0);
            }
        }
        else
        {
            state = new PressState(now, 0);
        }

        state = state with { Count = state.Count + 1 };
        _pressStates[key] = state;

        bool hasHigherPressCount = candidates.Any(pair => pair.Key.PressCount > state.Count);
        if (hasHigherPressCount)
            return;

        TryTriggerPressState(key, state, candidates, now);
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0 && !_disposed)
            {
                int message = wParam.ToInt32();
                if (message is WM_KEYDOWN or WM_SYSKEYDOWN)
                {
                    int vkCode = Marshal.ReadInt32(lParam);
                    if (_pressedKeys.Add(vkCode) && _triggerSuspendCount == 0)
                        PostToOwner(() => HandlePress(new ScriptHotkeyPressKey(ScriptHotkeyInputKind.Keyboard, KeyInterop.KeyFromVirtualKey(vkCode).ToString())));
                }
                else if (message is WM_KEYUP or WM_SYSKEYUP)
                {
                    int vkCode = Marshal.ReadInt32(lParam);
                    _pressedKeys.Remove(vkCode);
                }
            }
        }
        catch (Exception ex)
        {
            Logging.Logger.Error($"键盘热键 Hook 回调失败：{ex.Message}");
        }

        return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0 && !_disposed)
            {
                string? key = wParam.ToInt32() switch
                {
                    WM_LBUTTONDOWN => "Left",
                    WM_RBUTTONDOWN => "Right",
                    WM_MBUTTONDOWN => "Middle",
                    WM_XBUTTONDOWN => GetXButton(lParam),
                    WM_MOUSEWHEEL => GetWheelDirection(lParam),
                    _ => null,
                };
                if (key is not null && _triggerSuspendCount == 0)
                    PostToOwner(() => HandlePress(new ScriptHotkeyPressKey(ScriptHotkeyInputKind.Mouse, key)));
            }
        }
        catch (Exception ex)
        {
            Logging.Logger.Error($"鼠标热键 Hook 回调失败：{ex.Message}");
        }

        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private void FlushReadyPresses()
    {
        if (_disposed || _triggerSuspendCount > 0 || _pressStates.Count == 0)
            return;

        var now = DateTime.UtcNow;
        var keysToRemove = new List<ScriptHotkeyPressKey>();

        foreach (var (pressKey, state) in _pressStates)
        {
            var candidates = _bindings
                .Where(pair => pair.Key.InputKind == pressKey.InputKind && pair.Key.Key == pressKey.Key)
                .ToArray();
            if (candidates.Length == 0)
            {
                keysToRemove.Add(pressKey);
                continue;
            }

            var higherCountCandidates = candidates
                .Where(pair => pair.Key.PressCount > state.Count)
                .ToArray();
            if (higherCountCandidates.Length > 0)
            {
                int higherWindowMs = higherCountCandidates.Max(pair => Math.Max(100, pair.Value.TriggerWindowMs));
                if (now - state.FirstPressAt < TimeSpan.FromMilliseconds(higherWindowMs))
                    continue;

                TryTriggerPressState(pressKey, state, candidates, now, ignoreBindingWindow: true);
                keysToRemove.Add(pressKey);
                continue;
            }

            if (TryTriggerPressState(pressKey, state, candidates, now))
            {
                keysToRemove.Add(pressKey);
                continue;
            }

            int pressWindowMs = _pressWindows.GetValueOrDefault(pressKey, 1000);
            var pressWindow = TimeSpan.FromMilliseconds(Math.Max(100, pressWindowMs));

            if (now - state.FirstPressAt >= pressWindow)
                keysToRemove.Add(pressKey);
        }

        foreach (var key in keysToRemove)
            _pressStates.Remove(key);
    }

    private bool TryTriggerPressState(
        ScriptHotkeyPressKey key,
        PressState state,
        KeyValuePair<ScriptHotkeyMatchKey, ScriptHotkeyBinding>[] candidates,
        DateTime now,
        bool ignoreBindingWindow = false)
    {
        foreach (var (matchKey, binding) in candidates)
        {
            if (matchKey.PressCount != state.Count)
                continue;

            int bindingWindowMs = Math.Max(100, binding.TriggerWindowMs);
            if (!ignoreBindingWindow && now - state.FirstPressAt > TimeSpan.FromMilliseconds(bindingWindowMs))
                continue;

            _pressStates.Remove(key);
            _onTrigger(new ScriptHotkeyTrigger(binding.Asset, binding.Action));
            return true;
        }

        return false;
    }

    private static string GetXButton(IntPtr lParam)
    {
        var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
        int button = (short)(info.mouseData >> 16);
        return button == 1 ? "XButton1" : "XButton2";
    }

    private static string GetWheelDirection(IntPtr lParam)
    {
        var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
        int delta = (short)(info.mouseData >> 16);
        return delta > 0 ? "WheelForward" : "WheelBackward";
    }

    private HookInstallResult InstallHooks(bool keyboardRequired, bool mouseRequired)
    {
        if (!keyboardRequired)
            UninstallKeyboardHook();
        if (!mouseRequired)
            UninstallMouseHook();

        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule!;
        var moduleHandle = GetModuleHandle(module.ModuleName);
        int keyboardError = 0;
        if (keyboardRequired && _keyboardHook == IntPtr.Zero)
        {
            _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, moduleHandle, 0);
            if (_keyboardHook == IntPtr.Zero)
                keyboardError = Marshal.GetLastWin32Error();
        }

        int mouseError = 0;
        if (mouseRequired && _mouseHook == IntPtr.Zero)
        {
            _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, moduleHandle, 0);
            if (_mouseHook == IntPtr.Zero)
                mouseError = Marshal.GetLastWin32Error();
        }

        return new HookInstallResult(
            new HookEndpointInstallResult(keyboardRequired, _keyboardHook != IntPtr.Zero, keyboardError),
            new HookEndpointInstallResult(mouseRequired, _mouseHook != IntPtr.Zero, mouseError));
    }

    private void UninstallHooks()
    {
        UninstallKeyboardHook();
        UninstallMouseHook();

        _pressedKeys.Clear();
        _pressStates.Clear();
        _pressWindows.Clear();
    }

    private void UninstallKeyboardHook()
    {
        if (_keyboardHook == IntPtr.Zero)
            return;

        if (!UnhookWindowsHookEx(_keyboardHook))
            Logging.Logger.Warn($"键盘热键 Hook 卸载失败，Win32 错误码：{Marshal.GetLastWin32Error()}");
        _keyboardHook = IntPtr.Zero;
        _pressedKeys.Clear();
    }

    private void UninstallMouseHook()
    {
        if (_mouseHook == IntPtr.Zero)
            return;

        if (!UnhookWindowsHookEx(_mouseHook))
            Logging.Logger.Warn($"鼠标热键 Hook 卸载失败，Win32 错误码：{Marshal.GetLastWin32Error()}");
        _mouseHook = IntPtr.Zero;
    }

    private void ResumeTriggers()
    {
        if (_triggerSuspendCount <= 0)
            return;

        _triggerSuspendCount--;
        if (_triggerSuspendCount == 0)
            _pressStates.Clear();
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
                    Logging.Logger.Error($"全局热键 UI 回调失败：{ex.Message}");
                }
            }));
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static ScriptHotkeyMatchKey ToMatchKey(ScriptHotkeySettings hotkey) =>
        new(hotkey.InputKind, hotkey.Key, Math.Max(1, hotkey.PressCount));

    private sealed record ScriptHotkeyBinding(ContentAssetViewModel Asset, ScriptHotkeyAction Action, int PressCount, int TriggerWindowMs);

    private sealed record PressState(DateTime FirstPressAt, int Count);

    private readonly record struct ScriptHotkeyPressKey(ScriptHotkeyInputKind InputKind, string Key);

    private readonly record struct ScriptHotkeyMatchKey(ScriptHotkeyInputKind InputKind, string Key, int PressCount);

    private sealed class TriggerSuspension(ScriptHotkeyService owner) : IDisposable
    {
        private ScriptHotkeyService? _owner = owner;

        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?.ResumeTriggers();
        }
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public int mouseData;
        public int flags;
        public int time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, Delegate lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
