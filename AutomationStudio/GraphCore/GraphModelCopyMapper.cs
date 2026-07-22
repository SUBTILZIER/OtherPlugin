using AutomationStudioWpf.Graph;

namespace AutomationStudioWpf.GraphCore;

internal static class GraphModelCopyMapper
{
    public static GraphFileModel Copy(GraphFileModel source) => new()
    {
        Name = source.Name,
        AssetKind = source.AssetKind,
        EntryRole = source.EntryRole,
        Nodes = source.Nodes.Select(Copy).ToList(),
        Connections = source.Connections.Select(Copy).ToList(),
    };

    public static NodeFileModel Copy(NodeFileModel source) => new()
    {
        Id = source.Id,
        NodeTypeKey = source.NodeTypeKey,
        Title = source.Title,
        NodeNumber = source.NodeNumber,
        X = source.X,
        Y = source.Y,
        ImagePath = source.ImagePath,
        SourceImagePath = source.SourceImagePath,
        ImageSearchSourceMode = source.ImageSearchSourceMode,
        SimilarityThresholdPercent = source.SimilarityThresholdPercent,
        UseFindImageRegion = source.UseFindImageRegion,
        FindImageRegionX = source.FindImageRegionX,
        FindImageRegionY = source.FindImageRegionY,
        FindImageRegionWidth = source.FindImageRegionWidth,
        FindImageRegionHeight = source.FindImageRegionHeight,
        ProgramPath = source.ProgramPath,
        WaitTimeoutMs = source.WaitTimeoutMs,
        FailureAction = source.FailureAction,
        RetryCount = source.RetryCount,
        PrintLogMessage = source.PrintLogMessage,
        ClickMode = source.ClickMode,
        PositionX = source.PositionX,
        PositionY = source.PositionY,
        HasManualPosition = source.HasManualPosition,
        HoldDurationMs = source.HoldDurationMs,
        MouseButton = source.MouseButton,
        OperationMode = source.OperationMode,
        TriggerCount = source.TriggerCount,
        TriggerIntervalMs = source.TriggerIntervalMs,
        Key = source.Key,
        ScrollAction = source.ScrollAction,
        ScrollSpeed = source.ScrollSpeed,
        ScrollInterval = source.ScrollInterval,
        ScrollDuration = source.ScrollDuration,
        DelayMs = source.DelayMs,
        LoopCount = source.LoopCount,
        ConditionValue = source.ConditionValue,
        WhileLoopMode = source.WhileLoopMode,
        MaxIterations = source.MaxIterations,
        RoutedKind = source.RoutedKind,
        ProcessName = source.ProcessName,
        WindowInputMode = source.WindowInputMode,
        Text = source.Text,
        Text2 = source.Text2,
        Text3 = source.Text3,
        Number = source.Number,
        Number2 = source.Number2,
        Number3 = source.Number3,
        Number4 = source.Number4,
        Flag = source.Flag,
        VariadicInputCount = source.VariadicInputCount,
        VariadicInputDefaults = source.VariadicInputDefaults is null
            ? null
            : new Dictionary<string, string>(source.VariadicInputDefaults, StringComparer.Ordinal),
        ThreadOutputCount = source.ThreadOutputCount,
        FunctionId = source.FunctionId,
        CustomEventId = source.CustomEventId,
        ExitName = source.ExitName,
        Parameters = source.Parameters.Select(Copy).ToList(),
        InputParameters = source.InputParameters.Select(Copy).ToList(),
        OutputParameters = source.OutputParameters.Select(Copy).ToList(),
        TargetNodeTitle = source.TargetNodeTitle,
        TargetNodeNumber = source.TargetNodeNumber,
        TargetNodeId = source.TargetNodeId,
        ReturnAfterTarget = source.ReturnAfterTarget,
    };

    public static GraphParameterFileModel Copy(GraphParameterFileModel source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        Type = source.Type,
        DefaultValue = source.DefaultValue,
    };

    public static ConnectionFileModel Copy(ConnectionFileModel source) => new()
    {
        SourceNodeId = source.SourceNodeId,
        SourcePinName = source.SourcePinName,
        TargetNodeId = source.TargetNodeId,
        TargetPinName = source.TargetPinName,
    };
}
