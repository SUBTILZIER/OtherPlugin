namespace AutomationStudioWpf.Graph;

public enum NodeKind
{
    Start,
    FindImage,
    MouseClick,
    Delay,
    MouseMove,
    Keyboard,
    ScrollWheel,
    Reroute,
    If,
    ForLoop,
    WhileLoop,
    StartProgram,
    PrintLog,
    SelectWindow,
}

public enum PinDirection
{
    Input,
    Output,
}

public enum PinKind
{
    Execution,
    Boolean,
    Vector2D,
    String,
}

public enum MouseButton
{
    Left,
    Right,
    XButton1,
    XButton2,
}

public enum PressReleaseMode
{
    Press,
    Release,
    Click,
}

public enum ScrollWheelAction
{
    Press,
    Release,
    ScrollForward,
    ScrollBackward,
}

public enum ProgramStartFailureAction
{
    None,
    Retry,
}

public enum WhileLoopMode
{
    Infinite,
    Finite,
}

public enum WindowInputMode
{
    Manual,
    Auto,
}

public enum VirtualKeyCode
{
    A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
    D0, D1, D2, D3, D4, D5, D6, D7, D8, D9,
    F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
    Enter, Escape, Space, Tab, Backspace, CapsLock,
    Shift, Control, Alt,
    LShift, RShift, LControl, RControl, LAlt, RAlt,
    Left, Up, Right, Down,
    Insert, DeleteKey, Home, End, PageUp, PageDown,
    PrintScreen, ScrollLock, Pause,
    NumPad0, NumPad1, NumPad2, NumPad3, NumPad4, NumPad5, NumPad6, NumPad7, NumPad8, NumPad9,
    Add, Subtract, Multiply, Divide, Decimal,
    Oem1, Oem2, Oem3, Oem4, Oem5, Oem6, Oem7, Oem8,
    LWin, RWin, Apps,
}
