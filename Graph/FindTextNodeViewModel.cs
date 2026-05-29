namespace AutomationStudioWpf.Graph;

public sealed class FindTextNodeViewModel : NodeBaseViewModel
{
    private string _text = string.Empty;
    private int _similarityThresholdPercent = 80;

    public FindTextNodeViewModel(string id) : base(id, "找字")
    {
        AddInput("exec_in", string.Empty, PinKind.Execution);
        AddInput("text", "文字", PinKind.String);
        AddOutput("exec_out", string.Empty, PinKind.Execution);
        AddOutput("result", "结果", PinKind.Boolean);
        AddOutput("center", "中心点", PinKind.Vector2D);
        RefreshDescription();
    }

    public override NodeKind NodeKind => NodeKind.FindText;
    public override string NodeTypeKey => "find_text";

    public string Text
    {
        get => _text;
        set
        {
            if (SetProperty(ref _text, value))
                RefreshDescription();
        }
    }

    public int SimilarityThresholdPercent
    {
        get => _similarityThresholdPercent;
        set
        {
            int clamped = Math.Clamp(value, 0, 100);
            if (SetProperty(ref _similarityThresholdPercent, clamped))
                RefreshDescription();
        }
    }

    public override void RefreshDescription()
    {
        string preview = InputPins.FirstOrDefault(p => p.Name == "text")?.HasConnection == true
            ? "前置输入"
            : (string.IsNullOrWhiteSpace(_text) ? "未设置" : (_text.Length > 12 ? _text[..12] + "..." : _text));
        Description = $"{preview}\n阈值 {SimilarityThresholdPercent}%";
    }
}
