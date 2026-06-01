namespace AutomationStudioWpf.Graph;

public sealed class PrintLogNodeViewModel : NodeBaseViewModel
{
    private string _message = string.Empty;

    public PrintLogNodeViewModel(string id) : base(id, "打印log")
    {
        AddInput("exec_in", string.Empty, PinKind.Execution);
        AddInput("message", "字符串", PinKind.String);
        AddOutput("exec_out", string.Empty, PinKind.Execution);
        RefreshDescription();
    }

    public override NodeKind NodeKind => NodeKind.PrintLog;
    public override string NodeTypeKey => "print_log";

    public string Message
    {
        get => _message;
        set
        {
            if (SetProperty(ref _message, value))
                RefreshDescription();
        }
    }

    public override void RefreshDescription()
    {
        string preview = InputPins.FirstOrDefault(p => p.Name == "message")?.HasConnection == true
            ? "前置输入"
            : (string.IsNullOrWhiteSpace(_message) ? "空" : _message.Length > 20 ? _message[..20] + "..." : _message);
        Description = preview;
    }
}
