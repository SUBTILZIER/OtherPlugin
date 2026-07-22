using AutomationStudioWpf.Graph;
using AutomationStudioWpf.GraphCore;

namespace AutomationStudioWpf.Services;

internal sealed class GraphValidationService
{
    public IReadOnlyList<GraphValidationIssue> ValidateWorkspace(
        GraphWorkspaceSnapshot workspace,
        GraphDependencyIndex dependencyIndex)
    {
        var issues = CreateBaseIssues(workspace);
        var assetsById = BuildAssetLookup(workspace.Assets);
        foreach (ContentAssetSnapshot asset in workspace.Assets.Where(asset => asset.Kind != ContentAssetKind.Folder))
        {
            ValidateCustomEventIds(asset, assetsById, issues);
            foreach (GraphSnapshot graph in asset.Graphs)
                ValidateGraph(asset, graph, assetsById, dependencyIndex, issues);
        }
        return issues.AsReadOnly();
    }

    public IReadOnlyList<GraphValidationIssue> ValidateAsset(
        GraphWorkspaceSnapshot workspace,
        string assetId,
        GraphDependencyIndex dependencyIndex) =>
        ValidateAssets(workspace, [assetId], dependencyIndex);

    public IReadOnlyList<GraphValidationIssue> ValidateAssets(
        GraphWorkspaceSnapshot workspace,
        IEnumerable<string> assetIds,
        GraphDependencyIndex dependencyIndex)
    {
        var selectedAssetIds = assetIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);
        var issues = CreateBaseIssues(workspace, selectedAssetIds);
        var assetsById = BuildAssetLookup(workspace.Assets);
        foreach (string missingAssetId in selectedAssetIds.Where(id => !assetsById.ContainsKey(id)))
            issues.Add(Error("content", $"编译目标资产不存在：{missingAssetId}。"));

        foreach (ContentAssetSnapshot asset in workspace.Assets.Where(item => selectedAssetIds.Contains(item.Id)))
        {
            ValidateCustomEventIds(asset, assetsById, issues);
            foreach (GraphSnapshot graph in asset.Graphs)
                ValidateGraph(asset, graph, assetsById, dependencyIndex, issues);
        }
        return issues.AsReadOnly();
    }

    public IReadOnlyList<GraphValidationIssue> ValidateGraph(
        GraphWorkspaceSnapshot workspace,
        string assetId,
        string graphId,
        GraphDependencyIndex dependencyIndex)
    {
        var issues = CreateBaseIssues(
            workspace,
            new HashSet<string>(StringComparer.Ordinal) { assetId },
            new HashSet<string>(StringComparer.Ordinal) { graphId });
        var assetsById = BuildAssetLookup(workspace.Assets);
        ContentAssetSnapshot? asset = workspace.Assets.FirstOrDefault(item => item.Id == assetId);
        GraphSnapshot? graph = asset?.Graphs.FirstOrDefault(item => item.Id == graphId);
        if (asset is null || graph is null)
        {
            issues.Add(Error("content", $"编译目标图表不存在：{assetId}/{graphId}。"));
            return issues.AsReadOnly();
        }

        ValidateCustomEventIds(asset, assetsById, issues);
        ValidateGraph(asset, graph, assetsById, dependencyIndex, issues);
        return issues.AsReadOnly();
    }

    private static List<GraphValidationIssue> CreateBaseIssues(
        GraphWorkspaceSnapshot workspace,
        IReadOnlySet<string>? assetIds = null,
        IReadOnlySet<string>? graphIds = null) =>
        workspace.Issues
            .Where(issue => IsIssueInScope(issue.Scope, assetIds, graphIds))
            .Select(issue => new GraphValidationIssue(
                issue.Severity == GraphDependencyIssueSeverity.Error
                    ? GraphValidationSeverity.Error
                    : GraphValidationSeverity.Warning,
                $"{issue.Scope}: {issue.Message}"))
            .ToList();

    private static bool IsIssueInScope(
        string scope,
        IReadOnlySet<string>? assetIds,
        IReadOnlySet<string>? graphIds)
    {
        if (assetIds is null || scope == "workspace")
            return true;

        foreach (string assetId in assetIds)
        {
            if (string.Equals(scope, $"asset:{assetId}", StringComparison.Ordinal))
                return true;
            string graphPrefix = $"graph:{assetId}/";
            if (!scope.StartsWith(graphPrefix, StringComparison.Ordinal))
                continue;
            if (graphIds is null)
                return true;

            string remainder = scope[graphPrefix.Length..];
            string graphId = remainder.Split('/', 2)[0];
            if (graphIds.Contains(graphId))
                return true;
        }

        return false;
    }

    private static IReadOnlyDictionary<string, ContentAssetSnapshot> BuildAssetLookup(
        IEnumerable<ContentAssetSnapshot> assets) =>
        assets
            .Where(asset => !string.IsNullOrWhiteSpace(asset.Id))
            .GroupBy(asset => asset.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

    private static void ValidateCustomEventIds(
        ContentAssetSnapshot owner,
        IReadOnlyDictionary<string, ContentAssetSnapshot> assetsById,
        ICollection<GraphValidationIssue> issues)
    {
        if (owner.Kind != ContentAssetKind.Script)
            return;

        foreach (var group in owner.EventGraphs
                     .SelectMany(item => item.ToMutableModel().Nodes)
                     .Where(node => string.Equals(node.NodeTypeKey, "custom_event", StringComparison.OrdinalIgnoreCase))
                     .GroupBy(node => node.CustomEventId ?? string.Empty, StringComparer.Ordinal)
                     .Where(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1))
        {
            issues.Add(Error(
                BuildContentPath(owner, assetsById),
                string.IsNullOrWhiteSpace(group.Key)
                    ? "存在空自定义事件 ID。"
                    : $"自定义事件 ID 重复：{group.Key}。"));
        }
    }

    private static void ValidateGraph(
        ContentAssetSnapshot owner,
        GraphSnapshot item,
        IReadOnlyDictionary<string, ContentAssetSnapshot> assetsById,
        GraphDependencyIndex dependencyIndex,
        ICollection<GraphValidationIssue> issues)
    {
        GraphFileModel graph = item.ToMutableModel();
        string graphName = $"{BuildContentPath(owner, assetsById)}/{item.Name}";
        var nodesById = new Dictionary<string, NodeBaseViewModel>(StringComparer.Ordinal);
        var rawNodeIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (NodeFileModel fileNode in graph.Nodes)
        {
            if (string.IsNullOrWhiteSpace(fileNode.Id) || !rawNodeIds.Add(fileNode.Id))
                continue;

            NodeBaseViewModel? node = NodeSerializer.FromFileModel(fileNode);
            if (node is null)
            {
                issues.Add(Error(graphName, $"未知节点类型：{fileNode.NodeTypeKey}。"));
                continue;
            }
            nodesById.TryAdd(fileNode.Id, node);
        }

        ValidateGraphEntrypoints(
            graphName,
            item.Kind,
            item.EntryRole ?? GraphEntryRole.MainEvent,
            nodesById.Values,
            issues);
        ValidateNodeNumbers(graphName, nodesById.Values, issues);
        ValidateConnections(graphName, graph.Connections, nodesById, issues);
        ValidateToDoTargets(graphName, graph.Connections, nodesById.Values, issues);
        ValidateCallReferences(dependencyIndex, owner.Id, graph, graphName, issues);
    }

    private static void ValidateGraphEntrypoints(
        string graphName,
        GraphAssetKind kind,
        GraphEntryRole entryRole,
        IEnumerable<NodeBaseViewModel> nodes,
        ICollection<GraphValidationIssue> issues)
    {
        var nodeList = nodes.ToList();
        int Count(NodeKind nodeKind) => nodeList.Count(node => node.NodeKind == nodeKind);

        if (kind == GraphAssetKind.EventGraph)
        {
            if (entryRole == GraphEntryRole.MainEvent && Count(NodeKind.Start) != 1)
                issues.Add(Error(graphName, "事件图必须有且只有一个开始节点。"));
            if (entryRole == GraphEntryRole.AuxiliaryEvent && Count(NodeKind.Start) != 0)
                issues.Add(Error(graphName, "辅助事件图不能包含开始节点。"));
            return;
        }

        if (Count(NodeKind.FunctionEntry) != 1)
            issues.Add(Error(graphName, "函数图必须有且只有一个函数开始节点。"));
        if (Count(NodeKind.FunctionReturn) != 1)
            issues.Add(Error(graphName, "函数图必须有且只有一个函数返回节点。"));
    }

    private static void ValidateNodeNumbers(
        string graphName,
        IEnumerable<NodeBaseViewModel> nodes,
        ICollection<GraphValidationIssue> issues)
    {
        foreach (var group in nodes
                     .Where(node => NodeTraits.ShouldAssignNodeNumber(node.NodeKind))
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
        ICollection<GraphValidationIssue> issues)
    {
        var validConnections = new List<(ConnectionFileModel File, PinKind SourceKind, PinKind TargetKind)>();
        foreach (ConnectionFileModel connection in connections)
        {
            if (!nodesById.TryGetValue(connection.SourceNodeId, out NodeBaseViewModel? sourceNode))
            {
                issues.Add(Error(graphName, $"连线源节点不存在：{connection.SourceNodeId}。"));
                continue;
            }
            if (!nodesById.TryGetValue(connection.TargetNodeId, out NodeBaseViewModel? targetNode))
            {
                issues.Add(Error(graphName, $"连线目标节点不存在：{connection.TargetNodeId}。"));
                continue;
            }

            PinViewModel? sourcePin = sourceNode.OutputPins.FirstOrDefault(pin => pin.Name == connection.SourcePinName);
            PinViewModel? targetPin = targetNode.InputPins.FirstOrDefault(pin => pin.Name == connection.TargetPinName);
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

    private static void ValidateCallReferences(
        GraphDependencyIndex dependencyIndex,
        string ownerAssetId,
        GraphFileModel graph,
        string graphName,
        ICollection<GraphValidationIssue> issues)
    {
        var functions = dependencyIndex.ResolveFunctions(ownerAssetId)
            .Select(item => item.Id)
            .ToHashSet(StringComparer.Ordinal);
        var customEvents = dependencyIndex.ResolveCustomEvents(ownerAssetId)
            .Select(item => item.Id)
            .ToHashSet(StringComparer.Ordinal);

        foreach (NodeFileModel node in graph.Nodes)
        {
            if (node.NodeTypeKey == "function_call" &&
                (string.IsNullOrWhiteSpace(node.FunctionId) || !functions.Contains(node.FunctionId)))
            {
                issues.Add(Error(graphName, $"函数调用不可用或未公开：{node.Title}。"));
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
        ICollection<GraphValidationIssue> issues)
    {
        var candidates = nodes.Where(node => NodeTraits.IsToDoTarget(node.NodeKind)).ToList();
        foreach (ToDoNodeViewModel toDo in candidates.OfType<ToDoNodeViewModel>())
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

    private static bool IsInputConnected(
        IReadOnlyList<ConnectionFileModel> connections,
        string nodeId,
        string pinName) =>
        connections.Any(connection => connection.TargetNodeId == nodeId && connection.TargetPinName == pinName);

    private static GraphValidationIssue Error(string graphName, string message) =>
        new(GraphValidationSeverity.Error, $"{graphName}: {message}");

    private static string BuildContentPath(
        ContentAssetSnapshot asset,
        IReadOnlyDictionary<string, ContentAssetSnapshot> assetsById)
    {
        var segments = new Stack<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        ContentAssetSnapshot? current = asset;
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
        string.IsNullOrWhiteSpace(value) ? "Unnamed" : value.Replace('\\', '/').Trim('/');
}
