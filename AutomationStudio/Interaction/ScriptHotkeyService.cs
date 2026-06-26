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

    public void Refresh(IEnumerable<ContentAssetViewModel> assets)
    {
        _bindings.Clear();
        _pressStates.Clear();
        _pressWindows.Clear();
        foreach (var asset in assets.Where(asset => asset.Kind == ContentAssetKind.Script))
        {
            asset.RunSettings.Normalize();
            AddBinding(asset, ScriptHotkeyAction.Start, asset.RunSettings.StartHotkey);
            AddBinding(asset, ScriptHotkeyAction.Stop, asset.RunSettings.StopHotkey);
        }

        if (_bindings.Count == 0)
        {
            UninstallHooks();
            _flushTimer.Stop();
        }
        else
        {
            InstallHooks();
            if (!_flushTimer.IsEnabled)
                _flushTimer.Start();
        }
    }

    public IReadOnlyList<string> Validate(IEnumerable<ContentAssetViewModel> assets, ContentAssetViewModel? editingAsset = null, ScriptRunSettings? editingSettings = null)
    {
        var errors = new List<string>();
        var seen = new Dictionary<ScriptHotkeyMatchKey, string>();
        foreach (var asset in assets.Where(asset => asset.Kind == ContentAssetKind.Script))
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

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        UninstallHooks();
    }

    private void AddBinding(ContentAssetViewModel asset, ScriptHotkeyAction action, ScriptHotkeySettings hotkey)
    {
        if (!hotkey.IsConfigured)
            return;

        _bindings[ToMatchKey(hotkey)] = new ScriptHotkeyBinding(asset, action, Math.Max(1, hotkey.PressCount), hotkey.TriggerWindowMs);

        var pressKey = new ScriptHotkeyPressKey(hotkey.InputKind, hotkey.Key);
        int window = hotkey.TriggerWindowMs > 0 ? hotkey.TriggerWindowMs : 1000;
        if (!_pressWindows.TryGetValue(pressKey, out int existing) || window > existing)
            _pressWindows[pressKey] = window;
    }

    private void HandlePress(ScriptHotkeyPressKey key)
    {
        var now = DateTime.UtcNow;
        int windowMs = _pressWindows.GetValueOrDefault(key, 1000);
        var window = TimeSpan.FromMilliseconds(Math.Max(100, windowMs));

        if (!_pressStates.TryGetValue(key, out var state) || now - state.FirstPressAt > window)
            state = new PressState(now, 0);

        _pressStates[key] = state with { FirstPressAt = now, Count = state.Count + 1 };
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int message = wParam.ToInt32();
            if (message is WM_KEYDOWN or WM_SYSKEYDOWN)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                if (_pressedKeys.Add(vkCode))
                    _owner.Dispatcher.BeginInvoke(() => HandlePress(new ScriptHotkeyPressKey(ScriptHotkeyInputKind.Keyboard, KeyInterop.KeyFromVirtualKey(vkCode).ToString())));
            }
            else if (message is WM_KEYUP or WM_SYSKEYUP)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                _pressedKeys.Remove(vkCode);
            }
        }

        return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
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
            if (key is not null)
                _owner.Dispatcher.BeginInvoke(() => HandlePress(new ScriptHotkeyPressKey(ScriptHotkeyInputKind.Mouse, key)));
        }

        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private void FlushReadyPresses()
    {
        if (_pressStates.Count == 0)
            return;

        var now = DateTime.UtcNow;
        var keysToRemove = new List<ScriptHotkeyPressKey>();

        foreach (var (pressKey, state) in _pressStates)
        {
            int pressWindowMs = _pressWindows.GetValueOrDefault(pressKey, 1000);
            var pressWindow = TimeSpan.FromMilliseconds(Math.Max(100, pressWindowMs));

            if (now - state.FirstPressAt < pressWindow)
                continue;

            var matchKey = new ScriptHotkeyMatchKey(pressKey.InputKind, pressKey.Key, state.Count);
            if (_bindings.TryGetValue(matchKey, out var binding))
            {
                int bindingWindowMs = binding.TriggerWindowMs > 0 ? binding.TriggerWindowMs : 1000;
                if (now - state.FirstPressAt >= TimeSpan.FromMilliseconds(bindingWindowMs))
                    _onTrigger(new ScriptHotkeyTrigger(binding.Asset, binding.Action));
            }

            keysToRemove.Add(pressKey);
        }

        foreach (var key in keysToRemove)
            _pressStates.Remove(key);
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

    private void InstallHooks()
    {
        if (_keyboardHook != IntPtr.Zero && _mouseHook != IntPtr.Zero)
            return;

        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule!;
        var moduleHandle = GetModuleHandle(module.ModuleName);
        if (_keyboardHook == IntPtr.Zero)
            _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, moduleHandle, 0);
        if (_mouseHook == IntPtr.Zero)
            _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, moduleHandle, 0);
    }

    private void UninstallHooks()
    {
        if (_keyboardHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }

        if (_mouseHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }

        _pressedKeys.Clear();
        _pressStates.Clear();
        _pressWindows.Clear();
    }

    private static ScriptHotkeyMatchKey ToMatchKey(ScriptHotkeySettings hotkey) =>
        new(hotkey.InputKind, hotkey.Key, Math.Max(1, hotkey.PressCount));

    private sealed record ScriptHotkeyBinding(ContentAssetViewModel Asset, ScriptHotkeyAction Action, int PressCount, int TriggerWindowMs);

    private sealed record PressState(DateTime FirstPressAt, int Count);

    private readonly record struct ScriptHotkeyPressKey(ScriptHotkeyInputKind InputKind, string Key);

    private readonly record struct ScriptHotkeyMatchKey(ScriptHotkeyInputKind InputKind, string Key, int PressCount);

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