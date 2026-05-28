namespace AutomationStudioWpf.Graph;

public sealed class MouseClickNodeViewModel : InputNodeBase
{
    private MouseButton _mouseButton = MouseButton.Left;
    private double _positionX = 960;
    private double _positionY = 540;

    public MouseClickNodeViewModel(string id) : base(id, "鼠标点击")
    {
        AddInput("position", "点击位置", PinKind.Vector2D);
        RefreshDescription();
    }

    public override NodeKind NodeKind => NodeKind.MouseClick;
    public override string NodeTypeKey => "mouse_click";

    public MouseButton MouseButton
    {
        get => _mouseButton;
        set
        {
            if (SetProperty(ref _mouseButton, value))
            {
                RefreshDescription();
            }
        }
    }

    public double PositionX
    {
        get => _positionX;
        set
        {
            if (SetProperty(ref _positionX, value))
            {
                RefreshDescription();
            }
        }
    }

    public double PositionY
    {
        get => _positionY;
        set
        {
            if (SetProperty(ref _positionY, value))
            {
                RefreshDescription();
            }
        }
    }

    public override void RefreshDescription()
    {
        string modeLabel = OperationMode switch
        {
            PressReleaseMode.Press => "按下",
            PressReleaseMode.Release => "抬起",
            PressReleaseMode.Click => "点击",
            _ => "按下",
        };
        string buttonLabel = MouseButton switch
        {
            MouseButton.Left => "左键",
            MouseButton.Right => "右键",
            MouseButton.XButton1 => "侧键1",
            MouseButton.XButton2 => "侧键2",
            _ => "左键",
        };
        string posLabel = InputPins.FirstOrDefault(p => p.Name == "position")?.HasConnection == true
            ? "前置输入"
            : $"({PositionX:0}, {PositionY:0})";
        Description = $"{buttonLabel} · {modeLabel}\n{posLabel}";
    }
}
