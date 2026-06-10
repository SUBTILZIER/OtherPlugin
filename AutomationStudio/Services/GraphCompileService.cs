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
        var changedAssetIds = EnsureNodeNumbers(assetList);
        var sync = _referenceSyncService.Sync(assetList);
        changedAssetIds.UnionWith(sync.ChangedAssetIds);
        var issues = ValidateAssets(assetList);
        bool success = issues.All(issue => issue.Severity != GraphValidationSeverity.Error);
        if (!success)
        {
            return new GraphCompileResult
            {
                Success = false,
                UpdatedCallNodes = sync.UpdatedCallNodes,
                RemovedConnections = sync.RemovedConnections,
                ChangedAssetIds = changedAssetIds,
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
            ChangedAssetIds = changedAssetIds,
            Issues = issues,
        };
    }

    public GraphCompileResult CompileGraph(
        IEnumerable<ContentAssetViewModel> assets,
        ContentAssetViewModel owner,
        GraphListItemViewModel item)
    {
        var assetList = assets.ToList();
        var changedAssetIds = new HashSet<string>(StringComparer.Ordinal);
        bool changed = EnsureGraphNodeNumbers(item.Graph, item.Kind);
        changed |= EnsureGraphToDoTargets(item.Graph);
        if (changed)
            changedAssetIds.Add(owner.Id);

        var sync = _referenceSyncService.SyncGraph(assetList, owner, item.Graph);
        changedAssetIds.UnionWith(sync.ChangedAssetIds);

        var issues = ValidateSingleGraph(assetList, owner, item);
        bool success = issues.All(issue => issue.Severity != GraphValidationSeverity.Error);
        if (success)
            item.IsCompileDirty = false;

        return new GraphCompileResult
        {
            Success = success,
            UpdatedCallNodes = sync.UpdatedCallNodes,
            RemovedConnections = sync.RemovedConnections,
            ChangedAssetIds = changedAssetIds,
            Issues = issues,
        };
    }

    public GraphCompileResult CompileAsset(
        IEnumerable<ContentAssetViewModel> assets,
        ContentAssetViewModel owner)
    {
        var assetList = assets.ToList();
        var changedAssetIds = EnsureAssetNodeNumbers(owner);
        var sync = SyncAssetGraphs(assetList, owner);
        changedAssetIds.UnionWith(sync.ChangedAssetIds);

        var issues = ValidateAsset(assetList, owner);
        bool success = issues.All(issue => issue.Severity != GraphValidationSeverity.Error);
        if (success)
        {
            foreach (var item in GetCompilableGraphs(owner))
                item.IsCompileDirty = false;
        }

        return new GraphCompileResult
        {
            Success = success,
            UpdatedCallNodes = sync.UpdatedCallNodes,
            RemovedConnections = sync.RemovedConnections,
            ChangedAssetIds = changedAssetIds,
            Issues = issues,
        };
    }

    private GraphCallReferenceSyncResult SyncAssetGraphs(
        IReadOnlyList<ContentAssetViewModel> assets,
        ContentAssetViewModel owner)
    {
        var result = new GraphCallReferenceSyncResult();
        foreach (var item in GetCompilableGraphs(owner))
        {
            var sync = _referenceSyncService.SyncGraph(assets, owner, item.Graph);
            result.UpdatedCallNodes += sync.UpdatedCallNodes;
            result.RemovedConnections += sync.RemovedConnections;
            result.ChangedAssetIds.UnionWith(sync.ChangedAssetIds);
        }

        return result;
    }

    private static HashSet<string> EnsureNodeNumbers(IReadOnlyList<ContentAssetViewModel> assets)
    {
        var changedAssetIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var asset in assets.Where(asset => asset.Kind != ContentAssetKind.Folder))
        {
            foreach (var item in asset.EventGraphs.Concat(asset.Functions).Concat(asset.Macros))
            {
                bool changed = EnsureGraphNodeNumbers(item.Graph, item.Kind);
                changed |= EnsureGraphToDoTargets(item.Graph);
                if (changed)
                    changedAssetIds.Add(asset.Id);
            }
        }

        return changedAssetIds;
    }

    private static HashSet<string> EnsureAssetNodeNumbers(ContentAssetViewModel asset)
    {
        var changedAssetIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in GetCompilableGraphs(asset))
        {
            bool changed = EnsureGraphNodeNumbers(item.Graph, item.Kind);
            changed |= EnsureGraphToDoTargets(item.Graph);
            if (changed)
                changedAssetIds.Add(asset.Id);
        }

        return changedAssetIds;
    }

    private static IEnumerable<GraphListItemViewModel> GetCompilableGraphs(ContentAssetViewModel asset) => asset.Kind switch
    {
        ContentAssetKind.Script => asset.EventGraphs.Concat(asset.Functions).Concat(asset.Macros),
        ContentAssetKind.FunctionLibrary => asset.Functions,
        ContentAssetKind.MacroLibrary => asset.Macros,
        _ => Enumerable.Empty<GraphListItemViewModel>(),
    };

    private static bool EnsureGraphNodeNumbers(GraphFileModel graph, GraphAssetKind kind)
    {
        string prefix = NodeNumberPrefix(kind);
        var used = new HashSet<int>();
        var pending = new List<NodeFileModel>();
        bool changed = false;

        foreach (var node in graph.Nodes)
        {
            if (IsRerouteNode(node))
            {
                if (!string.IsNullOrWhiteSpace(node.NodeNumber))
                {
                    node.NodeNumber = string.Empty;
                    changed = true;
                }

                continue;
            }

            int? ordinal = ParseNodeOrdinal(node.NodeNumber, prefix);
            if (ordinal.HasValue && used.Add(ordinal.Value))
                continue;

            pending.Add(node);
        }

        int next = 1;
        foreach (var node in pending)
        {
            while (used.Contains(next))
                next++;

            string nodeNumber = $"{prefix}{next:000}";
            if (!string.Equals(node.NodeNumber, nodeNumber, StringComparison.Ordinal))
            {
                node.NodeNumber = nodeNumber;
                changed = true;
            }

            used.Add(next);
        }

        return changed;
    }

    private static bool IsRerouteNode(NodeFileModel node) =>
        string.Equals(node.NodeTypeKey, "reroute", StringComparison.OrdinalIgnoreCase);

    private static bool IsToDoNode(NodeFileModel node) =>
        string.Equals(node.NodeTypeKey, "todo", StringComparison.OrdinalIgnoreCase);

    private static bool EnsureGraphToDoTargets(GraphFileModel graph)
    {
        var nodesById = graph.Nodes
            .Where(node => !IsRerouteNode(node))
            .Where(node => !string.IsNullOrWhiteSpace(node.Id))
            .GroupBy(node => node.Id, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);

        bool changed = false;
        foreach (var node in graph.Nodes.Where(IsToDoNode))
        {
            if (string.IsNullOrWhiteSpace(node.TargetNodeId) ||
                !nodesById.TryGetValue(node.TargetNodeId!, out var target) ||
                string.Equals(target.Id, node.Id, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(target.Title) ||
                string.IsNullOrWhiteSpace(target.NodeNumber))
            {
                continue;
            }

            if (!string.Equals(node.TargetNodeTitle, target.Title, StringComparison.Ordinal))
            {
                node.TargetNodeTitle = target.Title;
                changed = true;
            }

            if (!string.Equals(node.TargetNodeNumber, target.NodeNumber, StringComparison.OrdinalIgnoreCase))
            {
                node.TargetNodeNumber = target.NodeNumber;
                changed = true;
            }
        }

        return changed;
    }

    private static string NodeNumberPrefix(GraphAssetKind kind) => kind switch
    {
        GraphAssetKind.Function => "Fun",
        GraphAssetKind.Macro => "Mac",
        _ => "N",
    };

    private static int? ParseNodeOrdinal(string? nodeNumber, string prefix)
    {
        if (string.IsNullOrWhiteSpace(nodeNumber) ||
            !nodeNumber.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        string suffix = nodeNumber[prefix.Length..];
        return int.TryParse(suffix, out int ordinal) && ordinal > 0 ? ordinal : null;
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

    private IReadOnlyList<GraphValidationIssue> ValidateAsset(
        IReadOnlyList<ContentAssetViewModel> assets,
        ContentAssetViewModel owner)
    {
        var issues = new List<GraphValidationIssue>();
        var assetsById = assets.ToDictionary(asset => asset.Id, StringComparer.Ordinal);
        foreach (var item in GetCompilableGraphs(owner))
            ValidateGraph(assets, assetsById, owner, item, issues);

        return issues;
    }

    private IReadOnlyList<GraphValidationIssue> ValidateSingleGraph(
        IReadOnlyList<ContentAssetViewModel> assets,
        ContentAssetViewModel owner,
        GraphListItemViewModel item)
    {
        var issues = new List<GraphValidationIssue>();
        var assetsById = assets.ToDictionary(asset => asset.Id, StringComparer.Ordinal);
        ValidateGraph(assets, assetsById, owner, item, issues);
        return issues;
    }

    private void ValidateGraph(
        IReadOnlyList<ContentAssetViewModel> assets,
        IReadOnlyDictionary<string, ContentAssetViewModel> assetsById,
        ContentAssetViewModel owner,
        GraphListItemViewModel item,
        List<GraphValidationIssue> issues)
    {
        EnsureGraphNodeNumbers(item.Graph, item.Kind);
        EnsureGraphToDoTargets(item.Graph);
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
        ValidateNodeNumbers(graphName, nodesById.Values, issues);
        ValidateConnections(graphName, item.Graph.Connections, nodesById, issues);
        ValidateToDoTargets(graphName, item.Graph.Connections, nodesById.Values, issues);
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

    private static void ValidateNodeNumbers(
        string graphName,
        IEnumerable<NodeBaseViewModel> nodes,
        List<GraphValidationIssue> issues)
    {
        var numberedNodes = nodes.Where(node => node.NodeKind != NodeKind.Reroute).ToList();
        foreach (var group in numberedNodes
                     .Where(node => !string.IsNullOrWhiteSpace(node.NodeNumber))
                     .GroupBy(node => node.NodeNumber, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            issues.Add(Error(graphName, $"节点编号重复：{group.Key}。"));
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

    private static void ValidateToDoTargets(
        string graphName,
        IReadOnlyList<ConnectionFileModel> connections,
        IEnumerable<NodeBaseViewModel> nodes,
        List<GraphValidationIssue> issues)
    {
        var candidates = nodes
            .Where(node => node.NodeKind != NodeKind.Reroute)
            .ToList();
        foreach (var toDo in candidates.OfType<ToDoNodeViewModel>())
        {
            if (IsInputConnected(connections, toDo.Id, "target_title") ||
                IsInputConnected(connections, toDo.Id, "target_number"))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(toDo.TargetNodeTitle) ||
                string.IsNullOrWhiteSpace(toDo.TargetNodeNumber))
            {
                issues.Add(Error(graphName, $"ToDo 节点缺少目标节点名或编号：{toDo.Title}。"));
                continue;
            }

            var matches = candidates
                .Where(node => string.Equals(node.Title, toDo.TargetNodeTitle, StringComparison.Ordinal) &&
                               string.Equals(node.NodeNumber, toDo.TargetNodeNumber, StringComparison.OrdinalIgnoreCase))
                .Take(2)
                .ToList();
            if (matches.Count == 0)
                issues.Add(Error(graphName, $"ToDo 目标不存在：{toDo.Title} -> {toDo.TargetNodeTitle} {toDo.TargetNodeNumber}。"));
            else if (matches.Count > 1)
                issues.Add(Error(graphName, $"ToDo 目标不唯一：{toDo.Title} -> {toDo.TargetNodeTitle} {toDo.TargetNodeNumber}。"));
            else if (matches[0].Id == toDo.Id)
                issues.Add(Error(graphName, $"ToDo 不能跳转到自身：{toDo.Title}。"));
        }
    }

    private static bool IsInputConnected(IReadOnlyList<ConnectionFileModel> connections, string nodeId, string pinName) =>
        connections.Any(connection => connection.TargetNodeId == nodeId && connection.TargetPinName == pinName);

    private static GraphValidationIssue Error(string graphName, string message) =>
        new(GraphValidationSeverity.Error, $"{graphName}: {message}");

    private static GraphValidationIssue Warning(string graphName, string message) =>
        new(GraphValidationSeverity.Warning, $"{graphName}: {message}");

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
