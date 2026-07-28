using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AutomationStudioWpf.Interaction;
using AutomationStudioWpf.Services;
using WpfButton = System.Windows.Controls.Button;
using WpfDragDrop = System.Windows.DragDrop;
using WpfDragDropEffects = System.Windows.DragDropEffects;
using WpfDragEventArgs = System.Windows.DragEventArgs;
using WpfDragEventHandler = System.Windows.DragEventHandler;
using WpfKey = System.Windows.Input.Key;
using WpfKeyboard = System.Windows.Input.Keyboard;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfKeyEventHandler = System.Windows.Input.KeyEventHandler;
using WpfModifierKeys = System.Windows.Input.ModifierKeys;
using WpfRoutedEventArgs = System.Windows.RoutedEventArgs;
using WpfRoutedEventHandler = System.Windows.RoutedEventHandler;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace AutomationStudioWpf;

public partial class MainWindow
{
    private bool _isReallyClosing;
    private bool _themedDialogOverridesInstalled;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        InstallWindowPlacementHook();
        InstallThemedDialogOverrides();
    }

    private void InstallThemedDialogOverrides()
    {
        if (_themedDialogOverridesInstalled)
            return;

        _themedDialogOverridesInstalled = true;
        InstallContentBrowserEnhancedInteractions();

        RunGraphButton.Click -= RunGraph_Click;
        RunGraphButton.Click += RunGraph_ClickThemed;

        ReplaceToolbarButton("保存", SaveGraph_Click, SaveGraph_ClickThemed);
        ReplaceToolbarButton("外部导入", OpenGraph_Click, OpenGraph_ClickThemed);

        ContentBrowserDeleteMenuItem.Click -= DeleteContentAssetMenuItem_Click;
        ContentBrowserDeleteMenuItem.Click -= DeleteSelectedContentAssetsMenuItem_Click;
        ContentBrowserDeleteMenuItem.Click += DeleteSelectedContentAssetsThemedMenuItem_Click;

        ContentBrowserListBox.RemoveHandler(WpfKeyboard.PreviewKeyDownEvent, new WpfKeyEventHandler(ContentBrowserEnhanced_PreviewKeyDown));
        ContentBrowserListBox.AddHandler(WpfKeyboard.PreviewKeyDownEvent, new WpfKeyEventHandler(ContentBrowserThemed_PreviewKeyDown), true);
        ContentBrowserListBox.KeyDown -= ContentBrowserListBox_KeyDown;
        ContentFolderListBox.KeyDown -= ContentFolderListBox_KeyDown;
        ContentFolderListBox.KeyDown += ContentFolderListBox_ThemedKeyDown;

        ContentBrowserBodyGrid.RemoveHandler(WpfDragDrop.PreviewDropEvent, new WpfDragEventHandler(ContentBrowserEnhanced_PreviewDrop));
        ContentBrowserBodyGrid.AddHandler(WpfDragDrop.PreviewDropEvent, new WpfDragEventHandler(ContentBrowserThemed_PreviewDrop), true);
    }

    private void ReplaceToolbarButton(string content, WpfRoutedEventHandler oldHandler, WpfRoutedEventHandler newHandler)
    {
        foreach (var button in FindVisualChildren<WpfButton>(this)
                     .Where(button => button.Content is string text && string.Equals(text, content, StringComparison.Ordinal)))
        {
            button.Click -= oldHandler;
            button.Click += newHandler;
        }
    }

    private void SaveGraph_ClickThemed(object sender, WpfRoutedEventArgs e)
    {
        if (!EnsureCompiledBeforeSaveThemed())
            return;

        SaveAllAssets();
    }

    private void OpenGraph_ClickThemed(object sender, WpfRoutedEventArgs e)
    {
        ImportExternalGraph();
    }

    private async void RunGraph_ClickThemed(object sender, WpfRoutedEventArgs e)
    {
        await RunActiveScriptFromToolbarAsync();
    }

    private bool EnsureCompiledBeforeSaveThemed()
    {
        CommitInspectorAndSnapshotAllSessions();
        if (!HasCompileDirtyAssets())
            return true;

        var result = ThemedDialog.ShowCustom(
            this,
            "存在未编译修改，是否先编译再保存？",
            "需要编译",
            MessageBoxImage.Question,
            new ThemedDialogButton("先编译", MessageBoxResult.Yes, true),
            new ThemedDialogButton("直接保存", MessageBoxResult.No),
            new ThemedDialogButton("取消", MessageBoxResult.Cancel));

        if (result == MessageBoxResult.Cancel)
            return false;
        if (result == MessageBoxResult.Yes)
            return CompileAllAssets(showPrompt: false);
        return true;
    }

    private void ContentFolderListBox_ThemedKeyDown(object sender, WpfKeyEventArgs e)
    {
        _contentFolderSelectionActive = true;
        ContentBrowserThemed_PreviewKeyDown(sender, e);
    }

    private void ContentBrowserThemed_PreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Handled || WpfKeyboard.FocusedElement is WpfTextBox)
            return;

        bool ctrl = (WpfKeyboard.Modifiers & WpfModifierKeys.Control) != 0;
        if (ctrl && e.Key == WpfKey.C)
        {
            CopySelectedContentAssets();
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == WpfKey.V)
        {
            PasteContentAssetsToCurrentFolder();
            e.Handled = true;
            return;
        }

        if (e.Key == WpfKey.Delete)
        {
            DeleteSelectedContentAssetsThemed();
            e.Handled = true;
            return;
        }

        if (e.Key == WpfKey.F2)
        {
            StartRenameSelectedContentAssetEnhanced();
            e.Handled = true;
        }
    }

    private void DeleteSelectedContentAssetsThemedMenuItem_Click(object sender, WpfRoutedEventArgs e)
    {
        DeleteSelectedContentAssetsThemed();
        e.Handled = true;
    }

    private bool DeleteSelectedContentAssetsThemed()
    {
        var targets = GetTopLevelContentAssets(GetSelectedContentAssetList());
        if (targets.Count == 0)
            return false;

        if (!CanDeleteContentAssets(targets))
            return true;

        string message = targets.Count == 1
            ? $"是否删除：{targets[0].Name}？"
            : $"是否删除 {targets.Count} 个资产？\n\n{string.Join("\n", targets.Take(8).Select(item => "- " + item.Name))}{(targets.Count > 8 ? "\n..." : string.Empty)}";

        var result = ThemedDialog.ShowCustom(this, message, "删除资产", MessageBoxImage.Question, new ThemedDialogButton("删除", MessageBoxResult.Yes, true), new ThemedDialogButton("取消", MessageBoxResult.Cancel));
        if (result != MessageBoxResult.Yes)
            return true;

        var deletingIds = targets.Select(item => item.Id).ToHashSet();

        foreach (var item in targets)
        {
            foreach (var child in ContentBrowserItems.Where(child => child.ParentFolderId == item.Id && !deletingIds.Contains(child.Id)).ToList())
            {
                child.ParentFolderId = item.ParentFolderId;
                child.IsDirty = true;
            }
        }

        foreach (var item in targets)
            ContentBrowserItems.Remove(item);

        CloseEditorSessionsForAssetIds(deletingIds);

        ContentBrowserListBox.SelectedItems.Clear();
        _contentRangeAnchor = null;
        RefreshContentBrowserViews();
        PersistAssetLibrary();
        SetStatus($"已删除{targets.Count} 个资产。");
        return true;
    }

    private void ContentBrowserThemed_PreviewDrop(object sender, WpfDragEventArgs e)
    {
        if (!TryGetDraggedContentAssets(e, out var sources))
            return;

        var target = GetContentDropTargetFolder(e.OriginalSource as System.Windows.DependencyObject);
        if (target is null)
        {
            e.Effects = WpfDragDropEffects.None;
            e.Handled = true;
            return;
        }

        var movableSources = GetDroppableTopLevelContentAssets(sources, target, copy: false);
        var copyableSources = GetDroppableTopLevelContentAssets(sources, target, copy: true);
        if (movableSources.Count == 0 && copyableSources.Count == 0)
        {
            e.Effects = WpfDragDropEffects.None;
            e.Handled = true;
            return;
        }

        var choice = ShowContentDropActionDialogThemed(sources.Count == 1 ? sources[0].Name : $"{sources.Count} 个资产");
    }

    private ContentDropAction ShowContentDropActionDialogThemed(string assetName)
    {
        var result = ThemedDialog.ShowCustom(this, $"选择对资产{assetName}的操作：", "拖拽资产", MessageBoxImage.Question, new ThemedDialogButton("移动到此", MessageBoxResult.Yes, true), new ThemedDialogButton("复制到此", MessageBoxResult.No), new ThemedDialogButton("取消", MessageBoxResult.Cancel));
        return result switch
        {
            MessageBoxResult.Yes => ContentDropAction.Move,
            MessageBoxResult.No => ContentDropAction.Copy,
            _ => ContentDropAction.Cancel,
        };
    }
}
