using AutomationStudioWpf.Graph;
using AutomationStudioWpf.GraphCore;
using AutomationStudioWpf.Interaction;

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
        NodeDescriptor descriptor = NodeDescriptorCatalog.Get(kind);
        if (!descriptor.CanCreate)
            throw new InvalidOperationException($"节点不能从菜单直接创建：{kind}");

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
            NodeKind.ToDo => CreateToDoNode(),
            NodeKind.MultiThread => CreateMultiThreadNode(),
            NodeKind.GetMousePosition => CreateCommonNode(NodeKind.GetMousePosition),
            NodeKind.KeyChord => CreateKeyChordNode(),
            NodeKind.WaitImage => CreateCommonNode(NodeKind.WaitImage),
            NodeKind.WaitImageDisappear => CreateCommonNode(NodeKind.WaitImageDisappear),
            NodeKind.Compare => CreateCommonNode(NodeKind.Compare),
            NodeKind.BooleanAnd => CreateCommonNode(NodeKind.BooleanAnd),
            NodeKind.BooleanOr => CreateCommonNode(NodeKind.BooleanOr),
            NodeKind.BooleanNot => CreateCommonNode(NodeKind.BooleanNot),
            NodeKind.StringConcat => CreateCommonNode(NodeKind.StringConcat),
            NodeKind.WaitWindow => CreateCommonNode(NodeKind.WaitWindow),
            NodeKind.CloseWindow => CreateCommonNode(NodeKind.CloseWindow),
            NodeKind.WindowExists => CreateCommonNode(NodeKind.WindowExists),
            NodeKind.GetForegroundWindow => CreateCommonNode(NodeKind.GetForegroundWindow),
            NodeKind.SaveScreenshot => CreateCommonNode(NodeKind.SaveScreenshot),
            NodeKind.ShowMessage => CreateCommonNode(NodeKind.ShowMessage),
            NodeKind.CustomEvent => new CustomEventNodeViewModel(CreateNodeId(), Guid.NewGuid().ToString("N")) { Title = DisplayName(NodeKind.CustomEvent) },
            _ => throw new InvalidOperationException($"不支持从菜单创建节点：{kind}"),
        };

        node.X = x;
        node.Y = y;
        return node;
    }

    public StartNodeViewModel CreateStartNode(double x = 80, double y = 210) =>
        new(CreateNodeId()) { Title = DisplayName(NodeKind.Start), X = x, Y = y };

    public FindImageNodeViewModel CreateFindImageNode(double offsetX = 0, double offsetY = 0) =>
        new(CreateNodeId()) { Title = DisplayName(NodeKind.FindImage), X = 260 + offsetX, Y = 180 + offsetY };

    public MouseClickNodeViewModel CreateMouseClickNode(double offsetX = 0, double offsetY = 0) =>
        new(CreateNodeId()) { Title = DisplayName(NodeKind.MouseClick), X = 320 + offsetX, Y = 220 + offsetY };

    public MouseMoveNodeViewModel CreateMouseMoveNode(double offsetX = 0, double offsetY = 0) =>
        new(CreateNodeId()) { Title = DisplayName(NodeKind.MouseMove), X = 420 + offsetX, Y = 300 + offsetY };

    public KeyboardNodeViewModel CreateKeyboardNode(double offsetX = 0, double offsetY = 0) =>
        new(CreateNodeId()) { Title = DisplayName(NodeKind.Keyboard), X = 340 + offsetX, Y = 240 + offsetY };

    public KeyChordNodeViewModel CreateKeyChordNode(double offsetX = 0, double offsetY = 0) =>
        new(CreateNodeId()) { Title = DisplayName(NodeKind.KeyChord), X = 380 + offsetX, Y = 280 + offsetY };

    public ScrollWheelNodeViewModel CreateScrollWheelNode(double offsetX = 0, double offsetY = 0) =>
        new(CreateNodeId()) { Title = DisplayName(NodeKind.ScrollWheel), X = 360 + offsetX, Y = 260 + offsetY };

    public StartProgramNodeViewModel CreateStartProgramNode(double offsetX = 0, double offsetY = 0) =>
        new(CreateNodeId()) { Title = DisplayName(NodeKind.StartProgram), X = 440 + offsetX, Y = 340 + offsetY };

    public PrintLogNodeViewModel CreatePrintLogNode(double offsetX = 0, double offsetY = 0) =>
        new(CreateNodeId()) { Title = DisplayName(NodeKind.PrintLog), X = 460 + offsetX, Y = 360 + offsetY };

    public SelectWindowNodeViewModel CreateSelectWindowNode(double offsetX = 0, double offsetY = 0) =>
        new(CreateNodeId()) { Title = DisplayName(NodeKind.SelectWindow), X = 480 + offsetX, Y = 380 + offsetY };

    public DelayNodeViewModel CreateDelayNode(double offsetX = 0, double offsetY = 0) =>
        new(CreateNodeId()) { Title = DisplayName(NodeKind.Delay), X = 360 + offsetX, Y = 260 + offsetY };

    public IfNodeViewModel CreateIfNode(double offsetX = 0, double offsetY = 0) =>
        new(CreateNodeId()) { Title = DisplayName(NodeKind.If), X = 380 + offsetX, Y = 280 + offsetY };

    public ForLoopNodeViewModel CreateForLoopNode(double offsetX = 0, double offsetY = 0) =>
        new(CreateNodeId()) { Title = DisplayName(NodeKind.ForLoop), X = 400 + offsetX, Y = 300 + offsetY };

    public WhileLoopNodeViewModel CreateWhileLoopNode(double offsetX = 0, double offsetY = 0) =>
        new(CreateNodeId()) { Title = DisplayName(NodeKind.WhileLoop), X = 420 + offsetX, Y = 320 + offsetY };

    public ToDoNodeViewModel CreateToDoNode(double offsetX = 0, double offsetY = 0) =>
        new(CreateNodeId()) { Title = DisplayName(NodeKind.ToDo), X = 440 + offsetX, Y = 340 + offsetY };

    public MultiThreadNodeViewModel CreateMultiThreadNode(double offsetX = 0, double offsetY = 0) =>
        new(CreateNodeId()) { Title = DisplayName(NodeKind.MultiThread), X = 520 + offsetX, Y = 360 + offsetY };

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

    private CommonNodeViewModel CreateCommonNode(NodeKind kind)
    {
        NodeDescriptor descriptor = NodeDescriptorCatalog.Get(kind);
        var node = new CommonNodeViewModel(CreateNodeId(), kind, descriptor.TypeKey, DisplayName(kind)) { X = 500, Y = 400 };
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

    private static string DisplayName(NodeKind kind) => NodePresentationCatalog.Get(kind).DisplayName;
}
