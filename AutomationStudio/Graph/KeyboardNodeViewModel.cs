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
        string modeLabel = OperationMode switch
        {
            PressReleaseMode.Press => "按下",
            PressReleaseMode.Release => "抬起",
            PressReleaseMode.Click => "点击",
            _ => "按下",
        };
        Description = $"{Key} / {modeLabel}";
    }
}
