namespace AutomationStudioWpf.Graph;

public static class NodeTraits
{
    public static bool IsPure(NodeKind kind) => kind is
        NodeKind.Compare or
        NodeKind.BooleanAnd or
        NodeKind.BooleanOr or
        NodeKind.BooleanNot or
        NodeKind.StringConcat;

    public static bool HasExecutionPins(NodeKind kind) =>
        kind != NodeKind.Reroute && !IsPure(kind);

    public static bool ShouldAssignNodeNumber(NodeKind kind) =>
        HasExecutionPins(kind);

    public static bool IsToDoTarget(NodeKind kind) =>
        ShouldAssignNodeNumber(kind);
}
