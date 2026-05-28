namespace AutomationStudioWpf.Graph;

/// <summary>
/// Default graph entry node.
/// Every execution chain should begin from this node, similar to the event entry node in UE blueprints.
/// </summary>
public sealed class StartNodeViewModel : NodeBaseViewModel
{
    public StartNodeViewModel(string id) : base(id, "开始")
    {
        AddOutput("exec_out", "执行输出", PinKind.Execution);
        RefreshDescription();
    }

    public override NodeKind NodeKind => NodeKind.Start;

    public override string NodeTypeKey => "start";

    public override bool CanDelete => false;

    public override double Width => 180;

    public override void RefreshDescription()
    {
        Description = "图谱执行入口";
    }
}
