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
            NodeKind.BooleanAnd => $"{InputLabel("left", Flag.ToString())} AND {InputLabel("right", Text)}",
            NodeKind.BooleanOr => $"{InputLabel("left", Flag.ToString())} OR {InputLabel("right", Text)}",
            NodeKind.BooleanNot => $"NOT {InputLabel("value", Flag.ToString())}",
            NodeKind.StringConcat => $"{InputLabel("left", Text)} + {InputLabel("right", Text2)}",
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
        AddInput("exec_in", "执行输入", PinKind.Execution);
        AddOutput("exec_out", "执行输出", PinKind.Execution);

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
                AddInput("left", "左值", PinKind.Boolean);
                AddInput("right", "右值", PinKind.Boolean);
                AddOutput("result", "结果", PinKind.Boolean);
                break;
            case NodeKind.BooleanNot:
                AddInput("value", "输入", PinKind.Boolean);
                AddOutput("result", "结果", PinKind.Boolean);
                break;
            case NodeKind.StringConcat:
                AddInput("left", "左文本", PinKind.String);
                AddInput("right", "右文本", PinKind.String);
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

    private string OperatorLabel() => string.IsNullOrWhiteSpace(Text3) ? "Equal" : Text3;

    private static string Truncate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "未设置";

        return value.Length <= 24 ? value : value[..24] + "...";
    }
}
