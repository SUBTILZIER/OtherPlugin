using AutomationStudioWpf.Graph;

namespace AutomationStudioWpf.Runtime;

public interface INodeExecutor
{
    NodeKind NodeKind { get; }

    NodeExecutionResult Execute(NodeExecutionRequest request);
}

