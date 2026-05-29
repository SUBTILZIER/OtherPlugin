using System.IO;

namespace AutomationStudioWpf.Graph;

public sealed class StartProgramNodeViewModel : NodeBaseViewModel
{
    private string _programPath = string.Empty;
    private int _waitTimeoutMs = 60000;
    private ProgramStartFailureAction _failureAction = ProgramStartFailureAction.None;
    private int _retryCount = 3;

    public StartProgramNodeViewModel(string id) : base(id, "启动程序")
    {
        AddInput("exec_in", string.Empty, PinKind.Execution);
        AddOutput("exec_out", string.Empty, PinKind.Execution);
        AddOutput("process_name", "程序进程名", PinKind.String);
        AddOutput("result", "结果", PinKind.Boolean);
        RefreshDescription();
    }

    public override NodeKind NodeKind => NodeKind.StartProgram;
    public override string NodeTypeKey => "start_program";

    public string ProgramPath
    {
        get => _programPath;
        set { if (SetProperty(ref _programPath, value)) RefreshDescription(); }
    }

    public int WaitTimeoutMs
    {
        get => _waitTimeoutMs;
        set
        {
            int clamped = Math.Max(0, value);
            if (SetProperty(ref _waitTimeoutMs, clamped)) RefreshDescription();
        }
    }

    public ProgramStartFailureAction FailureAction
    {
        get => _failureAction;
        set { if (SetProperty(ref _failureAction, value)) RefreshDescription(); }
    }

    public int RetryCount
    {
        get => _retryCount;
        set
        {
            int clamped = Math.Max(0, value);
            if (SetProperty(ref _retryCount, clamped)) RefreshDescription();
        }
    }

    public override void RefreshDescription()
    {
        string path = string.IsNullOrWhiteSpace(_programPath) ? "未设置" : Path.GetFileName(_programPath);
        Description = path;
    }
}
