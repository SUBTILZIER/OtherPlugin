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
    private readonly Action<string> _setStatus;

    private GraphListItemViewModel? _activeItem;
    private bool _isLoadingGraph;

    public GraphListController(
        Window owner,
        GraphEditorService editorService,
        GraphLibraryService libraryService,
        NodeFactory nodeFactory,
        WpfListBox graphListBox,
        ObservableCollection<GraphListItemViewModel> items,
        Action syncNodeFactorySequence,
        Action<string> setStatus)
    {
        _owner = owner;
        _editorService = editorService;
        _libraryService = libraryService;
        _nodeFactory = nodeFactory;
        _graphListBox = graphListBox;
        _items = items;
        _syncNodeFactorySequence = syncNodeFactorySequence;
        _setStatus = setStatus;
    }

    public bool IsLoadingGraph => _isLoadingGraph;

    public GraphListItemViewModel? ActiveItem => _activeItem;

    public GraphListItemViewModel? SelectedItem => _graphListBox.SelectedItem as GraphListItemViewModel;

    public void LoadLibrary()
    {
        _items.Clear();
        var state = _libraryService.Load();
        foreach (var item in GraphLibraryService.ToViewModels(state))
            _items.Add(item);

        if (_items.Count == 0)
            Add(loadImmediately: false);

        var target = _items.FirstOrDefault(item => item.Id == state.LastSelectedId)
            ?? _items.FirstOrDefault();

        if (target is not null)
        {
            _graphListBox.SelectedItem = target;
            Load(target);
        }
    }

    public void AddAndRename() => Add(loadImmediately: true);

    public void SaveAll()
    {
        SnapshotActive();
        foreach (var item in _items)
            item.IsDirty = false;

        Persist();
        _setStatus($"已保存全部图谱：{_items.Count} 个。");
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

        var item = new GraphListItemViewModel
        {
            Name = name,
            Graph = graph,
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
            $"是否删除图谱：{SelectedItem.Name}？",
            "删除图谱",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        bool deletingActive = ReferenceEquals(SelectedItem, _activeItem);
        int oldIndex = _items.IndexOf(SelectedItem);
        _items.Remove(SelectedItem);

        if (_items.Count == 0)
        {
            Add(loadImmediately: true);
        }
        else
        {
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

        _activeItem.Graph = _editorService.ExportGraphModel(_activeItem.Name);
        _activeItem.Graph.Name = _activeItem.Name;
    }

    public void MarkDirty()
    {
        if (!_isLoadingGraph && _activeItem is not null)
            _activeItem.IsDirty = true;
    }

    public bool HandleClosing(System.ComponentModel.CancelEventArgs e)
    {
        SnapshotActive();

        if (!_items.Any(item => item.IsDirty))
            return true;

        var result = WpfMessageBox.Show(
            _owner,
            "存在未保存图谱，是否保存？",
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

    public void Persist() => _libraryService.Save(_items, _activeItem?.Id ?? SelectedItem?.Id);

    private GraphListItemViewModel Add(bool loadImmediately)
    {
        SnapshotActive();

        string name = CreateUniqueName();
        var item = new GraphListItemViewModel
        {
            Name = name,
            Graph = CreateDefaultGraphModel(name),
            IsDirty = true,
        };

        _items.Add(item);
        _graphListBox.SelectedItem = item;

        if (loadImmediately)
        {
            Load(item);
            StartRename(item);
        }
        else
        {
            Persist();
        }

        return item;
    }

    private string CreateUniqueName()
    {
        int index = _items.Count + 1;
        string name;
        do
        {
            name = $"图表{index++}";
        }
        while (_items.Any(item => item.Name == name));

        return name;
    }

    private GraphFileModel CreateDefaultGraphModel(string name)
    {
        _isLoadingGraph = true;
        try
        {
            _editorService.NewGraph();
            _nodeFactory.ResetCounter(1);
            return _editorService.ExportGraphModel(name);
        }
        finally
        {
            _isLoadingGraph = false;
        }
    }

    private void Load(GraphListItemViewModel item)
    {
        SnapshotActive();

        _isLoadingGraph = true;
        try
        {
            _editorService.LoadFromModel(item.Graph);
            _syncNodeFactorySequence();
            _activeItem = item;
            _graphListBox.SelectedItem = item;
            _setStatus($"已进入图谱：{item.Name}");
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
        item.Name = string.IsNullOrWhiteSpace(item.Name) ? "未命名图谱" : item.Name.Trim();
        item.Graph.Name = item.Name;
        item.IsEditing = false;
        item.IsDirty = true;
        if (ReferenceEquals(item, _activeItem))
            _setStatus($"当前图谱已重命名：{item.Name}");

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

            current = VisualTreeHelper.GetParent(current);
        }

        item = null!;
        return false;
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
