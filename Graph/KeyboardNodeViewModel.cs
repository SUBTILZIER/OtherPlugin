namespace AutomationStudioWpf.Graph;

public sealed class KeyboardNodeViewModel : InputNodeBase
{
    private string _key = "A";

    public KeyboardNodeViewModel(string id) : base(id, "键盘输入")
    {
        RefreshDescription();
    }

    public override NodeKind NodeKind => NodeKind.Keyboard;
    public override string NodeTypeKey => "keyboard";

    public string Key
    {
        get => _key;
        set
        {
            if (SetProperty(ref _key, value))
                RefreshDescription();
        }
    }

    public override void RefreshDescription()
    {
        string modeLabel = OperationMode == PressReleaseMode.Press ? "按下" : "抬起";
        Description = $"{Key} / {modeLabel}";
    }
}
