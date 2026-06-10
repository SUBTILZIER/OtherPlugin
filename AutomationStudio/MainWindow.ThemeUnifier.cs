using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using AutomationStudioWpf.Services;
using WpfBorder = System.Windows.Controls.Border;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfButton = System.Windows.Controls.Button;
using WpfColor = System.Windows.Media.Color;
using WpfContextMenu = System.Windows.Controls.ContextMenu;
using WpfContextMenuEventArgs = System.Windows.Controls.ContextMenuEventArgs;
using WpfFrameworkElement = System.Windows.FrameworkElement;
using WpfItemsControl = System.Windows.Controls.ItemsControl;
using WpfListBox = System.Windows.Controls.ListBox;
using WpfListBoxItem = System.Windows.Controls.ListBoxItem;
using WpfMenuItem = System.Windows.Controls.MenuItem;
using WpfMouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using WpfMouseButtonEventHandler = System.Windows.Input.MouseButtonEventHandler;
using WpfPanel = System.Windows.Controls.Panel;
using WpfRadioButton = System.Windows.Controls.RadioButton;
using WpfRichTextBox = System.Windows.Controls.RichTextBox;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfUIElement = System.Windows.UIElement;
using WpfVisualTreeHelper = System.Windows.Media.VisualTreeHelper;

namespace AutomationStudioWpf;

public partial class MainWindow
{
    private static readonly SolidColorBrush UnifiedPanelBrush = FrozenBrush(0x20, 0x24, 0x2B);
    private static readonly SolidColorBrush UnifiedSurfaceBrush = FrozenBrush(0x1B, 0x20, 0x28);
    private static readonly SolidColorBrush UnifiedSurfaceAltBrush = FrozenBrush(0x18, 0x1B, 0x20);
    private static readonly SolidColorBrush UnifiedBorderBrush = FrozenBrush(0x30, 0x37, 0x44);
    private static readonly SolidColorBrush UnifiedStrongBorderBrush = FrozenBrush(0x38, 0x41, 0x50);
    private static readonly SolidColorBrush UnifiedSelectionBrush = FrozenBrush(0x30, 0x44, 0x5C);
    private static readonly SolidColorBrush UnifiedTextBrush = FrozenBrush(0xE8, 0xED, 0xF5);
    private static readonly SolidColorBrush UnifiedMutedTextBrush = FrozenBrush(0xA7, 0xB1, 0xBF);
    private static readonly SolidColorBrush UnifiedAccentBrush = FrozenBrush(0xD6, 0x8A, 0x22);
    private static readonly SolidColorBrush UnifiedErrorBrush = FrozenBrush(0xFF, 0x6B, 0x6B);

    private bool _unifiedThemeInstalled;

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        EnsureEditorSurfaceHost();
        InstallUnifiedDarkTheme();
    }

    private void InstallUnifiedDarkTheme()
    {
        if (_unifiedThemeInstalled)
            return;

        _unifiedThemeInstalled = true;
        ContextMenuOpening += MainWindow_ContextMenuOpeningTheme;
        ContentFolderListBox.AddHandler(WpfUIElement.PreviewMouseLeftButtonDownEvent, new WpfMouseButtonEventHandler(ContentFolderTree_PreviewMouseLeftButtonDownFix), true);
        InstallContentBrowserEnhancedInteractions();
        InstallContentAssetRenameValidation();
        Dispatcher.BeginInvoke(new Action(ApplyUnifiedDarkTheme), DispatcherPriority.ContextIdle);
    }

    private void MainWindow_ContextMenuOpeningTheme(object sender, WpfContextMenuEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(ApplyUnifiedContextMenuTheme), DispatcherPriority.ContextIdle);
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
        if (_currentContentFolderId is not null && ContentBrowserItems.All(item => item.Id != _currentContentFolderId))
            _currentContentFolderId = null;

        ContentVisibleItems.Clear();
        var items = ContentBrowserItems
            .Where(item => string.Equals(item.ParentFolderId, _currentContentFolderId, StringComparison.Ordinal))
            .OrderByDescending(item => item.IsFolder)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var item in items)
            ContentVisibleItems.Add(item);
    }

    private static T? FindVisualAncestor<T>(DependencyObject source) where T : DependencyObject
    {
        var current = source;
        while (current is not null)
        {
            if (current is T match)
                return match;
            current = WpfVisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private static SolidColorBrush FrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(WpfColor.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private void ApplyUnifiedDarkTheme()
    {
        ApplyThemeToVisualTree(this);
        ApplyUnifiedContextMenuTheme();
    }

    private void ApplyUnifiedContextMenuTheme()
    {
        var contextMenus = FindVisualChildren<WpfContextMenu>(this).ToList();
        foreach (var menu in contextMenus)
            StyleContextMenu(menu);
    }

    private void ApplyThemeToVisualTree(DependencyObject root)
    {
        foreach (var element in EnumerateVisualTree(root))
        {
            if (element is WpfBorder border)
                StyleBorder(border);
            else if (element is WpfButton button)
                StyleButton(button);
            else if (element is WpfTextBox textBox)
                StyleTextBox(textBox);
            else if (element is WpfListBox listBox)
                StyleListBox(listBox);
            else if (element is WpfListBoxItem listBoxItem)
                StyleListBoxItem(listBoxItem);
            else if (element is WpfRichTextBox richTextBox)
                StyleRichTextBox(richTextBox);
            else if (element is WpfRadioButton radioButton)
                StyleRadioButton(radioButton);
            else if (element is WpfMenuItem menuItem)
                StyleMenuItem(menuItem);
        }
    }

    private IEnumerable<DependencyObject> EnumerateVisualTree(DependencyObject root)
    {
        var stack = new Stack<DependencyObject>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            yield return current;

            int count = WpfVisualTreeHelper.GetChildrenCount(current);
            for (int i = count - 1; i >= 0; i--)
                stack.Push(WpfVisualTreeHelper.GetChild(current, i));
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is null)
            yield break;

        int count = WpfVisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = WpfVisualTreeHelper.GetChild(root, i);
            if (child is T typed)
                yield return typed;

            foreach (var descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }

    private static void StyleBorder(WpfBorder border)
    {
        if (border.Background is null or WpfBrushes.Transparent)
            return;

        border.BorderBrush ??= UnifiedBorderBrush;
    }

    private static void StyleButton(WpfButton button)
    {
        button.Foreground = UnifiedTextBrush;
        button.Background = UnifiedSurfaceBrush;
        button.BorderBrush = UnifiedBorderBrush;
    }

    private static void StyleTextBox(WpfTextBox textBox)
    {
        textBox.Foreground = UnifiedTextBrush;
        textBox.Background = UnifiedSurfaceAltBrush;
        textBox.BorderBrush = UnifiedStrongBorderBrush;
    }

    private static void StyleListBox(WpfListBox listBox)
    {
        listBox.Foreground = UnifiedTextBrush;
        listBox.Background = UnifiedSurfaceAltBrush;
        listBox.BorderBrush = UnifiedBorderBrush;
    }

    private static void StyleListBoxItem(WpfListBoxItem item)
    {
        item.Foreground = UnifiedTextBrush;
        item.Background = WpfBrushes.Transparent;
    }

    private static void StyleRichTextBox(WpfRichTextBox richTextBox)
    {
        richTextBox.Foreground = UnifiedTextBrush;
        richTextBox.Background = UnifiedSurfaceAltBrush;
        richTextBox.BorderBrush = UnifiedBorderBrush;
    }

    private static void StyleRadioButton(WpfRadioButton radioButton)
    {
        radioButton.Foreground = UnifiedTextBrush;
    }

    private static void StyleMenuItem(WpfMenuItem menuItem)
    {
        menuItem.Foreground = UnifiedTextBrush;
        menuItem.Background = UnifiedSurfaceBrush;
    }

    private static void StyleContextMenu(WpfContextMenu menu)
    {
        menu.Foreground = UnifiedTextBrush;
        menu.Background = UnifiedSurfaceBrush;
        foreach (var item in menu.Items.OfType<WpfMenuItem>())
            StyleMenuItem(item);
    }
}
