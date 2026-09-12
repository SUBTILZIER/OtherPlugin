using System.Windows;
using AutomationStudioWpf.Interaction;
using AutomationStudioWpf.Services;

namespace AutomationStudioWpf;

public partial class MainWindow
{
    private bool DeleteContentAssetsWithPolicy(IReadOnlyCollection<ContentAssetViewModel> requestedTargets)
    {
        var targets = GetTopLevelContentAssets(requestedTargets);
        if (targets.Count == 0)
            return false;

        bool hasNonEmptyFolder = targets.Any(target =>
            target.IsFolder && ContentBrowserItems.Any(item => item.ParentFolderId == target.Id));
        bool deleteFolderContents;

        if (hasNonEmptyFolder)
        {
            string folderNames = string.Join("、", targets
                .Where(target => target.IsFolder && ContentBrowserItems.Any(item => item.ParentFolderId == target.Id))
                .Select(target => target.Name));
            var result = ThemedDialog.ShowCustom(
                this,
                $"文件夹“{folderNames}”包含内容，是否删除文件夹内容？",
                "删除文件夹",
                MessageBoxImage.Question,
                new ThemedDialogButton("删除文件夹及内容", MessageBoxResult.Yes, true),
                new ThemedDialogButton("仅删除文件夹", MessageBoxResult.No),
                new ThemedDialogButton("取消", MessageBoxResult.Cancel));
            if (result == MessageBoxResult.Cancel)
                return true;

            deleteFolderContents = result == MessageBoxResult.Yes;
        }
        else
        {
            string message = targets.Count == 1
                ? $"是否删除：{targets[0].Name}？"
                : $"是否删除 {targets.Count} 个资产？\n\n{string.Join("\n", targets.Take(8).Select(item => "- " + item.Name))}{(targets.Count > 8 ? "\n..." : string.Empty)}";
            var result = ThemedDialog.ShowCustom(
                this,
                message,
                "删除资产",
                MessageBoxImage.Question,
                new ThemedDialogButton("删除", MessageBoxResult.Yes, true),
                new ThemedDialogButton("取消", MessageBoxResult.Cancel));
            if (result != MessageBoxResult.Yes)
                return true;

            deleteFolderContents = true;
        }

        var plan = ContentAssetDeletionPlanner.Build(ContentBrowserItems, targets, deleteFolderContents);
        var deletingItems = ContentBrowserItems
            .Where(item => plan.DeleteIds.Contains(item.Id))
            .ToList();
        if (!CanDeleteContentAssets(deletingItems))
            return true;

        var byId = ContentBrowserItems.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var folderCursor = _currentContentFolderId;
        var visitedFolders = new HashSet<string>(StringComparer.Ordinal);
        while (folderCursor is not null &&
               plan.DeleteIds.Contains(folderCursor) &&
               visitedFolders.Add(folderCursor) &&
               byId.TryGetValue(folderCursor, out var deletedFolder))
        {
            folderCursor = deletedFolder.ParentFolderId;
        }
        _currentContentFolderId = folderCursor;

        foreach (var (childId, parentId) in plan.ReparentById)
        {
            if (byId.TryGetValue(childId, out var child) && !plan.DeleteIds.Contains(childId))
            {
                child.ParentFolderId = parentId;
                child.IsDirty = true;
            }
        }

        foreach (var item in deletingItems)
            ContentBrowserItems.Remove(item);

        CloseEditorSessionsForAssetIds(plan.DeleteIds.ToHashSet(StringComparer.Ordinal));
        _contentBrowserContextTargetAsset = null;
        _contentBrowserContextTargetsAsset = false;
        _contentFolderSelectionActive = false;
        _contentRangeAnchor = null;
        ContentBrowserListBox.SelectedItems.Clear();
        ContentFolderListBox.SelectedItem = null;
        RefreshContentBrowserViews();
        PersistAssetLibrary();
        SetStatus($"已删除 {deletingItems.Count} 个资产。");
        return true;
    }
}
