namespace AutomationStudioWpf.Graph;

public sealed class ForLoopNodeViewModel : NodeBaseViewModel
{
    private int _loopCount = 5;
    private bool _endConditionValue;

    public ForLoopNodeViewModel(string id) : base(id, "For循环")
    {
        AddInput("exec_in", string.Empty, PinKind.Execution);
        AddInput("end_condition", "结束条件", PinKind.Boolean);
        AddOutput("exec_loop_body", "循环体", PinKind.Execution);
        AddOutput("exec_completed", "完成", PinKind.Execution);
        RefreshDescription();
    }

    public override NodeKind NodeKind => NodeKind.ForLoop;
    public override string NodeTypeKey => "for_loop";

    public int LoopCount
    {
        get => _loopCount;
        set
        {
            int clamped = Math.Max(1, value);
            if (SetProperty(ref _loopCount, clamped))
                RefreshDescription();
        }
    }

    public bool EndConditionValue
    {
        get => _endConditionValue;
        set
        {
            if (SetProperty(ref _endConditionValue, value))
                RefreshDescription();
        }
    }

    public override void RefreshDescription()
    {
        string endLabel = InputPins.FirstOrDefault(p => p.Name == "end_condition")?.HasConnection == true
            ? "前置输入"
            : (_endConditionValue ? "真" : "假");
        Description = $"循环次数 {LoopCount}\n结束 {endLabel}";
    }
}
