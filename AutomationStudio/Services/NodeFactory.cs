using AutomationStudioWpf.Graph;

namespace AutomationStudioWpf.Services;

public sealed class NodeFactory
{
    private int _nodeCounter;

    public void ResetCounter(int startValue = 0)
    {
        _nodeCounter = startValue;
    }

    public string CreateNodeId()
    {
        return $"node_{++_nodeCounter:000}";
    }

    public NodeBaseViewModel CreateNode(NodeKind kind, double x, double y)
    {
        NodeBaseViewModel node = kind switch
        {
            NodeKind.FindImage => CreateFindImageNode(),
            NodeKind.MouseClick => CreateMouseClickNode(),
            NodeKind.MouseMove => CreateMouseMoveNode(),
            NodeKind.Keyboard => CreateKeyboardNode(),
            NodeKind.ScrollWheel => CreateScrollWheelNode(),
            NodeKind.StartProgram => CreateStartProgramNode(),
            NodeKind.SelectWindow => CreateSelectWindowNode(),
            NodeKind.PrintLog => CreatePrintLogNode(),
            NodeKind.Delay => CreateDelayNode(),
            NodeKind.If => CreateIfNode(),
            NodeKind.ForLoop => CreateForLoopNode(),
            NodeKind.WhileLoop => CreateWhileLoopNode(),
            NodeKind.MouseDoubleClick => CreateCommonNode(NodeKind.MouseDoubleClick, "mouse_double_click", "鼠标双击"),
            NodeKind.GetMousePosition => CreateCommonNode(NodeKind.GetMousePosition, "get_mouse_position", "获取鼠标位置"),
            NodeKind.KeyChord => CreateCommonNode(NodeKind.KeyChord, "key_chord", "组合键"),
            NodeKind.WaitImage => CreateCommonNode(NodeKind.WaitImage, "wait_image", "等待图片"),
            NodeKind.WaitImageDisappear => CreateCommonNode(NodeKind.WaitImageDisappear, "wait_image_disappear", "图片消失"),
            NodeKind.Compare => CreateCommonNode(NodeKind.Compare, "compare", "比较"),
            NodeKind.BooleanAnd => CreateCommonNode(NodeKind.BooleanAnd, "boolean_and", "布尔与"),
            NodeKind.BooleanOr => CreateCommonNode(NodeKind.BooleanOr, "boolean_or", "布尔或"),
            NodeKind.BooleanNot => CreateCommonNode(NodeKind.BooleanNot, "boolean_not", "布尔非"),
            NodeKind.StringConcat => CreateCommonNode(NodeKind.StringConcat, "string_concat", "字符串拼接"),
            NodeKind.WaitWindow => CreateCommonNode(NodeKind.WaitWindow, "wait_window", "等待窗口"),
            NodeKind.CloseWindow => CreateCommonNode(NodeKind.CloseWindow, "close_window", "关闭窗口"),
            NodeKind.WindowExists => CreateCommonNode(NodeKind.WindowExists, "window_exists", "窗口是否存在"),
            NodeKind.GetForegroundWindow => CreateCommonNode(NodeKind.GetForegroundWindow, "get_foreground_window", "获取前台窗口"),
            NodeKind.SaveScreenshot => CreateCommonNode(NodeKind.SaveScreenshot, "save_screenshot", "截图"),
            NodeKind.ShowMessage => CreateCommonNode(NodeKind.ShowMessage, "show_message", "弹窗提示"),
            NodeKind.FunctionEntry => new FunctionEntryNodeViewModel(CreateNodeId()) { Title = "函数开始" },
            NodeKind.FunctionReturn => new FunctionReturnNodeViewModel(CreateNodeId()) { Title = "函数返回" },
            NodeKind.MacroEntry => new MacroEntryNodeViewModel(CreateNodeId()) { Title = "宏开始" },
            NodeKind.MacroOutput => new MacroOutputNodeViewModel(CreateNodeId()) { Title = "宏输出" },
            NodeKind.CustomEvent => new CustomEventNodeViewModel(CreateNodeId()) { Title = "自定义事件" },
            _ => throw new InvalidOperationException($"不支持从菜单创建节点：{kind}"),
        };

        node.X = x;
        node.Y = y;
        return node;
    }

    public StartNodeViewModel CreateStartNode(double x = 80, double y = 210) =>
        new(CreateNodeId()) { Title = "事件开始运行", X = x, Y = y };

    public FindImageNodeViewModel CreateFindImageNode(double offsetX = 0, double offsetY = 0) =>
        new(CreateNodeId()) { Title = "找图节点", X = 260 + offsetX, Y = 180 + offsetY };

    public MouseClickNodeViewModel CreateMouseClickNode(double offsetX = 0, double offsetY = 0) =>
        new(CreateNodeId()) { Title = "鼠标点击节点", X = 320 + offsetX, Y = 220 + offsetY };

    public MouseMoveNodeViewModel CreateMouseMoveNode(double offsetX = 0, double offsetY = 0) =>
        new(CreateNodeId()) { Title = "鼠标移动节点", X = 420 + offsetX, Y = 300 + offsetY };

    public KeyboardNodeViewModel CreateKeyboardNode(double offsetX = 0, double offsetY = 0) =>
        new(CreateNodeId()) { Title = "键盘节点", X = 340 + offsetX, Y = 240 + offsetY };

    public ScrollWheelNodeViewModel CreateScrollWheelNode(double offsetX = 0, double offsetY = 0) =>
        new(CreateNodeId()) { Title = "鼠标滚轮节点", X = 360 + offsetX, Y = 260 + offsetY };

    public StartProgramNodeViewModel CreateStartProgramNode(double offsetX = 0, double offsetY = 0) =>
        new(CreateNodeId()) { Title = "启动程序", X = 440 + offsetX, Y = 340 + offsetY };

    public PrintLogNodeViewModel CreatePrintLogNode(double offsetX = 0, double offsetY = 0) =>
        new(CreateNodeId()) { Title = "打印log", X = 460 + offsetX, Y = 360 + offsetY };

    public SelectWindowNodeViewModel CreateSelectWindowNode(double offsetX = 0, double offsetY = 0) =>
        new(CreateNodeId()) { Title = "选中窗口", X = 480 + offsetX, Y = 380 + offsetY };

    public DelayNodeViewModel CreateDelayNode(double offsetX = 0, double offsetY = 0) =>
        new(CreateNodeId()) { Title = "延迟节点", X = 360 + offsetX, Y = 260 + offsetY };

    public IfNodeViewModel CreateIfNode(double offsetX = 0, double offsetY = 0) =>
        new(CreateNodeId()) { Title = "分支节点", X = 380 + offsetX, Y = 280 + offsetY };

    public ForLoopNodeViewModel CreateForLoopNode(double offsetX = 0, double offsetY = 0) =>
        new(CreateNodeId()) { Title = "For循环节点", X = 400 + offsetX, Y = 300 + offsetY };

    public WhileLoopNodeViewModel CreateWhileLoopNode(double offsetX = 0, double offsetY = 0) =>
        new(CreateNodeId()) { Title = "While循环", X = 420 + offsetX, Y = 320 + offsetY };

    public RerouteNodeViewModel CreateRerouteNode(PinKind kind, double x, double y) =>
        new(CreateNodeId(), kind) { Title = string.Empty, X = x, Y = y };

    public FunctionCallNodeViewModel CreateFunctionCallNode(
        string functionId,
        string functionName,
        IEnumerable<GraphParameterDefinition> inputs,
        IEnumerable<GraphParameterDefinition> outputs,
        double x,
        double y)
    {
        var node = new FunctionCallNodeViewModel(CreateNodeId(), functionId, functionName) { X = x, Y = y };
        node.ConfigurePins(inputs.Select(p => p.Clone()), outputs.Select(p => p.Clone()));
        return node;
    }

    public MacroCallNodeViewModel CreateMacroCallNode(
        string macroId,
        string macroName,
        IEnumerable<GraphParameterDefinition> inputs,
        IEnumerable<GraphParameterDefinition> outputs,
        IEnumerable<(string Id, string Name)> exits,
        double x,
        double y)
    {
        var node = new MacroCallNodeViewModel(CreateNodeId(), macroId, macroName) { X = x, Y = y };
        node.ConfigurePins(inputs.Select(p => p.Clone()), outputs.Select(p => p.Clone()), exits);
        return node;
    }

    public CustomEventCallNodeViewModel CreateCustomEventCallNode(
        string customEventId,
        string customEventName,
        IEnumerable<GraphParameterDefinition> inputs,
        double x,
        double y)
    {
        var node = new CustomEventCallNodeViewModel(CreateNodeId(), customEventId, customEventName) { X = x, Y = y };
        node.ConfigurePins(inputs.Select(p => p.Clone()));
        return node;
    }

    private CommonNodeViewModel CreateCommonNode(NodeKind kind, string typeKey, string title)
    {
        var node = new CommonNodeViewModel(CreateNodeId(), kind, typeKey, title) { X = 500, Y = 400 };
        if (kind is NodeKind.WaitImage or NodeKind.WaitImageDisappear)
        {
            node.Text2 = ImageSearchSourceMode.RealtimeScreenshot.ToString();
        }
        else if (kind == NodeKind.SaveScreenshot)
        {
            node.Text2 = "Auto";
        }

        return node;
    }
}
