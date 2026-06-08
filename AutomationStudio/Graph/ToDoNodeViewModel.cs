namespace AutomationStudioWpf.Graph;

public sealed class ToDoNodeViewModel : NodeBaseViewModel
{
    private string _targetNodeTitle = string.Empty;
    private string _targetNodeNumber = string.Empty;
    private string? _targetNodeId;
    private bool _returnAfterTarget;

    public ToDoNodeViewModel(string id) : base(id, "ToDo")
    {
        AddInput("exec_in", "执行输入", PinKind.Execution);
        AddInput("target_title", "节点名", PinKind.String);
        AddInput("target_number", "编号", PinKind.String);
        AddOutput("exec_out", "执行输出", PinKind.Execution);
        RefreshDescription();
    }

    public override NodeKind NodeKind => NodeKind.ToDo;

    public override string NodeTypeKey => "todo";

    public string TargetNodeTitle
    {
        get => _targetNodeTitle;
        set => SetProperty(ref _targetNodeTitle, value);
    }

    public string TargetNodeNumber
    {
        get => _targetNodeNumber;
        set => SetProperty(ref _targetNodeNumber, value);
    }

    public string? TargetNodeId
    {
        get => _targetNodeId;
        set => SetProperty(ref _targetNodeId, string.IsNullOrWhiteSpace(value) ? null : value);
    }

    public bool ReturnAfterTarget
    {
        get => _returnAfterTarget;
        set => SetProperty(ref _returnAfterTarget, value);
    }

    public override void RefreshDescription()
    {
        string target = string.IsNullOrWhiteSpace(TargetNodeTitle) && string.IsNullOrWhiteSpace(TargetNodeNumber)
            ? "未设置目标"
            : $"{TargetNodeTitle} {TargetNodeNumber}".Trim();
        Description = ReturnAfterTarget
            ? $"跳转到 {target}，完成后返回。"
            : $"跳转到 {target}。";
    }
}
