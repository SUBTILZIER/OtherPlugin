namespace AutomationStudioWpf.Graph;

/// <summary>
/// Mouse click node.
/// Supports single click and hold.
/// Position can be configured directly or fed from a Vector2D upstream pin.
/// </summary>
public sealed class MouseLeftClickNodeViewModel : NodeBaseViewModel
{
    private MouseClickMode _clickMode = MouseClickMode.SingleClick;
    private MouseButton _mouseButton = MouseButton.Left;
    private double _positionX = 960;
    private double _positionY = 540;

    public MouseLeftClickNodeViewModel(string id) : base(id, "鼠标点击")
    {
        AddInput("exec_in", "执行输入", PinKind.Execution);
        AddInput("position", "点击位置", PinKind.Vector2D);
        AddOutput("exec_out", "执行输出", PinKind.Execution);
        AddOutput("success", "成功", PinKind.Boolean);
        RefreshDescription();
    }

    public override NodeKind NodeKind => NodeKind.MouseClick;

    public override string NodeTypeKey => "mouse_click";

    public MouseClickMode ClickMode
    {
        get => _clickMode;
        set
        {
            if (SetProperty(ref _clickMode, value))
            {
                RefreshDescription();
            }
        }
    }

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
        string modeLabel = ClickMode == MouseClickMode.SingleClick ? "单击" : "长按";
        string buttonLabel = MouseButton switch
        {
            MouseButton.Left => "左键",
            MouseButton.Right => "右键",
            MouseButton.XButton1 => "侧键1",
            MouseButton.XButton2 => "侧键2",
            _ => "左键",
        };
        Description = $"按键：{buttonLabel}\n模式：{modeLabel}\n位置：({PositionX:0}, {PositionY:0})\n输出：bool";
    }
}
