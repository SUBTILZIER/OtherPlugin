namespace AutomationStudioWpf.Graph;

public sealed class DelayNodeViewModel : NodeBaseViewModel
{
    private int _delayMs = 500;

    public DelayNodeViewModel(string id) : base(id, "延迟")
    {
        AddInput("exec_in", "执行输入", PinKind.Execution);
        AddOutput("exec_out", "执行输出", PinKind.Execution);
        RefreshDescription();
    }

    public override NodeKind NodeKind => NodeKind.Delay;
    public override string NodeTypeKey => "delay";

    public int DelayMs
    {
        get => _delayMs;
        set
        {
            int clamped = Math.Max(0, value);
            if (SetProperty(ref _delayMs, clamped))
                RefreshDescription();
        }
    }

    public override void RefreshDescription()
    {
        Description = $"{DelayMs}ms";
    }
}
