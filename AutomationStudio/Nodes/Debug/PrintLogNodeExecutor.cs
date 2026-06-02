using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Logging;
using AutomationStudioWpf.Runtime;

namespace AutomationStudioWpf.Nodes.Debug;

public sealed class PrintLogNodeExecutor : INodeExecutor
{
    public NodeKind NodeKind => NodeKind.PrintLog;

    public NodeExecutionResult Execute(NodeExecutionRequest request)
    {
        if (!request.Context.TryResolveStringInput(request.Plan, request.Node, "message", out string message, out bool hasConnection))
        {
            if (hasConnection)
            {
                Logger.Warn("打印log：消息输入已连接，但上游没有输出。继续执行。");
                return NodeExecutionResult.Warn($"打印log未执行：{request.Node.Title} 上游消息缺失");
            }

            message = request.Node.ImagePath ?? string.Empty;
        }

        Logger.Info($"打印log：{message}");
        return NodeExecutionResult.Ok($"已打印log：{message}");
    }
}
