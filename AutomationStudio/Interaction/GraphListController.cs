using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Services;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfListBox = System.Windows.Controls.ListBox;
using WpfMessageBox = System.Windows.MessageBox;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace AutomationStudioWpf.Interaction;

public sealed class GraphListController
{
    private readonly Window _owner;
    private readonly GraphEditorService _editorService;
    private readonly GraphLibraryService _libraryService;
    private readonly NodeFactory _nodeFactory;
    private readonly WpfListBox _graphListBox;
    private readonly ObservableCollection<GraphListItemViewModel> _items;
    private readonly Action _syncNodeFactorySequence;
    private readonly Action _persistAll;
    private readonly Action<string> _setStatus;
    private readonly GraphAssetKind _kind;
    private readonly string _displayName;
    private readonly string _namePrefix;

    private GraphListItemViewModel? _activeItem;
    private bool _isLoadingGraph;
    private bool _isSectionExpanded;
    private bool _hasUserToggledSection;

    public GraphListController(
        Window owner,
        GraphEditorService editorService,
        GraphLibraryService libraryService,
        NodeFactory nodeFactory,
        WpfListBox graphListBox,
        ObservableCollection<GraphListItemViewModel> items,
        Action syncNodeFactorySequence,
        Action persistAll,
        GraphAssetKind kind,
        string displayName,
        string namePrefix,
        Action<string> setStatus)
    {
        _owner = owner;
        _editorService = editorService;
        _libraryService = libraryService;
        _nodeFactory = nodeFactory;
        _graphListBox = graphListBox;
        _items = items;
        _syncNodeFactorySequence = syncNodeFactorySequence;
        _persistAll = persistAll;
        _kind = kind;
        _displayName = displayName;
        _namePrefix = namePrefix;
        _setStatus = setStatus;
    }

    public bool IsLoadingGraph => _isLoadingGraph;

    public GraphListItemViewModel? ActiveItem => _activeItem;

    public GraphAssetKind AssetKind => _kind;

    public IEnumerable<GraphListItemViewModel> Items => _items;

    public GraphListItemViewModel? SelectedItem => _graphListBox.SelectedItem as GraphListItemViewModel;

    public bool IsSectionExpanded => _isSectionExpanded;

    public bool HasUserToggledSection => _hasUserToggledSection;

    public int ItemCount => _items.Count;

    public bool HasCompileDirtyItems => _items.Any(item => item.IsCompileDirty);

    public void SetSectionExpanded(bool expanded, bool userToggled = true)
    {
        _isSectionExpanded = expanded;
        if (userToggled)
            _hasUserToggledSection = true;
    }

    public void LoadSectionExpansion(bool expanded, bool hasUserState)
    {
        _isSectionExpanded = expanded;
        _hasUserToggledSection = hasUserState;
        RefreshSectionExpansion();
    }

    public void RefreshSectionExpansion()
    {
        if (_items.Count == 0)
        {
            _isSectionExpanded = false;
            return;
        }

        if (!_hasUserToggledSection)
            _isSectionExpanded = true;
    }

    public void LoadLibrary()
    {
        _items.Clear();
        var state = _libraryService.Load();
        var source = _kind switch
        {
            GraphAssetKind.Function => GraphLibraryService.ToFunctionViewModels(state),
            _ => GraphLibraryService.ToViewModels(state),
        };
        foreach (var item in source)
            _items.Add(item);

        if (_items.Count == 0 && _kind == GraphAssetKind.EventGraph)
            Add(loadImmediately: false);

        var target = _items.FirstOrDefault(item => item.Id == state.LastSelectedId)
            ?? _items.FirstOrDefault();

        if (target is not null)
        {
            _graphListBox.SelectedItem = target;
            Load(target);
        }
    }

    public void AddAndRename(bool snapshotCurrent = true) => Add(loadImmediately: true, snapshotCurrent);

    public void ReplaceItems(IEnumerable<GraphListItemViewModel> items)
    {
        _items.Clear();
        foreach (var item in items)
            _items.Add(item);

        if (_kind == GraphAssetKind.EventGraph)
            GraphLibraryService.NormalizeEventGraphRoles(_items);
        ClearActive();
        RefreshSectionExpansion();
    }

    public void ClearActive()
    {
        _activeItem = null;
        _graphListBox.SelectedItem = null;
    }

    public void LoadItem(GraphListItemViewModel item, bool snapshotCurrent = true) => Load(item, snapshotCurrent);

    public bool LoadDoubleClickedItem(MouseButtonEventArgs e)
    {
        if (!TryGetItemFromMouse(e, out var item))
        {
            return false;
        }

        Load(item, snapshotCurrent: false);
        e.Handled = true;
        return true;
    }

    public bool TryGetItemFromMouse(MouseButtonEventArgs e, out GraphListItemViewModel item)
    {
        item = null!;
        return e.OriginalSource is DependencyObject source &&
               TryFindGraphItemFromSource(source, out item);
    }

    public GraphListItemViewModel AddDefaultItem(string name, bool loadImmediately, bool snapshotCurrent = true)
    {
        var item = Add(loadImmediately, snapshotCurrent, name);
        item.IsEditing = false;
        return item;
    }

    public void SaveAll()
    {
        SnapshotActive();
        foreach (var item in _items)
            item.IsDirty = false;

        Persist();
        _setStatus($"已保存全部{_displayName}：{_items.Count} 个。");
    }

    public void SaveAs()
    {
        var dialog = new SaveFileDialog
        {
            Title = "保存图谱",
            Filter = "图谱文件 (*.json)|*.json|所有文件(*.*)|*.*",
            FileName = "graph.json",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        };

        if (dialog.ShowDialog(_owner) == true)
        {
            _editorService.SaveGraph(dialog.FileName);
            if (_activeItem is not null)
                _activeItem.IsDirty = false;
        }
    }

    public void ImportFromDialog()
    {
        var dialog = new OpenFileDialog
        {
            Title = "打开图谱",
            Filter = "图谱文件 (*.json)|*.json|所有文件(*.*)|*.*",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        };

        if (dialog.ShowDialog(_owner) != true) return;

        try
        {
            ImportFile(dialog.FileName);
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show(_owner, ex.Message, "打开失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void ImportFile(string path)
    {
        string json = File.ReadAllText(path);
        var graph = JsonSerializer.Deserialize<GraphFileModel>(json)
            ?? throw new InvalidOperationException("图谱文件解析失败。");

        string name = string.IsNullOrWhiteSpace(graph.Name)
            ? Path.GetFileNameWithoutExtension(path)
            : graph.Name;

        var entryRole = GetDefaultEntryRoleForImportedGraph(graph);
        graph.EntryRole = graph.AssetKind == GraphAssetKind.EventGraph ? entryRole : null;
        var item = new GraphListItemViewModel
        {
            Name = name,
            Kind = graph.AssetKind,
            Graph = graph,
            EntryRole = entryRole,
            IsDirty = true,
        };

        _items.Add(item);
        _graphListBox.SelectedItem = item;
        Load(item);
        Persist();
    }

    public void HandleDoubleClick(MouseButtonEventArgs e)
    {
        if (SelectedItem is null) return;
        if (e.OriginalSource is DependencyObject source && !TryFindGraphItemFromSource(source, out _)) return;

        Load(SelectedItem);
        e.Handled = true;
    }

    public void HandleKeyDown(WpfKeyEventArgs e)
    {
        if (Keyboard.FocusedElement is WpfTextBox) return;

        if (e.Key == Key.Delete)
        {
            DeleteSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.F2)
        {
            if (SelectedItem is not null)
                StartRename(SelectedItem);
            e.Handled = true;
        }
    }

    public void SelectRightClickedItem(object sender)
    {
        if (sender is ListBoxItem { DataContext: GraphListItemViewModel item })
            _graphListBox.SelectedItem = item;
    }

    public void RenameSelected()
    {
        if (SelectedItem is { } item)
            StartRename(item);
    }

    public void DeleteSelected()
    {
        if (SelectedItem is null) return;

        var result = WpfMessageBox.Show(
            _owner,
            $"是否删除{_displayName}：{SelectedItem.Name}？",
            $"删除{_displayName}",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        DeleteSelectedConfirmed();
    }

    private void DeleteSelectedConfirmed()
    {
        if (SelectedItem is null) return;

        bool deletingActive = ReferenceEquals(SelectedItem, _activeItem);
        int oldIndex = _items.IndexOf(SelectedItem);
        bool deletingMainEvent = _kind == GraphAssetKind.EventGraph &&
                                 SelectedItem.EntryRole == GraphEntryRole.MainEvent;
        _items.Remove(SelectedItem);

        if (_items.Count == 0)
        {
            if (_kind == GraphAssetKind.EventGraph)
            {
                var item = AddDefaultItem(CreateUniqueName(), loadImmediately: true, snapshotCurrent: false);
                item.EntryRole = GraphEntryRole.MainEvent;
                item.Graph.EntryRole = GraphEntryRole.MainEvent;
                deletingActive = false;
            }
            else
            {
                ClearActive();
                _editorService.ClearGraph();
                RefreshSectionExpansion();
            }
        }
        else
        {
            if (deletingMainEvent)
                PromoteFirstEventGraphToMain();

            var next = _items[Math.Clamp(oldIndex, 0, _items.Count - 1)];
            _graphListBox.SelectedItem = next;
            if (deletingActive)
                Load(next);
        }

        Persist();
    }

    public void HandleRenameKeyDown(WpfTextBox textBox, WpfKeyEventArgs e)
    {
        if (textBox.DataContext is not GraphListItemViewModel item) return;

        if (e.Key == Key.Enter)
        {
            CommitRename(item);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            item.IsEditing = false;
            e.Handled = true;
        }
    }

    public void HandleRenameLostFocus(WpfTextBox textBox)
    {
        if (textBox.DataContext is GraphListItemViewModel item)
            CommitRename(item);
    }

    public void SnapshotActive()
    {
        if (_isLoadingGraph || _activeItem is null)
            return;

        if (_activeItem.EntryRole == GraphEntryRole.AuxiliaryEvent)
            _editorService.RemoveStartNodes();

        _activeItem.Graph = _editorService.ExportGraphModel(_activeItem.Name, _kind, GetEntryRoleForItem(_activeItem));
        _activeItem.Graph.Name = _activeItem.Name;
        _activeItem.Graph.EntryRole = GetEntryRoleForItem(_activeItem);
    }

    public void MarkDirty() => MarkLogicDirty();

    public void MarkLayoutDirty()
    {
        if (!_isLoadingGraph && _activeItem is not null)
            _activeItem.IsDirty = true;
    }

    public void MarkLogicDirty()
    {
        if (!_isLoadingGraph && _activeItem is not null)
        {
            _activeItem.IsDirty = true;
            _activeItem.IsCompileDirty = true;
        }
    }

    public bool HandleClosing(System.ComponentModel.CancelEventArgs e)
    {
        SnapshotActive();

        if (!_items.Any(item => item.IsDirty))
            return true;

        var result = WpfMessageBox.Show(
            _owner,
            $"存在未保存{_displayName}，是否保存？",
            "是否保存",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Cancel)
        {
            e.Cancel = true;
            return false;
        }

        if (result == MessageBoxResult.Yes)
        {
            SaveAll();
        }

        return true;
    }

    public void Persist() => _persistAll();

    private GraphListItemViewModel Add(bool loadImmediately, bool snapshotCurrent = true, string? fixedName = null)
    {
        if (snapshotCurrent)
            SnapshotActive();

        string name = string.IsNullOrWhiteSpace(fixedName) ? CreateUniqueName() : fixedName;
        var entryRole = GetNewItemEntryRole();
        var item = new GraphListItemViewModel
        {
            Name = name,
            Kind = _kind,
            Graph = CreateDefaultGraphModel(name, entryRole),
            EntryRole = entryRole ?? GraphEntryRole.MainEvent,
            IsDirty = true,
            IsCompileDirty = true,
        };

        _items.Add(item);
        _graphListBox.SelectedItem = item;
        _isSectionExpanded = true;
        _hasUserToggledSection = false;

        if (loadImmediately)
        {
            Load(item, snapshotCurrent: false);
            StartRename(item);
        }
        else
        {
            Persist();
        }

        return item;
    }

    public void ReloadItemWithoutPersist(GraphListItemViewModel item)
    {
        _isLoadingGraph = true;
        try
        {
            if (_kind == GraphAssetKind.EventGraph)
                item.Graph.EntryRole = item.EntryRole;
            _editorService.LoadFromModel(item.Graph);
            _syncNodeFactorySequence();
            _activeItem = item;
            _graphListBox.SelectedItem = item;
        }
        finally
        {
            _isLoadingGraph = false;
        }
    }

    private string CreateUniqueName()
    {
        int index = _items.Count + 1;
        string name;
        do
        {
            name = $"{_namePrefix}{index++}";
        }
        while (_items.Any(item => item.Name == name));

        return name;
    }

    private GraphFileModel CreateDefaultGraphModel(string name, GraphEntryRole? entryRole)
    {
        _isLoadingGraph = true;
        try
        {
            if (_kind == GraphAssetKind.Function)
            {
                _editorService.NewFunctionGraph();
                entryRole = null;
            }
            else if (entryRole == GraphEntryRole.AuxiliaryEvent)
            {
                _editorService.NewAuxiliaryEventGraph();
            }
            else
            {
                _editorService.NewMainEventGraph();
            }
            _syncNodeFactorySequence();
            ApplyEntryNodeTitle(_editorService.Nodes, name);
            return _editorService.ExportGraphModel(name, _kind, entryRole);
        }
        finally
        {
            _isLoadingGraph = false;
        }
    }

    private void Load(GraphListItemViewModel item, bool snapshotCurrent = true)
    {
        if (snapshotCurrent)
            SnapshotActive();

        _isLoadingGraph = true;
        try
        {
            if (_kind == GraphAssetKind.EventGraph)
                item.Graph.EntryRole = item.EntryRole;
            _editorService.LoadFromModel(item.Graph);
            _syncNodeFactorySequence();
            _activeItem = item;
            _graphListBox.SelectedItem = item;
            _setStatus($"已进入{_displayName}：{item.Name}");
            Persist();
        }
        finally
        {
            _isLoadingGraph = false;
        }
    }

    private void StartRename(GraphListItemViewModel item)
    {
        item.IsEditing = true;
        _graphListBox.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_graphListBox.ItemContainerGenerator.ContainerFromItem(item) is not ListBoxItem container)
                return;

            var tb = FindVisualChild<WpfTextBox>(container);
            if (tb is null) return;

            tb.Focus();
            tb.SelectAll();
        }), DispatcherPriority.Render);
    }

    private void CommitRename(GraphListItemViewModel item)
    {
        if (string.IsNullOrWhiteSpace(item.Name))
        {
            item.Name = string.IsNullOrWhiteSpace(item.Graph.Name) ? $"未命名{_displayName}" : item.Graph.Name;
            item.IsEditing = false;
            return;
        }

        item.Name = item.Name.Trim();
        item.Graph.Name = item.Name;
        ApplyEntryNodeTitle(item.Graph, item.Name);
        if (ReferenceEquals(item, _activeItem))
            ApplyEntryNodeTitle(_editorService.Nodes, item.Name);
        item.IsEditing = false;
        item.IsDirty = true;
        if (ReferenceEquals(item, _activeItem))
            _setStatus($"当前{_displayName}已重命名：{item.Name}");

        Persist();
    }

    private static bool TryFindGraphItemFromSource(DependencyObject source, out GraphListItemViewModel item)
    {
        var current = source;
        while (current is not null)
        {
            if (current is FrameworkElement { DataContext: GraphListItemViewModel graphItem })
            {
                item = graphItem;
                return true;
            }

            current = VisualTreeUtility.GetParent(current);
        }

        item = null!;
        return false;
    }

    private void ApplyEntryNodeTitle(IEnumerable<NodeBaseViewModel> nodes, string graphName)
    {
        string title = $"{graphName}开始";
        foreach (var node in nodes)
        {
            if (_kind == GraphAssetKind.Function && node is FunctionEntryNodeViewModel)
            {
                node.Title = title;
            }
        }
    }

    private void ApplyEntryNodeTitle(GraphFileModel graph, string graphName)
    {
        string? entryKey = _kind switch
        {
            GraphAssetKind.Function => "function_entry",
            _ => null,
        };
        if (entryKey is null)
            return;

        foreach (var node in graph.Nodes.Where(node => node.NodeTypeKey == entryKey))
            node.Title = $"{graphName}开始";
    }

    private GraphEntryRole? GetNewItemEntryRole()
    {
        if (_kind != GraphAssetKind.EventGraph)
            return null;

        return _items.Any(item => item.EntryRole == GraphEntryRole.MainEvent)
            ? GraphEntryRole.AuxiliaryEvent
            : GraphEntryRole.MainEvent;
    }

    private GraphEntryRole? GetEntryRoleForItem(GraphListItemViewModel item) =>
        _kind == GraphAssetKind.EventGraph ? item.EntryRole : null;

    private GraphEntryRole GetDefaultEntryRoleForImportedGraph(GraphFileModel graph)
    {
        if (graph.AssetKind != GraphAssetKind.EventGraph)
            return GraphEntryRole.MainEvent;

        return _items.Any(item => item.EntryRole == GraphEntryRole.MainEvent)
            ? GraphEntryRole.AuxiliaryEvent
            : graph.EntryRole ?? GraphEntryRole.MainEvent;
    }

    private void PromoteFirstEventGraphToMain()
    {
        if (_kind != GraphAssetKind.EventGraph || _items.Count == 0)
            return;

        foreach (var item in _items)
            item.EntryRole = GraphEntryRole.AuxiliaryEvent;

        var promoted = _items[0];
        promoted.EntryRole = GraphEntryRole.MainEvent;
        promoted.Graph.EntryRole = GraphEntryRole.MainEvent;
        if (promoted.Graph.Nodes.All(node => node.NodeTypeKey != "start"))
        {
            string id = CreateUniqueNodeId(promoted.Graph);
            promoted.Graph.Nodes.Insert(0, new NodeFileModel
            {
                Id = id,
                NodeTypeKey = "start",
                Title = "开始运行",
                NodeNumber = "N001",
                X = 80,
                Y = 210,
            });
            promoted.IsDirty = true;
            promoted.IsCompileDirty = true;
        }
    }

    private static string CreateUniqueNodeId(GraphFileModel graph)
    {
        var ids = graph.Nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        for (int i = 1; i < 10000; i++)
        {
            string id = $"node_{i:000}";
            if (!ids.Contains(id))
                return id;
        }

        return $"node_{Guid.NewGuid():N}";
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T target)
                return target;

            var nested = FindVisualChild<T>(child);
            if (nested is not null)
                return nested;
        }

        return null;
    }
}
