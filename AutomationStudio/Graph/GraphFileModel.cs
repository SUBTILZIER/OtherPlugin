namespace AutomationStudioWpf.Graph;

/// <summary>
/// Simple JSON DTOs for graph persistence.
/// These are intentionally explicit so future manual maintenance is straightforward.
/// </summary>
public sealed class GraphFileModel
{
    public string Name { get; set; } = "未命名图谱";

    public GraphAssetKind AssetKind { get; set; } = GraphAssetKind.EventGraph;

    public List<NodeFileModel> Nodes { get; set; } = [];

    public List<ConnectionFileModel> Connections { get; set; } = [];
}

public sealed class NodeFileModel
{
    public string Id { get; set; } = string.Empty;

    public string NodeTypeKey { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public double X { get; set; }

    public double Y { get; set; }

    // FindImage 节点属性。旧图中 StartProgram/PrintLog 也可能复用该字段。
    public string? ImagePath { get; set; }

    public string? SourceImagePath { get; set; }

    public string? ImageSearchSourceMode { get; set; }

    public int SimilarityThresholdPercent { get; set; } = 80;

    public bool UseFindImageRegion { get; set; }

    public double FindImageRegionX { get; set; }

    public double FindImageRegionY { get; set; }

    public double FindImageRegionWidth { get; set; }

    public double FindImageRegionHeight { get; set; }

    // StartProgram 节点属性。新保存使用明确字段，旧图兼容读取 ImagePath/DelayMs/ScrollAction/ScrollSpeed。
    public string? ProgramPath { get; set; }

    public int WaitTimeoutMs { get; set; }

    public string? FailureAction { get; set; }

    public int RetryCount { get; set; }

    // PrintLog 节点属性。旧图兼容读取 ImagePath。
    public string? PrintLogMessage { get; set; }

    // MouseClick 节点属性
    public string? ClickMode { get; set; }

    public double PositionX { get; set; }

    public double PositionY { get; set; }

    public int HoldDurationMs { get; set; } = 600;

    public string? MouseButton { get; set; }

    public string? OperationMode { get; set; }

    // Keyboard 节点属性
    public string? Key { get; set; }

    // ScrollWheel 节点属性
    public string? ScrollAction { get; set; }

    public int ScrollSpeed { get; set; } = 120;

    public int ScrollInterval { get; set; } = 100;

    public int ScrollDuration { get; set; } = 1000;

    // Delay 节点属性
    public int DelayMs { get; set; }

    // ForLoop 节点属性
    public int LoopCount { get; set; } = 5;

    // If/WhileLoop 节点属性
    public bool ConditionValue { get; set; }

    public string? WhileLoopMode { get; set; }

    public int MaxIterations { get; set; }

    // Reroute 节点属性
    public string? RoutedKind { get; set; }

    // SelectWindow 节点属性
    public string? ProcessName { get; set; }

    public string? WindowInputMode { get; set; }

    // Stage-5 common node properties.
    public string? Text { get; set; }

    public string? Text2 { get; set; }

    public string? Text3 { get; set; }

    public double Number { get; set; }

    public double Number2 { get; set; }

    public double Number3 { get; set; }

    public double Number4 { get; set; }

    public bool Flag { get; set; }

    // Function / macro nodes.
    public string? FunctionId { get; set; }

    public string? MacroId { get; set; }

    public string? CustomEventId { get; set; }

    public string? ExitName { get; set; }

    public List<GraphParameterFileModel> Parameters { get; set; } = [];

    public List<GraphParameterFileModel> InputParameters { get; set; } = [];

    public List<GraphParameterFileModel> OutputParameters { get; set; } = [];

    public List<MacroExitFileModel> MacroExits { get; set; } = [];
}

public sealed class GraphParameterFileModel
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = "NewParam";

    public GraphParameterType Type { get; set; } = GraphParameterType.Boolean;
}

public sealed class MacroExitFileModel
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = "完成";
}

public sealed class ConnectionFileModel
{
    public string SourceNodeId { get; set; } = string.Empty;

    public string SourcePinName { get; set; } = string.Empty;

    public string TargetNodeId { get; set; } = string.Empty;

    public string TargetPinName { get; set; } = string.Empty;
}
