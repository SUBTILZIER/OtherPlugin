namespace AutomationStudioWpf.Graph;

public abstract class InputNodeBase : NodeBaseViewModel
{
    private PressReleaseMode _operationMode = PressReleaseMode.Press;
    private int _triggerCount = 1;
    private int _triggerIntervalMs = 1000;

    protected InputNodeBase(string id, string title) : base(id, title)
    {
        AddInput("exec_in", "执行输入", PinKind.Execution);
        AddOutput("exec_out", "执行输出", PinKind.Execution);
        AddOutput("result", "结果", PinKind.Boolean);
    }

    public PressReleaseMode OperationMode
    {
        get => _operationMode;
        set
        {
            if (SetProperty(ref _operationMode, value))
                RefreshDescription();
        }
    }

    public int TriggerCount
    {
        get => _triggerCount;
        set
        {
            int next = Math.Max(0, value);
            if (SetProperty(ref _triggerCount, next))
                RefreshDescription();
        }
    }

    public int TriggerIntervalMs
    {
        get => _triggerIntervalMs;
        set
        {
            int next = Math.Max(1, value);
            if (SetProperty(ref _triggerIntervalMs, next))
                RefreshDescription();
        }
    }

    protected string TriggerDescription()
    {
        string countLabel = TriggerCount == 0 ? "无限次" : $"{TriggerCount} 次";
        return $"{countLabel} / 间隔 {TriggerIntervalMs}ms";
    }
}
