using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Services;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using DragEventArgs = System.Windows.DragEventArgs;
using TextBox = System.Windows.Controls.TextBox;
using MouseButton = System.Windows.Input.MouseButton;
using DragDropEffects = System.Windows.DragDropEffects;
using WpfMessageBox = System.Windows.MessageBox;

namespace AutomationStudioWpf;

public partial class MainWindow
{
    private enum ContentDropAction
    {
        Cancel,
        Move,
        Copy,
    }

    private void NewContentFolder_Click(object sender, RoutedEventArgs e) => AddContentAsset(ContentAssetKind.Folder, "新文件夹");

    private void NewScriptAsset_Click(object sender, RoutedEventArgs e) => AddContentAsset(ContentAssetKind.Script, "新脚本");

    private void NewFunctionLibraryAsset_Click(object sender, RoutedEventArgs e) => AddContentAsset(ContentAssetKind.FunctionLibrary, "新函数库");

    private void ContentBrowserListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        _contentFolderSelectionActive = false;
        if (GetContentAssetFromMouseEvent(e) is not { } asset)
            return;

        if (asset.Kind == ContentAssetKind.Folder)
            EnterContentFolder(asset);
        else
            OpenContentAsset(asset);
    }

    private void ContentFolderListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        _contentFolderSelectionActive = true;
        if (GetContentAssetFromMouseEvent(e) is not { IsFolder: true } folder)
            return;

        EnterContentFolder(ReferenceEquals(folder, _rootContentFolder) ? null : folder);
        e.Handled = true;
    }

    private void ContentFolderItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source &&
            (HasVisualAncestor<System.Windows.Controls.Button>(source) || HasVisualAncestor<TextBox>(source)))
            return;

        if (sender is not ListBoxItem { DataContext: ContentAssetViewModel { IsFolder: true } folder })
            return;

        _contentFolderSelectionActive = true;
        ContentFolderListBox.SelectedItem = folder;
        ContentBrowserListBox.SelectedItem = null;
        EnterContentFolder(ReferenceEquals(folder, _rootContentFolder) ? null : folder);
        e.Handled = true;
    }

    private void ContentFolderToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { DataContext: ContentAssetViewModel { IsFolder: true } folder })
            return;

        folder.IsTreeExpanded = !folder.IsTreeExpanded;
        RefreshContentBrowserViews();
        e.Handled = true;
    }

    private void ContentFolderListBox_KeyDown(object sender, KeyEventArgs e)
    {
        _contentFolderSelectionActive = true;
        HandleContentKeyDown(e);
    }

    private void ContentBrowserListBox_KeyDown(object sender, KeyEventArgs e)
    {
        _contentFolderSelectionActive = false;
        HandleContentKeyDown(e);
    }

    private void ContentAsset_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _contentFolderSelectionActive = false;
        _contentBrowserContextTargetsAsset = true;
        if (sender is ListBoxItem { DataContext: ContentAssetViewModel item })
            ContentBrowserListBox.SelectedItem = item;
    }

    private void ContentBrowserListBox_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _contentFolderSelectionActive = false;
        _contentBrowserContextTargetsAsset = GetContentAssetFromMouseEvent(e) is not null;
        if (!_contentBrowserContextTargetsAsset)
        {
            ContentBrowserListBox.SelectedItem = null;
            ContentFolderListBox.SelectedItem = null;
            ContentBrowserListBox.Focus();
        }
    }

    private void ContentBrowserContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        var assetVisibility = _contentBrowserContextTargetsAsset ? Visibility.Visible : Visibility.Collapsed;
        var newVisibility = _contentBrowserContextTargetsAsset ? Visibility.Collapsed : Visibility.Visible;

        ContentBrowserRenameMenuItem.Visibility = assetVisibility;
        ContentBrowserDeleteMenuItem.Visibility = assetVisibility;
        ContentBrowserAssetMenuSeparator.Visibility = assetVisibility;
        ContentBrowserNewScriptMenuItem.Visibility = newVisibility;
        ContentBrowserNewFolderMenuItem.Visibility = newVisibility;
        ContentBrowserNewLibraryMenuSeparator.Visibility = newVisibility;
        ContentBrowserNewFunctionLibraryMenuItem.Visibility = newVisibility;
    }

    private void ContentFolder_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem { DataContext: ContentAssetViewModel item })
        {
            ContentFolderListBox.SelectedItem = item;
            ContentBrowserListBox.SelectedItem = null;
            _contentFolderSelectionActive = true;
            if (!ReferenceEquals(item, _rootContentFolder))
                ContentFolderListBox.Focus();
        }
    }

    private void ContentBrowserListBox_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _contentDragStartPoint = e.GetPosition(ContentBrowserListBox);
            return;
        }

        var current = e.GetPosition(ContentBrowserListBox);
        if (Math.Abs(current.X - _contentDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _contentDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        if (ContentBrowserListBox.SelectedItem is ContentAssetViewModel asset && !ReferenceEquals(asset, _rootContentFolder))
            DragDrop.DoDragDrop(ContentBrowserListBox, new System.Windows.DataObject(typeof(ContentAssetViewModel), asset), DragDropEffects.Move | DragDropEffects.Copy);
    }

    private void ContentFolder_DragEnter(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(ContentAssetViewModel))
            ? DragDropEffects.Move | DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void ContentFolder_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(ContentAssetViewModel)) ||
            e.Data.GetData(typeof(ContentAssetViewModel)) is not ContentAssetViewModel source)
            return;

        var target = sender is ListBoxItem { DataContext: ContentAssetViewModel item } ? item : null;
        if (target is null || !target.IsFolder)
            return;

        MoveOrCopyContentAsset(source, ReferenceEquals(target, _rootContentFolder) ? null : target.Id);
        e.Handled = true;
    }

    private void RenameContentAssetMenuItem_Click(object sender, RoutedEventArgs e) => StartRenameSelectedContentAsset();

    private void DeleteContentAssetMenuItem_Click(object sender, RoutedEventArgs e) => DeleteSelectedContentAsset();

    private void ContentAssetNameTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: ContentAssetViewModel item }) return;

        if (e.Key == Key.Enter)
        {
            CommitContentAssetRename(item);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            item.IsEditing = false;
            e.Handled = true;
        }
    }

    private void ContentAssetNameTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: ContentAssetViewModel item })
            CommitContentAssetRename(item);
    }

    private void ContentBrowserArea_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox focusedTextBox &&
            focusedTextBox.DataContext is ContentAssetViewModel focusedItem &&
            focusedItem.IsEditing &&
            e.OriginalSource is DependencyObject source &&
            !IsVisualAncestor(focusedTextBox, source))
        {
            CommitContentAssetRename(focusedItem);
        }
    }

    private void AddContentAsset(ContentAssetKind kind, string namePrefix)
    {
        var asset = CreateContentAsset(kind, CreateUniqueContentName(namePrefix, _currentContentFolderId));
        asset.ParentFolderId = _currentContentFolderId;
        ContentBrowserItems.Add(asset);
        asset.IsEditing = true;
        RefreshContentBrowserViews();
        ContentBrowserListBox.SelectedItem = asset;
        _contentFolderSelectionActive = false;
        FocusContentRenameTextBox(asset);
        PersistAssetLibrary();
    }

    private ContentAssetViewModel CreateContentAsset(ContentAssetKind kind, string name)
    {
        var asset = new ContentAssetViewModel
        {
            Kind = kind,
            Name = name,
            IsDirty = true,
        };

        return asset;
    }

    private GraphListItemViewModel CreateGraphItem(GraphAssetKind kind, string name)
    {
        var graph = CreateDefaultGraphModel(kind, name);
        return new GraphListItemViewModel
        {
            Kind = kind,
            Name = name,
            Graph = graph,
            IsDirty = true,
            IsCompileDirty = true,
        };
    }

    private GraphFileModel CreateDefaultGraphModel(GraphAssetKind kind, string name)
    {
        if (kind == GraphAssetKind.Function)
            _editorService.NewFunctionGraph();
        else
            _editorService.NewGraph();

        ApplyEntryNodeTitle(_editorService.Nodes, kind, name);
        SyncNodeFactorySequence();
        return _editorService.ExportGraphModel(name, kind);
    }

    private string CreateUniqueContentName(string prefix, string? parentFolderId, ContentAssetViewModel? exclude = null)
    {
        int index = 1;
        string name;
        do
        {
            name = $"{prefix}{index++}";
        }
        while (HasSameLevelContentName(name, parentFolderId, exclude));

        return name;
    }

    private void OpenContentAsset(ContentAssetViewModel asset)
    {
        OpenOrActivateAsset(asset);
    }

    private void RefreshContentBrowserViews()
    {
        _contentBrowserIndex = new ContentBrowserIndex(ContentBrowserItems);
        var assetById = BuildContentAssetLookup();
        if (_currentContentFolderId is not null &&
            (!assetById.TryGetValue(_currentContentFolderId, out var currentFolder) || !currentFolder.IsFolder))
        {
            _currentContentFolderId = null;
        }

        ExpandFolderPath(_currentContentFolderId, assetById);
        var folderChildrenByParent = BuildContentChildrenLookup(foldersOnly: true);
        var childrenByParent = BuildContentChildrenLookup(foldersOnly: false);

        _rootContentFolder.ViewDepth = 0;
        _rootContentFolder.IsTreeExpanded = true;
        _rootContentFolder.HasFolderChildren = folderChildrenByParent[null].Any();
        ContentFolderItems.ReplaceAll(new[] { _rootContentFolder }
            .Concat(BuildFolderTree(null, 1, new HashSet<string>(), folderChildrenByParent)));
        ContentVisibleItems.ReplaceAll(SortContentChildren(childrenByParent[_currentContentFolderId]));

        ContentFolderListBox.SelectedItem = _currentContentFolderId is null
            ? _rootContentFolder
            : ContentFolderItems.FirstOrDefault(item => item.Id == _currentContentFolderId);
    }

    private static IEnumerable<ContentAssetViewModel> BuildFolderTree(
        string? parentId,
        int depth,
        HashSet<string> visited,
        ILookup<string?, ContentAssetViewModel> folderChildrenByParent)
    {
        foreach (var folder in folderChildrenByParent[parentId].OrderBy(item => item.Name))
        {
            if (!visited.Add(folder.Id))
                continue;

            folder.ViewDepth = depth;
            folder.HasFolderChildren = folderChildrenByParent[folder.Id].Any();
            yield return folder;

            if (!folder.IsTreeExpanded)
                continue;

            foreach (var child in BuildFolderTree(folder.Id, depth + 1, visited, folderChildrenByParent))
                yield return child;
        }
    }

    private IReadOnlyDictionary<string, ContentAssetViewModel> BuildContentAssetLookup()
    {
        return GetContentBrowserIndex().AssetById;
    }

    private ILookup<string?, ContentAssetViewModel> BuildContentChildrenLookup(bool foldersOnly)
    {
        return foldersOnly
            ? GetContentBrowserIndex().FolderChildrenByParent
            : GetContentBrowserIndex().ChildrenByParent;
    }

    private static IEnumerable<ContentAssetViewModel> SortContentChildren(IEnumerable<ContentAssetViewModel> items) =>
        items.OrderByDescending(item => item.IsFolder).ThenBy(item => item.Name);

    private void EnterContentFolder(ContentAssetViewModel? folder)
    {
        _currentContentFolderId = folder?.Id;
        ExpandFolderPath(_currentContentFolderId);
        RefreshContentBrowserViews();
        SetStatus(folder is null ? "已进入内容根目录。" : $"已进入文件夹：{folder.Name}");
    }

    private void HandleContentKeyDown(KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox)
            return;

        if (e.Key == Key.Delete)
        {
            DeleteSelectedContentAsset();
            e.Handled = true;
        }
        else if (e.Key == Key.F2)
        {
            StartRenameSelectedContentAsset();
            e.Handled = true;
        }
    }

    private ContentAssetViewModel? GetSelectedContentAsset()
    {
        if (_contentFolderSelectionActive || IsFocusInside(ContentFolderListBox))
        {
            return ContentFolderListBox.SelectedItem is ContentAssetViewModel focusedFolder &&
                   !ReferenceEquals(focusedFolder, _rootContentFolder)
                ? focusedFolder
                : null;
        }

        if (ContentBrowserListBox.SelectedItem is ContentAssetViewModel asset)
            return asset;

        if (ContentFolderListBox.SelectedItem is ContentAssetViewModel folder && !ReferenceEquals(folder, _rootContentFolder))
            return folder;

        return null;
    }

    private void StartRenameSelectedContentAsset()
    {
        if (GetSelectedContentAsset() is ContentAssetViewModel item)
        {
            item.IsEditing = true;
            FocusContentRenameTextBox(item);
        }
    }

    private void CommitContentAssetRename(ContentAssetViewModel item)
    {
        if (_isCommittingContentAssetRename)
            return;

        _isCommittingContentAssetRename = true;
        try
        {
            string baseName = string.IsNullOrWhiteSpace(item.Name) ? "未命名资产" : item.Name.Trim();
            item.Name = MakeUniqueContentName(baseName, item.ParentFolderId, item);
            item.IsEditing = false;
            item.IsDirty = true;
            RefreshContentBrowserViews();
            PersistAssetLibrary();
        }
        finally
        {
            _isCommittingContentAssetRename = false;
        }
    }

    private void DeleteSelectedContentAsset()
    {
        if (GetSelectedContentAsset() is not ContentAssetViewModel item)
            return;

        var result = WpfMessageBox.Show(this, $"是否删除：{item.Name}？", "删除资产", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
            return;

        var deletingIds = new HashSet<string> { item.Id };
        foreach (var child in ContentBrowserItems.Where(child => child.ParentFolderId == item.Id).ToList())
            child.ParentFolderId = item.ParentFolderId;
        ContentBrowserItems.Remove(item);
        CloseEditorSessionsForAssetIds(deletingIds);
        RefreshContentBrowserViews();
        PersistAssetLibrary();
    }

    private void MoveOrCopyContentAsset(ContentAssetViewModel source, string? targetFolderId)
    {
        if (source.ParentFolderId == targetFolderId || IsDescendantFolder(targetFolderId, source.Id))
            return;

        var choice = ShowContentDropActionDialog(source.Name);
        ApplyContentDropAction(source, targetFolderId, choice);
    }

    private void ApplyContentDropAction(ContentAssetViewModel source, string? targetFolderId, ContentDropAction choice)
    {
        if (choice == ContentDropAction.Cancel)
            return;

        if (choice == ContentDropAction.Move)
            MoveContentAsset(source, targetFolderId);
        else if (choice == ContentDropAction.Copy)
            ContentBrowserItems.Add(CloneContentAssetForCopy(source, targetFolderId));

        RefreshContentBrowserViews();
        PersistAssetLibrary();
    }

    private void MoveContentAsset(ContentAssetViewModel source, string? targetFolderId)
    {
        source.Name = MakeUniqueContentName(source.Name, targetFolderId, source);
        source.ParentFolderId = targetFolderId;
        source.IsDirty = true;
    }

    private ContentDropAction ShowContentDropActionDialog(string assetName)
    {
        var result = ContentDropAction.Cancel;
        var dialog = new Window
        {
            Owner = this,
            Title = "拖拽资产",
            Width = 360,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(27, 32, 40)),
            Foreground = System.Windows.Media.Brushes.White,
            Content = CreateContentDropDialogContent(assetName, action =>
            {
                result = action;
            }),
        };

        if (dialog.Content is FrameworkElement root)
        {
            foreach (var button in FindVisualChildren<System.Windows.Controls.Button>(root))
            {
                button.Click += (_, _) => dialog.Close();
            }
        }

        dialog.ShowDialog();
        return result;
    }

    private static FrameworkElement CreateContentDropDialogContent(string assetName, Action<ContentDropAction> setResult)
    {
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock
        {
            Text = $"选择对资产“{assetName}”的操作：",
            Foreground = System.Windows.Media.Brushes.White,
            Margin = new Thickness(0, 0, 0, 16),
        });

        var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        AddDropDialogButton(buttons, "移动到此处", ContentDropAction.Move, setResult);
        AddDropDialogButton(buttons, "复制到此处", ContentDropAction.Copy, setResult);
        AddDropDialogButton(buttons, "取消", ContentDropAction.Cancel, setResult);
        panel.Children.Add(buttons);
        return panel;
    }

    private static void AddDropDialogButton(System.Windows.Controls.Panel panel, string text, ContentDropAction action, Action<ContentDropAction> setResult)
    {
        var button = new System.Windows.Controls.Button
        {
            Content = text,
            Margin = new Thickness(6, 0, 0, 0),
            Padding = new Thickness(10, 5, 10, 5),
            MinWidth = 78,
        };
        button.Click += (_, _) => setResult(action);
        panel.Children.Add(button);
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T target)
                yield return target;

            foreach (var nested in FindVisualChildren<T>(child))
                yield return nested;
        }
    }

    private bool IsDescendantFolder(string? candidateFolderId, string sourceFolderId)
    {
        while (candidateFolderId is not null)
        {
            if (candidateFolderId == sourceFolderId)
                return true;

            candidateFolderId = ContentBrowserItems.FirstOrDefault(item => item.Id == candidateFolderId)?.ParentFolderId;
        }

        return false;
    }

    private ContentAssetViewModel CloneContentAssetForCopy(ContentAssetViewModel source, string? targetFolderId)
    {
        var clone = CreateContentAsset(source.Kind, CreateUniqueContentName($"{source.Name}_Copy", targetFolderId));
        clone.ParentFolderId = targetFolderId;
        clone.EventGraphs = new ObservableCollection<GraphListItemViewModel>(source.EventGraphs.Select(CloneGraphItem));
        clone.Functions = new ObservableCollection<GraphListItemViewModel>(source.Functions.Select(CloneGraphItem));
        return clone;
    }

    private static GraphListItemViewModel CloneGraphItem(GraphListItemViewModel source) => new()
    {
        Kind = source.Kind,
        Name = source.Name,
        Graph = new GraphFileModel
        {
            Name = source.Graph.Name,
            AssetKind = source.Graph.AssetKind,
            Nodes = source.Graph.Nodes.Select(CloneNodeFile).ToList(),
            Connections = source.Graph.Connections.Select(conn => new ConnectionFileModel
            {
                SourceNodeId = conn.SourceNodeId,
                SourcePinName = conn.SourcePinName,
                TargetNodeId = conn.TargetNodeId,
                TargetPinName = conn.TargetPinName,
            }).ToList(),
        },
        IsDirty = true,
        IsCompileDirty = true,
        IsPublicToLibrary = source.IsPublicToLibrary,
    };

    private string MakeUniqueContentName(string baseName, string? parentFolderId, ContentAssetViewModel? exclude = null)
    {
        if (!HasSameLevelContentName(baseName, parentFolderId, exclude))
            return baseName;

        int index = 1;
        string name;
        do
        {
            name = $"{baseName}{index++}";
        }
        while (HasSameLevelContentName(name, parentFolderId, exclude));

        return name;
    }

    private bool HasSameLevelContentName(string name, string? parentFolderId, ContentAssetViewModel? exclude = null) =>
        ContentBrowserItems.Any(item =>
            !ReferenceEquals(item, exclude) &&
            item.ParentFolderId == parentFolderId &&
            string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));

    private void ExpandFolderPath(string? folderId)
    {
        ExpandFolderPath(folderId, GetContentBrowserIndex().AssetById);
    }

    private static void ExpandFolderPath(string? folderId, IReadOnlyDictionary<string, ContentAssetViewModel> assetById)
    {
        if (folderId is null ||
            !assetById.TryGetValue(folderId, out var current) ||
            !current.IsFolder)
        {
            return;
        }

        folderId = current.Id;
        while (folderId is not null)
        {
            if (!assetById.TryGetValue(folderId, out var folder) || !folder.IsFolder)
                return;

            folder.IsTreeExpanded = true;
            folderId = folder.ParentFolderId;
        }
    }

    private void FocusContentRenameTextBox(ContentAssetViewModel item)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            System.Windows.Controls.ListBox targetList = item.IsFolder && _contentFolderSelectionActive
                ? ContentFolderListBox
                : ContentBrowserListBox;
            if (targetList.ItemContainerGenerator.ContainerFromItem(item) is not ListBoxItem container)
                return;

            var tb = FindVisualChild<TextBox>(container);
            if (tb is null)
                return;

            tb.Focus();
            tb.SelectAll();
        }), DispatcherPriority.Render);
    }

    private ContentAssetViewModel? GetContentAssetFromMouseEvent(MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
            return null;

        DependencyObject? current = source;
        while (current is not null)
        {
            if (current is FrameworkElement { DataContext: ContentAssetViewModel item })
                return item;

            current = GetSafeVisualOrLogicalParent(current);
        }

        return null;
    }
}
