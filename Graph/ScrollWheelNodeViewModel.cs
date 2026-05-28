namespace AutomationStudioWpf.Graph;

public sealed class ScrollWheelNodeViewModel : NodeBaseViewModel
{
    private ScrollWheelAction _scrollAction = ScrollWheelAction.ScrollForward;
    private int _scrollSpeed = 120;

    public ScrollWheelNodeViewModel(string id) : base(id, "滚轮")
    {
        AddInput("exec_in", "执行输入", PinKind.Execution);
        AddOutput("exec_out", "执行输出", PinKind.Execution);
        AddOutput("result", "结果", PinKind.Boolean);
        RefreshDescription();
    }

    public override NodeKind NodeKind => NodeKind.ScrollWheel;
    public override string NodeTypeKey => "scroll_wheel";

    public ScrollWheelAction ScrollAction
    {
        get => _scrollAction;
        set
        {
            if (SetProperty(ref _scrollAction, value))
            {
                OnPropertyChanged(nameof(IsScrollSpeedVisible));
                RefreshDescription();
            }
        }
    }

    public int ScrollSpeed
    {
        get => _scrollSpeed;
        set
        {
            int clamped = Math.Max(0, value);
            if (SetProperty(ref _scrollSpeed, clamped))
            {
                RefreshDescription();
            }
        }
    }

    public bool IsScrollSpeedVisible =>
        ScrollAction == ScrollWheelAction.ScrollForward ||
        ScrollAction == ScrollWheelAction.ScrollBackward;

    public override void RefreshDescription()
    {
        string actionLabel = ScrollAction switch
        {
            ScrollWheelAction.Press => "中键按下",
            ScrollWheelAction.Release => "中键抬起",
            ScrollWheelAction.ScrollForward => "向前滚动",
            ScrollWheelAction.ScrollBackward => "向后滚动",
            _ => ScrollAction.ToString(),
        };
        string speedPart = IsScrollSpeedVisible ? $"\n速度：{ScrollSpeed}" : "";
        Description = $"动作：{actionLabel}{speedPart}\n输出：bool";
    }
}
