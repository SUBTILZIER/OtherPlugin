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
}
