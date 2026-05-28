using AutomationStudioWpf.Graph;

namespace AutomationStudioWpf.Runtime;

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
    PressReleaseMode OperationMode,
    MouseButton MouseButton,
    double PositionX,
    double PositionY,
    int DelayMs,
    string? Key,
    ScrollWheelAction ScrollAction,
    int ScrollSpeed)
{
    public static GraphRuntimeNode ForStart(string id, string title) =>
        new(id, title, NodeKind.Start, null, 0, PressReleaseMode.Press, MouseButton.Left, 0, 0, 0, null, ScrollWheelAction.ScrollForward, 120);

    public static GraphRuntimeNode ForFindImage(string id, string title, string imagePath, int similarityThresholdPercent) =>
        new(id, title, NodeKind.FindImage, imagePath, similarityThresholdPercent, PressReleaseMode.Press, MouseButton.Left, 0, 0, 0, null, ScrollWheelAction.ScrollForward, 120);

    public static GraphRuntimeNode ForMouseClick(string id, string title, PressReleaseMode operationMode, MouseButton mouseButton, double positionX, double positionY) =>
        new(id, title, NodeKind.MouseClick, null, 0, operationMode, mouseButton, positionX, positionY, 0, null, ScrollWheelAction.ScrollForward, 120);

    public static GraphRuntimeNode ForDelay(string id, string title, int delayMs) =>
        new(id, title, NodeKind.Delay, null, 0, PressReleaseMode.Press, MouseButton.Left, 0, 0, delayMs, null, ScrollWheelAction.ScrollForward, 120);

    public static GraphRuntimeNode ForMouseMove(string id, string title, double positionX, double positionY) =>
        new(id, title, NodeKind.MouseMove, null, 0, PressReleaseMode.Press, MouseButton.Left, positionX, positionY, 0, null, ScrollWheelAction.ScrollForward, 120);

    public static GraphRuntimeNode ForKeyboard(string id, string title, PressReleaseMode operationMode, string key) =>
        new(id, title, NodeKind.Keyboard, null, 0, operationMode, MouseButton.Left, 0, 0, 0, key, ScrollWheelAction.ScrollForward, 120);

    public static GraphRuntimeNode ForScrollWheel(string id, string title, ScrollWheelAction scrollAction, int scrollSpeed) =>
        new(id, title, NodeKind.ScrollWheel, null, 0, PressReleaseMode.Press, MouseButton.Left, 0, 0, 0, null, scrollAction, scrollSpeed);
}

public sealed record GraphExecutionResult(bool Success, string Message);
