namespace AutomationStudioWpf.Graph;

public abstract class InputNodeBase : NodeBaseViewModel
{
    private PressReleaseMode _operationMode = PressReleaseMode.Press;

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
            {
                RefreshDescription();
            }
        }
    }
}
