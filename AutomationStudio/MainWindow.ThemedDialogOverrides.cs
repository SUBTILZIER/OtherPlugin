using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using AutomationStudioWpf.Controls;
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
using WpfListBox = System.Windows.Controls.ListBox;
using WpfMenuItem = System.Windows.Controls.MenuItem;
using WpfMessageBoxButton = System.Windows.MessageBoxButton;
using WpfMessageBoxImage = System.Windows.MessageBoxImage;
using WpfMessageBoxResult = System.Windows.MessageBoxResult;
using WpfModifierKeys = System.Windows.Input.ModifierKeys;
using WpfRoutedEventArgs = System.Windows.RoutedEventArgs;
using WpfRoutedEventHandler = System.Windows.RoutedEventHandler;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace AutomationStudioWpf;

public partial class MainWindow
{
    private bool _themedDialogOverridesInstalled;
    private readonly HashSet<EditorSurfaceControl> _themedGraphListHandlerSurfaces = [];

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        InstallThemedDialogOverrides();
    }

    private void InstallThemedDialogOverrides()
    {
        if (_themedDialogOverridesInstalled)
            return;

        _themedDialogOverridesInstalled = true;
        InstallContentBrowserEnhancedInteractions();

        Closing -= Window_Closing;
        Closing += Window_ClosingThemed;

        RunGraphButton.Click -= RunGraph_Click;
        RunGraphButton.Click += RunGraph_ClickThemed;

        ReplaceToolbarButton("保存", SaveGraph_Click, SaveGraph_ClickThemed);
        ReplaceToolbarButton("打开图谱", OpenGraph_Click, OpenGraph_ClickThemed);

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

        InstallGraphListHandlersForActiveSurface();
    }

    private void InstallGraphListHandlersForActiveSurface()
    {
        if (TryGetActiveEditorSurface() is { } surface)
            InstallGraphListHandlersForSurface(surface, surface.SurfaceContext);
    }

    private void InstallGraphListHandlersForSurface(EditorSurfaceControl surface, EditorSurfaceContext? context = null)
    {
        context ??= surface.SurfaceContext;
        if (context is null)
            return;

        if (!_themedGraphListHandlerSurfaces.Add(surface))
            return;

        ReplaceGraphListHandlers(surface.GraphListBox, context.GraphListController, GraphListBox_KeyDown);
        ReplaceGraphListHandlers(surface.FunctionListBox, context.FunctionListController, FunctionListBox_KeyDown);
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

    private void ReplaceGraphListHandlers(WpfListBox listBox, GraphListController controller, WpfKeyEventHandler oldKeyHandler)
    {
        listBox.KeyDown -= oldKeyHandler;
        listBox.KeyDown += (_, e) => GraphListBox_ThemedKeyDown(controller, listBox, e);

        if (listBox.ContextMenu is null)
            return;

        foreach (var menuItem in listBox.ContextMenu.Items.OfType<WpfMenuItem>()
                     .Where(item => string.Equals(item.Header?.ToString(), "删除", StringComparison.Ordinal)))
        {
            menuItem.Click -= DeleteGraphMenuItem_Click;
            menuItem.Click += (_, e) =>
            {
                DeleteGraphSelectedThemed(controller, listBox);
                SaveSectionExpansionForActiveAsset(controller);
                UpdateGraphSectionVisibility();
                e.Handled = true;
            };
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
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "打开图谱",
            Filter = "图谱文件 (*.json)|*.json|所有文件(*.*)|*.*",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            _graphListController.ImportFile(dialog.FileName);
        }
        catch (Exception ex)
        {
            ThemedDialog.Show(this, ex.Message, "打开失败", WpfMessageBoxButton.OK, WpfMessageBoxImage.Error);
        }
    }

    private async void RunGraph_ClickThemed(object sender, WpfRoutedEventArgs e)
    {
        if (!EnsureCompiledBeforeRunThemed())
            return;

        var activeSessionController = _activeEditorSession is null ? _activeAssetController : GetSessionActiveAssetController(_activeEditorSession);
        if (_activeContentAsset?.Kind != ContentAssetKind.Script || !ReferenceEquals(activeSessionController, _graphListController))
        {
            ThemedDialog.Show(this, "只有脚本里的事件图可以直接执行。请从内容浏览器打开脚本，并进入事件图。", "不能执行", WpfMessageBoxButton.OK, WpfMessageBoxImage.Information);
            return;
        }

        CommitInspectorAndSnapshotAllSessions();
        await _executionController.RunAsync();
    }

    private void Window_ClosingThemed(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _scriptRunManager.StopAll();
        _executionController.ReleaseAllKeys();
        if (_isClosing) return;

        CommitInspectorAndSnapshotAllSessions();
        if (ContentBrowserItems.Any(item => item.IsDirty) ||
            GraphListItems.Concat(FunctionListItems).Any(item => item.IsDirty))
        {
            var result = ThemedDialog.ShowCustom(
                this,
                "存在未保存资产，是否保存？",
                "是否保存",
                WpfMessageBoxImage.Question,
                new ThemedDialogButton("保存", WpfMessageBoxResult.Yes, true),
                new ThemedDialogButton("不保存", WpfMessageBoxResult.No),
                new ThemedDialogButton("取消", WpfMessageBoxResult.Cancel));

            if (result == WpfMessageBoxResult.Cancel)
            {
                e.Cancel = true;
                return;
            }

            if (result == WpfMessageBoxResult.Yes)
                SaveAllAssets();
        }

        if (e.Cancel)
            return;

        _isClosing = true;
        _scriptRunManager.Dispose();
        _scriptHotkeyService.Dispose();
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
            WpfMessageBoxImage.Question,
            new ThemedDialogButton("先编译", WpfMessageBoxResult.Yes, true),
            new ThemedDialogButton("直接保存", WpfMessageBoxResult.No),
            new ThemedDialogButton("取消", WpfMessageBoxResult.Cancel));

        if (result == WpfMessageBoxResult.Cancel)
            return false;
        if (result == WpfMessageBoxResult.Yes)
            return CompileAllAssets(showPrompt: false);
        return true;
    }

    private bool EnsureCompiledBeforeRunThemed()
    {
        return EnsureCompiledBeforeRun();
    }

    private void GraphListBox_ThemedKeyDown(GraphListController controller, WpfListBox listBox, WpfKeyEventArgs e)
    {
        if (WpfKeyboard.FocusedElement is WpfTextBox)
            return;

        if (e.Key == WpfKey.Delete)
        {
            DeleteGraphSelectedThemed(controller, listBox);
            SaveSectionExpansionForActiveAsset(controller);
            UpdateGraphSectionVisibility();
            e.Handled = true;
        }
        else if (e.Key == WpfKey.F2)
        {
            controller.RenameSelected();
            e.Handled = true;
        }
    }

    private void DeleteGraphSelectedThemed(GraphListController controller, WpfListBox listBox)
    {
        var selected = controller.SelectedItem;
        if (selected is null)
            return;

        var result = ThemedDialog.ShowCustom(
            this,
            $"是否删除{GetGraphDisplayName(controller)}：{selected.Name}？",
            $"删除{GetGraphDisplayName(controller)}",
            WpfMessageBoxImage.Question,
            new ThemedDialogButton("删除", WpfMessageBoxResult.Yes, true),
            new ThemedDialogButton("取消", WpfMessageBoxResult.Cancel));
        if (result != WpfMessageBoxResult.Yes)
            return;

        var items = controller.Items.ToList();
        int oldIndex = items.IndexOf(selected);
        bool deletingActive = ReferenceEquals(selected, controller.ActiveItem);
        if (controller.Items is ICollection<GraphListItemViewModel> collection)
            collection.Remove(selected);

        if (!controller.Items.Any())
        {
            controller.ClearActive();
            _editorService.ClearGraph();
            _graphCommandService.Clear();
            controller.RefreshSectionExpansion();
        }
        else
        {
            var next = controller.Items.ElementAt(Math.Clamp(oldIndex, 0, controller.Items.Count() - 1));
            listBox.SelectedItem = next;
            if (deletingActive)
                LoadGraphItem(controller, next, snapshotCurrent: false);
        }

        controller.Persist();
    }

    private string GetGraphDisplayName(GraphListController controller)
    {
        return ReferenceEquals(controller, _functionListController) ? "函数" : "事件图";
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

        string message = targets.Count == 1
            ? $"是否删除：{targets[0].Name}？"
            : $"是否删除 {targets.Count} 个资产？\n\n{string.Join("\n", targets.Take(8).Select(item => "- " + item.Name))}{(targets.Count > 8 ? "\n..." : string.Empty)}";

        var result = ThemedDialog.ShowCustom(this, message, "删除资产", WpfMessageBoxImage.Question, new ThemedDialogButton("删除", WpfMessageBoxResult.Yes, true), new ThemedDialogButton("取消", WpfMessageBoxResult.Cancel));
        if (result != WpfMessageBoxResult.Yes)
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
        SetStatus($"已删除 {targets.Count} 个资产。");
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
        if (choice == ContentDropAction.Cancel)
        {
            e.Effects = WpfDragDropEffects.None;
            e.Handled = true;
            return;
        }

        bool copy = choice == ContentDropAction.Copy;
        var acceptedSources = copy ? copyableSources : movableSources;
        if (acceptedSources.Count == 0)
        {
            SetStatus(copy ? "没有可复制到此处的资产。" : "没有可移动到此处的资产。");
            e.Effects = WpfDragDropEffects.None;
            e.Handled = true;
            return;
        }

        ApplyContentAssetDrop(acceptedSources, target, copy, alreadyFiltered: true);
        e.Effects = copy ? WpfDragDropEffects.Copy : WpfDragDropEffects.Move;
        e.Handled = true;
    }

    private ContentDropAction ShowContentDropActionDialogThemed(string assetName)
    {
        var result = ThemedDialog.ShowCustom(this, $"选择对资产“{assetName}”的操作：", "拖拽资产", WpfMessageBoxImage.Question, new ThemedDialogButton("移动到此处", WpfMessageBoxResult.Yes, true), new ThemedDialogButton("复制到此处", WpfMessageBoxResult.No), new ThemedDialogButton("取消", WpfMessageBoxResult.Cancel));
        return result switch
        {
            WpfMessageBoxResult.Yes => ContentDropAction.Move,
            WpfMessageBoxResult.No => ContentDropAction.Copy,
            _ => ContentDropAction.Cancel,
        };
    }
}
