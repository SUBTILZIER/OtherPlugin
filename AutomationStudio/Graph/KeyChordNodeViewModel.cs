namespace AutomationStudioWpf.Graph;

public sealed class KeyChordNodeViewModel : InputNodeBase
{
    private string _chord = string.Empty;

    public KeyChordNodeViewModel(string id) : base(id, "组合键")
    {
        OperationMode = PressReleaseMode.Click;
        RefreshDescription();
    }

    public override NodeKind NodeKind => NodeKind.KeyChord;
    public override string NodeTypeKey => "key_chord";

    public string Chord
    {
        get => _chord;
        set
        {
            if (SetProperty(ref _chord, value ?? string.Empty))
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
            _ => "点击",
        };
        string chordLabel = string.IsNullOrWhiteSpace(Chord) ? "未设置组合键" : Chord;
        Description = $"{chordLabel} / {modeLabel}\n{TriggerDescription()}";
    }
}
