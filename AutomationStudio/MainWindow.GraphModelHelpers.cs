using AutomationStudioWpf.Graph;

namespace AutomationStudioWpf;

public partial class MainWindow
{
    private static void ApplyEntryNodeTitle(IEnumerable<NodeBaseViewModel> nodes, GraphAssetKind kind, string graphName)
    {
        string title = $"{graphName}开始";
        foreach (var node in nodes)
        {
            if (kind == GraphAssetKind.Function && node is FunctionEntryNodeViewModel)
            {
                node.Title = title;
            }
        }
    }

    private static void ApplyEntryNodeTitle(GraphFileModel graph, GraphAssetKind kind, string graphName)
    {
        string? entryKey = kind switch
        {
            GraphAssetKind.Function => "function_entry",
            _ => null,
        };
        if (entryKey is null)
            return;

        foreach (var node in graph.Nodes.Where(node => node.NodeTypeKey == entryKey))
            node.Title = $"{graphName}开始";
    }

    private static NodeFileModel CloneNodeFile(NodeFileModel node) => new()
    {
        Id = node.Id,
        NodeTypeKey = node.NodeTypeKey,
        Title = node.Title,
        X = node.X,
        Y = node.Y,
        ImagePath = node.ImagePath,
        SourceImagePath = node.SourceImagePath,
        ImageSearchSourceMode = node.ImageSearchSourceMode,
        SimilarityThresholdPercent = node.SimilarityThresholdPercent,
        UseFindImageRegion = node.UseFindImageRegion,
        FindImageRegionX = node.FindImageRegionX,
        FindImageRegionY = node.FindImageRegionY,
        FindImageRegionWidth = node.FindImageRegionWidth,
        FindImageRegionHeight = node.FindImageRegionHeight,
        ProgramPath = node.ProgramPath,
        WaitTimeoutMs = node.WaitTimeoutMs,
        FailureAction = node.FailureAction,
        RetryCount = node.RetryCount,
        PrintLogMessage = node.PrintLogMessage,
        ClickMode = node.ClickMode,
        PositionX = node.PositionX,
        PositionY = node.PositionY,
        HoldDurationMs = node.HoldDurationMs,
        MouseButton = node.MouseButton,
        OperationMode = node.OperationMode,
        Key = node.Key,
        ScrollAction = node.ScrollAction,
        ScrollSpeed = node.ScrollSpeed,
        ScrollInterval = node.ScrollInterval,
        ScrollDuration = node.ScrollDuration,
        DelayMs = node.DelayMs,
        LoopCount = node.LoopCount,
        ConditionValue = node.ConditionValue,
        WhileLoopMode = node.WhileLoopMode,
        MaxIterations = node.MaxIterations,
        RoutedKind = node.RoutedKind,
        ProcessName = node.ProcessName,
        WindowInputMode = node.WindowInputMode,
        Text = node.Text,
        Text2 = node.Text2,
        Text3 = node.Text3,
        Number = node.Number,
        Number2 = node.Number2,
        Number3 = node.Number3,
        Number4 = node.Number4,
        Flag = node.Flag,
        FunctionId = node.FunctionId,
        CustomEventId = node.CustomEventId,
        ExitName = node.ExitName,
        Parameters = node.Parameters.Select(CloneParameterFile).ToList(),
        InputParameters = node.InputParameters.Select(CloneParameterFile).ToList(),
        OutputParameters = node.OutputParameters.Select(CloneParameterFile).ToList(),
    };

    private static GraphParameterFileModel CloneParameterFile(GraphParameterFileModel parameter) => new()
    {
        Id = parameter.Id,
        Name = parameter.Name,
        Type = parameter.Type,
    };
}
