using AutomationStudioWpf.Adapters;

namespace AutomationStudioWpf.Runtime;

public sealed record NodeExecutionRequest(
    GraphExecutionPlan Plan,
    GraphRuntimeNode Node,
    RuntimeContext Context,
    string BaseDirectory,
    RuntimeAdapters Adapters,
    CancellationToken CancellationToken);

