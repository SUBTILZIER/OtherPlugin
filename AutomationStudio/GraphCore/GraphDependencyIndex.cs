using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Services;

namespace AutomationStudioWpf.GraphCore;

public enum GraphCallKind
{
    Function,
    CustomEvent,
}

public sealed record GraphCallEdge(
    string SourceAssetId,
    string SourceGraphId,
    string SourceNodeId,
    GraphCallKind Kind,
    string TargetId,
    string? TargetAssetId,
    string? TargetGraphId);

public sealed record GraphFunctionTarget(
    string Id,
    string Name,
    string GroupName,
    string OwnerAssetId,
    ContentAssetKind OwnerKind,
    GraphSnapshot Graph);

public sealed record GraphCustomEventTarget(
    string Id,
    string Name,
    string GroupName,
    string OwnerAssetId,
    string EntryNodeId,
    IReadOnlyList<GraphParameterFileModel> Parameters,
    GraphSnapshot Graph);

public sealed record GraphReachabilityResult(
    IReadOnlySet<string> GraphIds,
    bool RequiresPython,
    IReadOnlyList<GraphDependencyIssue> Issues);

public sealed class GraphDependencyIndex
{
    private static readonly HashSet<string> PythonNodeTypeKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "find_image",
        "wait_image",
        "wait_image_disappear",
    };

    private readonly IReadOnlyDictionary<string, ContentAssetSnapshot> _assetsById;
    private readonly IReadOnlyDictionary<string, GraphSnapshot> _graphsById;
    private readonly IReadOnlyDictionary<string, GraphFunctionTarget> _functionsById;
    private readonly IReadOnlyDictionary<CustomEventKey, GraphCustomEventTarget> _customEventsByKey;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<GraphCallEdge>> _edgesBySourceGraphId;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<GraphCallEdge>> _edgesByTargetGraphId;
    private readonly IReadOnlyDictionary<CallTargetKey, IReadOnlyList<GraphCallEdge>> _edgesByTargetId;
    private readonly IReadOnlyDictionary<string, GraphSnapshot> _mainEventsByAssetId;

    internal GraphDependencyIndex(
        IReadOnlyDictionary<string, ContentAssetSnapshot> assetsById,
        IReadOnlyDictionary<string, GraphSnapshot> graphsById,
        IReadOnlyDictionary<string, GraphFunctionTarget> functionsById,
        IReadOnlyDictionary<CustomEventKey, GraphCustomEventTarget> customEventsByKey,
        IReadOnlyDictionary<string, IReadOnlyList<GraphCallEdge>> edgesBySourceGraphId,
        IReadOnlyDictionary<string, IReadOnlyList<GraphCallEdge>> edgesByTargetGraphId,
        IReadOnlyDictionary<CallTargetKey, IReadOnlyList<GraphCallEdge>> edgesByTargetId,
        IReadOnlyDictionary<string, GraphSnapshot> mainEventsByAssetId,
        IReadOnlyList<GraphCallEdge> callEdges,
        IReadOnlyList<GraphDependencyIssue> issues)
    {
        _assetsById = assetsById;
        _graphsById = graphsById;
        _functionsById = functionsById;
        _customEventsByKey = customEventsByKey;
        _edgesBySourceGraphId = edgesBySourceGraphId;
        _edgesByTargetGraphId = edgesByTargetGraphId;
        _edgesByTargetId = edgesByTargetId;
        _mainEventsByAssetId = mainEventsByAssetId;
        CallEdges = callEdges;
        Issues = issues;
    }

    public IReadOnlyList<GraphCallEdge> CallEdges { get; }

    public IReadOnlyList<GraphDependencyIssue> Issues { get; }

    public ContentAssetSnapshot? FindAsset(string? assetId) =>
        !string.IsNullOrWhiteSpace(assetId) && _assetsById.TryGetValue(assetId, out var asset) ? asset : null;

    public GraphSnapshot? FindGraph(string? graphId) =>
        !string.IsNullOrWhiteSpace(graphId) && _graphsById.TryGetValue(graphId, out var graph) ? graph : null;

    public GraphFunctionTarget? FindFunction(string? functionId) =>
        !string.IsNullOrWhiteSpace(functionId) && _functionsById.TryGetValue(functionId, out var function)
            ? function
            : null;

    public GraphSnapshot? GetMainEvent(string? assetId) =>
        !string.IsNullOrWhiteSpace(assetId) && _mainEventsByAssetId.TryGetValue(assetId, out var graph) ? graph : null;

    public IReadOnlyList<GraphCallEdge> GetCallers(string? targetGraphId) =>
        !string.IsNullOrWhiteSpace(targetGraphId) && _edgesByTargetGraphId.TryGetValue(targetGraphId, out var edges)
            ? edges
            : [];

    public IReadOnlyList<GraphCallEdge> GetFunctionCallers(string? functionId) =>
        !string.IsNullOrWhiteSpace(functionId) &&
        _edgesByTargetId.TryGetValue(new CallTargetKey(GraphCallKind.Function, functionId), out var edges)
            ? edges
            : [];

    public IReadOnlySet<string> GetAffectedCallerAssetIds(IEnumerable<string> targetGraphIds)
    {
        ArgumentNullException.ThrowIfNull(targetGraphIds);
        var affectedAssetIds = new HashSet<string>(StringComparer.Ordinal);
        var visitedGraphIds = new HashSet<string>(StringComparer.Ordinal);
        var pendingGraphIds = new Stack<string>(targetGraphIds.Where(id => !string.IsNullOrWhiteSpace(id)));

        while (pendingGraphIds.Count > 0)
        {
            string targetGraphId = pendingGraphIds.Pop();
            if (!visitedGraphIds.Add(targetGraphId))
                continue;

            IEnumerable<GraphCallEdge> callers = GetCallers(targetGraphId)
                .Concat(GetFunctionCallers(targetGraphId))
                .Distinct();
            foreach (GraphCallEdge caller in callers)
            {
                affectedAssetIds.Add(caller.SourceAssetId);
                if (!string.IsNullOrWhiteSpace(caller.SourceGraphId))
                    pendingGraphIds.Push(caller.SourceGraphId);
            }
        }

        return affectedAssetIds;
    }

    public IReadOnlyList<GraphFunctionTarget> ResolveFunctions(string? activeAssetId)
    {
        ContentAssetSnapshot? activeAsset = FindAsset(activeAssetId);
        var result = new List<GraphFunctionTarget>();
        if (activeAsset is not null)
        {
            result.AddRange(activeAsset.Functions
                .Select(graph => _functionsById.TryGetValue(graph.Id, out var target) ? target : null)
                .Where(target => target is not null)
                .Cast<GraphFunctionTarget>()
                .Select(target => target with
                {
                    GroupName = activeAsset.Kind == ContentAssetKind.Script ? "本脚本函数" : "本函数库",
                }));
        }

        result.AddRange(_functionsById.Values.Where(target =>
            target.OwnerAssetId != activeAssetId &&
            FindAsset(target.OwnerAssetId)?.Kind == ContentAssetKind.FunctionLibrary &&
            target.Graph.IsPublicToLibrary));

        return result
            .GroupBy(target => target.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList()
            .AsReadOnly();
    }

    public IReadOnlyList<GraphCustomEventTarget> ResolveCustomEvents(string? ownerAssetId)
    {
        if (FindAsset(ownerAssetId)?.Kind != ContentAssetKind.Script || string.IsNullOrWhiteSpace(ownerAssetId))
            return [];

        return _customEventsByKey
            .Where(pair => pair.Key.AssetId == ownerAssetId)
            .Select(pair => pair.Value)
            .ToList()
            .AsReadOnly();
    }

    public GraphReachabilityResult GetReachability(string assetId, string graphId)
    {
        var issues = new List<GraphDependencyIssue>();
        if (!_assetsById.ContainsKey(assetId))
        {
            issues.Add(Error($"asset:{assetId}", "入口资产不存在。"));
            return new GraphReachabilityResult(new HashSet<string>(), false, issues.AsReadOnly());
        }
        if (!_graphsById.TryGetValue(graphId, out var root) || !OwnsGraph(assetId, graphId))
        {
            issues.Add(Error($"graph:{assetId}/{graphId}", "入口图表不存在或不属于目标资产。"));
            return new GraphReachabilityResult(new HashSet<string>(), false, issues.AsReadOnly());
        }

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>();
        pending.Push(root.Id);
        bool requiresPython = false;
        while (pending.Count > 0)
        {
            string currentGraphId = pending.Pop();
            if (!visited.Add(currentGraphId) || !_graphsById.TryGetValue(currentGraphId, out var currentGraph))
                continue;

            requiresPython |= currentGraph.Nodes.Any(node => PythonNodeTypeKeys.Contains(node.NodeTypeKey));
            if (!_edgesBySourceGraphId.TryGetValue(currentGraphId, out var edges))
                continue;

            foreach (var edge in edges)
            {
                if (string.IsNullOrWhiteSpace(edge.TargetGraphId))
                {
                    issues.Add(Error(
                        $"graph:{edge.SourceAssetId}/{edge.SourceGraphId}/node:{edge.SourceNodeId}",
                        $"调用目标无效：{edge.TargetId}。"));
                    continue;
                }

                pending.Push(edge.TargetGraphId);
            }
        }

        return new GraphReachabilityResult(visited, requiresPython, issues.AsReadOnly());
    }

    public GraphReachabilityResult GetMainEventReachability(string assetId)
    {
        GraphSnapshot? main = GetMainEvent(assetId);
        if (main is not null)
            return GetReachability(assetId, main.Id);

        var issues = new[] { Error($"asset:{assetId}", "脚本没有主事件图。") };
        return new GraphReachabilityResult(new HashSet<string>(), false, issues);
    }

    private bool OwnsGraph(string assetId, string graphId) =>
        _assetsById.TryGetValue(assetId, out var asset) && asset.Graphs.Any(graph => graph.Id == graphId);

    private static GraphDependencyIssue Error(string scope, string message) =>
        new(GraphDependencyIssueSeverity.Error, scope, message);

    internal readonly record struct CustomEventKey(string AssetId, string EventId);

    internal readonly record struct CallTargetKey(GraphCallKind Kind, string TargetId);
}

internal sealed class GraphDependencyIndexBuilder
{
    public GraphDependencyIndex Build(GraphWorkspaceSnapshot workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var issues = workspace.Issues.ToList();
        var assetsById = UniqueById(workspace.Assets, asset => asset.Id);
        var graphOwners = workspace.Assets
            .SelectMany(asset => asset.Graphs.Select(graph => (Asset: asset, Graph: graph)))
            .ToList();
        var graphsById = UniqueById(graphOwners, item => item.Graph.Id)
            .ToDictionary(pair => pair.Key, pair => pair.Value.Graph, StringComparer.Ordinal);
        var functionsById = BuildFunctions(graphOwners);
        var customEvents = BuildCustomEvents(workspace.Assets, issues);
        var mainEvents = workspace.Assets
            .Where(asset => asset.Kind == ContentAssetKind.Script)
            .Select(asset => (Asset: asset, Main: asset.EventGraphs.FirstOrDefault(graph => graph.EntryRole == GraphEntryRole.MainEvent)))
            .Where(pair => pair.Main is not null && !string.IsNullOrWhiteSpace(pair.Asset.Id))
            .GroupBy(pair => pair.Asset.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Main!, StringComparer.Ordinal);

        var edges = BuildEdges(workspace.Assets, functionsById, customEvents, issues);
        var edgesByGraph = edges
            .GroupBy(edge => edge.SourceGraphId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<GraphCallEdge>)group.ToList().AsReadOnly(),
                StringComparer.Ordinal);
        var edgesByTargetGraph = edges
            .Where(edge => !string.IsNullOrWhiteSpace(edge.TargetGraphId))
            .GroupBy(edge => edge.TargetGraphId!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<GraphCallEdge>)group.ToList().AsReadOnly(),
                StringComparer.Ordinal);
        var edgesByTargetId = edges
            .Where(edge => !string.IsNullOrWhiteSpace(edge.TargetId))
            .GroupBy(edge => new GraphDependencyIndex.CallTargetKey(edge.Kind, edge.TargetId))
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<GraphCallEdge>)group.ToList().AsReadOnly());

        return new GraphDependencyIndex(
            assetsById,
            graphsById,
            functionsById,
            customEvents,
            edgesByGraph,
            edgesByTargetGraph,
            edgesByTargetId,
            mainEvents,
            edges.AsReadOnly(),
            issues.AsReadOnly());
    }

    private static IReadOnlyDictionary<string, GraphFunctionTarget> BuildFunctions(
        IReadOnlyList<(ContentAssetSnapshot Asset, GraphSnapshot Graph)> graphOwners)
    {
        return graphOwners
            .Where(item => item.Graph.Kind == GraphAssetKind.Function && !string.IsNullOrWhiteSpace(item.Graph.Id))
            .GroupBy(item => item.Graph.Id, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var item = group.Single();
                    return new GraphFunctionTarget(
                        item.Graph.Id,
                        item.Graph.Name,
                        item.Asset.Name,
                        item.Asset.Id,
                        item.Asset.Kind,
                        item.Graph);
                },
                StringComparer.Ordinal);
    }

    private static IReadOnlyDictionary<GraphDependencyIndex.CustomEventKey, GraphCustomEventTarget> BuildCustomEvents(
        IEnumerable<ContentAssetSnapshot> assets,
        ICollection<GraphDependencyIssue> issues)
    {
        var candidates = assets
            .Where(asset => asset.Kind == ContentAssetKind.Script)
            .SelectMany(asset => asset.EventGraphs.SelectMany(graph => graph.Nodes
                .Where(node => string.Equals(node.NodeTypeKey, "custom_event", StringComparison.OrdinalIgnoreCase))
                .Select(node => (Asset: asset, Graph: graph, Node: node, EventId: node.CustomEventId ?? string.Empty))))
            .ToList();
        var result = new Dictionary<GraphDependencyIndex.CustomEventKey, GraphCustomEventTarget>();
        foreach (var group in candidates.GroupBy(
                     item => new GraphDependencyIndex.CustomEventKey(item.Asset.Id, item.EventId)))
        {
            if (string.IsNullOrWhiteSpace(group.Key.EventId) || group.Count() > 1)
            {
                issues.Add(new GraphDependencyIssue(
                    GraphDependencyIssueSeverity.Error,
                    $"asset:{group.Key.AssetId}",
                    string.IsNullOrWhiteSpace(group.Key.EventId)
                        ? "存在空自定义事件 ID。"
                        : $"自定义事件 ID 重复：{group.Key.EventId}。"));
                continue;
            }

            var item = group.Single();
            result[group.Key] = new GraphCustomEventTarget(
                item.EventId,
                string.IsNullOrWhiteSpace(item.Node.Title) ? "自定义事件" : item.Node.Title,
                item.Graph.Name,
                item.Asset.Id,
                item.Node.Id,
                item.Node.Parameters.Select(parameter => parameter.ToMutableModel()).ToList().AsReadOnly(),
                item.Graph);
        }
        return result;
    }

    private static List<GraphCallEdge> BuildEdges(
        IEnumerable<ContentAssetSnapshot> assets,
        IReadOnlyDictionary<string, GraphFunctionTarget> functions,
        IReadOnlyDictionary<GraphDependencyIndex.CustomEventKey, GraphCustomEventTarget> customEvents,
        ICollection<GraphDependencyIssue> issues)
    {
        var edges = new List<GraphCallEdge>();
        foreach (var asset in assets)
        {
            foreach (var graph in asset.Graphs)
            {
                foreach (GraphNodeSnapshot node in graph.Nodes)
                {
                    if (string.Equals(node.NodeTypeKey, "function_call", StringComparison.OrdinalIgnoreCase))
                    {
                        string targetId = node.FunctionId ?? string.Empty;
                        GraphFunctionTarget? target = ResolveFunction(asset, targetId, functions);
                        edges.Add(new GraphCallEdge(
                            asset.Id,
                            graph.Id,
                            node.Id,
                            GraphCallKind.Function,
                            targetId,
                            target?.OwnerAssetId,
                            target?.Graph.Id));
                        if (target is null)
                            issues.Add(InvalidCallIssue(asset.Id, graph.Id, node.Id, "函数", targetId));
                    }
                    else if (string.Equals(node.NodeTypeKey, "custom_event_call", StringComparison.OrdinalIgnoreCase))
                    {
                        string targetId = node.CustomEventId ?? string.Empty;
                        customEvents.TryGetValue(new GraphDependencyIndex.CustomEventKey(asset.Id, targetId), out var target);
                        edges.Add(new GraphCallEdge(
                            asset.Id,
                            graph.Id,
                            node.Id,
                            GraphCallKind.CustomEvent,
                            targetId,
                            target?.OwnerAssetId,
                            target?.Graph.Id));
                        if (target is null)
                            issues.Add(InvalidCallIssue(asset.Id, graph.Id, node.Id, "自定义事件", targetId));
                    }
                }
            }
        }
        return edges;
    }

    private static GraphFunctionTarget? ResolveFunction(
        ContentAssetSnapshot sourceAsset,
        string targetId,
        IReadOnlyDictionary<string, GraphFunctionTarget> functions)
    {
        if (string.IsNullOrWhiteSpace(targetId) || !functions.TryGetValue(targetId, out var target))
            return null;
        if (target.OwnerAssetId == sourceAsset.Id)
            return target;

        return target.OwnerKind == ContentAssetKind.FunctionLibrary && target.Graph.IsPublicToLibrary
            ? target
            : null;
    }

    private static GraphDependencyIssue InvalidCallIssue(
        string assetId,
        string graphId,
        string nodeId,
        string kind,
        string targetId) => new(
        GraphDependencyIssueSeverity.Error,
        $"graph:{assetId}/{graphId}/node:{nodeId}",
        string.IsNullOrWhiteSpace(targetId) ? $"{kind}调用缺少目标 ID。" : $"{kind}调用目标不存在或不可见：{targetId}。");

    private static Dictionary<string, T> UniqueById<T>(IEnumerable<T> values, Func<T, string> idSelector) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(idSelector(value)))
            .GroupBy(idSelector, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);
}
