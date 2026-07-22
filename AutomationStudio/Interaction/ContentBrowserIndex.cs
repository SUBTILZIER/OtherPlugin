using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Services;
using AutomationStudioWpf.Logging;

namespace AutomationStudioWpf;

internal sealed class ContentBrowserIndex
{
    public ContentBrowserIndex(IReadOnlyList<ContentAssetViewModel> items)
    {
        var duplicateIds = items
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();
        if (duplicateIds.Count > 0)
            Logger.Error($"内容资产 ID 重复，索引暂时保留首项：{string.Join(", ", duplicateIds)}");
        AssetById = items
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        FolderChildrenByParent = items.Where(item => item.IsFolder).ToLookup(item => item.ParentFolderId, StringComparer.Ordinal);
        ChildrenByParent = items.ToLookup(item => item.ParentFolderId, StringComparer.Ordinal);

        var pathCache = new Dictionary<string, string>(StringComparer.Ordinal);
        SearchEntries = items
            .Select(item =>
            {
                string path = GetContentAssetPath(item, pathCache, new HashSet<string>(StringComparer.Ordinal));
                string searchable = $"{item.Name} {item.DisplayName} {item.Kind} {path}";
                return new ContentAssetSearchEntry(item, path, searchable);
            })
            .ToList();
    }

    public IReadOnlyDictionary<string, ContentAssetViewModel> AssetById { get; }
    public ILookup<string?, ContentAssetViewModel> FolderChildrenByParent { get; }
    public ILookup<string?, ContentAssetViewModel> ChildrenByParent { get; }
    public IReadOnlyList<ContentAssetSearchEntry> SearchEntries { get; }

    public string GetContentAssetPath(ContentAssetViewModel item) =>
        GetContentAssetPath(item, new Dictionary<string, string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal));

    public bool IsInScope(ContentAssetViewModel item, string? currentFolderId)
    {
        if (currentFolderId is null)
            return true;

        string? parentId = item.ParentFolderId;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (!string.IsNullOrWhiteSpace(parentId))
        {
            if (!visited.Add(parentId))
                return false;
            if (string.Equals(parentId, currentFolderId, StringComparison.Ordinal))
                return true;

            parentId = AssetById.TryGetValue(parentId, out var parent) ? parent.ParentFolderId : null;
        }

        return false;
    }

    private string GetContentAssetPath(
        ContentAssetViewModel item,
        Dictionary<string, string> pathCache,
        HashSet<string> visiting)
    {
        if (pathCache.TryGetValue(item.Id, out var cached))
            return cached;
        if (!visiting.Add(item.Id))
        {
            Logger.Error($"内容目录存在循环父级关系：{item.Name} ({item.Id})");
            return $"[目录循环]/{item.Name}";
        }

        string path = item.ParentFolderId is null
            ? item.Name
            : AssetById.TryGetValue(item.ParentFolderId, out var parent) && parent is not null
                ? $"{GetContentAssetPath(parent, pathCache, visiting)}/{item.Name}"
                : item.Name;
        visiting.Remove(item.Id);
        pathCache[item.Id] = path;
        return path;
    }
}

internal sealed record ContentAssetSearchEntry(
    ContentAssetViewModel Asset,
    string Path,
    string SearchableText);
