using AutomationStudioWpf.Graph;

namespace AutomationStudioWpf.Runtime;

/// <summary>
/// Serializable execution snapshot built from current graph UI state.
/// Runtime never touches live WPF view-model instances directly.
/// </summary>
public sealed record GraphExecutionPlan(
    IReadOnlyList<GraphRuntimeNode> Nodes,
    IReadOnlyList<GraphRuntimeConnection> Connections);

public sealed record GraphRuntimeConnection(
    string SourceNodeId,
    string SourcePinName,
    PinKind SourcePinKind,
    string TargetNodeId,
    string TargetPinName,
    PinKind TargetPinKind);

public sealed record GraphRuntimeNode(
    string Id,
    string Title,
    NodeKind NodeKind,
    string? ImagePath,
    int SimilarityThresholdPercent,
    MouseClickMode ClickMode,
    MouseButton MouseButton,
    double PositionX,
    double PositionY,
    int DelayMs)
{
    public static GraphRuntimeNode ForStart(string id, string title) =>
        new(id, title, NodeKind.Start, null, 0, MouseClickMode.SingleClick, MouseButton.Left, 0, 0, 0);

    public static GraphRuntimeNode ForFindImage(string id, string title, string imagePath, int similarityThresholdPercent) =>
        new(id, title, NodeKind.FindImage, imagePath, similarityThresholdPercent, MouseClickMode.SingleClick, MouseButton.Left, 0, 0, 0);

    public static GraphRuntimeNode ForMouseLeftClick(string id, string title, MouseClickMode clickMode, MouseButton mouseButton, double positionX, double positionY) =>
        new(id, title, NodeKind.MouseClick, null, 0, clickMode, mouseButton, positionX, positionY, 0);

    public static GraphRuntimeNode ForDelay(string id, string title, int delayMs) =>
        new(id, title, NodeKind.Delay, null, 0, MouseClickMode.SingleClick, MouseButton.Left, 0, 0, delayMs);

    public static GraphRuntimeNode ForMouseMove(string id, string title, double positionX, double positionY) =>
        new(id, title, NodeKind.MouseMove, null, 0, MouseClickMode.SingleClick, MouseButton.Left, positionX, positionY, 0);
}

public sealed record GraphExecutionResult(bool Success, string Message);
