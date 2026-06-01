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

/// <summary>
/// 运行时节点数据模型 - 扁平化设计用于执行引擎
/// </summary>
public sealed record GraphRuntimeNode(
    string Id,
    string Title,
    NodeKind NodeKind,
    // FindImage
    string? ImagePath,
    int SimilarityThresholdPercent,
    // MouseClick / Keyboard
    PressReleaseMode OperationMode,
    MouseButton MouseButton,
    // Position (MouseClick, MouseMove)
    double PositionX,
    double PositionY,
    // Delay
    int DelayMs,
    // Keyboard
    string? Key,
    // ScrollWheel
    ScrollWheelAction ScrollAction,
    int ScrollSpeed,
    int ScrollInterval,
    int ScrollDuration,
    // ForLoop
    int LoopCount,
    // If / WhileLoop
    bool ConditionValue,
    // Reroute
    PinKind RoutedKind,
    // StartProgram
    ProgramStartFailureAction FailureAction,
    int RetryCount,
    // SelectWindow
    string? ProcessName)
{
    public static GraphRuntimeNode ForStart(string id, string title) =>
        new(id, title, NodeKind.Start, null, 0, PressReleaseMode.Press, MouseButton.Left, 0, 0, 0, null, ScrollWheelAction.ScrollForward, 120, 100, 1000, 0, false, PinKind.Execution, ProgramStartFailureAction.None, 0, null);

    public static GraphRuntimeNode ForFindImage(string id, string title, string imagePath, int similarityThresholdPercent) =>
        new(id, title, NodeKind.FindImage, imagePath, similarityThresholdPercent, PressReleaseMode.Press, MouseButton.Left, 0, 0, 0, null, ScrollWheelAction.ScrollForward, 120, 100, 1000, 0, false, PinKind.Execution, ProgramStartFailureAction.None, 0, null);

    public static GraphRuntimeNode ForMouseClick(string id, string title, PressReleaseMode operationMode, MouseButton mouseButton, double positionX, double positionY) =>
        new(id, title, NodeKind.MouseClick, null, 0, operationMode, mouseButton, positionX, positionY, 0, null, ScrollWheelAction.ScrollForward, 120, 100, 1000, 0, false, PinKind.Execution, ProgramStartFailureAction.None, 0, null);

    public static GraphRuntimeNode ForDelay(string id, string title, int delayMs) =>
        new(id, title, NodeKind.Delay, null, 0, PressReleaseMode.Press, MouseButton.Left, 0, 0, delayMs, null, ScrollWheelAction.ScrollForward, 120, 100, 1000, 0, false, PinKind.Execution, ProgramStartFailureAction.None, 0, null);

    public static GraphRuntimeNode ForMouseMove(string id, string title, double positionX, double positionY) =>
        new(id, title, NodeKind.MouseMove, null, 0, PressReleaseMode.Press, MouseButton.Left, positionX, positionY, 0, null, ScrollWheelAction.ScrollForward, 120, 100, 1000, 0, false, PinKind.Execution, ProgramStartFailureAction.None, 0, null);

    public static GraphRuntimeNode ForKeyboard(string id, string title, PressReleaseMode operationMode, string key) =>
        new(id, title, NodeKind.Keyboard, null, 0, operationMode, MouseButton.Left, 0, 0, 0, key, ScrollWheelAction.ScrollForward, 120, 100, 1000, 0, false, PinKind.Execution, ProgramStartFailureAction.None, 0, null);

    public static GraphRuntimeNode ForScrollWheel(string id, string title, ScrollWheelAction scrollAction, int scrollSpeed, int scrollInterval, int scrollDuration) =>
        new(id, title, NodeKind.ScrollWheel, null, 0, PressReleaseMode.Press, MouseButton.Left, 0, 0, 0, null, scrollAction, scrollSpeed, scrollInterval, scrollDuration, 0, false, PinKind.Execution, ProgramStartFailureAction.None, 0, null);

    public static GraphRuntimeNode ForIf(string id, string title, bool conditionValue) =>
        new(id, title, NodeKind.If, null, 0, PressReleaseMode.Press, MouseButton.Left, 0, 0, 0, null, ScrollWheelAction.ScrollForward, 120, 100, 1000, 0, conditionValue, PinKind.Execution, ProgramStartFailureAction.None, 0, null);

    public static GraphRuntimeNode ForForLoop(string id, string title, int loopCount, bool endConditionValue) =>
        new(id, title, NodeKind.ForLoop, null, 0, PressReleaseMode.Press, MouseButton.Left, 0, 0, 0, null, ScrollWheelAction.ScrollForward, 120, 100, 1000, loopCount, endConditionValue, PinKind.Execution, ProgramStartFailureAction.None, 0, null);

    public static GraphRuntimeNode ForWhileLoop(string id, string title, bool conditionValue, WhileLoopMode loopMode,
        int maxIterations) =>
        new(id, title, NodeKind.WhileLoop, null, 0, PressReleaseMode.Press, MouseButton.Left, 0, 0,
            maxIterations, null, ScrollWheelAction.ScrollForward, (int)loopMode, 100, 1000, 0,
            conditionValue, PinKind.Execution, ProgramStartFailureAction.None, 0, null);

    public static GraphRuntimeNode ForReroute(string id, string title, PinKind routedKind) =>
        new(id, title, NodeKind.Reroute, null, 0, PressReleaseMode.Press, MouseButton.Left, 0, 0, 0, null, ScrollWheelAction.ScrollForward, 120, 100, 1000, 0, false, routedKind, ProgramStartFailureAction.None, 0, null);

    public static GraphRuntimeNode ForPrintLog(string id, string title, string message) =>
        new(id, title, NodeKind.PrintLog, message, 0, PressReleaseMode.Press, MouseButton.Left,
            0, 0, 0, null, ScrollWheelAction.ScrollForward, 0, 100, 1000,
            0, false, PinKind.Execution, ProgramStartFailureAction.None, 0, null);

    public static GraphRuntimeNode ForStartProgram(string id, string title, string programPath, int waitTimeoutMs,
        ProgramStartFailureAction failureAction, int retryCount) =>
        new(id, title, NodeKind.StartProgram, programPath, 0, PressReleaseMode.Press, MouseButton.Left,
            0, 0, waitTimeoutMs, null, ScrollWheelAction.ScrollForward, retryCount, 100, 1000,
            0, false, PinKind.Execution, failureAction, retryCount, null);

    public static GraphRuntimeNode ForSelectWindow(string id, string title, string processName) =>
        new(id, title, NodeKind.SelectWindow, null, 0, PressReleaseMode.Press, MouseButton.Left,
            0, 0, 0, null, ScrollWheelAction.ScrollForward, 0, 100, 1000,
            0, false, PinKind.Execution, ProgramStartFailureAction.None, 0, processName);

    public static GraphRuntimeNode ForFindText(string id, string title, string text, int similarityThresholdPercent) =>
        new(id, title, NodeKind.FindText, text, similarityThresholdPercent,
            PressReleaseMode.Press, MouseButton.Left,
            0, 0, 0, null, ScrollWheelAction.ScrollForward, 120, 100, 1000,
            0, false, PinKind.Execution, ProgramStartFailureAction.None, 0, null);
}

public sealed record GraphExecutionResult(bool Success, string Message, bool ContinueExecution = true);
