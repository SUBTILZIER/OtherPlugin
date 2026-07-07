using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using AutomationStudioWpf.Interaction;
using AutomationStudioWpf.Services;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfButton = System.Windows.Controls.Button;
using WpfControl = System.Windows.Controls.Control;
using WpfContextMenu = System.Windows.Controls.ContextMenu;
using WpfContextMenuEventArgs = System.Windows.Controls.ContextMenuEventArgs;
using WpfFrameworkElement = System.Windows.FrameworkElement;
using WpfItemsControl = System.Windows.Controls.ItemsControl;
using WpfListBox = System.Windows.Controls.ListBox;
using WpfListBoxItem = System.Windows.Controls.ListBoxItem;
using WpfMenuItem = System.Windows.Controls.MenuItem;
using WpfMouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using WpfMouseButtonEventHandler = System.Windows.Input.MouseButtonEventHandler;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfUIElement = System.Windows.UIElement;
using WpfVisualTreeHelper = System.Windows.Media.VisualTreeHelper;

namespace AutomationStudioWpf;

public partial class MainWindow
{
    private bool _unifiedThemeInstalled;

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        EnsureEditorSurfaceHost();
        InstallUnifiedThemeInteractionFixes();
    }

    private void InstallUnifiedThemeInteractionFixes()
    {
        if (_unifiedThemeInstalled)
            return;

        _unifiedThemeInstalled = true;
        ContextMenuOpening += MainWindow_ContextMenuOpeningTheme;
        ContentFolderListBox.AddHandler(WpfUIElement.PreviewMouseLeftButtonDownEvent, new WpfMouseButtonEventHandler(ContentFolderTree_PreviewMouseLeftButtonDownFix), true);
        InstallContentBrowserEnhancedInteractions();
        InstallContentAssetRenameValidation();
    }

    private void MainWindow_ContextMenuOpeningTheme(object sender, WpfContextMenuEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(ApplyContextMenuResourceReferences), DispatcherPriority.ContextIdle);
    }

    private void ContentFolderTree_PreviewMouseLeftButtonDownFix(object sender, WpfMouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
            return;

        var toggleButton = FindVisualAncestor<WpfButton>(source);
        if (toggleButton?.DataContext is ContentAssetViewModel { IsFolder: true } buttonFolder)
        {
            SelectContentFolderFromTree(buttonFolder);
            buttonFolder.IsTreeExpanded = !buttonFolder.IsTreeExpanded;
            RefreshContentBrowserTreeKeepingExpansion();
            RefreshContentVisibleItemsForCurrentFolder();
            e.Handled = true;
            return;
        }

        if (HasVisualAncestor<WpfTextBox>(source))
            return;

        var item = FindVisualAncestor<WpfListBoxItem>(source);
        if (item?.DataContext is not ContentAssetViewModel { IsFolder: true } folder)
            return;

        SelectContentFolderFromTree(folder);

        if (e.ClickCount >= 2)
        {
            folder.IsTreeExpanded = !folder.IsTreeExpanded;
            RefreshContentBrowserTreeKeepingExpansion();
        }

        RefreshContentVisibleItemsForCurrentFolder();
        e.Handled = true;
    }

    private void SelectContentFolderFromTree(ContentAssetViewModel folder)
    {
        _contentFolderSelectionActive = true;
        _currentContentFolderId = ReferenceEquals(folder, _rootContentFolder) ? null : folder.Id;
        ContentFolderListBox.SelectedItem = folder;
        ContentBrowserListBox.SelectedItem = null;
        ContentFolderListBox.Focus();
        SetStatus(ReferenceEquals(folder, _rootContentFolder) ? "已进入内容根目录。" : $"已进入文件夹：{folder.Name}");
    }

    private void RefreshContentVisibleItemsForCurrentFolder()
    {
        var assetById = BuildContentAssetLookup();
        if (_currentContentFolderId is not null &&
            (!assetById.TryGetValue(_currentContentFolderId, out var currentFolder) || !currentFolder.IsFolder))
        {
            _currentContentFolderId = null;
        }

        var childrenByParent = BuildContentChildrenLookup(foldersOnly: false);
        ContentVisibleItems.ReplaceAll(SortContentChildren(childrenByParent[_currentContentFolderId]));
    }

    private void RefreshContentBrowserTreeKeepingExpansion()
    {
        var folderChildrenByParent = BuildContentChildrenLookup(foldersOnly: true);
        _rootContentFolder.ViewDepth = 0;
        _rootContentFolder.IsTreeExpanded = true;
        _rootContentFolder.HasFolderChildren = folderChildrenByParent[null].Any();
        ContentFolderItems.ReplaceAll(new[] { _rootContentFolder }
            .Concat(BuildFolderTree(null, 1, new HashSet<string>(), folderChildrenByParent)));

        ContentFolderListBox.SelectedItem = _currentContentFolderId is null
            ? _rootContentFolder
            : ContentFolderItems.FirstOrDefault(item => item.Id == _currentContentFolderId);
    }

    private void ApplyContextMenuResourceReferences()
    {
        foreach (var menu in EnumerateContextMenus(this))
        {
            ThemeResourceHelper.SetResource(menu, WpfControl.BackgroundProperty, "DropdownBackgroundBrush");
            ThemeResourceHelper.SetResource(menu, WpfControl.BorderBrushProperty, "DropdownBorderBrush");
            ThemeResourceHelper.SetResource(menu, WpfControl.ForegroundProperty, "DropdownTextBrush");

            foreach (var item in EnumerateMenuItems(menu))
            {
                item.Background = WpfBrushes.Transparent;
                ThemeResourceHelper.SetResource(item, WpfControl.ForegroundProperty, item.IsEnabled ? "DropdownTextBrush" : "DropdownMutedTextBrush");
                item.Padding = new Thickness(10, 6, 10, 6);
                item.MinWidth = Math.Max(item.MinWidth, 130);
                item.BorderBrush = WpfBrushes.Transparent;
            }
        }
    }

    private static IEnumerable<WpfMenuItem> EnumerateMenuItems(WpfItemsControl root)
    {
        foreach (var rawItem in root.Items)
        {
            if (rawItem is not WpfMenuItem menuItem)
                continue;

            yield return menuItem;
            foreach (var child in EnumerateMenuItems(menuItem))
                yield return child;
        }
    }

    private static IEnumerable<WpfContextMenu> EnumerateContextMenus(DependencyObject root)
    {
        foreach (var element in EnumerateVisualDescendants<WpfFrameworkElement>(root))
        {
            if (element.ContextMenu is { } menu)
                yield return menu;
        }
    }

    private static IEnumerable<T> EnumerateVisualDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        int childCount = WpfVisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < childCount; index++)
        {
            var child = WpfVisualTreeHelper.GetChild(root, index);
            if (child is T typedChild)
                yield return typedChild;

            foreach (var descendant in EnumerateVisualDescendants<T>(child))
                yield return descendant;
        }
    }

    private static T? FindVisualAncestor<T>(DependencyObject? source)
        where T : DependencyObject
    {
        var current = source;
        while (current is not null)
        {
            if (current is T typed)
                return typed;

            current = GetSafeVisualOrLogicalParent(current);
        }

        return null;
    }

}
