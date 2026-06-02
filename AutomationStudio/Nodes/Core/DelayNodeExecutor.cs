using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Logging;
using AutomationStudioWpf.Runtime;

namespace AutomationStudioWpf.Nodes.Core;

public sealed class DelayNodeExecutor : INodeExecutor
{
    public NodeKind NodeKind => NodeKind.Delay;

    public NodeExecutionResult Execute(NodeExecutionRequest request)
    {
        int delayMs = request.Node.DelayMs > 0 ? request.Node.DelayMs : 500;
        if (request.Node.DelayMs <= 0)
            Logger.Warn($"延迟节点：延迟时间无效 ({request.Node.DelayMs}ms)，将使用默认值 500ms。");

        Logger.Info($"延迟：{delayMs}ms");
        Thread.Sleep(delayMs);
        Logger.Info($"延迟完成：{delayMs}ms");
        return NodeExecutionResult.Ok($"延迟完成：{delayMs}ms");
    }
}

