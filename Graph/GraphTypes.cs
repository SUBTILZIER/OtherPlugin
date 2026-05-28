namespace AutomationStudioWpf.Graph;

public enum NodeKind
{
    Start,
    FindImage,
    MouseLeftClick,
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
