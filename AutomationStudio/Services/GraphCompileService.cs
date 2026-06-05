using AutomationStudioWpf.Graph;
using AutomationStudioWpf.GraphCore;

namespace AutomationStudioWpf.Services;

public sealed class GraphCompileResult
{
    public bool Success { get; init; }
    public int UpdatedCallNodes { get; init; }
    public int RemovedConnections { get; init; }
    public IReadOnlySet<string> ChangedAssetIds { get; init; } = new HashSet<string>();
    public IReadOnlyList<GraphValidationIssue> Issues { get; init; } = [];
}

public sealed class GraphCompileService
{
    private readonly CallableGraphResolver _callableResolver;
    private readonly GraphCallReferenceSyncService _referenceSyncService;

    public GraphCompileService()
        : this(new CallableGraphResolver())
    {
    }

    public GraphCompileService(CallableGraphResolver callableResolver)
    {
        _callableResolver = callableResolver;
        _referenceSyncService = new GraphCallReferenceSyncService(callableResolver);
    }

    public GraphCompileResult Compile(IEnumerable<ContentAssetViewModel> assets)
    {
        var assetList = assets.ToList();
        var sync = _referenceSyncService.Sync(assetList);
        var issues = ValidateAssets(assetList);
        bool success = issues.All(issue => issue.Severity != GraphValidationSeverity.Error);
        if (!success)
        {
            return new GraphCompileResult
            {
                Success = false,
                UpdatedCallNodes = sync.UpdatedCallNodes,
                RemovedConnections = sync.RemovedConnections,
                ChangedAssetIds = sync.ChangedAssetIds,
                Issues = issues,
            };
        }

        foreach (var item in assetList
                     .Where(asset => asset.Kind != ContentAssetKind.Folder)
                     .SelectMany(asset => asset.EventGraphs.Concat(asset.Functions).Concat(asset.Macros)))
        {
            item.IsCompileDirty = false;
        }

        return new GraphCompileResult
        {
            Success = success,
            UpdatedCallNodes = sync.UpdatedCallNodes,
            RemovedConnections = sync.RemovedConnections,
            ChangedAssetIds = sync.ChangedAssetIds,
            Issues = issues,
        };
    }

    private IReadOnlyList<GraphValidationIssue> ValidateAssets(IReadOnlyList<ContentAssetViewModel> assets)
    {
        var issues = new List<GraphValidationIssue>();
        var assetsById = assets.ToDictionary(asset => asset.Id, StringComparer.Ordinal);
        foreach (var asset in assets.Where(asset => asset.Kind != ContentAssetKind.Folder))
        {
            foreach (var item in asset.EventGraphs.Concat(asset.Functions).Concat(asset.Macros))
                ValidateGraph(assets, assetsById, asset, item, issues);
        }

        return issues;
    }

    private void ValidateGraph(
        IReadOnlyList<ContentAssetViewModel> assets,
        IReadOnlyDictionary<string, ContentAssetViewModel> assetsById,
        ContentAssetViewModel owner,
        GraphListItemViewModel item,
        List<GraphValidationIssue> issues)
    {
        string graphName = $"{BuildContentPath(owner, assetsById)}/{item.Name}";
        var nodesById = new Dictionary<string, NodeBaseViewModel>(StringComparer.Ordinal);
        var rawNodeIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var fileNode in item.Graph.Nodes)
        {
            if (string.IsNullOrWhiteSpace(fileNode.Id))
            {
                issues.Add(Error(graphName, "存在空节点 ID。"));
                continue;
            }

            if (!rawNodeIds.Add(fileNode.Id))
                issues.Add(Error(graphName, $"节点 ID 重复：{fileNode.Id}。"));

            var node = NodeSerializer.FromFileModel(fileNode);
            if (node is null)
            {
                issues.Add(Error(graphName, $"未知节点类型：{fileNode.NodeTypeKey}。"));
                continue;
            }

            nodesById[fileNode.Id] = node;
        }

        ValidateGraphEntrypoints(graphName, item.Kind, nodesById.Values, issues);
        ValidateConnections(graphName, item.Graph.Connections, nodesById, issues);
        ValidateCallReferences(assets, owner, item.Graph, graphName, issues);
    }

    private static void ValidateGraphEntrypoints(
        string graphName,
        GraphAssetKind kind,
        IEnumerable<NodeBaseViewModel> nodes,
        List<GraphValidationIssue> issues)
    {
        int Count(NodeKind nodeKind) => nodes.Count(node => node.NodeKind == nodeKind);

        switch (kind)
        {
            case GraphAssetKind.EventGraph:
                if (Count(NodeKind.Start) != 1)
                    issues.Add(Error(graphName, "事件图必须有且只有一个开始节点。"));
                break;
            case GraphAssetKind.Function:
                if (Count(NodeKind.FunctionEntry) != 1)
                    issues.Add(Error(graphName, "函数图必须有且只有一个函数开始节点。"));
                if (Count(NodeKind.FunctionReturn) != 1)
                    issues.Add(Error(graphName, "函数图必须有且只有一个函数返回节点。"));
                break;
            case GraphAssetKind.Macro:
                if (Count(NodeKind.MacroEntry) != 1)
                    issues.Add(Error(graphName, "宏图必须有且只有一个宏开始节点。"));
                if (Count(NodeKind.MacroOutput) == 0)
                    issues.Add(Error(graphName, "宏图必须至少有一个宏输出节点。"));
                break;
        }
    }

    private static void ValidateConnections(
        string graphName,
        IReadOnlyList<ConnectionFileModel> connections,
        IReadOnlyDictionary<string, NodeBaseViewModel> nodesById,
        List<GraphValidationIssue> issues)
    {
        var validConnections = new List<(ConnectionFileModel File, PinKind SourceKind, PinKind TargetKind)>();
        foreach (var connection in connections)
        {
            if (!nodesById.TryGetValue(connection.SourceNodeId, out var sourceNode))
            {
                issues.Add(Error(graphName, $"连线源节点不存在：{connection.SourceNodeId}。"));
                continue;
            }

            if (!nodesById.TryGetValue(connection.TargetNodeId, out var targetNode))
            {
                issues.Add(Error(graphName, $"连线目标节点不存在：{connection.TargetNodeId}。"));
                continue;
            }

            var sourcePin = sourceNode.OutputPins.FirstOrDefault(pin => pin.Name == connection.SourcePinName);
            var targetPin = targetNode.InputPins.FirstOrDefault(pin => pin.Name == connection.TargetPinName);
            if (sourcePin is null)
            {
                issues.Add(Error(graphName, $"连线源引脚不存在：{connection.SourceNodeId}.{connection.SourcePinName}。"));
                continue;
            }

            if (targetPin is null)
            {
                issues.Add(Error(graphName, $"连线目标引脚不存在：{connection.TargetNodeId}.{connection.TargetPinName}。"));
                continue;
            }

            if (sourcePin.Kind != targetPin.Kind && targetPin.Kind != PinKind.String)
            {
                issues.Add(Error(
                    graphName,
                    $"非法连线类型：{connection.SourceNodeId}.{connection.SourcePinName}({sourcePin.Kind}) -> {connection.TargetNodeId}.{connection.TargetPinName}({targetPin.Kind})。"));
            }

            validConnections.Add((connection, sourcePin.Kind, targetPin.Kind));
        }

        foreach (var group in validConnections
                     .Where(connection => connection.SourceKind == PinKind.Execution)
                     .GroupBy(connection => (connection.File.SourceNodeId, connection.File.SourcePinName))
                     .Where(group => group.Count() > 1))
        {
            issues.Add(Error(graphName, $"执行输出引脚存在多条连线：{group.Key.SourceNodeId}.{group.Key.SourcePinName}。"));
        }

        foreach (var group in validConnections
                     .Where(connection => connection.TargetKind != PinKind.Execution)
                     .GroupBy(connection => (connection.File.TargetNodeId, connection.File.TargetPinName))
                     .Where(group => group.Count() > 1))
        {
            issues.Add(Error(graphName, $"数据输入引脚存在多条入线：{group.Key.TargetNodeId}.{group.Key.TargetPinName}。"));
        }
    }

    private void ValidateCallReferences(
        IReadOnlyList<ContentAssetViewModel> assets,
        ContentAssetViewModel owner,
        GraphFileModel graph,
        string graphName,
        List<GraphValidationIssue> issues)
    {
        var functions = _callableResolver.ResolveFunctions(assets, owner)
            .ToDictionary(item => item.Id, StringComparer.Ordinal);
        var macros = _callableResolver.ResolveMacros(assets, owner)
            .ToDictionary(item => item.Id, StringComparer.Ordinal);
        var customEvents = graph.Nodes
            .Where(node => node.NodeTypeKey == "custom_event")
            .Select(node => string.IsNullOrWhiteSpace(node.CustomEventId) ? node.Id : node.CustomEventId!)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var node in graph.Nodes)
        {
            if (node.NodeTypeKey == "function_call" &&
                (string.IsNullOrWhiteSpace(node.FunctionId) || !functions.ContainsKey(node.FunctionId)))
            {
                issues.Add(Error(graphName, $"函数调用不可用或未公开：{node.Title}。"));
            }
            else if (node.NodeTypeKey == "macro_call" &&
                     (string.IsNullOrWhiteSpace(node.MacroId) || !macros.ContainsKey(node.MacroId)))
            {
                issues.Add(Error(graphName, $"宏调用不可用或未公开：{node.Title}。"));
            }
            else if (node.NodeTypeKey == "custom_event_call" &&
                     (string.IsNullOrWhiteSpace(node.CustomEventId) || !customEvents.Contains(node.CustomEventId)))
            {
                issues.Add(Error(graphName, $"自定义事件调用不存在：{node.Title}。"));
            }
        }
    }

    private static GraphValidationIssue Error(string graphName, string message) =>
        new(GraphValidationSeverity.Error, $"{graphName}: {message}");

    private static string BuildContentPath(
        ContentAssetViewModel asset,
        IReadOnlyDictionary<string, ContentAssetViewModel> assetsById)
    {
        var segments = new Stack<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        ContentAssetViewModel? current = asset;
        while (current is not null && visited.Add(current.Id))
        {
            segments.Push(SafeSegment(current.Name));
            if (string.IsNullOrWhiteSpace(current.ParentFolderId) ||
                !assetsById.TryGetValue(current.ParentFolderId, out current))
            {
                break;
            }
        }

        return $"content/{string.Join("/", segments)}";
    }

    private static string SafeSegment(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? "Unnamed"
            : value.Replace('\\', '/').Trim('/');
}
