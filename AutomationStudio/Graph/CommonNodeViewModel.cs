using System.IO;

namespace AutomationStudioWpf.Graph;

/// <summary>
/// Generic ViewModel for small automation nodes. Complex nodes should be split
/// into dedicated ViewModel/Inspector/Executor classes when their UI grows.
/// </summary>
public sealed class CommonNodeViewModel : NodeBaseViewModel
{
    private string _text = string.Empty;
    private string _text2 = string.Empty;
    private string _text3 = string.Empty;
    private double _number;
    private double _number2;
    private double _number3;
    private double _number4;
    private bool _flag;
    private int _variadicInputCount = 2;
    private readonly Dictionary<string, string> _variadicInputDefaults = new(StringComparer.Ordinal);

    public CommonNodeViewModel(string id, NodeKind nodeKind, string typeKey, string title)
        : base(id, title)
    {
        NodeKind = nodeKind;
        NodeTypeKey = typeKey;
        ConfigurePins();
        RefreshDescription();
    }

    public override NodeKind NodeKind { get; }

    public override string NodeTypeKey { get; }

    public override bool CanAddVariadicInput => NodeKind is NodeKind.BooleanAnd or NodeKind.BooleanOr or NodeKind.StringConcat;

    public override bool CanRemoveVariadicInput => CanAddVariadicInput && VariadicInputCount > 2;

    public IReadOnlyDictionary<string, string> VariadicInputDefaults => _variadicInputDefaults;

    public int VariadicInputCount
    {
        get => _variadicInputCount;
        set
        {
            int next = Math.Max(2, value);
            if (SetProperty(ref _variadicInputCount, next))
            {
                TrimVariadicDefaults();
                EnsureVariadicDefaults();
                SyncVariadicPins();
                RefreshDescription();
                OnPropertyChanged(nameof(CanRemoveVariadicInput));
                OnPropertyChanged(nameof(CanRemoveDynamicPin));
            }
        }
    }

    public string Text
    {
        get => _text;
        set
        {
            if (SetProperty(ref _text, value))
                RefreshDescription();
        }
    }

    public string Text2
    {
        get => _text2;
        set
        {
            if (SetProperty(ref _text2, value))
            {
                SyncDynamicPins();
                RefreshDescription();
            }
        }
    }

    public string Text3
    {
        get => _text3;
        set
        {
            if (SetProperty(ref _text3, value))
                RefreshDescription();
        }
    }

    public double Number
    {
        get => _number;
        set
        {
            if (SetProperty(ref _number, value))
                RefreshDescription();
        }
    }

    public double Number2
    {
        get => _number2;
        set
        {
            if (SetProperty(ref _number2, value))
                RefreshDescription();
        }
    }

    public double Number3
    {
        get => _number3;
        set
        {
            if (SetProperty(ref _number3, value))
                RefreshDescription();
        }
    }

    public double Number4
    {
        get => _number4;
        set
        {
            if (SetProperty(ref _number4, value))
                RefreshDescription();
        }
    }

    public bool Flag
    {
        get => _flag;
        set
        {
            if (SetProperty(ref _flag, value))
                RefreshDescription();
        }
    }

    public override void RefreshDescription()
    {
        Description = NodeKind switch
        {
            NodeKind.MouseDoubleClick => $"位置 {InputLabel("position", $"({Number:0},{Number2:0})")}",
            NodeKind.GetMousePosition => "输出 position(Vector2D)",
            NodeKind.KeyChord => string.IsNullOrWhiteSpace(Text) ? "未设置组合键" : Text,
            NodeKind.WaitImage => $"源：{ImageSourceLabel()}\n目标：{InputLabel("image_path", ImageLabel())}\n超时 {TimeoutLabel}",
            NodeKind.WaitImageDisappear => $"源：{ImageSourceLabel()}\n目标：{InputLabel("image_path", ImageLabel())}\n超时 {TimeoutLabel}",
            NodeKind.Compare => $"{InputLabel("left", Text)} {OperatorLabel()} {InputLabel("right", Text2)}",
            NodeKind.BooleanAnd => BuildVariadicDescription("AND", Flag.ToString(), Text),
            NodeKind.BooleanOr => BuildVariadicDescription("OR", Flag.ToString(), Text),
            NodeKind.BooleanNot => $"NOT {InputLabel("value", Flag.ToString())}",
            NodeKind.StringConcat => BuildVariadicDescription("+", Text, Text2),
            NodeKind.WaitWindow => $"{InputLabel("process_name", Text)} / 超时 {TimeoutLabel}",
            NodeKind.CloseWindow => InputLabel("process_name", Text),
            NodeKind.WindowExists => InputLabel("process_name", Text),
            NodeKind.GetForegroundWindow => "输出 process_name + window_title",
            NodeKind.SaveScreenshot => ScreenshotLabel(),
            NodeKind.ShowMessage => InputLabel("text", string.IsNullOrWhiteSpace(Text2) ? Truncate(Text) : Text2),
            _ => Truncate(Text),
        };
    }

    public int TimeoutMs => Number >= 0 ? (int)Number : 5000;

    public string TimeoutLabel => TimeoutMs == 0 ? "不超时" : $"{TimeoutMs}ms";

    public int IntervalMs => Number2 > 0 ? (int)Number2 : 200;

    private void ConfigurePins()
    {
        if (NodeTraits.HasExecutionPins(NodeKind))
        {
            AddInput("exec_in", "执行输入", PinKind.Execution);
            AddOutput("exec_out", "执行输出", PinKind.Execution);
        }

        switch (NodeKind)
        {
            case NodeKind.MouseDoubleClick:
                AddInput("position", "点击位置", PinKind.Vector2D);
                AddOutput("result", "结果", PinKind.Boolean);
                break;
            case NodeKind.GetMousePosition:
                AddOutput("position", "当前位置", PinKind.Vector2D);
                AddOutput("result", "结果", PinKind.Boolean);
                break;
            case NodeKind.KeyChord:
                AddOutput("result", "结果", PinKind.Boolean);
                break;
            case NodeKind.WaitImage:
                AddInput("image_path", "查找目标", PinKind.String);
                AddOutput("result", "结果", PinKind.Boolean);
                AddOutput("center", "中心点", PinKind.Vector2D);
                AddOutput("image_path", "查找目标", PinKind.String);
                SyncDynamicPins();
                break;
            case NodeKind.WaitImageDisappear:
                AddInput("image_path", "查找目标", PinKind.String);
                AddOutput("result", "结果", PinKind.Boolean);
                SyncDynamicPins();
                break;
            case NodeKind.Compare:
                AddInput("left", "左值", PinKind.String);
                AddInput("right", "右值", PinKind.String);
                AddOutput("result", "结果", PinKind.Boolean);
                break;
            case NodeKind.BooleanAnd:
            case NodeKind.BooleanOr:
                SyncVariadicPins();
                AddOutput("result", "结果", PinKind.Boolean);
                break;
            case NodeKind.BooleanNot:
                AddInput("value", "输入", PinKind.Boolean);
                AddOutput("result", "结果", PinKind.Boolean);
                break;
            case NodeKind.StringConcat:
                SyncVariadicPins();
                AddOutput("value", "结果", PinKind.String);
                break;
            case NodeKind.WaitWindow:
            case NodeKind.CloseWindow:
            case NodeKind.WindowExists:
                AddInput("process_name", "进程名", PinKind.String);
                AddOutput("process_name", "进程名", PinKind.String);
                AddOutput("result", "结果", PinKind.Boolean);
                break;
            case NodeKind.GetForegroundWindow:
                AddOutput("process_name", "进程名", PinKind.String);
                AddOutput("window_title", "窗口标题", PinKind.String);
                AddOutput("result", "结果", PinKind.Boolean);
                break;
            case NodeKind.SaveScreenshot:
                AddOutput("image_path", "图像路径", PinKind.String);
                SyncDynamicPins();
                break;
            case NodeKind.ShowMessage:
                AddInput("text", "文本", PinKind.String);
                AddOutput("result", "结果", PinKind.Boolean);
                break;
        }
    }

    public override bool AddVariadicInput()
    {
        if (!CanAddVariadicInput)
            return false;

        VariadicInputCount++;
        return true;
    }

    public override bool RemoveLastVariadicInput()
    {
        if (!CanRemoveVariadicInput)
            return false;

        string pinName = VariadicInputName(VariadicInputCount);
        _variadicInputDefaults.Remove(pinName);
        VariadicInputCount--;
        return true;
    }

    private string ImageLabel() => string.IsNullOrWhiteSpace(Text) ? "未设置图片" : Path.GetFileName(Text);

    public void SyncDynamicPins()
    {
        if (NodeKind is not (NodeKind.WaitImage or NodeKind.WaitImageDisappear or NodeKind.SaveScreenshot))
            return;

        if (NodeKind is NodeKind.WaitImage or NodeKind.WaitImageDisappear)
        {
            bool manualSource = Enum.TryParse(Text2, true, out ImageSearchSourceMode mode) &&
                                mode == ImageSearchSourceMode.ManualImage;
            bool exists = InputPins.Any(pin => pin.Name == "source_image_path");
            if (manualSource && !exists)
            {
                InsertInput(1, "source_image_path", "查找源", PinKind.String);
            }
            else if (!manualSource && exists)
            {
                RemoveInput("source_image_path");
            }
        }

        if (NodeKind == NodeKind.SaveScreenshot)
        {
            bool manualPath = string.Equals(Text2, "Manual", StringComparison.OrdinalIgnoreCase);
            bool exists = InputPins.Any(pin => pin.Name == "path");
            if (manualPath && !exists)
            {
                InsertInput(1, "path", "保存路径", PinKind.String);
            }
            else if (!manualPath && exists)
            {
                RemoveInput("path");
            }
        }
    }

    public void SyncVariadicPins()
    {
        if (!CanAddVariadicInput)
            return;

        EnsureVariadicDefaults();
        PinKind kind = NodeKind is NodeKind.BooleanAnd or NodeKind.BooleanOr
            ? PinKind.Boolean
            : PinKind.String;
        for (int i = InputPins.Count - 1; i >= 0; i--)
        {
            if (IsVariadicInputName(InputPins[i].Name))
                InputPins.RemoveAt(i);
        }

        for (int i = 1; i <= VariadicInputCount; i++)
        {
            AddInput(VariadicInputName(i), VariadicInputLabel(i), kind);
        }

        OnPropertyChanged(nameof(Height));
    }

    private string ScreenshotLabel()
    {
        string mode = string.IsNullOrWhiteSpace(Text2) ? "Auto" : Text2;
        if (string.Equals(mode, "Auto", StringComparison.OrdinalIgnoreCase))
            return "自动保存\n输出 image_path";

        return $"手动保存\n{InputLabel("path", string.IsNullOrWhiteSpace(Text) ? "未设置路径" : Path.GetFileName(Text))}";
    }

    private string ImageSourceLabel()
    {
        if (!Enum.TryParse(Text2, true, out ImageSearchSourceMode mode))
            mode = ImageSearchSourceMode.RealtimeScreenshot;

        if (mode == ImageSearchSourceMode.RealtimeScreenshot)
            return "实时截屏";

        return InputLabel("source_image_path",
            string.IsNullOrWhiteSpace(Text3) ? "未设置源" : Path.GetFileName(Text3));
    }

    private string InputLabel(string pinName, string fallback)
    {
        return InputPins.FirstOrDefault(pin => pin.Name == pinName)?.HasConnection == true
            ? "前置输入"
            : fallback;
    }

    private string BuildVariadicDescription(string separator, string firstFallback, string secondFallback)
    {
        var labels = new List<string>();
        for (int i = 1; i <= VariadicInputCount; i++)
        {
            string pinName = VariadicInputName(i);
            string fallback = GetVariadicInputDefault(pinName);
            if (NodeKind == NodeKind.StringConcat && i > 2 && string.IsNullOrWhiteSpace(fallback))
                fallback = "未设置";
            labels.Add(InputLabel(pinName, fallback));
        }

        string joined = string.Join($" {separator} ", labels);
        return string.IsNullOrWhiteSpace(joined) ? "未设置" : joined;
    }

    public string GetVariadicInputDefault(string pinName)
    {
        EnsureVariadicDefaults();
        if (_variadicInputDefaults.TryGetValue(pinName, out string? value))
            return value;

        return GetLegacyVariadicDefault(pinName);
    }

    public void SetVariadicInputDefault(string pinName, string value)
    {
        if (!CanAddVariadicInput || !IsVariadicInputName(pinName))
            return;

        string normalized = NodeKind is NodeKind.BooleanAnd or NodeKind.BooleanOr
            ? (bool.TryParse(value, out bool parsed) && parsed ? "True" : "False")
            : value;

        _variadicInputDefaults[pinName] = normalized;
        MirrorLegacyVariadicDefault(pinName, normalized);
        RefreshDescription();
    }

    public void LoadVariadicInputDefaults(IReadOnlyDictionary<string, string>? defaults)
    {
        _variadicInputDefaults.Clear();
        if (defaults is not null)
        {
            foreach (var (key, value) in defaults)
            {
                if (IsVariadicInputName(key))
                    _variadicInputDefaults[key] = value;
            }
        }

        EnsureVariadicDefaults();
        RefreshDescription();
    }

    private void EnsureVariadicDefaults()
    {
        if (!CanAddVariadicInput)
            return;

        for (int i = 1; i <= VariadicInputCount; i++)
        {
            string pinName = VariadicInputName(i);
            if (!_variadicInputDefaults.ContainsKey(pinName))
                _variadicInputDefaults[pinName] = GetLegacyVariadicDefault(pinName);
        }
    }

    private void TrimVariadicDefaults()
    {
        if (!CanAddVariadicInput)
            return;

        var validNames = Enumerable.Range(1, VariadicInputCount)
            .Select(VariadicInputName)
            .ToHashSet(StringComparer.Ordinal);
        foreach (string key in _variadicInputDefaults.Keys.Where(key => !validNames.Contains(key)).ToList())
            _variadicInputDefaults.Remove(key);
    }

    private string GetLegacyVariadicDefault(string pinName)
    {
        if (NodeKind is NodeKind.BooleanAnd or NodeKind.BooleanOr)
        {
            return pinName switch
            {
                "left" => Flag ? "True" : "False",
                "right" => bool.TryParse(Text, out bool parsed) && parsed ? "True" : "False",
                _ => "False",
            };
        }

        return pinName switch
        {
            "left" => Text,
            "right" => Text2,
            _ => string.Empty,
        };
    }

    private void MirrorLegacyVariadicDefault(string pinName, string value)
    {
        if (NodeKind is NodeKind.BooleanAnd or NodeKind.BooleanOr)
        {
            bool parsed = bool.TryParse(value, out bool boolValue) && boolValue;
            if (pinName == "left")
                Flag = parsed;
            else if (pinName == "right")
                Text = parsed ? "True" : "False";
            return;
        }

        if (pinName == "left")
            Text = value;
        else if (pinName == "right")
            Text2 = value;
    }

    public static string VariadicInputName(int ordinal) => ordinal switch
    {
        1 => "left",
        2 => "right",
        _ => $"value_{ordinal}",
    };

    public string VariadicInputLabel(int ordinal)
    {
        if (NodeKind is NodeKind.BooleanAnd or NodeKind.BooleanOr)
            return $"布尔{ordinal}";

        return $"文本{ordinal}";
    }

    private static bool IsVariadicInputName(string name) =>
        name is "left" or "right" ||
        (name.StartsWith("value_", StringComparison.Ordinal) && int.TryParse(name[6..], out _));

    private string OperatorLabel() => string.IsNullOrWhiteSpace(Text3) ? "Equal" : Text3;

    private static string Truncate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "未设置";

        return value.Length <= 24 ? value : value[..24] + "...";
    }
}
