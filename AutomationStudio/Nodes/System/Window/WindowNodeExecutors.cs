using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Logging;
using AutomationStudioWpf.Runtime;

namespace AutomationStudioWpf.Nodes.System.Window;

public sealed class StartProgramNodeExecutor : INodeExecutor
{
    public NodeKind NodeKind => NodeKind.StartProgram;

    public NodeExecutionResult Execute(NodeExecutionRequest request)
    {
        GraphRuntimeNode node = request.Node;
        string programPath = node.ProgramPath ?? string.Empty;
        if (string.IsNullOrWhiteSpace(programPath))
        {
            request.Context.Set(node.Id, "result", false);
            request.Context.Set(node.Id, "process_name", string.Empty);
            Logger.Warn("启动程序：程序路径为空。继续执行。");
            return NodeExecutionResult.Warn("启动程序未执行：程序路径为空。");
        }

        int waitTimeoutMs = node.WaitTimeoutMs > 0 ? node.WaitTimeoutMs : 60000;
        int retryCount = node.RetryCount > 0 ? node.RetryCount : 3;
        var result = request.Adapters.Process.StartProgram(programPath, waitTimeoutMs, node.FailureAction, retryCount, request.CancellationToken);
        request.Context.Set(node.Id, "result", result.Success);
        request.Context.Set(node.Id, "process_name", result.ProcessName);

        if (result.Success)
        {
            Logger.Info(result.Message);
            return NodeExecutionResult.Ok(result.Message);
        }

        Logger.Warn($"启动程序：{result.Message}。继续执行。");
        return NodeExecutionResult.Warn(result.Message);
    }
}

public sealed class SelectWindowNodeExecutor : INodeExecutor
{
    public NodeKind NodeKind => NodeKind.SelectWindow;

    public NodeExecutionResult Execute(NodeExecutionRequest request)
    {
        GraphRuntimeNode node = request.Node;
        if (!request.Context.TryResolveStringInput(request.Plan, node, "process_name", out string processName, out bool hasConnection))
        {
            if (hasConnection)
            {
                request.Context.Set(node.Id, "result", false);
                request.Context.Set(node.Id, "process_name", string.Empty);
                Logger.Warn("选中窗口：进程名输入已连接，但上游没有输出。继续执行。");
                return NodeExecutionResult.Warn($"选中窗口未执行：{node.Title} 上游进程名缺失");
            }

            processName = node.ProcessName ?? string.Empty;
        }

        var result = request.Adapters.Window.SelectWindowByProcessName(processName);
        request.Context.Set(node.Id, "result", result.Success);
        request.Context.Set(node.Id, "process_name", result.ProcessName);

        if (result.Success)
        {
            Logger.Info(result.Message);
            return NodeExecutionResult.Ok(result.Message);
        }

        Logger.Warn($"选中窗口：{result.Message}。继续执行。");
        return NodeExecutionResult.Warn(result.Message);
    }
}
