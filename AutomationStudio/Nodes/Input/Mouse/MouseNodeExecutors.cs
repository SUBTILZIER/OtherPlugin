using System.Drawing;
using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Logging;
using AutomationStudioWpf.Runtime;
using MouseButton = AutomationStudioWpf.Graph.MouseButton;

namespace AutomationStudioWpf.Nodes.Input.Mouse;

public sealed class MouseClickNodeExecutor : INodeExecutor
{
    public NodeKind NodeKind => NodeKind.MouseClick;

    public NodeExecutionResult Execute(NodeExecutionRequest request)
    {
        GraphRuntimeNode node = request.Node;
        bool hasPositionInput = request.Context.TryResolvePointInput(request.Plan, node, "position", out Point point, out bool hasConnection);
        if (hasConnection && !hasPositionInput)
        {
            request.Context.Set(node.Id, "result", false);
            Logger.Warn("鼠标点击：位置输入已连接，但上游没有输出坐标。跳过点击，继续执行。");
            return NodeExecutionResult.Warn($"已跳过节点：{node.Title} 上游坐标缺失");
        }

        if (!hasConnection)
        {
            if (!HasUsablePosition(node.PositionX, node.PositionY))
            {
                request.Context.Set(node.Id, "result", false);
                Logger.Warn("鼠标点击：未设置点击位置，也未连接位置输入。继续执行。");
                return NodeExecutionResult.Warn($"已跳过节点：{node.Title} 缺少点击位置");
            }

            point = new Point((int)Math.Round(node.PositionX), (int)Math.Round(node.PositionY));
        }

        string buttonLabel = node.MouseButton switch
        {
            MouseButton.Left => "左键",
            MouseButton.Right => "右键",
            MouseButton.Middle => "中键",
            MouseButton.XButton1 => "侧键1",
            MouseButton.XButton2 => "侧键2",
            _ => "左键",
        };
        string modeLabel = node.OperationMode switch
        {
            PressReleaseMode.Press => "按下",
            PressReleaseMode.Release => "抬起",
            PressReleaseMode.Click => "点击",
            _ => "点击",
        };

        Logger.Info($"鼠标点击：{buttonLabel} {modeLabel} ({point.X},{point.Y})");
        request.Adapters.Mouse.MoveTo(point);
        request.Adapters.Mouse.ExecuteButton(node.MouseButton, node.OperationMode);
        request.Context.Set(node.Id, "result", true);
        return NodeExecutionResult.Ok($"鼠标{buttonLabel}{modeLabel}：({point.X},{point.Y})");
    }

    private static bool HasUsablePosition(double x, double y)
    {
        if (double.IsNaN(x) || double.IsNaN(y) || double.IsInfinity(x) || double.IsInfinity(y))
            return false;

        return Math.Abs(x) > 0.001 || Math.Abs(y) > 0.001;
    }
}

public sealed class MouseMoveNodeExecutor : INodeExecutor
{
    public NodeKind NodeKind => NodeKind.MouseMove;

    public NodeExecutionResult Execute(NodeExecutionRequest request)
    {
        GraphRuntimeNode node = request.Node;
        bool hasPositionInput = request.Context.TryResolvePointInput(request.Plan, node, "position", out Point point, out bool hasConnection);
        if (hasConnection && !hasPositionInput)
        {
            request.Context.Set(node.Id, "result", false);
            Logger.Warn("鼠标移动：位置输入已连接，但上游没有输出坐标。跳过移动，继续执行。");
            return NodeExecutionResult.Warn($"已跳过节点：{node.Title} 上游坐标缺失");
        }

        if (!hasConnection)
        {
            if (Math.Abs(node.PositionX) < 0.001 && Math.Abs(node.PositionY) < 0.001)
            {
                request.Context.Set(node.Id, "result", false);
                Logger.Warn("鼠标移动：未设置目标位置，也未连接位置输入。继续执行。");
                return NodeExecutionResult.Warn($"已跳过节点：{node.Title} 缺少目标位置");
            }

            point = new Point((int)Math.Round(node.PositionX), (int)Math.Round(node.PositionY));
        }

        Logger.Info($"鼠标移动：({point.X},{point.Y})");
        request.Adapters.Mouse.MoveTo(point);
        request.Context.Set(node.Id, "result", true);
        request.Context.Set(node.Id, "position", point);
        return NodeExecutionResult.Ok($"移动完成：{node.Title} -> ({point.X},{point.Y})");
    }
}

public sealed class ScrollWheelNodeExecutor : INodeExecutor
{
    public NodeKind NodeKind => NodeKind.ScrollWheel;

    public NodeExecutionResult Execute(NodeExecutionRequest request)
    {
        GraphRuntimeNode node = request.Node;
        int speed = node.ScrollSpeed > 0 ? node.ScrollSpeed : 120;
        int interval = node.ScrollInterval > 0 ? node.ScrollInterval : 100;
        int duration = node.ScrollDuration >= 0 ? node.ScrollDuration : 1000;

        if (node.ScrollSpeed <= 0)
            Logger.Warn("滚轮节点：滚动速度无效，将使用默认值 120。");

        Logger.Info($"滚轮：{node.ScrollAction}");
        request.Adapters.Mouse.ExecuteScroll(node.ScrollAction, speed, interval, duration, request.CancellationToken);
        request.Context.Set(node.Id, "result", true);
        return NodeExecutionResult.Ok($"滚轮：{node.ScrollAction}");
    }
}

