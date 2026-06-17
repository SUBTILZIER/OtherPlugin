using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Services;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfKey = System.Windows.Input.Key;
using WpfKeyboard = System.Windows.Input.Keyboard;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfModifierKeys = System.Windows.Input.ModifierKeys;
using WpfMouseButton = System.Windows.Input.MouseButton;
using WpfMouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using WpfMouseButtonEventHandler = System.Windows.Input.MouseButtonEventHandler;
using WpfSolidColorBrush = System.Windows.Media.SolidColorBrush;
using WpfTextBlock = System.Windows.Controls.TextBlock;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfTextChangedEventArgs = System.Windows.Controls.TextChangedEventArgs;
using WpfUIElement = System.Windows.UIElement;
using WpfVisualTreeHelper = System.Windows.Media.VisualTreeHelper;

namespace AutomationStudioWpf;

public partial class MainWindow
{
    private const int AutoFitReadyStableFrames = 2;
    private const int AutoFitMaxRenderFrames = 90;

    private WpfTextBox? _contentBrowserSearchBox;
    private bool _isApplyingContentBrowserSearch;
    private bool _contentBrowserSearchRefreshQueued;
    private ContentBrowserIndex? _contentBrowserIndex;
    private bool _navigationFeaturesInstalled;
    private bool _autoFitGraphQueued;
    private bool _autoFitRenderingAttached;
    private bool _assetCompileButtonStateQueued;
    private ObservableCollection<GraphListItemViewModel>? _attachedGraphListItems;
    private ObservableCollection<GraphListItemViewModel>? _attachedFunctionListItems;
    private int _autoFitStableFrames;
    private int _autoFitFramesRemaining;

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        Loaded += MainWindow_NavigationFeaturesLoaded;
    }

    private void MainWindow_NavigationFeaturesLoaded(object sender, RoutedEventArgs e)
    {
        if (_navigationFeaturesInstalled)
            return;

        _navigationFeaturesInstalled = true;
        Loaded -= MainWindow_NavigationFeaturesLoaded;

        InstallContentBrowserSearchBox();
        AttachActiveEditorService(_editorService);
        AttachGraphCollectionChangeHandlers();
        ContentBrowserItems.CollectionChanged += GraphCollections_AssetCompileStateChanged;
        AddHandler(WpfUIElement.PreviewMouseLeftButtonDownEvent, new WpfMouseButtonEventHandler(GraphCallableNode_PreviewMouseLeftButtonDown), true);
        AddHandler(WpfKeyboard.PreviewKeyDownEvent, new System.Windows.Input.KeyEventHandler(MainWindow_NavigationPreviewKeyDown), true);
        ContentBrowserListBox.PreviewKeyDown += ContentBrowserListBox_NavigationPreviewKeyDown;
        ContentVisibleItems.CollectionChanged += ContentVisibleItems_SearchRefreshRequested;
        ScheduleFitActiveGraphToView();
        QueueAssetCompileButtonStateUpdate();
    }

    private void NavigationFeatures_GraphChanged()
    {
        if (_activeAssetController?.IsLoadingGraph == true)
            ScheduleFitActiveGraphToView();

        QueueAssetCompileButtonStateUpdate();
    }

    private void GraphCollections_AssetCompileStateChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        QueueAssetCompileButtonStateUpdate();
    }

    private void AttachGraphCollectionChangeHandlers()
    {
        if (_attachedGraphListItems is not null)
            _attachedGraphListItems.CollectionChanged -= GraphCollections_AssetCompileStateChanged;
        if (_attachedFunctionListItems is not null)
            _attachedFunctionListItems.CollectionChanged -= GraphCollections_AssetCompileStateChanged;

        _attachedGraphListItems = GraphListItems;
        _attachedFunctionListItems = FunctionListItems;

        _attachedGraphListItems.CollectionChanged += GraphCollections_AssetCompileStateChanged;
        _attachedFunctionListItems.CollectionChanged += GraphCollections_AssetCompileStateChanged;
        QueueAssetCompileButtonStateUpdate();
    }

    private void QueueAssetCompileButtonStateUpdate()
    {
        if (_assetCompileButtonStateQueued)
            return;

        _assetCompileButtonStateQueued = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _assetCompileButtonStateQueued = false;
            ApplyAssetCompileButtonState();
        }), DispatcherPriority.ContextIdle);
    }

    private void ApplyAssetCompileButtonState()
    {
        if (CompileGraphButton is null || CompileButtonText is null || CompileDirtyIcon is null)
            return;

        bool dirty = ActiveContentAssetHasCompileDirtyGraphs();
        CompileButtonText.Text = dirty ? "编译*" : "编译";
        CompileDirtyIcon.Visibility = dirty ? Visibility.Visible : Visibility.Collapsed;
        CompileGraphButton.Background = dirty
            ? AppBrush("CompileDirtyBackgroundBrush", 0x4B, 0x36, 0x1C)
            : AppBrush("ToolbarButtonBackgroundBrush", 0x20, 0x24, 0x2B);
        CompileGraphButton.BorderBrush = dirty
            ? AppBrush("CompileDirtyBorderBrush", 0xD6, 0x8A, 0x22)
            : AppBrush("ToolbarButtonBorderBrush", 0x2E, 0x34, 0x40);
    }

    private WpfBrush AppBrush(string key, byte r, byte g, byte b)
    {
        return TryFindResource(key) as WpfBrush ?? new WpfSolidColorBrush(WpfColor.FromRgb(r, g, b));
    }

    private bool ActiveContentAssetHasCompileDirtyGraphs()
    {
        var session = _activeEditorSession;
        var asset = session?.ContentAsset ?? _activeContentAsset;
        if (asset is null)
            return false;

        var graphItems = session?.GraphListItems ?? GraphListItems;
        var functionItems = session?.FunctionListItems ?? FunctionListItems;
        return asset.Kind switch
        {
            ContentAssetKind.Script => graphItems.Concat(functionItems).Any(item => item.IsCompileDirty),
            ContentAssetKind.FunctionLibrary => functionItems.Any(item => item.IsCompileDirty),
            _ => false,
        };
    }

    private void ScheduleFitActiveGraphToView()
    {
        if (_editorService.Nodes.Count == 0)
            return;

        _autoFitGraphQueued = true;
        _autoFitStableFrames = 0;
        _autoFitFramesRemaining = AutoFitMaxRenderFrames;
        if (_autoFitRenderingAttached)
            return;

        _autoFitRenderingAttached = true;
        CompositionTarget.Rendering += AutoFitGraphWhenLayoutIsReady;
    }

    private void AutoFitGraphWhenLayoutIsReady(object? sender, EventArgs e)
    {
        if (!_autoFitGraphQueued)
        {
            DetachAutoFitRendering();
            return;
        }

        _autoFitFramesRemaining--;
        if (IsGraphVisualLayoutReady())
        {
            _autoFitStableFrames++;
        }
        else
        {
            _autoFitStableFrames = 0;
        }

        if (_autoFitStableFrames < AutoFitReadyStableFrames && _autoFitFramesRemaining > 0)
            return;

        DetachAutoFitRendering();
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _autoFitGraphQueued = false;
            FitGraphToView();
        }), DispatcherPriority.ApplicationIdle);
    }

    private bool IsGraphVisualLayoutReady()
    {
        if (_editorService.Nodes.Count == 0)
            return false;
        if (TryGetActiveEditorSurface() is not { } surface)
            return false;
        if (surface.GraphViewport.ActualWidth <= 1 || surface.GraphViewport.ActualHeight <= 1)
            return false;
        if (surface.ActualWidth <= 1 || surface.ActualHeight <= 1)
            return false;

        return _editorService.Nodes.All(node =>
            !double.IsNaN(node.X) &&
            !double.IsNaN(node.Y) &&
            node.Width > 1 &&
            node.Height > 1);
    }

    private void DetachAutoFitRendering()
    {
        if (!_autoFitRenderingAttached)
            return;

        CompositionTarget.Rendering -= AutoFitGraphWhenLayoutIsReady;
        _autoFitRenderingAttached = false;
    }

    private void InstallContentBrowserSearchBox()
    {
        if (_contentBrowserSearchBox is not null)
            return;

        var label = new WpfTextBlock
        {
            Text = "搜索",
            Foreground = WpfBrushes.White,
            Margin = new Thickness(14, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12,
        };

        _contentBrowserSearchBox = new WpfTextBox
        {
            Width = 240,
            Height = 22,
            Margin = new Thickness(0, 4, 8, 4),
            Padding = new Thickness(6, 2, 6, 2),
            ToolTip = "搜索当前文件夹及所有子文件夹中的资产。支持空格分隔关键字、模糊匹配、不区分大小写。",
        };
        _contentBrowserSearchBox.TextChanged += ContentBrowserSearchBox_TextChanged;

        var hint = new WpfTextBlock
        {
            Text = "双击打开，Ctrl+B 定位真实路径",
            Foreground = AppBrush("StatusMutedBrush", 0x7A, 0x87, 0x97),
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 11,
        };

        ContentBrowserHeaderBar.Children.Add(label);
        ContentBrowserHeaderBar.Children.Add(_contentBrowserSearchBox);
        ContentBrowserHeaderBar.Children.Add(hint);
    }

    private void ContentBrowserSearchBox_TextChanged(object sender, WpfTextChangedEventArgs e)
    {
        if (_isApplyingContentBrowserSearch)
            return;

        if (string.IsNullOrWhiteSpace(GetContentBrowserSearchText()))
        {
            RefreshContentBrowserViews();
            SetStatus("内容浏览器搜索已清空。");
            return;
        }

        ApplyContentBrowserSearchResults(updateStatus: true);
    }

    private void ContentVisibleItems_SearchRefreshRequested(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_isApplyingContentBrowserSearch || string.IsNullOrWhiteSpace(GetContentBrowserSearchText()))
            return;
        if (_contentBrowserSearchRefreshQueued)
            return;

        _contentBrowserSearchRefreshQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _contentBrowserSearchRefreshQueued = false;
            if (!string.IsNullOrWhiteSpace(GetContentBrowserSearchText()))
                ApplyContentBrowserSearchResults(updateStatus: false);
        });
    }

    private void ApplyContentBrowserSearchResults(bool updateStatus)
    {
        string query = GetContentBrowserSearchText();
        if (string.IsNullOrWhiteSpace(query))
            return;

        _isApplyingContentBrowserSearch = true;
        try
        {
            var tokens = SplitContentSearchTokens(query);
            var index = GetContentBrowserIndex();
            var results = index.SearchEntries
                .Where(entry => index.IsInScope(entry.Asset, _currentContentFolderId))
                .Where(entry => ContentAssetMatchesQuery(entry, tokens))
                .OrderByDescending(entry => entry.Asset.IsFolder)
                .ThenBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
                .Select(entry => entry.Asset)
                .ToList();

            ContentVisibleItems.ReplaceAll(results);

            if (updateStatus)
                SetStatus($"搜索“{query}”：找到 {results.Count} 个资产。");
        }
        finally
        {
            _isApplyingContentBrowserSearch = false;
        }
    }

    private string GetContentBrowserSearchText() => _contentBrowserSearchBox?.Text.Trim() ?? string.Empty;

    private ContentBrowserIndex GetContentBrowserIndex() =>
        _contentBrowserIndex ??= new ContentBrowserIndex(ContentBrowserItems);

    private static bool ContentAssetMatchesQuery(ContentAssetSearchEntry entry, IReadOnlyList<string> tokens)
    {
        return tokens.Count == 0 ||
               tokens.All(token => ContainsIgnoreCase(entry.SearchableText, token) ||
                                   IsFuzzyMatch(entry.SearchableText, token));
    }

    private static string[] SplitContentSearchTokens(string query) =>
        query.Split([' ', '\t', '/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool ContainsIgnoreCase(string text, string token) =>
        text.Contains(token, StringComparison.OrdinalIgnoreCase);

    private static bool IsFuzzyMatch(string text, string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return true;

        int tokenIndex = 0;
        foreach (char ch in text)
        {
            if (char.ToUpperInvariant(ch) != char.ToUpperInvariant(token[tokenIndex]))
                continue;

            tokenIndex++;
            if (tokenIndex == token.Length)
                return true;
        }

        return false;
    }

    private string GetContentAssetPath(ContentAssetViewModel item)
    {
        return GetContentBrowserIndex().GetContentAssetPath(item);
    }

    private void MainWindow_NavigationPreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Handled || (WpfKeyboard.Modifiers & WpfModifierKeys.Control) == 0 || e.Key != WpfKey.B)
            return;
        if (WpfKeyboard.FocusedElement is WpfTextBox)
            return;

        if (IsFocusInside(ContentBrowserListBox) && ContentBrowserListBox.SelectedItem is ContentAssetViewModel selectedAsset)
        {
            LocateContentAsset(selectedAsset);
            e.Handled = true;
            return;
        }

        if (_activeContentAsset is not null)
        {
            LocateContentAsset(_activeContentAsset);
            e.Handled = true;
        }
    }

    private void ContentBrowserListBox_NavigationPreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        if ((WpfKeyboard.Modifiers & WpfModifierKeys.Control) == 0 || e.Key != WpfKey.B)
            return;

        if (ContentBrowserListBox.SelectedItem is not ContentAssetViewModel asset)
            return;

        LocateContentAsset(asset);
        e.Handled = true;
    }

    private void LocateContentAsset(ContentAssetViewModel asset)
    {
        if (_contentBrowserSearchBox is not null && !string.IsNullOrWhiteSpace(_contentBrowserSearchBox.Text))
            _contentBrowserSearchBox.Text = string.Empty;

        _currentContentFolderId = asset.ParentFolderId;
        ExpandFolderPath(_currentContentFolderId);
        RefreshContentBrowserViews();
        ContentFolderListBox.SelectedItem = _currentContentFolderId is null
            ? _rootContentFolder
            : ContentFolderItems.FirstOrDefault(folder => folder.Id == _currentContentFolderId);
        ContentBrowserListBox.SelectedItem = asset;
        ContentBrowserListBox.ScrollIntoView(asset);
        ContentBrowserListBox.Focus();
        _contentFolderSelectionActive = false;
        SetStatus($"已定位资产：{GetContentAssetPath(asset)}");
    }

    private void GraphCallableNode_PreviewMouseLeftButtonDown(object sender, WpfMouseButtonEventArgs e)
    {
        if (e.ChangedButton != WpfMouseButton.Left || e.ClickCount < 2)
            return;
        if (e.OriginalSource is not DependencyObject source)
            return;

        if (TryOpenCallableGraphFromSource(source))
            e.Handled = true;
    }

    private bool TryOpenCallableGraphFromSource(DependencyObject source)
    {
        var node = FindAncestorDataContext<NodeBaseViewModel>(source);
        return node switch
        {
            FunctionCallNodeViewModel functionCall when !string.IsNullOrWhiteSpace(functionCall.FunctionId) =>
                OpenCallableGraph(functionCall.FunctionId, GraphAssetKind.Function),
            _ => false,
        };
    }

    private bool OpenCallableGraph(string graphId, GraphAssetKind kind)
    {
        CommitInspectorAndSnapshotAllSessions();

        var target = FindCallableGraphLocation(graphId, kind);
        if (target is null)
        {
            SetStatus("找不到函数目标，可能已删除或未公开到库。");
            return false;
        }

        OpenOrActivateAsset(target.Asset, target.Graph, kind);

        if (TryGetActiveEditorSurface() is not { } surface)
            return false;
        var listBox = surface.FunctionListBox;
        var controller = _functionListController;
        controller.SetSectionExpanded(true);
        SaveSectionExpansionForActiveAsset(controller);
        listBox.SelectedItem = target.Graph;
        listBox.ScrollIntoView(target.Graph);
        listBox.Focus();
        UpdateGraphSectionVisibility();
        ScheduleFitActiveGraphToView();
        QueueAssetCompileButtonStateUpdate();

        SetStatus($"已跳转到函数：{GetContentAssetPath(target.Asset)}/{target.Graph.Name}");
        return true;
    }

    private CallableGraphLocation? FindCallableGraphLocation(string graphId, GraphAssetKind kind)
    {
        foreach (var asset in EnumerateCallableSearchAssets())
        {
            foreach (var graph in asset.Functions)
            {
                if (string.Equals(graph.Id, graphId, StringComparison.Ordinal))
                    return new CallableGraphLocation(asset, graph);
            }
        }

        return null;
    }

    private IEnumerable<ContentAssetViewModel> EnumerateCallableSearchAssets()
    {
        if (_activeContentAsset is not null)
            yield return _activeContentAsset;

        foreach (var asset in ContentBrowserItems)
        {
            if (_activeContentAsset is not null && string.Equals(asset.Id, _activeContentAsset.Id, StringComparison.Ordinal))
                continue;
            if (asset.Kind is ContentAssetKind.Script or ContentAssetKind.FunctionLibrary)
                yield return asset;
        }
    }

    private static T? FindAncestorDataContext<T>(DependencyObject source)
        where T : class
    {
        DependencyObject? current = source;
        while (current is not null)
        {
            if (current is FrameworkElement { DataContext: T value })
                return value;

            current = GetSafeVisualOrLogicalParent(current);
        }

        return null;
    }

    private sealed record CallableGraphLocation(ContentAssetViewModel Asset, GraphListItemViewModel Graph);
}
