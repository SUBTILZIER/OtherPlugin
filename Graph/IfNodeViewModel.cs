namespace AutomationStudioWpf.Graph;

public sealed class IfNodeViewModel : NodeBaseViewModel
{
    private bool _conditionValue;

    public IfNodeViewModel(string id) : base(id, "分支")
    {
        AddInput("exec_in", string.Empty, PinKind.Execution);
        AddInput("condition", "判断条件", PinKind.Boolean);
        AddOutput("exec_true", "True", PinKind.Execution);
        AddOutput("exec_false", "False", PinKind.Execution);
        RefreshDescription();
    }

    public override NodeKind NodeKind => NodeKind.If;
    public override string NodeTypeKey => "if";

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
            : (_conditionValue ? "True" : "False");
        Description = condLabel;
    }
}
