namespace AutomationStudioWpf.Graph;

/// <summary>
/// Blueprint-style node for left mouse button actions.
/// Supports single click and hold, and accepts a Vector2D position input.
/// </summary>
public sealed class MouseLeftClickNodeViewModel : NodeBaseViewModel
{
    private MouseClickMode _clickMode = MouseClickMode.SingleClick;
    private double _positionX = 960;
    private double _positionY = 540;
    private int _holdDurationMs = 600;

    public MouseLeftClickNodeViewModel(string id) : base(id, "鼠标左键")
    {
        AddInput("exec_in", "执行输入", PinKind.Execution);
        AddInput("position", "点击位置", PinKind.Vector2D);
        AddOutput("exec_out", "执行输出", PinKind.Execution);
        AddOutput("success", "成功", PinKind.Boolean);
        RefreshDescription();
    }

    public override NodeKind NodeKind => NodeKind.MouseLeftClick;

    public override string NodeTypeKey => "mouse_left_click";

    public MouseClickMode ClickMode
    {
        get => _clickMode;
        set
        {
            if (SetProperty(ref _clickMode, value))
            {
                OnPropertyChanged(nameof(IsHoldDurationEnabled));
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

    public int HoldDurationMs
    {
        get => _holdDurationMs;
        set
        {
            int clamped = Math.Max(0, value);
            if (SetProperty(ref _holdDurationMs, clamped))
            {
                RefreshDescription();
            }
        }
    }

    public bool IsHoldDurationEnabled => ClickMode == MouseClickMode.Hold;

    public override void RefreshDescription()
    {
        string modeLabel = ClickMode == MouseClickMode.SingleClick ? "单击" : "长按";
        string durationLabel = ClickMode == MouseClickMode.SingleClick ? "无" : $"{HoldDurationMs}ms";
        Description = $"模式：{modeLabel}\n位置：({PositionX:0}, {PositionY:0})\n时长：{durationLabel}\n输出：bool";
    }
}

