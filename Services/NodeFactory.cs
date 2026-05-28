using AutomationStudioWpf.Graph;

namespace AutomationStudioWpf.Services;

/// <summary>
/// 节点工厂 - 负责创建各种类型的节点
/// </summary>
public sealed class NodeFactory
{
    private int _nodeCounter = 0;

    public void ResetCounter(int startValue = 0)
    {
        _nodeCounter = startValue;
    }

    public string CreateNodeId()
    {
        return $"node_{++_nodeCounter:000}";
    }

    public StartNodeViewModel CreateStartNode(double x = 80, double y = 210)
    {
        return new StartNodeViewModel(CreateNodeId())
        {
            Title = "事件开始运行",
            X = x,
            Y = y,
        };
    }

    public FindImageNodeViewModel CreateFindImageNode(double offsetX = 0, double offsetY = 0)
    {
        return new FindImageNodeViewModel(CreateNodeId())
        {
            Title = "找图节点",
            X = 260 + offsetX,
            Y = 180 + offsetY,
        };
    }

    public MouseClickNodeViewModel CreateMouseClickNode(double offsetX = 0, double offsetY = 0)
    {
        return new MouseClickNodeViewModel(CreateNodeId())
        {
            Title = "鼠标点击节点",
            X = 320 + offsetX,
            Y = 220 + offsetY,
        };
    }

    public MouseMoveNodeViewModel CreateMouseMoveNode(double offsetX = 0, double offsetY = 0)
    {
        return new MouseMoveNodeViewModel(CreateNodeId())
        {
            Title = "鼠标移动节点",
            X = 420 + offsetX,
            Y = 300 + offsetY,
        };
    }

    public KeyboardNodeViewModel CreateKeyboardNode(double offsetX = 0, double offsetY = 0)
    {
        return new KeyboardNodeViewModel(CreateNodeId())
        {
            Title = "键盘节点",
            X = 340 + offsetX,
            Y = 240 + offsetY,
        };
    }

    public ScrollWheelNodeViewModel CreateScrollWheelNode(double offsetX = 0, double offsetY = 0)
    {
        return new ScrollWheelNodeViewModel(CreateNodeId())
        {
            Title = "滚轮节点",
            X = 360 + offsetX,
            Y = 260 + offsetY,
        };
    }

    public DelayNodeViewModel CreateDelayNode(double offsetX = 0, double offsetY = 0)
    {
        return new DelayNodeViewModel(CreateNodeId())
        {
            Title = "延迟节点",
            X = 360 + offsetX,
            Y = 260 + offsetY,
        };
    }

    public IfNodeViewModel CreateIfNode(double offsetX = 0, double offsetY = 0)
    {
        return new IfNodeViewModel(CreateNodeId())
        {
            Title = "分支节点",
            X = 380 + offsetX,
            Y = 280 + offsetY,
        };
    }

    public ForLoopNodeViewModel CreateForLoopNode(double offsetX = 0, double offsetY = 0)
    {
        return new ForLoopNodeViewModel(CreateNodeId())
        {
            Title = "循环节点",
            X = 400 + offsetX,
            Y = 300 + offsetY,
        };
    }

    public WhileLoopNodeViewModel CreateWhileLoopNode(double offsetX = 0, double offsetY = 0)
    {
        return new WhileLoopNodeViewModel(CreateNodeId())
        {
            Title = "While循环",
            X = 420 + offsetX,
            Y = 320 + offsetY,
        };
    }

    public RerouteNodeViewModel CreateRerouteNode(PinKind kind, double x, double y)
    {
        return new RerouteNodeViewModel(CreateNodeId(), kind)
        {
            Title = string.Empty,
            X = x,
            Y = y,
        };
    }
}
