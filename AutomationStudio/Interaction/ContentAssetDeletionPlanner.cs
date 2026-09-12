using AutomationStudioWpf.Services;

namespace AutomationStudioWpf.Interaction;

public sealed record ContentAssetDeletionPlan(
    IReadOnlySet<string> DeleteIds,
    IReadOnlyDictionary<string, string?> ReparentById);

/// <summary>
/// Computes content deletion without mutating the asset collection. Keeping this
/// separate from WPF commands makes folder semantics testable and consistent
/// across context menus, keyboard shortcuts, and multi-select actions.
/// </summary>
public static class ContentAssetDeletionPlanner
{
    public static ContentAssetDeletionPlan Build(
        IEnumerable<ContentAssetViewModel> allAssets,
        IEnumerable<ContentAssetViewModel> topLevelTargets,
        bool deleteFolderContents)
    {
        var assets = allAssets
            .Where(asset => asset is not null)
            .GroupBy(asset => asset.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
        var byId = assets.ToDictionary(asset => asset.Id, StringComparer.Ordinal);
        var byParent = assets.ToLookup(asset => asset.ParentFolderId, StringComparer.Ordinal);
        var selectedTargets = topLevelTargets
            .Where(target => target is not null)
            .GroupBy(target => target.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
        var selectedIds = selectedTargets.Select(target => target.Id).ToHashSet(StringComparer.Ordinal);
        var targets = selectedTargets
            .Where(target => !HasSelectedAncestor(target, selectedIds, byId))
            .ToList();

        var deleteIds = new HashSet<string>(StringComparer.Ordinal);
        var reparentById = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var target in targets)
        {
            if (deleteFolderContents && target.IsFolder)
                AddDescendants(target, byParent, deleteIds);
            else
                deleteIds.Add(target.Id);

            if (!deleteFolderContents && target.IsFolder)
            {
                foreach (var child in byParent[target.Id])
                    reparentById[child.Id] = target.ParentFolderId;
            }
        }

        return new ContentAssetDeletionPlan(deleteIds, reparentById);
    }

    private static bool HasSelectedAncestor(
        ContentAssetViewModel asset,
        IReadOnlySet<string> selectedIds,
        IReadOnlyDictionary<string, ContentAssetViewModel> byId)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        string? parentId = asset.ParentFolderId;
        while (parentId is not null && visited.Add(parentId))
        {
            if (string.Equals(parentId, asset.Id, StringComparison.Ordinal))
                break;
            if (selectedIds.Contains(parentId))
                return true;
            if (!byId.TryGetValue(parentId, out var parent))
                break;
            parentId = parent.ParentFolderId;
        }

        return false;
    }

    private static void AddDescendants(
        ContentAssetViewModel root,
        ILookup<string?, ContentAssetViewModel> byParent,
        ISet<string> deleteIds)
    {
        var pending = new Stack<ContentAssetViewModel>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!deleteIds.Add(current.Id))
                continue;

            foreach (var child in byParent[current.Id])
                pending.Push(child);
        }
    }
}
