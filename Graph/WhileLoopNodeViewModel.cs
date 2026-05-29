namespace AutomationStudioWpf.Graph;

public sealed class WhileLoopNodeViewModel : NodeBaseViewModel
{
    private bool _conditionValue;
    private WhileLoopMode _loopMode = WhileLoopMode.Finite;
    private int _maxIterations = 10000;

    public WhileLoopNodeViewModel(string id) : base(id, "While循环")
    {
        AddInput("exec_in", string.Empty, PinKind.Execution);
        AddInput("condition", "结束条件", PinKind.Boolean);
        AddOutput("exec_loop_body", "循环体", PinKind.Execution);
        AddOutput("exec_completed", "完成", PinKind.Execution);
        RefreshDescription();
    }

    public override NodeKind NodeKind => NodeKind.WhileLoop;
    public override string NodeTypeKey => "while_loop";

    public bool ConditionValue
    {
        get => _conditionValue;
        set
        {
            if (SetProperty(ref _conditionValue, value))
                RefreshDescription();
        }
    }

    public WhileLoopMode LoopMode
    {
        get => _loopMode;
        set
        {
            if (SetProperty(ref _loopMode, value))
                RefreshDescription();
        }
    }

    public int MaxIterations
    {
        get => _maxIterations;
        set
        {
            int clamped = Math.Max(1, value);
            if (SetProperty(ref _maxIterations, clamped))
                RefreshDescription();
        }
    }

    public override void RefreshDescription()
    {
        string condLabel = InputPins.FirstOrDefault(p => p.Name == "condition")?.HasConnection == true
            ? "前置输入"
            : (_conditionValue ? "真" : "假");
        string modeLabel = _loopMode == WhileLoopMode.Infinite ? "∞" : $"≤{_maxIterations}";
        Description = $"{condLabel} · {modeLabel}";
    }
}
