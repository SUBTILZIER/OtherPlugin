using AutomationStudioWpf.Graph;
using AutomationStudioWpf.GraphCore;

namespace AutomationStudioWpf.Services;

internal sealed record GraphPreparationResult(
    IReadOnlySet<string> ChangedAssetIds,
    IReadOnlyList<string> RepairMessages)
{
    public static GraphPreparationResult Empty { get; } = new(
        new HashSet<string>(StringComparer.Ordinal),
        Array.Empty<string>());
}

internal sealed class GraphPreparationService
{
    public GraphPreparationResult PrepareWorkspace(IReadOnlyList<ContentAssetViewModel> assets)
    {
        var changedAssetIds = new HashSet<string>(StringComparer.Ordinal);
        var messages = new List<string>();
        foreach (ContentAssetViewModel asset in assets.Where(asset => asset.Kind != ContentAssetKind.Folder))
            PrepareAssetCore(asset, changedAssetIds, messages);
        return CreateResult(changedAssetIds, messages);
    }

    public GraphPreparationResult PrepareAsset(ContentAssetViewModel asset)
    {
        var changedAssetIds = new HashSet<string>(StringComparer.Ordinal);
        var messages = new List<string>();
        PrepareAssetCore(asset, changedAssetIds, messages);
        return CreateResult(changedAssetIds, messages);
    }

    public GraphPreparationResult PrepareGraph(ContentAssetViewModel owner, GraphListItemViewModel graph)
    {
        var changedAssetIds = new HashSet<string>(StringComparer.Ordinal);
        var messages = new List<string>();

        if (GraphStructureNormalizer.NormalizeContentAsset(owner))
        {
            MarkAssetChanged(owner, changedAssetIds);
            messages.Add($"已归一化资产结构：{owner.Name}。");
        }

        (bool structureChanged, bool metadataChanged) = PrepareGraphCore(graph);
        if (structureChanged)
        {
            MarkGraphChanged(owner, graph, changedAssetIds);
            messages.Add($"已修复图表结构或静态元数据：{owner.Name}/{graph.Name}。");
        }
        else if (metadataChanged)
        {
            MarkGraphMetadataChanged(owner, graph, changedAssetIds);
            messages.Add($"已更新图表显示元数据：{owner.Name}/{graph.Name}。");
        }

        return CreateResult(changedAssetIds, messages);
    }

    private static void PrepareAssetCore(
        ContentAssetViewModel asset,
        ISet<string> changedAssetIds,
        ICollection<string> messages)
    {
        if (GraphStructureNormalizer.NormalizeContentAsset(asset))
        {
            MarkAssetChanged(asset, changedAssetIds);
            messages.Add($"已归一化资产结构：{asset.Name}。");
        }

        foreach (GraphListItemViewModel graph in GetCompilableGraphs(asset))
        {
            (bool structureChanged, bool metadataChanged) = PrepareGraphCore(graph);
            if (!structureChanged && !metadataChanged)
                continue;

            if (structureChanged)
            {
                MarkGraphChanged(asset, graph, changedAssetIds);
                messages.Add($"已修复图表结构：{asset.Name}/{graph.Name}。");
            }
            else
            {
                MarkGraphMetadataChanged(asset, graph, changedAssetIds);
                messages.Add($"已更新图表显示元数据：{asset.Name}/{graph.Name}。");
            }
        }
    }

    private static (bool StructureChanged, bool MetadataChanged) PrepareGraphCore(GraphListItemViewModel item)
    {
        bool structureChanged = EnsureAuxiliaryEventGraph(item);
        bool metadataChanged = EnsureGraphNodeNumbers(item.Graph, item.Kind);
        metadataChanged |= EnsureGraphToDoTargets(item.Graph);
        return (structureChanged, metadataChanged);
    }

    internal static IEnumerable<GraphListItemViewModel> GetCompilableGraphs(ContentAssetViewModel asset) => asset.Kind switch
    {
        ContentAssetKind.Script => asset.EventGraphs.Concat(asset.Functions),
        ContentAssetKind.FunctionLibrary => asset.Functions,
        _ => Enumerable.Empty<GraphListItemViewModel>(),
    };

    private static bool EnsureAuxiliaryEventGraph(GraphListItemViewModel item)
    {
        if (item.Kind != GraphAssetKind.EventGraph || item.EntryRole != GraphEntryRole.AuxiliaryEvent)
            return false;

        var startNodeIds = item.Graph.Nodes
            .Where(node => string.Equals(node.NodeTypeKey, "start", StringComparison.OrdinalIgnoreCase))
            .Select(node => node.Id)
            .ToHashSet(StringComparer.Ordinal);
        if (startNodeIds.Count == 0)
            return false;

        item.Graph.Nodes.RemoveAll(node => startNodeIds.Contains(node.Id));
        item.Graph.Connections.RemoveAll(connection =>
            startNodeIds.Contains(connection.SourceNodeId) ||
            startNodeIds.Contains(connection.TargetNodeId));
        return true;
    }

    private static bool EnsureGraphNodeNumbers(GraphFileModel graph, GraphAssetKind kind)
    {
        string prefix = kind == GraphAssetKind.Function ? "Fun" : "N";
        var used = new HashSet<int>();
        var pending = new List<NodeFileModel>();
        bool changed = false;

        foreach (NodeFileModel node in graph.Nodes)
        {
            if (!ShouldAssignNodeNumber(node))
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
        foreach (NodeFileModel node in pending)
        {
            while (used.Contains(next))
                next++;
            string number = $"{prefix}{next:000}";
            if (!string.Equals(node.NodeNumber, number, StringComparison.Ordinal))
            {
                node.NodeNumber = number;
                changed = true;
            }
            used.Add(next);
        }

        return changed;
    }

    private static bool EnsureGraphToDoTargets(GraphFileModel graph)
    {
        var nodesById = graph.Nodes
            .Where(ShouldAssignNodeNumber)
            .Where(node => !string.IsNullOrWhiteSpace(node.Id))
            .GroupBy(node => node.Id, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);

        bool changed = false;
        foreach (NodeFileModel node in graph.Nodes.Where(IsToDoNode))
        {
            if (string.IsNullOrWhiteSpace(node.TargetNodeId) ||
                !nodesById.TryGetValue(node.TargetNodeId, out NodeFileModel? target) ||
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

    private static bool ShouldAssignNodeNumber(NodeFileModel node) =>
        NodeDescriptorCatalog.FromTypeKey(node.NodeTypeKey) is { } kind && NodeTraits.ShouldAssignNodeNumber(kind);

    private static bool IsToDoNode(NodeFileModel node) =>
        string.Equals(node.NodeTypeKey, "todo", StringComparison.OrdinalIgnoreCase);

    private static int? ParseNodeOrdinal(string? nodeNumber, string prefix)
    {
        if (string.IsNullOrWhiteSpace(nodeNumber) ||
            !nodeNumber.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return int.TryParse(nodeNumber[prefix.Length..], out int ordinal) && ordinal > 0
            ? ordinal
            : null;
    }

    private static void MarkAssetChanged(ContentAssetViewModel asset, ISet<string> changedAssetIds)
    {
        asset.IsDirty = true;
        changedAssetIds.Add(asset.Id);
        foreach (GraphListItemViewModel graph in GetCompilableGraphs(asset))
        {
            graph.IsDirty = true;
            graph.IsCompileDirty = true;
        }
    }

    private static void MarkGraphChanged(
        ContentAssetViewModel asset,
        GraphListItemViewModel graph,
        ISet<string> changedAssetIds)
    {
        asset.IsDirty = true;
        graph.IsDirty = true;
        graph.IsCompileDirty = true;
        changedAssetIds.Add(asset.Id);
    }

    private static void MarkGraphMetadataChanged(
        ContentAssetViewModel asset,
        GraphListItemViewModel graph,
        ISet<string> changedAssetIds)
    {
        // Node numbers and ToDo labels are persisted editor metadata, not executable graph logic.
        asset.IsDirty = true;
        graph.IsDirty = true;
        changedAssetIds.Add(asset.Id);
    }

    private static GraphPreparationResult CreateResult(
        HashSet<string> changedAssetIds,
        List<string> messages) =>
        new(changedAssetIds, messages.AsReadOnly());
}
