using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Logging;
using AutomationStudioWpf.Nodes.Input;
using AutomationStudioWpf.Runtime;

namespace AutomationStudioWpf.Nodes.Input.Keyboard;

public sealed class KeyboardNodeExecutor : INodeExecutor
{
    public NodeKind NodeKind => NodeKind.Keyboard;

    public NodeExecutionResult Execute(NodeExecutionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Node.Key))
        {
            request.Context.Set(request.Node.Id, "result", false);
            Logger.Warn("键盘：未设置按键，跳过并继续执行。");
            return NodeExecutionResult.Warn($"已跳过节点：{request.Node.Title} 缺少按键");
        }

        string key = request.Node.Key;

        int interval = Math.Max(1, request.Node.TriggerIntervalMs);
        Logger.Info($"键盘：{key} {request.Node.OperationMode}，触发 {TriggerRepeatRunner.CountLabel(request.Node.TriggerCount)}，间隔 {interval}ms");
        TriggerRepeatRunner.Run(
            request.Node.TriggerCount,
            interval,
            request.CancellationToken,
            _ => request.Adapters.Keyboard.ExecuteKey(key, request.Node.OperationMode));

        request.Context.Set(request.Node.Id, "result", true);
        return NodeExecutionResult.Ok($"键盘{key}{request.Node.OperationMode}，触发 {TriggerRepeatRunner.CountLabel(request.Node.TriggerCount)}");
    }
}
