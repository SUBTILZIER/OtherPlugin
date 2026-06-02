using System.Runtime.InteropServices;
using AutomationStudioWpf.Graph;

namespace AutomationStudioWpf.Adapters;

public sealed class Win32KeyboardAdapter : IKeyboardAdapter
{
    private readonly HashSet<byte> _pressedKeys = [];

    public void ExecuteKey(string key, PressReleaseMode mode)
    {
        byte vkCode = MapKeyToVirtualKeyCode(key);

        switch (mode)
        {
            case PressReleaseMode.Click:
                SendKey(vkCode, false);
                Thread.Sleep(50);
                SendKey(vkCode, true);
                break;
            case PressReleaseMode.Press:
                SendKey(vkCode, false);
                lock (_pressedKeys) { _pressedKeys.Add(vkCode); }
                break;
            case PressReleaseMode.Release:
                SendKey(vkCode, true);
                lock (_pressedKeys) { _pressedKeys.Remove(vkCode); }
                break;
        }
    }

    public void ReleaseAllKeys()
    {
        lock (_pressedKeys)
        {
            foreach (byte vk in _pressedKeys)
                SendKey(vk, true);

            _pressedKeys.Clear();
        }
    }

    private static void SendKey(byte vkCode, bool keyUp)
    {
        uint scanCode = MapVirtualKey(vkCode, 0);
        uint flags = KEYEVENTF_SCANCODE;
        if (keyUp) flags |= KEYEVENTF_KEYUP;
        if (IsExtendedKey(vkCode)) flags |= KEYEVENTF_EXTENDEDKEY;

        var input = new INPUT64
        {
            type = INPUT_KEYBOARD,
            ki = new KEYBDINPUT
            {
                wVk = 0,
                wScan = (ushort)scanCode,
                dwFlags = flags,
                time = 0,
                dwExtraInfo = IntPtr.Zero,
            },
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT64>());
    }

    private static byte MapKeyToVirtualKeyCode(string? key)
    {
        return key?.ToUpperInvariant() switch
        {
            "A" => 0x41, "B" => 0x42, "C" => 0x43, "D" => 0x44, "E" => 0x45,
            "F" => 0x46, "G" => 0x47, "H" => 0x48, "I" => 0x49, "J" => 0x4A,
            "K" => 0x4B, "L" => 0x4C, "M" => 0x4D, "N" => 0x4E, "O" => 0x4F,
            "P" => 0x50, "Q" => 0x51, "R" => 0x52, "S" => 0x53, "T" => 0x54,
            "U" => 0x55, "V" => 0x56, "W" => 0x57, "X" => 0x58, "Y" => 0x59, "Z" => 0x5A,
            "D0" => 0x30, "D1" => 0x31, "D2" => 0x32, "D3" => 0x33, "D4" => 0x34,
            "D5" => 0x35, "D6" => 0x36, "D7" => 0x37, "D8" => 0x38, "D9" => 0x39,
            "F1" => 0x70, "F2" => 0x71, "F3" => 0x72, "F4" => 0x73, "F5" => 0x74, "F6" => 0x75,
            "F7" => 0x76, "F8" => 0x77, "F9" => 0x78, "F10" => 0x79, "F11" => 0x7A, "F12" => 0x7B,
            "ENTER" => 0x0D, "ESCAPE" => 0x1B, "SPACE" => 0x20, "TAB" => 0x09, "BACKSPACE" => 0x08,
            "SHIFT" => 0x10, "CONTROL" => 0x11, "ALT" => 0x12,
            "LSHIFT" => 0xA0, "RSHIFT" => 0xA1, "LCONTROL" => 0xA2, "RCONTROL" => 0xA3, "LALT" => 0xA4, "RALT" => 0xA5,
            "LEFT" => 0x25, "UP" => 0x26, "RIGHT" => 0x27, "DOWN" => 0x28,
            "INSERT" => 0x2D, "DELETEKEY" => 0x2E, "HOME" => 0x24, "END" => 0x23,
            "PAGEUP" => 0x21, "PAGEDOWN" => 0x22,
            "NUMPAD0" => 0x60, "NUMPAD1" => 0x61, "NUMPAD2" => 0x62, "NUMPAD3" => 0x63, "NUMPAD4" => 0x64,
            "NUMPAD5" => 0x65, "NUMPAD6" => 0x66, "NUMPAD7" => 0x67, "NUMPAD8" => 0x68, "NUMPAD9" => 0x69,
            "ADD" => 0x6B, "SUBTRACT" => 0x6D, "MULTIPLY" => 0x6A, "DIVIDE" => 0x6F, "DECIMAL" => 0x6E,
            "LWIN" => 0x5B, "RWIN" => 0x5C, "APPS" => 0x5D,
            _ => 0x41,
        };
    }

    private static bool IsExtendedKey(byte vkCode)
    {
        return vkCode switch
        {
            0x25 or 0x26 or 0x27 or 0x28 => true,
            0x2D or 0x2E or 0x24 or 0x23 or 0x21 or 0x22 => true,
            0x5B or 0x5C or 0x5D => true,
            >= 0x60 and <= 0x6F => true,
            _ => false,
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
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
        public KEYBDINPUT ki;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT64[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_SCANCODE = 0x0008;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
}

