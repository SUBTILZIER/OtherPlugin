using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Services;

namespace AutomationStudioWpf;

internal sealed class ContentBrowserIndex
{
    public ContentBrowserIndex(IReadOnlyList<ContentAssetViewModel> items)
    {
        AssetById = items.ToDictionary(item => item.Id, StringComparer.Ordinal);
        FolderChildrenByParent = items.Where(item => item.IsFolder).ToLookup(item => item.ParentFolderId, StringComparer.Ordinal);
        ChildrenByParent = items.ToLookup(item => item.ParentFolderId, StringComparer.Ordinal);

        var pathCache = new Dictionary<string, string>(StringComparer.Ordinal);
        SearchEntries = items
            .Select(item =>
            {
                string path = GetContentAssetPath(item, pathCache);
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
        GetContentAssetPath(item, new Dictionary<string, string>(StringComparer.Ordinal));

    public bool IsInScope(ContentAssetViewModel item, string? currentFolderId)
    {
        if (currentFolderId is null)
            return true;

        string? parentId = item.ParentFolderId;
        while (!string.IsNullOrWhiteSpace(parentId))
        {
            if (string.Equals(parentId, currentFolderId, StringComparison.Ordinal))
                return true;

            parentId = AssetById.TryGetValue(parentId, out var parent) ? parent.ParentFolderId : null;
        }

        return false;
    }

    private string GetContentAssetPath(ContentAssetViewModel item, Dictionary<string, string> pathCache)
    {
        if (pathCache.TryGetValue(item.Id, out var cached))
            return cached;

        string path = item.ParentFolderId is null
            ? item.Name
            : AssetById.TryGetValue(item.ParentFolderId, out var parent) && parent is not null
                ? $"{GetContentAssetPath(parent, pathCache)}/{item.Name}"
                : item.Name;
        pathCache[item.Id] = path;
        return path;
    }
}

internal sealed record ContentAssetSearchEntry(
    ContentAssetViewModel Asset,
    string Path,
    string SearchableText);
