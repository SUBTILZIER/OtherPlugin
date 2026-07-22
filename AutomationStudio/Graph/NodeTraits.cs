using AutomationStudioWpf.GraphCore;

namespace AutomationStudioWpf.Graph;

public static class NodeTraits
{
    public static bool IsPure(NodeKind kind) =>
        NodeDescriptorCatalog.TryGet(kind, out var descriptor) && descriptor.IsPure;

    public static bool HasExecutionPins(NodeKind kind) =>
        NodeDescriptorCatalog.TryGet(kind, out var descriptor) && descriptor.HasExecutionPins;

    public static bool ShouldAssignNodeNumber(NodeKind kind) =>
        NodeDescriptorCatalog.TryGet(kind, out var descriptor) && descriptor.ShouldAssignNumber;

    public static bool IsToDoTarget(NodeKind kind) =>
        NodeDescriptorCatalog.TryGet(kind, out var descriptor) && descriptor.IsToDoTarget;
}
