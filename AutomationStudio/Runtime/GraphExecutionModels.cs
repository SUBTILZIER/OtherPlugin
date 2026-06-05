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

public sealed record RuntimeAssetLibrary(
    IReadOnlyDictionary<string, GraphExecutionPlan> Functions,
    IReadOnlyDictionary<string, GraphExecutionPlan> Macros);

public sealed record GraphRuntimeParameter(
    string Id,
    GraphParameterType Type,
    string DefaultValue);

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
    public bool UseFindImageRegion { get; init; }

    public string? SourceImagePath { get; init; }

    public ImageSearchSourceMode ImageSearchSourceMode { get; init; } = ImageSearchSourceMode.RealtimeScreenshot;

    public double FindImageRegionX { get; init; }

    public double FindImageRegionY { get; init; }

    public double FindImageRegionWidth { get; init; }

    public double FindImageRegionHeight { get; init; }

    public string? ProgramPath { get; init; }

    public int WaitTimeoutMs { get; init; }

    public string? PrintLogMessage { get; init; }

    public WhileLoopMode WhileLoopMode { get; init; } = WhileLoopMode.Finite;

    public int MaxIterations { get; init; }

    public string? Text { get; init; }

    public string? Text2 { get; init; }

    public string? Text3 { get; init; }

    public double Number { get; init; }

    public double Number2 { get; init; }

    public double Number3 { get; init; }

    public double Number4 { get; init; }

    public bool Flag { get; init; }

    public string? FunctionId { get; init; }

    public string? MacroId { get; init; }

    public string? CustomEventId { get; init; }

    public string? ExitName { get; init; }

    public IReadOnlyList<GraphRuntimeParameter> Parameters { get; init; } = [];

    public static GraphRuntimeNode ForStart(string id, string title) =>
        new(id, title, NodeKind.Start, null, 0, PressReleaseMode.Press, MouseButton.Left, 0, 0, 0, null, ScrollWheelAction.ScrollForward, 120, 100, 1000, 0, false, PinKind.Execution, ProgramStartFailureAction.None, 0, null);

    public static GraphRuntimeNode ForFindImage(
        string id,
        string title,
        string imagePath,
        string sourceImagePath,
        ImageSearchSourceMode sourceMode,
        int similarityThresholdPercent,
        bool useRegion,
        double regionX,
        double regionY,
        double regionWidth,
        double regionHeight) =>
        new(id, title, NodeKind.FindImage, imagePath, similarityThresholdPercent, PressReleaseMode.Press, MouseButton.Left, 0, 0, 0, null, ScrollWheelAction.ScrollForward, 120, 100, 1000, 0, false, PinKind.Execution, ProgramStartFailureAction.None, 0, null)
        {
            SourceImagePath = sourceImagePath,
            ImageSearchSourceMode = sourceMode,
            UseFindImageRegion = useRegion,
            FindImageRegionX = regionX,
            FindImageRegionY = regionY,
            FindImageRegionWidth = regionWidth,
            FindImageRegionHeight = regionHeight,
        };

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
            0, null, ScrollWheelAction.ScrollForward, 120, 100, 1000, 0,
            conditionValue, PinKind.Execution, ProgramStartFailureAction.None, 0, null)
        {
            WhileLoopMode = loopMode,
            MaxIterations = maxIterations,
        };

    public static GraphRuntimeNode ForReroute(string id, string title, PinKind routedKind) =>
        new(id, title, NodeKind.Reroute, null, 0, PressReleaseMode.Press, MouseButton.Left, 0, 0, 0, null, ScrollWheelAction.ScrollForward, 120, 100, 1000, 0, false, routedKind, ProgramStartFailureAction.None, 0, null);

    public static GraphRuntimeNode ForPrintLog(string id, string title, string message) =>
        new(id, title, NodeKind.PrintLog, null, 0, PressReleaseMode.Press, MouseButton.Left,
            0, 0, 0, null, ScrollWheelAction.ScrollForward, 0, 100, 1000,
            0, false, PinKind.Execution, ProgramStartFailureAction.None, 0, null)
        {
            PrintLogMessage = message,
        };

    public static GraphRuntimeNode ForStartProgram(string id, string title, string programPath, int waitTimeoutMs,
        ProgramStartFailureAction failureAction, int retryCount) =>
        new(id, title, NodeKind.StartProgram, null, 0, PressReleaseMode.Press, MouseButton.Left,
            0, 0, 0, null, ScrollWheelAction.ScrollForward, 120, 100, 1000,
            0, false, PinKind.Execution, failureAction, retryCount, null)
        {
            ProgramPath = programPath,
            WaitTimeoutMs = waitTimeoutMs,
        };

    public static GraphRuntimeNode ForSelectWindow(string id, string title, string processName) =>
        new(id, title, NodeKind.SelectWindow, null, 0, PressReleaseMode.Press, MouseButton.Left,
            0, 0, 0, null, ScrollWheelAction.ScrollForward, 0, 100, 1000,
            0, false, PinKind.Execution, ProgramStartFailureAction.None, 0, processName);

    public static GraphRuntimeNode ForCommon(
        string id,
        string title,
        NodeKind kind,
        string text,
        string text2,
        string text3,
        double number,
        double number2,
        double number3,
        double number4,
        bool flag) =>
        new(id, title, kind, null, 0, PressReleaseMode.Press, MouseButton.Left,
            0, 0, 0, null, ScrollWheelAction.ScrollForward, 120, 100, 1000,
            0, false, PinKind.Execution, ProgramStartFailureAction.None, 0, null)
        {
            Text = text,
            Text2 = text2,
            Text3 = text3,
            Number = number,
            Number2 = number2,
            Number3 = number3,
            Number4 = number4,
            Flag = flag,
        };

    public static GraphRuntimeNode ForAssetNode(
        string id,
        string title,
        NodeKind kind,
        IEnumerable<GraphParameterDefinition>? parameters = null) =>
        new(id, title, kind, null, 0, PressReleaseMode.Press, MouseButton.Left, 0, 0, 0, null, ScrollWheelAction.ScrollForward, 120, 100, 1000, 0, false, PinKind.Execution, ProgramStartFailureAction.None, 0, null)
        {
            Parameters = ToRuntimeParameters(parameters),
        };

    public static GraphRuntimeNode ForMacroOutput(
        string id,
        string title,
        string exitName,
        IEnumerable<GraphParameterDefinition>? parameters = null) =>
        new(id, title, NodeKind.MacroOutput, null, 0, PressReleaseMode.Press, MouseButton.Left, 0, 0, 0, null, ScrollWheelAction.ScrollForward, 120, 100, 1000, 0, false, PinKind.Execution, ProgramStartFailureAction.None, 0, null)
        {
            ExitName = exitName,
            Parameters = ToRuntimeParameters(parameters),
        };

    public static GraphRuntimeNode ForFunctionCall(
        string id,
        string title,
        string functionId,
        IEnumerable<GraphParameterDefinition>? parameters = null) =>
        new(id, title, NodeKind.FunctionCall, null, 0, PressReleaseMode.Press, MouseButton.Left, 0, 0, 0, null, ScrollWheelAction.ScrollForward, 120, 100, 1000, 0, false, PinKind.Execution, ProgramStartFailureAction.None, 0, null)
        {
            FunctionId = functionId,
            Parameters = ToRuntimeParameters(parameters),
        };

    public static GraphRuntimeNode ForMacroCall(
        string id,
        string title,
        string macroId,
        IEnumerable<GraphParameterDefinition>? parameters = null) =>
        new(id, title, NodeKind.MacroCall, null, 0, PressReleaseMode.Press, MouseButton.Left, 0, 0, 0, null, ScrollWheelAction.ScrollForward, 120, 100, 1000, 0, false, PinKind.Execution, ProgramStartFailureAction.None, 0, null)
        {
            MacroId = macroId,
            Parameters = ToRuntimeParameters(parameters),
        };

    public static GraphRuntimeNode ForCustomEvent(
        string id,
        string title,
        string customEventId,
        IEnumerable<GraphParameterDefinition>? parameters = null) =>
        new(id, title, NodeKind.CustomEvent, null, 0, PressReleaseMode.Press, MouseButton.Left, 0, 0, 0, null, ScrollWheelAction.ScrollForward, 120, 100, 1000, 0, false, PinKind.Execution, ProgramStartFailureAction.None, 0, null)
        {
            CustomEventId = customEventId,
            Parameters = ToRuntimeParameters(parameters),
        };

    public static GraphRuntimeNode ForCustomEventCall(
        string id,
        string title,
        string customEventId,
        IEnumerable<GraphParameterDefinition>? parameters = null) =>
        new(id, title, NodeKind.CustomEventCall, null, 0, PressReleaseMode.Press, MouseButton.Left, 0, 0, 0, null, ScrollWheelAction.ScrollForward, 120, 100, 1000, 0, false, PinKind.Execution, ProgramStartFailureAction.None, 0, null)
        {
            CustomEventId = customEventId,
            Parameters = ToRuntimeParameters(parameters),
        };

    private static IReadOnlyList<GraphRuntimeParameter> ToRuntimeParameters(IEnumerable<GraphParameterDefinition>? parameters) =>
        parameters?
            .Select(parameter => new GraphRuntimeParameter(parameter.Id, parameter.Type, parameter.DefaultValue))
            .ToList()
        ?? [];
}

public sealed record GraphExecutionResult(bool Success, string Message, bool ContinueExecution = true);
