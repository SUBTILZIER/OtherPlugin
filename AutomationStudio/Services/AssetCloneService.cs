using System.Collections.ObjectModel;
using System.Text.Json;
using AutomationStudioWpf.Graph;

namespace AutomationStudioWpf.Services;

internal sealed class AssetCloneService
{
    private static readonly JsonSerializerOptions JsonOptions = new();

    public AssetCloneResult CloneTree(
        IEnumerable<ContentAssetViewModel> roots,
        IReadOnlyCollection<ContentAssetViewModel> allAssets,
        string? targetFolderId)
    {
        var rootList = roots.DistinctBy(asset => asset.Id).ToList();
        var sourceAssets = CollectTrees(rootList, allAssets);
        var duplicateGraphId = sourceAssets
            .SelectMany(asset => asset.EventGraphs.Concat(asset.Functions))
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1);
        if (duplicateGraphId is not null)
            throw new InvalidOperationException($"复制已取消：源资产包含空或重复图表 ID：{duplicateGraphId.Key}。");
        var clonesBySourceId = new Dictionary<string, ContentAssetViewModel>(StringComparer.Ordinal);
        var graphIdMap = sourceAssets
            .SelectMany(asset => asset.EventGraphs.Concat(asset.Functions))
            .ToDictionary(item => item.Id, _ => Guid.NewGuid().ToString("N"), StringComparer.Ordinal);
        var reservedNames = allAssets
            .GroupBy(asset => ParentKey(asset.ParentFolderId), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(asset => asset.Name).ToHashSet(StringComparer.OrdinalIgnoreCase),
                StringComparer.Ordinal);

        foreach (var source in sourceAssets)
        {
            bool isRoot = rootList.Any(root => root.Id == source.Id);
            string? parentId = isRoot
                ? targetFolderId
                : source.ParentFolderId is not null && clonesBySourceId.TryGetValue(source.ParentFolderId, out var parentClone)
                    ? parentClone.Id
                    : targetFolderId;
            string baseName = isRoot ? $"{source.Name}_Copy" : source.Name;
            string name = ReserveUniqueName(baseName, parentId, reservedNames);
            var clone = new ContentAssetViewModel
            {
                Kind = source.Kind,
                Name = name,
                ParentFolderId = parentId,
                RunSettings = source.RunSettings.Clone(),
                IsScriptEnabled = false,
                IsDirty = true,
            };
            clonesBySourceId[source.Id] = clone;
        }

        foreach (var source in sourceAssets)
        {
            var clone = clonesBySourceId[source.Id];
            clone.EventGraphs = new ObservableCollection<GraphListItemViewModel>(
                source.EventGraphs.Select(item => CloneGraphItem(item, graphIdMap)));
            clone.Functions = new ObservableCollection<GraphListItemViewModel>(
                source.Functions.Select(item => CloneGraphItem(item, graphIdMap)));
            GraphStructureNormalizer.NormalizeContentAsset(clone);
        }

        return new AssetCloneResult(
            sourceAssets.Select(source => clonesBySourceId[source.Id]).ToList(),
            rootList.Select(root => clonesBySourceId[root.Id]).ToList());
    }

    private static List<ContentAssetViewModel> CollectTrees(
        IReadOnlyList<ContentAssetViewModel> roots,
        IReadOnlyCollection<ContentAssetViewModel> allAssets)
    {
        var result = new List<ContentAssetViewModel>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<ContentAssetViewModel>(roots);
        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            if (!visited.Add(current.Id))
                continue;
            result.Add(current);
            foreach (var child in allAssets.Where(asset => asset.ParentFolderId == current.Id))
                pending.Enqueue(child);
        }
        return result;
    }

    private static GraphListItemViewModel CloneGraphItem(
        GraphListItemViewModel source,
        IReadOnlyDictionary<string, string> graphIdMap)
    {
        var graph = JsonSerializer.Deserialize<GraphFileModel>(
            JsonSerializer.Serialize(source.Graph, JsonOptions), JsonOptions) ?? new GraphFileModel();
        foreach (var node in graph.Nodes)
        {
            if (!string.IsNullOrWhiteSpace(node.FunctionId) && graphIdMap.TryGetValue(node.FunctionId, out var mappedId))
                node.FunctionId = mappedId;
        }

        return new GraphListItemViewModel
        {
            Id = graphIdMap[source.Id],
            Kind = source.Kind,
            Name = source.Name,
            Graph = graph,
            EntryRole = source.EntryRole,
            IsPublicToLibrary = source.IsPublicToLibrary,
            IsDirty = true,
            IsCompileDirty = true,
        };
    }

    private static string ReserveUniqueName(
        string baseName,
        string? parentId,
        IDictionary<string, HashSet<string>> reservedNames)
    {
        string parentKey = ParentKey(parentId);
        if (!reservedNames.TryGetValue(parentKey, out var names))
        {
            names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            reservedNames[parentKey] = names;
        }
        if (names.Add(baseName))
            return baseName;

        int suffix = 1;
        string candidate;
        do { candidate = $"{baseName}{suffix++}"; } while (!names.Add(candidate));
        return candidate;
    }

    private static string ParentKey(string? parentId) => parentId ?? "<root>";
}

internal sealed record AssetCloneResult(
    IReadOnlyList<ContentAssetViewModel> AllClones,
    IReadOnlyList<ContentAssetViewModel> RootClones);
