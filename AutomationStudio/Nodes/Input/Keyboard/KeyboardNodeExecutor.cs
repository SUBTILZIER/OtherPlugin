using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Logging;
using AutomationStudioWpf.Runtime;

namespace AutomationStudioWpf.Nodes.Input.Keyboard;

public sealed class KeyboardNodeExecutor : INodeExecutor
{
    public NodeKind NodeKind => NodeKind.Keyboard;

    public NodeExecutionResult Execute(NodeExecutionRequest request)
    {
        string key = string.IsNullOrWhiteSpace(request.Node.Key) ? "A" : request.Node.Key;
        if (string.IsNullOrWhiteSpace(request.Node.Key))
            Logger.Warn("键盘节点：未设置按键，将使用默认值 A。");

        Logger.Info($"键盘：{key} {request.Node.OperationMode}");
        request.Adapters.Keyboard.ExecuteKey(key, request.Node.OperationMode);
        request.Context.Set(request.Node.Id, "result", true);
        return NodeExecutionResult.Ok($"键盘{key}{request.Node.OperationMode}");
    }
}

