namespace AutomationStudioWpf.Graph;

public sealed class WhileLoopNodeViewModel : NodeBaseViewModel
{
    private bool _conditionValue;

    public WhileLoopNodeViewModel(string id) : base(id, "While循环")
    {
        AddInput("exec_in", string.Empty, PinKind.Execution);
        AddInput("condition", "条件", PinKind.Boolean);
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

    public override void RefreshDescription()
    {
        string condLabel = InputPins.FirstOrDefault(p => p.Name == "condition")?.HasConnection == true
            ? "前置输入"
            : (_conditionValue ? "真" : "假");
        Description = condLabel;
    }
}
