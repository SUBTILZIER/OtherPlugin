namespace AutomationStudioWpf.Graph;

public sealed class MultiThreadNodeViewModel : NodeBaseViewModel
{
    public const int MinimumThreadOutputCount = 2;
    public const string CompletedPinName = "exec_completed";

    private int _threadOutputCount = MinimumThreadOutputCount;

    public MultiThreadNodeViewModel(string id)
        : base(id, "多线程")
    {
        AddInput("exec_in", "执行输入", PinKind.Execution);
        SyncThreadOutputs();
        RefreshDescription();
    }

    public override NodeKind NodeKind => NodeKind.MultiThread;

    public override string NodeTypeKey => "multi_thread";

    public override bool CanAddDynamicPin => true;

    public override bool CanRemoveDynamicPin => ThreadOutputCount > MinimumThreadOutputCount;

    public override string AddDynamicPinToolTip => "添加线程输出";

    public override string RemoveDynamicPinToolTip => "删除最后一个线程输出";

    public int ThreadOutputCount
    {
        get => _threadOutputCount;
        set
        {
            int next = Math.Max(MinimumThreadOutputCount, value);
            if (SetProperty(ref _threadOutputCount, next))
            {
                SyncThreadOutputs();
                RefreshDescription();
                OnPropertyChanged(nameof(CanRemoveDynamicPin));
                OnPropertyChanged(nameof(CanAddDynamicPin));
            }
        }
    }

    public override void RefreshDescription()
    {
        Description = $"并行线程 {ThreadOutputCount} 个，全部完成后继续。";
    }

    public override bool AddDynamicPin()
    {
        ThreadOutputCount++;
        return true;
    }

    public override string? GetLastDynamicPinName() =>
        CanRemoveDynamicPin ? ThreadOutputPinName(ThreadOutputCount) : null;

    public override bool RemoveLastDynamicPin()
    {
        if (!CanRemoveDynamicPin)
            return false;

        ThreadOutputCount--;
        return true;
    }

    public static string ThreadOutputPinName(int ordinal) => $"exec_thread_{ordinal}";

    public static string ThreadOutputPinLabel(int ordinal) => $"线程{ordinal}";

    private void SyncThreadOutputs()
    {
        for (int i = OutputPins.Count - 1; i >= 0; i--)
        {
            string pinName = OutputPins[i].Name;
            if (pinName.StartsWith("exec_thread_", StringComparison.Ordinal) || pinName == CompletedPinName)
                OutputPins.RemoveAt(i);
        }

        for (int i = 1; i <= ThreadOutputCount; i++)
            AddOutput(ThreadOutputPinName(i), ThreadOutputPinLabel(i), PinKind.Execution);

        AddOutput(CompletedPinName, "全部完成", PinKind.Execution, ExecutionPinRole.Completion);
        OnPropertyChanged(nameof(Height));
    }
}
