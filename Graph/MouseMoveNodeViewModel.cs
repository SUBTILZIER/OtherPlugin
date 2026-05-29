namespace AutomationStudioWpf.Graph;

public sealed class MouseMoveNodeViewModel : NodeBaseViewModel
{
    private double _positionX = 960;
    private double _positionY = 540;

    public MouseMoveNodeViewModel(string id) : base(id, "鼠标移动")
    {
        AddInput("exec_in", "执行输入", PinKind.Execution);
        AddInput("position", "目标坐标", PinKind.Vector2D);
        AddOutput("exec_out", "执行输出", PinKind.Execution);
        AddOutput("position", "目标坐标", PinKind.Vector2D);
        AddOutput("result", "结果", PinKind.Boolean);
        RefreshDescription();
    }

    public override NodeKind NodeKind => NodeKind.MouseMove;
    public override string NodeTypeKey => "mouse_move";

    public double PositionX
    {
        get => _positionX;
        set
        {
            if (SetProperty(ref _positionX, value))
                RefreshDescription();
        }
    }

    public double PositionY
    {
        get => _positionY;
        set
        {
            if (SetProperty(ref _positionY, value))
                RefreshDescription();
        }
    }

    public override void RefreshDescription()
    {
        string posLabel = InputPins.FirstOrDefault(p => p.Name == "position")?.HasConnection == true
            ? "前置输入"
            : $"({PositionX:0}, {PositionY:0})";
        Description = posLabel;
    }
}
