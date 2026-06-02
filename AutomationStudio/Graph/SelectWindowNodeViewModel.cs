namespace AutomationStudioWpf.Graph;

public sealed class SelectWindowNodeViewModel : NodeBaseViewModel
{
    private string _processName = string.Empty;
    private WindowInputMode _inputMode = WindowInputMode.Manual;

    public SelectWindowNodeViewModel(string id) : base(id, "选中窗口")
    {
        AddInput("exec_in", string.Empty, PinKind.Execution);
        AddInput("process_name", "进程名", PinKind.String);
        AddOutput("exec_out", string.Empty, PinKind.Execution);
        AddOutput("process_name", "进程名", PinKind.String);
        AddOutput("result", "结果", PinKind.Boolean);
        RefreshDescription();
    }

    public override NodeKind NodeKind => NodeKind.SelectWindow;
    public override string NodeTypeKey => "select_window";

    public string ProcessName
    {
        get => _processName;
        set
        {
            if (SetProperty(ref _processName, value))
                RefreshDescription();
        }
    }

    public WindowInputMode InputMode
    {
        get => _inputMode;
        set
        {
            if (SetProperty(ref _inputMode, value))
                RefreshDescription();
        }
    }

    public override void RefreshDescription()
    {
        string name = InputPins.FirstOrDefault(p => p.Name == "process_name")?.HasConnection == true
            ? "前置输入"
            : _inputMode == WindowInputMode.Auto
                ? (string.IsNullOrWhiteSpace(_processName) ? "自动获取：未选择" : $"自动获取：{_processName}")
                : (string.IsNullOrWhiteSpace(_processName) ? "未设置" : _processName);
        Description = name;
    }
}
