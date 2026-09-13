using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Interaction;
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
using WpfComboBox = System.Windows.Controls.ComboBox;
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
    private WpfTextBlock? _contentBrowserHint;
    private System.Windows.Controls.StackPanel? _contentBreadcrumbBar;
    private WpfComboBox? _contentTypeFilter;
    private readonly DispatcherTimer _contentFilterSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private bool _isApplyingContentBrowserSearch;
    private bool _contentBrowserSearchRefreshQueued;
    private ContentBrowserIndex? _contentBrowserIndex;
    private bool _navigationFeaturesInstalled;
    private bool _autoFitGraphQueued;
    private bool _autoFitRenderingAttached;
    private EditorSessionViewModel? _autoFitSession;
    private GraphListItemViewModel? _autoFitGraph;
    private bool _suppressAutoFitOnGraphLoad;
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
        if (!_suppressAutoFitOnGraphLoad && _activeAssetController?.IsLoadingGraph == true)
            ScheduleFitActiveGraphToView();

        QueueAssetCompileButtonStateUpdate();
    }

    private void RunWithoutAutoFitOnGraphLoad(Action action)
    {
        bool previous = _suppressAutoFitOnGraphLoad;
        _suppressAutoFitOnGraphLoad = true;
        try
        {
            action();
        }
        finally
        {
            _suppressAutoFitOnGraphLoad = previous;
        }
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
        IsActiveAssetCompileDirty = ActiveContentAssetHasCompileDirtyGraphs();
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
        _autoFitSession = _activeEditorSession;
        _autoFitGraph = _activeAssetController?.ActiveItem;
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

        if (!ReferenceEquals(_autoFitSession, _activeEditorSession) ||
            !ReferenceEquals(_autoFitGraph, _activeAssetController?.ActiveItem))
        {
            _autoFitGraphQueued = false;
            _autoFitSession = null;
            _autoFitGraph = null;
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
            if (!ReferenceEquals(_autoFitSession, _activeEditorSession) ||
                !ReferenceEquals(_autoFitGraph, _activeAssetController?.ActiveItem))
                return;

            _autoFitGraphQueued = false;
            _autoFitSession = null;
            _autoFitGraph = null;
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

    private void DetachNavigationFeatureHandlers()
    {
        Loaded -= MainWindow_NavigationFeaturesLoaded;
        if (!_navigationFeaturesInstalled)
            return;

        ContentBrowserItems.CollectionChanged -= GraphCollections_AssetCompileStateChanged;
        ContentVisibleItems.CollectionChanged -= ContentVisibleItems_SearchRefreshRequested;
        if (_attachedGraphListItems is not null)
            _attachedGraphListItems.CollectionChanged -= GraphCollections_AssetCompileStateChanged;
        if (_attachedFunctionListItems is not null)
            _attachedFunctionListItems.CollectionChanged -= GraphCollections_AssetCompileStateChanged;
        _attachedGraphListItems = null;
        _attachedFunctionListItems = null;

        RemoveHandler(WpfUIElement.PreviewMouseLeftButtonDownEvent, new WpfMouseButtonEventHandler(GraphCallableNode_PreviewMouseLeftButtonDown));
        RemoveHandler(WpfKeyboard.PreviewKeyDownEvent, new System.Windows.Input.KeyEventHandler(MainWindow_NavigationPreviewKeyDown));
        ContentBrowserListBox.PreviewKeyDown -= ContentBrowserListBox_NavigationPreviewKeyDown;
        if (_contentBrowserSearchBox is not null)
            _contentBrowserSearchBox.TextChanged -= ContentBrowserSearchBox_TextChanged;
        ContentBrowserHeaderBar.SizeChanged -= ContentBrowserHeaderBar_SizeChanged;
        _navigationFeaturesInstalled = false;
    }

    private void InstallContentBrowserSearchBox()
    {
        if (_contentBrowserSearchBox is not null)
            return;

        var label = new WpfTextBlock
        {
            Text = "搜索",
            Foreground = AppBrush("ForegroundPrimaryBrush", 0xE6, 0xED, 0xF5),
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
            ToolTip = "支持关键字、模糊匹配、type:script、type:function、type:folder。",
        };
        _contentBrowserSearchBox.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, "内容搜索");
        _contentBrowserSearchBox.TabIndex = 29;
        _contentBrowserSearchBox.TextChanged += ContentBrowserSearchBox_TextChanged;

        var hint = new WpfTextBlock
        {
            Text = "双击打开，Ctrl+B 定位真实路径",
            Foreground = AppBrush("StatusMutedBrush", 0x7A, 0x87, 0x97),
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 11,
        };
        _contentBrowserHint = hint;
        _contentBrowserSearchBox.Text = _appSettings.ContentBrowserFilter;
        _contentBreadcrumbBar = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(4, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };

        ContentBrowserHeaderBar.Children.Add(label);
        ContentBrowserHeaderBar.Children.Add(_contentBrowserSearchBox);
        ContentBrowserHeaderBar.Children.Add(hint);
        ContentBrowserHeaderBar.Children.Add(_contentBreadcrumbBar);
        _contentTypeFilter = new WpfComboBox { Width = 90, Height = 22, Margin = new Thickness(0,4,6,4), ToolTip = "按资产类型过滤" };
        _contentTypeFilter.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, "资产类型过滤");
        _contentTypeFilter.Items.Add("全部"); _contentTypeFilter.Items.Add("脚本"); _contentTypeFilter.Items.Add("函数库"); _contentTypeFilter.Items.Add("文件夹"); _contentTypeFilter.SelectedIndex = _appSettings.ContentBrowserTypeFilter switch { "script" => 1, "function" => 2, "folder" => 3, _ => 0 };
        _contentTypeFilter.SelectionChanged += (_, _) => { _appSettings.ContentBrowserTypeFilter = _contentTypeFilter.SelectedIndex switch { 1=>"script",2=>"function",3=>"folder",_=>null }; try { _appSettingsService.Save(_appSettings); } catch { } if (_contentBrowserSearchBox is null) return; var q = System.Text.RegularExpressions.Regex.Replace(_contentBrowserSearchBox.Text, @"\btype:\S+", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim(); string? t = _contentTypeFilter.SelectedIndex switch { 1=>"script",2=>"function",3=>"folder",_=>null }; _contentBrowserSearchBox.Text = t is null ? q : (q + " type:" + t).Trim(); };
        ContentBrowserHeaderBar.Children.Add(_contentTypeFilter);
        ContentBrowserHeaderBar.SizeChanged += ContentBrowserHeaderBar_SizeChanged;
        UpdateContentBreadcrumb();
    }

    private void ContentBrowserHeaderBar_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_contentBrowserSearchBox is null || _contentBreadcrumbBar is null)
            return;
        double width = e.NewSize.Width;
        _contentBrowserSearchBox.Width = width < 640 ? 140 : width < 820 ? 180 : 240;
        if (_contentBrowserHint is not null)
            _contentBrowserHint.Visibility = width < 720 ? Visibility.Collapsed : Visibility.Visible;
        _contentBreadcrumbBar.MaxWidth = Math.Max(80, width < 720 ? 120 : width < 960 ? 220 : 360);
    }

    private void UpdateContentBreadcrumb()
    {
        if (_contentBreadcrumbBar is null)
            return;

        _contentBreadcrumbBar.Children.Clear();
        var map = ContentBrowserItems.ToDictionary(a => a.Id);
        var path = new List<ContentAssetViewModel>();
        var id = _currentContentFolderId;
        while (id is not null && map.TryGetValue(id, out var item))
        {
            path.Add(item);
            id = item.ParentFolderId;
        }
        path.Reverse();

        AddBreadcrumbButton("内容", null, path.Count == 0);
        for (int i = 0; i < path.Count; i++)
        {
            _contentBreadcrumbBar.Children.Add(new WpfTextBlock
            {
                Text = " / ",
                Foreground = AppBrush("StatusMutedBrush", 0x7A, 0x87, 0x97),
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 11,
            });
            AddBreadcrumbButton(path[i].Name, path[i], i == path.Count - 1);
        }
    }

    private void AddBreadcrumbButton(string text, ContentAssetViewModel? folder, bool isCurrent)
    {
        if (_contentBreadcrumbBar is null)
            return;
        var button = new System.Windows.Controls.Button
        {
            Content = text,
            Padding = new Thickness(3, 1, 3, 1),
            Margin = new Thickness(0),
            Background = WpfBrushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = AppBrush(isCurrent ? "EditorTextBrightBrush" : "AccentBrush", 0xE6, 0xED, 0xF5),
            IsEnabled = !isCurrent,
            ToolTip = isCurrent ? "当前目录" : $"进入{text}",
        };
        button.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, $"内容路径 {text}");
        button.Click += (_, _) => EnterContentFolder(folder);
        _contentBreadcrumbBar.Children.Add(button);
    }

    private void ContentBrowserSearchBox_TextChanged(object sender, WpfTextChangedEventArgs e)
    {
        if (_isApplyingContentBrowserSearch)
            return;
        _appSettings.ContentBrowserFilter = _contentBrowserSearchBox?.Text ?? string.Empty;
        _contentFilterSaveTimer.Stop(); _contentFilterSaveTimer.Tick -= ContentFilterSaveTimer_Tick; _contentFilterSaveTimer.Tick += ContentFilterSaveTimer_Tick; _contentFilterSaveTimer.Start();

        if (string.IsNullOrWhiteSpace(GetContentBrowserSearchText()))
        {
            RefreshContentBrowserViews();
            SetStatus("内容浏览器搜索已清空。");
            return;
        }

        ApplyContentBrowserSearchResults(updateStatus: true);
    }

    private void ContentFilterSaveTimer_Tick(object? sender, EventArgs e) { _contentFilterSaveTimer.Stop(); try { _appSettingsService.Save(_appSettings); } catch { } }

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
            var typeToken = tokens.FirstOrDefault(t => t.StartsWith("type:", StringComparison.OrdinalIgnoreCase));
            ContentAssetKind? typeFilter = typeToken is null ? null : typeToken[5..].ToLowerInvariant() switch { "script" => ContentAssetKind.Script, "function" or "functionlibrary" => ContentAssetKind.FunctionLibrary, "folder" => ContentAssetKind.Folder, _ => null };
            var index = GetContentBrowserIndex();
            var results = index.SearchEntries
                .Where(entry => index.IsInScope(entry.Asset, _currentContentFolderId))
                .Where(entry => typeFilter is null || entry.Asset.Kind == typeFilter)
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
               tokens.Where(token => !token.StartsWith("type:", StringComparison.OrdinalIgnoreCase)).All(token => ContainsIgnoreCase(entry.SearchableText, token) ||
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

        EditorSessionViewModel targetSession = OpenOrActivateAsset(target.Asset, target.Graph, kind);
        // Cross-asset navigation creates/activates a session. Refresh the chrome
        // before querying the newly active surface so its tab is visible immediately.
        UpdateEditorSessionChrome();

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
        QueueAssetCompileButtonStateUpdate();
        QueueCallableTargetSessionActivation(targetSession);

        SetStatus($"已跳转到函数：{GetContentAssetPath(target.Asset)}/{target.Graph.Name}");
        return true;
    }

    private void QueueCallableTargetSessionActivation(EditorSessionViewModel targetSession)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!_editorSessions.Contains(targetSession) || targetSession.DockMode == EditorDockMode.Detached)
                return;

            // The callable jump starts during PreviewMouseLeftButtonDown. A later
            // handler in that same input route can promote the source session again.
            // Re-activate without reloading after the routed event has completed.
            ActivateEditorSessionFromMainTab(targetSession);
            UpdateEditorSessionChrome();
        }), DispatcherPriority.Input);
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
