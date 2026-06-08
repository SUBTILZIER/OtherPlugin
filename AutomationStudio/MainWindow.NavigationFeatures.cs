using System.Collections.Specialized;
using System.Windows;
using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Services;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfControl = System.Windows.Controls.Control;
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
using WpfVisualTreeHelper = System.Windows.Media.VisualTreeHelper;

namespace AutomationStudioWpf;

public partial class MainWindow
{
    private WpfTextBox? _contentBrowserSearchBox;
    private bool _isApplyingContentBrowserSearch;
    private bool _contentBrowserSearchRefreshQueued;
    private bool _navigationFeaturesInstalled;

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
        AddHandler(WpfControl.MouseDoubleClickEvent, new WpfMouseButtonEventHandler(GraphCallableNode_MouseDoubleClick), true);
        ContentBrowserListBox.PreviewKeyDown += ContentBrowserListBox_NavigationPreviewKeyDown;
        ContentVisibleItems.CollectionChanged += ContentVisibleItems_SearchRefreshRequested;
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
            Foreground = new WpfSolidColorBrush(WpfColor.FromRgb(122, 135, 151)),
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
            var results = ContentBrowserItems
                .Where(IsInCurrentContentSearchScope)
                .Where(item => ContentAssetMatchesQuery(item, query))
                .OrderByDescending(item => item.IsFolder)
                .ThenBy(GetContentAssetPath, StringComparer.OrdinalIgnoreCase)
                .ToList();

            ContentVisibleItems.Clear();
            foreach (var item in results)
                ContentVisibleItems.Add(item);

            if (updateStatus)
                SetStatus($"搜索“{query}”：找到 {results.Count} 个资产。");
        }
        finally
        {
            _isApplyingContentBrowserSearch = false;
        }
    }

    private string GetContentBrowserSearchText() => _contentBrowserSearchBox?.Text.Trim() ?? string.Empty;

    private bool IsInCurrentContentSearchScope(ContentAssetViewModel item)
    {
        if (_currentContentFolderId is null)
            return true;

        string? parentId = item.ParentFolderId;
        while (!string.IsNullOrWhiteSpace(parentId))
        {
            if (string.Equals(parentId, _currentContentFolderId, StringComparison.Ordinal))
                return true;

            parentId = ContentBrowserItems.FirstOrDefault(candidate => candidate.Id == parentId)?.ParentFolderId;
        }

        return false;
    }

    private bool ContentAssetMatchesQuery(ContentAssetViewModel item, string query)
    {
        var tokens = query
            .Split([' ', '\t', '/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
            return true;

        string searchable = $"{item.Name} {item.DisplayName} {item.Kind} {GetContentAssetPath(item)}";
        return tokens.All(token => ContainsIgnoreCase(searchable, token) || IsFuzzyMatch(searchable, token));
    }

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
        var names = new Stack<string>();
        names.Push(item.Name);

        string? parentId = item.ParentFolderId;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (!string.IsNullOrWhiteSpace(parentId) && visited.Add(parentId))
        {
            var parent = ContentBrowserItems.FirstOrDefault(candidate => candidate.Id == parentId);
            if (parent is null)
                break;

            names.Push(parent.Name);
            parentId = parent.ParentFolderId;
        }

        return names.Count == 0 ? item.Name : string.Join("/", names);
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

    private void GraphCallableNode_MouseDoubleClick(object sender, WpfMouseButtonEventArgs e)
    {
        if (e.ChangedButton != WpfMouseButton.Left)
            return;
        if (e.OriginalSource is not DependencyObject source)
            return;

        var node = FindAncestorDataContext<NodeBaseViewModel>(source);
        switch (node)
        {
            case FunctionCallNodeViewModel functionCall when !string.IsNullOrWhiteSpace(functionCall.FunctionId):
                if (OpenCallableGraph(functionCall.FunctionId, GraphAssetKind.Function))
                    e.Handled = true;
                break;
            case MacroCallNodeViewModel macroCall when !string.IsNullOrWhiteSpace(macroCall.MacroId):
                if (OpenCallableGraph(macroCall.MacroId, GraphAssetKind.Macro))
                    e.Handled = true;
                break;
        }
    }

    private bool OpenCallableGraph(string graphId, GraphAssetKind kind)
    {
        SaveVisibleGraphsToActiveContent();

        var target = FindCallableGraphLocation(graphId, kind);
        if (target is null)
        {
            SetStatus(kind == GraphAssetKind.Function
                ? "找不到函数目标，可能已删除或未公开到库。"
                : "找不到宏目标，可能已删除或未公开到库。");
            return false;
        }

        bool sameAsset = _activeContentAsset is not null &&
                         string.Equals(_activeContentAsset.Id, target.Asset.Id, StringComparison.Ordinal);
        if (!sameAsset)
        {
            OpenContentAsset(target.Asset);
        }
        else
        {
            SnapshotActiveAsset();
        }

        var controller = kind == GraphAssetKind.Function ? _functionListController : _macroListController;
        controller.SetSectionExpanded(true);
        SaveSectionExpansionForActiveAsset(controller);
        LoadGraphItem(controller, target.Graph, snapshotCurrent: false);
        UpdateGraphSectionVisibility();

        SetStatus(kind == GraphAssetKind.Function
            ? $"已跳转到函数：{target.Asset.Name}/{target.Graph.Name}"
            : $"已跳转到宏：{target.Asset.Name}/{target.Graph.Name}");
        return true;
    }

    private CallableGraphLocation? FindCallableGraphLocation(string graphId, GraphAssetKind kind)
    {
        foreach (var asset in EnumerateCallableSearchAssets())
        {
            var graphs = kind == GraphAssetKind.Function ? asset.Functions : asset.Macros;
            foreach (var graph in graphs)
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
            if (asset.Kind is ContentAssetKind.Script or ContentAssetKind.FunctionLibrary or ContentAssetKind.MacroLibrary)
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

            current = WpfVisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private sealed record CallableGraphLocation(ContentAssetViewModel Asset, GraphListItemViewModel Graph);
}
