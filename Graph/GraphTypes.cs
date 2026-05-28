namespace AutomationStudioWpf.Graph;

public enum NodeKind
{
    Start,
    FindImage,
    MouseClick,
    Delay,
    MouseMove,
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
}

public enum MouseClickMode
{
    SingleClick,
    Hold,
}

public enum MouseButton
{
    Left,
    Right,
    XButton1,
    XButton2,
}
