using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Interaction;
using AutomationStudioWpf.Services;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using DragEventArgs = System.Windows.DragEventArgs;
using TextBox = System.Windows.Controls.TextBox;
using MouseButton = System.Windows.Input.MouseButton;
using DragDropEffects = System.Windows.DragDropEffects;

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
        _contentBrowserContextTargetAsset = sender is ListBoxItem { DataContext: ContentAssetViewModel item } ? item : null;
        if (_contentBrowserContextTargetAsset is not null)
            ContentBrowserListBox.SelectedItem = _contentBrowserContextTargetAsset;
    }

    private void ContentBrowserListBox_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _contentFolderSelectionActive = false;
        _contentBrowserContextTargetsAsset = GetContentAssetFromMouseEvent(e) is not null;
        _contentBrowserContextTargetAsset = GetContentAssetFromMouseEvent(e);
        if (!_contentBrowserContextTargetsAsset)
        {
            ContentBrowserListBox.SelectedItem = null;
            ContentFolderListBox.SelectedItem = null;
            ContentBrowserListBox.Focus();
        }
    }

    private void ContentBrowserContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        var selected = _contentBrowserContextTargetAsset ?? GetSelectedContentAsset();
        var assetVisibility = selected is not null ? Visibility.Visible : Visibility.Collapsed;
        var newVisibility = selected is not null ? Visibility.Collapsed : Visibility.Visible;

        ContentBrowserOpenMenuItem.Visibility = assetVisibility;
        ContentBrowserRenameMenuItem.Visibility = assetVisibility;
        ContentBrowserDeleteMenuItem.Visibility = assetVisibility;
        ContentBrowserPropertiesMenuItem.Visibility =
            selected?.Kind == ContentAssetKind.Script ? Visibility.Visible : Visibility.Collapsed;
        ContentBrowserToggleScriptEnabledMenuItem.Visibility =
            selected?.Kind == ContentAssetKind.Script ? Visibility.Visible : Visibility.Collapsed;
        UpdateScriptEnabledMenuItem(selected);
        ContentBrowserAssetMenuSeparator.Visibility = assetVisibility;
        ContentBrowserNewScriptMenuItem.Visibility = newVisibility;
        ContentBrowserNewFolderMenuItem.Visibility = newVisibility;
        ContentBrowserNewLibraryMenuSeparator.Visibility = newVisibility;
        ContentBrowserNewFunctionLibraryMenuItem.Visibility = newVisibility;
    }

    private void ContentBrowserContextMenu_Closed(object sender, RoutedEventArgs e)
    {
        _contentBrowserContextTargetAsset = null;
        _contentBrowserContextTargetsAsset = false;
    }

    private void OpenContentAssetMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if ((_contentBrowserContextTargetAsset ?? GetSelectedContentAsset()) is not { } asset)
            return;

        _contentBrowserContextTargetAsset = null;
        _contentBrowserContextTargetsAsset = false;
        if (asset.Kind == ContentAssetKind.Folder)
            EnterContentFolder(ReferenceEquals(asset, _rootContentFolder) ? null : asset);
        else
            OpenContentAsset(asset);

        e.Handled = true;
    }

    private void ContentFolder_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem { DataContext: ContentAssetViewModel item })
        {
            ContentFolderListBox.SelectedItem = item;
            ContentBrowserListBox.SelectedItem = null;
            _contentFolderSelectionActive = true;
            _contentBrowserContextTargetAsset = item;
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

    private void ContentAssetPropertiesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ShowSelectedScriptProperties();
        _contentBrowserContextTargetsAsset = false;
    }

    private void ContentBrowserToggleScriptEnabledMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if ((_contentBrowserContextTargetAsset ?? GetSelectedContentAsset()) is not { Kind: ContentAssetKind.Script } asset)
            return;

        SetScriptAssetEnabled(asset, !asset.IsScriptEnabled);
        e.Handled = true;
    }

    private void ContentAssetScriptEnabledCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.CheckBox { DataContext: ContentAssetViewModel { Kind: ContentAssetKind.Script } asset } checkBox)
            return;

        SetScriptAssetEnabled(asset, checkBox.IsChecked == true);
        // Preserve the OneWay binding so context-menu and workbench changes still reach the badge.
        checkBox.SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, asset.IsScriptEnabled);
        e.Handled = true;
    }

    private bool SetScriptAssetEnabled(ContentAssetViewModel asset, bool enabled)
    {
        if (asset.Kind != ContentAssetKind.Script || asset.IsScriptEnabled == enabled)
            return asset.Kind == ContentAssetKind.Script;

        if (!enabled && _scriptRunManager.IsHotkeyRunActive(asset))
        {
            SetStatus($"脚本正在运行，请先使用终止热键或顶部停止按钮：{asset.Name}");
            ThemedDialog.Show(
                this,
                "脚本运行期间不能关闭热键监听。请先使用终止热键或顶部“停止执行”按钮结束脚本。",
                "脚本正在运行",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            UpdateScriptEnabledMenuItem(asset);
            return false;
        }

        asset.IsScriptEnabled = enabled;
        if (enabled)
        {
            var conflicts = _scriptHotkeyService.Validate(ContentBrowserItems);
            if (conflicts.Count > 0)
            {
                asset.IsScriptEnabled = false;
                ThemedDialog.Show(this, string.Join(Environment.NewLine, conflicts), "热键冲突", MessageBoxButton.OK, MessageBoxImage.Warning);
                UpdateScriptEnabledMenuItem(asset);
                return false;
            }
        }

        asset.IsDirty = true;
        PersistAssetLibrary();
        RefreshScriptHotkeys();
        UpdateScriptEnabledMenuItem(asset);
        SetStatus(enabled
            ? $"已启用脚本热键监听：{asset.Name}"
            : $"已禁用脚本热键监听：{asset.Name}");
        return true;
    }

    private void UpdateScriptEnabledMenuItem(ContentAssetViewModel? asset)
    {
        bool enabled = asset?.Kind == ContentAssetKind.Script && asset.IsScriptEnabled;
        ContentBrowserToggleScriptEnabledMenuItem.Header = enabled ? "关闭脚本" : "启用脚本";
        ContentBrowserToggleScriptEnabledMenuItem.IsChecked = enabled;
        ContentBrowserToggleScriptEnabledMenuItem.ToolTip = enabled
            ? "关闭后，该脚本不再监听全局热键。"
            : "启用后，该脚本才有资格监听全局热键。";
    }

    private void ContentAssetNameTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: ContentAssetViewModel item } textBox) return;

        if (e.Key == Key.Enter)
        {
            TryCommitContentAssetRenameValidated(item, textBox, keepFocusOnError: true);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelContentAssetRenameValidated(item, textBox);
            e.Handled = true;
        }
    }

    private void ContentAssetNameTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: ContentAssetViewModel item } textBox)
            TryCommitContentAssetRenameValidated(item, textBox, keepFocusOnError: true);
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
        if (asset.Kind == ContentAssetKind.Script)
        {
            asset.RunSettings.Normalize();
            GraphStructureNormalizer.NormalizeContentAsset(asset);
        }

        return asset;
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
        _contentBreadcrumbText?.SetCurrentValue(TextBlock.TextProperty, BuildBreadcrumbText());

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
        _contentBreadcrumbText?.SetCurrentValue(TextBlock.TextProperty, BuildBreadcrumbText());
        SetStatus(folder is null ? "已进入内容根目录。" : $"已进入文件夹：{folder.Name}");
    }

    private string BuildBreadcrumbText()
    {
        if (_currentContentFolderId is null) return "路径：内容";
        var map = ContentBrowserItems.ToDictionary(a => a.Id); var parts = new List<string>(); var id = _currentContentFolderId;
        while (id is not null && map.TryGetValue(id, out var item)) { parts.Add(item.Name); id = item.ParentFolderId; }
        parts.Reverse(); return "路径：内容 / " + string.Join(" / ", parts);
    }

    private void HandleContentKeyDown(KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox)
            return;

        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Alt) != 0)
        {
            ShowSelectedScriptProperties();
            e.Handled = true;
            return;
        }

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
        if ((_contentBrowserContextTargetAsset ?? GetSelectedContentAsset()) is ContentAssetViewModel item)
        {
            _contentBrowserContextTargetAsset = null;
            _contentBrowserContextTargetsAsset = false;
            item.IsEditing = true;
            FocusContentRenameTextBox(item);
        }
    }

    private void CommitContentAssetRename(ContentAssetViewModel item)
    {
        string newName = item.RenameText.Trim();
        if (string.IsNullOrWhiteSpace(newName))
        {
            item.RenameError = string.Empty;
            item.IsEditing = false;
            SetStatus("名称不能为空，已保留原名称。");
            return;
        }

        if (HasSameLevelContentName(newName, item.ParentFolderId, item))
        {
            item.RenameError = "同层级已存在同名资产。";
            item.IsEditing = true;
            SetStatus(item.RenameError);
            return;
        }

        item.RenameError = string.Empty;
        if (!string.Equals(item.Name, newName, StringComparison.Ordinal))
        {
            item.Name = newName;
            item.IsDirty = true;
            PersistAssetLibrary();
            SetStatus($"已重命名资产：{newName}");
        }

        item.IsEditing = false;
        item.RenameText = item.Name;
        RefreshContentBrowserViews();
    }

    private void DeleteSelectedContentAsset()
    {
        if ((_contentBrowserContextTargetAsset ?? GetSelectedContentAsset()) is not ContentAssetViewModel item)
            return;

        DeleteContentAssetsWithPolicy([item]);
    }

    private bool CanDeleteContentAssets(IReadOnlyCollection<ContentAssetViewModel> targets)
    {
        var running = targets
            .Where(item =>
                _scriptRunManager.IsHotkeyRunActive(item) ||
                (_executionController.IsManualDebugRunning &&
                 string.Equals(_activeEditorSession?.ContentAsset.Id, item.Id, StringComparison.Ordinal)))
            .ToList();
        if (running.Count == 0)
            return true;

        string names = string.Join("、", running.Select(item => item.Name));
        SetStatus($"运行中的脚本不能删除：{names}");
        ThemedDialog.Show(
            this,
            $"请先停止以下脚本，再删除资产：\n{string.Join(Environment.NewLine, running.Select(item => "- " + item.Name))}",
            "脚本正在运行",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        return false;
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
        {
            var result = TryCloneContentAssets([source], targetFolderId);
            if (result is null)
                return;
            foreach (var clone in result.AllClones)
                ContentBrowserItems.Add(clone);
        }

        RefreshContentBrowserViews();
        PersistAssetLibrary();
    }

    private AssetCloneResult? TryCloneContentAssets(
        IEnumerable<ContentAssetViewModel> sources,
        string? targetFolderId)
    {
        try
        {
            return _assetCloneService.CloneTree(sources, ContentBrowserItems, targetFolderId);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
            ThemedDialog.Show(this, ex.Message, "复制失败", MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }
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
            Background = ThemeResourceHelper.Brush("EditorPanelBackgroundBrush"),
            Foreground = ThemeResourceHelper.Brush("EditorTextBrush"),
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
            Foreground = ThemeResourceHelper.Brush("EditorTextBrush"),
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
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (candidateFolderId is not null)
        {
            if (!visited.Add(candidateFolderId))
                return true;
            if (candidateFolderId == sourceFolderId)
                return true;

            candidateFolderId = ContentBrowserItems.FirstOrDefault(item => item.Id == candidateFolderId)?.ParentFolderId;
        }

        return false;
    }

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
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (folderId is not null)
        {
            if (!visited.Add(folderId))
                return;
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
