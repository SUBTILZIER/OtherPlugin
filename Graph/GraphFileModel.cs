namespace AutomationStudioWpf.Graph;

/// <summary>
/// Simple JSON DTOs for graph persistence.
/// These are intentionally explicit so future manual maintenance is straightforward.
/// </summary>
public sealed class GraphFileModel
{
    public string Name { get; set; } = "未命名图谱";

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

    public string? ImagePath { get; set; }

    public int SimilarityThresholdPercent { get; set; } = 80;

    public string? ClickMode { get; set; }

    public double PositionX { get; set; }

    public double PositionY { get; set; }

    public int HoldDurationMs { get; set; } = 600;

    public int DelayMs { get; set; }

    public string? MouseButton { get; set; }

    public string? OperationMode { get; set; }

    public string? Key { get; set; }

    public string? ScrollAction { get; set; }

    public int ScrollSpeed { get; set; } = 120;

    public int ScrollInterval { get; set; } = 100;

    public int ScrollDuration { get; set; } = 1000;

    public string? RoutedKind { get; set; }
}

public sealed class ConnectionFileModel
{
    public string SourceNodeId { get; set; } = string.Empty;

    public string SourcePinName { get; set; } = string.Empty;

    public string TargetNodeId { get; set; } = string.Empty;

    public string TargetPinName { get; set; } = string.Empty;
}
