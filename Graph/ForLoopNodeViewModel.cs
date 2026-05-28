namespace AutomationStudioWpf.Graph;

public sealed class ForLoopNodeViewModel : NodeBaseViewModel
{
    private int _loopCount = 5;

    public ForLoopNodeViewModel(string id) : base(id, "循环")
    {
        AddInput("exec_in", string.Empty, PinKind.Execution);
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
            {
                RefreshDescription();
            }
        }
    }

    public override void RefreshDescription()
    {
        Description = $"× {LoopCount}";
    }
}
