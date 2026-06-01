using System.IO;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Logging;
using AutomationStudioWpf.Runtime;
using AutomationStudioWpf.Services;
using Microsoft.Win32;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using DragEventArgs = System.Windows.DragEventArgs;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using TextBox = System.Windows.Controls.TextBox;
using MouseButton = System.Windows.Input.MouseButton;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;
using WpfMessageBox = System.Windows.MessageBox;

namespace AutomationStudioWpf;

/// <summary>
/// 主窗口 - 节点编辑器
/// 重构后：职责精简为协调各服务，具体逻辑下沉到 Services 层
/// </summary>
public partial class MainWindow : Window
{
    // 核心服务
    private readonly GraphEditorService _editorService = new();
    private readonly NodeClipboardService _clipboardService = new();
    private readonly NodeFactory _nodeFactory = new();
    private readonly GraphRuntimeExecutor _runtimeExecutor = new();
    private readonly GraphLibraryService _graphLibraryService = new();

    // 运行状态
    private CancellationTokenSource? _executionCts;
    private Point _lastMousePosition;
    private bool _isLoadingInspector;
    private bool _isLoadingGraph;
    private bool _isClosing;
    private GraphListItemViewModel? _selectedGraphItem;
    private GraphListItemViewModel? _activeGraphItem;

    // 连接拖拽状态
    private PinViewModel? _pendingOutputPin;
    private bool _isConnecting;
    private bool _wireWasDragged;

    // 节点拖拽状态
    private NodeBaseViewModel? _dragNode;
    private Point _dragOffset;
    private List<(NodeBaseViewModel Node, double OffsetX, double OffsetY)> _dragGroup = [];

    // 框选状态
    private bool _isSelecting;
    private Point _selectionStart;

    // 画布平移状态
    private bool _isPanning;
    private Point _panStart;
    private Point _panStartOffset;
    private bool _isCanvasFocusActive;

    // 缩放
    private double _zoomLevel = 1.0;

    // 右键菜单状态
    private bool _rightClickPending;
    private Point _rightClickStartPos;

    public MainWindow()
    {
        DataContext = this;
        InitializeComponent();
        InitializeServices();
        InitializeEditor();
    }

    #region 初始化

    private void InitializeServices()
    {

        // 绑定服务事件
        _editorService.GraphChanged += OnGraphChanged;
        _editorService.StatusChanged += SetStatus;

        // 日志更新
        Logger.Entries.CollectionChanged += (_, _) => RefreshLogList();

        // 窗口关闭时释放按键，并处理未保存图谱。
        Closing += Window_Closing;

        // 全局鼠标点击检测：点击节点菜单外部时关闭菜单
        PreviewMouseDown += Window_PreviewMouseDown;
    }

    private void InitializeEditor()
    {
        LoadGraphLibrary();
        InitializeNodePalette();
        EnsureCanvasLargeEnough();
    }

    #endregion

    #region 属性绑定

    public System.Collections.IEnumerable Nodes => _editorService.Nodes;
    public System.Collections.IEnumerable Connections => _editorService.Connections;
    public ObservableCollection<GraphListItemViewModel> GraphListItems { get; } = [];

    public GraphListItemViewModel? SelectedGraphItem
    {
        get => _selectedGraphItem;
        set => _selectedGraphItem = value;
    }

    #endregion

    #region 工具栏事件 - 文件操作

    private void NewGraph_Click(object sender, RoutedEventArgs e)
    {
        AddGraphListItem(loadImmediately: true);
    }

    private void SaveGraph_Click(object sender, RoutedEventArgs e)
    {
        if (_activeGraphItem is not null)
        {
            _activeGraphItem.Graph = _editorService.ExportGraphModel(_activeGraphItem.Name);
            _activeGraphItem.Graph.Name = _activeGraphItem.Name;
        }

        foreach (var item in GraphListItems)
        {
            item.IsDirty = false;
        }

        PersistGraphLibrary();
        SetStatus($"已保存全部图谱：{GraphListItems.Count} 个。");
    }

    private void SaveGraphAs_Click(object sender, RoutedEventArgs e)
    {
        SaveGraphAs();
    }

    private void SaveGraphAs()
    {
        var dialog = new SaveFileDialog
        {
            Title = "保存图谱",
            Filter = "图谱文件 (*.json)|*.json|所有文件(*.*)|*.*",
            FileName = "graph.json",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        };

        if (dialog.ShowDialog(this) == true)
        {
            _editorService.SaveGraph(dialog.FileName);
            if (_activeGraphItem is not null)
            {
                _activeGraphItem.IsDirty = false;
            }
        }
    }

    private void OpenGraph_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "打开图谱",
            Filter = "图谱文件 (*.json)|*.json|所有文件(*.*)|*.*",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        };

        if (dialog.ShowDialog(this) != true) return;

        try
        {
            ImportGraphFileToList(dialog.FileName);
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show(this, ex.Message, "打开失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #endregion

    #region 图谱列表

    private void LoadGraphLibrary()
    {
        GraphListItems.Clear();
        var state = _graphLibraryService.Load();
        foreach (var item in GraphLibraryService.ToViewModels(state))
        {
            GraphListItems.Add(item);
        }

        if (GraphListItems.Count == 0)
        {
            AddGraphListItem(loadImmediately: false);
        }

        var target = GraphListItems.FirstOrDefault(item => item.Id == state.LastSelectedId)
            ?? GraphListItems.FirstOrDefault();
        if (target is not null)
        {
            GraphListBox.SelectedItem = target;
            LoadGraphListItem(target);
        }
    }

    private void AddGraphListItem_Click(object sender, RoutedEventArgs e)
    {
        AddGraphListItem(loadImmediately: true);
    }

    private GraphListItemViewModel AddGraphListItem(bool loadImmediately)
    {
        SnapshotActiveGraph();

        string name = CreateUniqueGraphName();
        var item = new GraphListItemViewModel
        {
            Name = name,
            Graph = CreateDefaultGraphModel(name),
            IsDirty = true,
        };

        GraphListItems.Add(item);
        GraphListBox.SelectedItem = item;

        if (loadImmediately)
        {
            LoadGraphListItem(item);
            StartRenameGraphItem(item);
        }
        else
        {
            PersistGraphLibrary();
        }

        return item;
    }

    private string CreateUniqueGraphName()
    {
        int index = GraphListItems.Count + 1;
        string name;
        do
        {
            name = $"图表{index++}";
        }
        while (GraphListItems.Any(item => item.Name == name));

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

    private void GraphListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SelectedGraphItem is null) return;
        if (e.OriginalSource is DependencyObject source && !TryFindGraphItemFromSource(source, out _)) return;

        LoadGraphListItem(SelectedGraphItem);
        e.Handled = true;
    }

    private void GraphListBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox) return;

        if (e.Key == Key.Delete)
        {
            DeleteSelectedGraphItem();
            e.Handled = true;
        }
        else if (e.Key == Key.F2)
        {
            if (SelectedGraphItem is not null)
                StartRenameGraphItem(SelectedGraphItem);
            e.Handled = true;
        }
    }

    private void RenameGraphMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedGraphItem is { } item)
        {
            StartRenameGraphItem(item);
        }
    }

    private void DeleteGraphMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedGraphItem is { } item)
        {
            GraphListBox.SelectedItem = item;
            DeleteSelectedGraphItem();
        }
    }

    private void GraphListItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem { DataContext: GraphListItemViewModel item })
        {
            GraphListBox.SelectedItem = item;
            e.Handled = false;
        }
    }

    private void GraphNameTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb || tb.DataContext is not GraphListItemViewModel item) return;

        if (e.Key == Key.Enter)
        {
            CommitGraphRename(item);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            item.IsEditing = false;
            e.Handled = true;
        }
    }

    private void GraphNameTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: GraphListItemViewModel item })
        {
            CommitGraphRename(item);
        }
    }

    private void StartRenameGraphItem(GraphListItemViewModel item)
    {
        item.IsEditing = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (GraphListBox.ItemContainerGenerator.ContainerFromItem(item) is not ListBoxItem container)
                return;

            var tb = FindVisualChild<TextBox>(container);
            if (tb is null) return;

            tb.Focus();
            tb.SelectAll();
        }), DispatcherPriority.Render);
    }

    private void CommitGraphRename(GraphListItemViewModel item)
    {
        item.Name = string.IsNullOrWhiteSpace(item.Name) ? "未命名图谱" : item.Name.Trim();
        item.Graph.Name = item.Name;
        item.IsEditing = false;
        item.IsDirty = true;
        if (ReferenceEquals(item, _activeGraphItem))
        {
            SetStatus($"当前图谱已重命名：{item.Name}");
        }
        PersistGraphLibrary();
    }

    private void DeleteSelectedGraphItem()
    {
        if (SelectedGraphItem is null) return;

        var result = WpfMessageBox.Show(
            this,
            $"是否删除图谱：{SelectedGraphItem.Name}？",
            "删除图谱",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        bool deletingActive = ReferenceEquals(SelectedGraphItem, _activeGraphItem);
        int oldIndex = GraphListItems.IndexOf(SelectedGraphItem);
        GraphListItems.Remove(SelectedGraphItem);

        if (GraphListItems.Count == 0)
        {
            AddGraphListItem(loadImmediately: true);
        }
        else
        {
            var next = GraphListItems[Math.Clamp(oldIndex, 0, GraphListItems.Count - 1)];
            GraphListBox.SelectedItem = next;
            if (deletingActive)
            {
                LoadGraphListItem(next);
            }
        }

        PersistGraphLibrary();
    }

    private void LoadGraphListItem(GraphListItemViewModel item)
    {
        SnapshotActiveGraph();

        _isLoadingGraph = true;
        try
        {
            _editorService.LoadFromModel(item.Graph);
            SyncNodeFactorySequence();
            EnsureCanvasLargeEnough();
            _activeGraphItem = item;
            GraphListBox.SelectedItem = item;
            SetStatus($"已进入图谱：{item.Name}");
            PersistGraphLibrary();
        }
        finally
        {
            _isLoadingGraph = false;
        }
    }

    private void SaveCurrentGraphToLibrary()
    {
        GraphListItemViewModel item = _activeGraphItem ?? SelectedGraphItem ?? AddGraphListItem(loadImmediately: false);
        item.Graph = _editorService.ExportGraphModel(item.Name);
        item.Graph.Name = item.Name;
        item.IsDirty = false;
        _activeGraphItem = item;
        GraphListBox.SelectedItem = item;
        SetStatus($"图谱已保存：{item.Name}");
    }

    private void ImportGraphFileToList(string path)
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

        GraphListItems.Add(item);
        GraphListBox.SelectedItem = item;
        LoadGraphListItem(item);
        PersistGraphLibrary();
    }

    private void SnapshotActiveGraph()
    {
        if (_isLoadingGraph || _activeGraphItem is null)
            return;

        _activeGraphItem.Graph = _editorService.ExportGraphModel(_activeGraphItem.Name);
        _activeGraphItem.Graph.Name = _activeGraphItem.Name;
    }

    private bool ConfirmSaveCurrentGraphIfDirty()
    {
        if (_activeGraphItem?.IsDirty != true) return true;

        var result = WpfMessageBox.Show(
            this,
            $"图谱“{_activeGraphItem.Name}”尚未保存，是否保存？",
            "保存图谱",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Cancel) return false;
        if (result == MessageBoxResult.Yes)
        {
            SaveCurrentGraphToLibrary();
            PersistGraphLibrary();
        }

        return true;
    }

    private void PersistGraphLibrary()
    {
        _graphLibraryService.Save(GraphListItems, _activeGraphItem?.Id ?? SelectedGraphItem?.Id);
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

    private static bool IsFocusInside(DependencyObject root)
    {
        if (Keyboard.FocusedElement is not DependencyObject focused)
            return false;

        var current = focused;
        while (current is not null)
        {
            if (ReferenceEquals(current, root))
                return true;

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    #endregion

    #region 窗口生命周期

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _runtimeExecutor.ReleaseAllKeys();
        if (_isClosing) return;

        if (_activeGraphItem is not null)
        {
            _activeGraphItem.Graph = _editorService.ExportGraphModel(_activeGraphItem.Name);
        }

        if (GraphListItems.Any(item => item.IsDirty))
        {
            var result = WpfMessageBox.Show(
                this,
                "存在未保存图谱，是否保存？",
                "是否保存",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Cancel)
            {
                e.Cancel = true;
                return;
            }

            if (result == MessageBoxResult.Yes)
            {
                SaveCurrentGraphToLibrary();
                foreach (var item in GraphListItems)
                    item.IsDirty = false;
                PersistGraphLibrary();
            }
        }

        _isClosing = true;
    }

    #endregion

    #region 工具栏事件 - 执行

    private async void RunGraph_Click(object sender, RoutedEventArgs e)
    {
        if (_executionCts is not null)
        {
            SetStatus("已有图谱正在执行。");
            return;
        }

        try
        {
            RunGraphButton.IsEnabled = false;
            _executionCts = new CancellationTokenSource();
            var plan = _editorService.BuildExecutionPlan();
            var baseDirectory = !string.IsNullOrWhiteSpace(_editorService.CurrentGraphPath)
                ? Path.GetDirectoryName(_editorService.CurrentGraphPath) ?? Environment.CurrentDirectory
                : Environment.CurrentDirectory;

            if (plan.Nodes.Any(n => n.NodeKind == NodeKind.FindImage))
            {
                bool pythonReady = await PythonAutoInstaller.EnsurePythonAsync(new Progress<string>(SetStatus));
                if (!pythonReady)
                {
                    _executionCts = null;
                    RunGraphButton.IsEnabled = true;
                    SetStatus("Python 环境未就绪，执行已取消。");
                    return;
                }
            }

            SetStatus("执行开始...");
            var ct = _executionCts.Token;
            var result = await Task.Run(() => _runtimeExecutor.Execute(plan, baseDirectory, ct), ct);
            SetStatus(result.Message);
        }
        catch (OperationCanceledException)
        {
            Logger.Info("===== 执行已取消=====");
            _runtimeExecutor.ReleaseAllKeys();
            SetStatus("执行已取消。");
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show(this, ex.Message, "执行失败", MessageBoxButton.OK, MessageBoxImage.Error);
            SetStatus("执行失败。");
        }
        finally
        {
            _executionCts = null;
            RunGraphButton.IsEnabled = true;
        }
    }

    #endregion

    #region 工具栏事件 - 其他

    private void DeleteSelectedNode_Click(object sender, RoutedEventArgs e)
    {
        _editorService.RemoveSelectedNodes();
        SelectNode(null);
    }

    private void ClearPendingConnection_Click(object sender, RoutedEventArgs e)
    {
        CancelPendingConnection("已清除待连接引脚。");
    }

    #endregion

    #region 画布交互 - 鼠标事件

    private void NodeCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not NodeBaseViewModel node)
            return;

        _isCanvasFocusActive = false;
        SelectNode(node);
        e.Handled = true;
    }

    private void NodeHeader_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isConnecting) return;
        if (IsPinInteractionSource(e.OriginalSource as DependencyObject)) return;
        if (sender is not FrameworkElement element || element.DataContext is not NodeBaseViewModel node)
            return;

        _isCanvasFocusActive = false;
        if (!node.IsSelected)
            SelectNode(node);
        else
            LoadNodeToInspector(node);

        var point = ViewportToGraph(e.GetPosition(GraphViewport));
        _dragNode = node;
        _dragOffset = new Point(point.X - node.X, point.Y - node.Y);

        var selectedCount = _editorService.Nodes.Count(n => n.IsSelected);
        if (selectedCount > 1 && node.IsSelected)
        {
            _dragGroup = _editorService.Nodes
                .Where(n => n.IsSelected)
                .Select(n => (n, point.X - n.X, point.Y - n.Y))
                .ToList();
        }
        else
        {
            _dragGroup = [(node, _dragOffset.X, _dragOffset.Y)];
        }

        element.CaptureMouse();
        e.Handled = true;
    }

    private void NodeHeader_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_isConnecting) return;
        if (_dragNode is null || e.LeftButton != MouseButtonState.Pressed) return;

        var point = ViewportToGraph(e.GetPosition(GraphViewport));
        foreach (var (item, offsetX, offsetY) in _dragGroup)
        {
            item.X = point.X - offsetX;
            item.Y = point.Y - offsetY;
        }
        if (_activeGraphItem is not null)
        {
            _activeGraphItem.IsDirty = true;
        }
    }

    private void NodeHeader_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is UIElement element)
            element.ReleaseMouseCapture();

        _dragNode = null;
        _dragGroup.Clear();
    }

    private void GraphViewport_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 节点菜单打开时，点击菜单内部不处理画布事件
        if (NodePalette.Visibility == Visibility.Visible)
        {
            var posInPalette = e.GetPosition(NodePalette);
            if (posInPalette.X >= 0 && posInPalette.X <= NodePalette.ActualWidth &&
                posInPalette.Y >= 0 && posInPalette.Y <= NodePalette.ActualHeight)
            {
                return;
            }
        }

        if (!IsGraphBlankSource(e.OriginalSource as DependencyObject)) return;

        if (_isConnecting)
        {
            CancelPendingConnection("已取消连线。");
            e.Handled = true;
            return;
        }

        _isCanvasFocusActive = true;
        GraphViewport.Focus();
        ClearSelection();
        SelectNode(null);
        _isSelecting = true;
        _selectionStart = ViewportToGraph(e.GetPosition(GraphViewport));
        Canvas.SetLeft(SelectionRectangle, _selectionStart.X);
        Canvas.SetTop(SelectionRectangle, _selectionStart.Y);
        SelectionRectangle.Width = 0;
        SelectionRectangle.Height = 0;
        SelectionRectangle.Visibility = Visibility.Visible;
        GraphViewport.CaptureMouse();
        e.Handled = true;
    }

    private void GraphViewport_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        _lastMousePosition = ViewportToGraph(e.GetPosition(GraphViewport));

        // 右键拖动检测：移动超过阈值则转为平移
        if (_rightClickPending && e.RightButton == MouseButtonState.Pressed)
        {
            var currentPos = e.GetPosition(GraphViewport);
            var delta = currentPos - _rightClickStartPos;
            if (Math.Abs(delta.X) > 3 || Math.Abs(delta.Y) > 3)
            {
                _rightClickPending = false;
                _isPanning = true;
                _panStart = currentPos;
                _panStartOffset = new Point(GraphPanTransform.X, GraphPanTransform.Y);
                GraphViewport.CaptureMouse();
            }
        }

        if (_isPanning)
        {
            if (e.RightButton != MouseButtonState.Pressed)
            {
                _isPanning = false;
                GraphViewport.ReleaseMouseCapture();
            }
            var currentPan = e.GetPosition(GraphViewport);
            var delta = currentPan - _panStart;
            GraphPanTransform.X = _panStartOffset.X + delta.X;
            GraphPanTransform.Y = _panStartOffset.Y + delta.Y;
        }

        if (_isConnecting && _pendingOutputPin is not null)
        {
            _wireWasDragged = true;
            UpdatePreviewConnectionGeometry(_pendingOutputPin, _lastMousePosition);
        }

        if (!_isSelecting) return;

        var current = _lastMousePosition;
        var left = Math.Min(current.X, _selectionStart.X);
        var top = Math.Min(current.Y, _selectionStart.Y);
        var width = Math.Abs(current.X - _selectionStart.X);
        var height = Math.Abs(current.Y - _selectionStart.Y);

        Canvas.SetLeft(SelectionRectangle, left);
        Canvas.SetTop(SelectionRectangle, top);
        SelectionRectangle.Width = width;
        SelectionRectangle.Height = height;
    }

    private void GraphViewport_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isConnecting)
        {
            if (_pendingOutputPin is not null &&
                TryGetPinAtPosition(ViewportToGraph(e.GetPosition(GraphViewport)), out var targetPin) &&
                targetPin is not null &&
                targetPin != _pendingOutputPin)
            {
                TryConnectPending(targetPin);
            }
            else if (_wireWasDragged && _pendingOutputPin is not null)
            {
                CancelPendingConnection("已取消连线。");
            }

            ReleasePreviewWire();
            e.Handled = true;
            return;
        }

        if (!_isSelecting) return;

        _isSelecting = false;
        GraphViewport.ReleaseMouseCapture();

        var selectionBounds = new Rect(
            Canvas.GetLeft(SelectionRectangle),
            Canvas.GetTop(SelectionRectangle),
            SelectionRectangle.Width,
            SelectionRectangle.Height);

        SelectionRectangle.Visibility = Visibility.Collapsed;
        ApplySelection(selectionBounds);
        e.Handled = true;
    }

    private void GraphViewport_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsGraphBlankSource(e.OriginalSource as DependencyObject)) return;

        if (_isSelecting)
        {
            _isSelecting = false;
            SelectionRectangle.Visibility = Visibility.Collapsed;
        }

        _dragNode = null;
        _dragGroup.Clear();
        GraphViewport.ReleaseMouseCapture();
        _isCanvasFocusActive = true;
        GraphViewport.Focus();

        // 判断是点击还是拖动：记录起始位置，尝试移动后决定
        _rightClickPending = true;
        _rightClickStartPos = e.GetPosition(GraphViewport);
        e.Handled = true;
    }

    private void GraphViewport_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_rightClickPending)
        {
            // 右键点击（非拖动），弹出节点菜单
            _rightClickPending = false;
            OpenNodePalette(_rightClickStartPos);
            e.Handled = true;
            return;
        }

        if (!_isPanning)
        {
            GraphViewport.ReleaseMouseCapture();
            return;
        }

        _isPanning = false;
        GraphViewport.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void GraphViewport_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (NodePalette.Visibility == Visibility.Visible)
        {
            var pos = e.GetPosition(NodePalette);
            if (pos.X >= 0 && pos.X <= NodePalette.ActualWidth &&
                pos.Y >= 0 && pos.Y <= NodePalette.ActualHeight)
            {
                return;
            }
        }

        var mouseInViewport = e.GetPosition(GraphViewport);
        var graphPoint = ViewportToGraph(mouseInViewport);

        var oldZoom = _zoomLevel;
        var zoomDelta = e.Delta > 0 ? 0.1 : -0.1;
        var newZoom = Math.Clamp(oldZoom + zoomDelta, 0.1, 2.5);

        if (Math.Abs(newZoom - oldZoom) < 0.001) return;

        _zoomLevel = newZoom;
        GraphZoomTransform.ScaleX = newZoom;
        GraphZoomTransform.ScaleY = newZoom;
        GraphPanTransform.X = mouseInViewport.X - graphPoint.X * newZoom;
        GraphPanTransform.Y = mouseInViewport.Y - graphPoint.Y * newZoom;

        e.Handled = true;
    }

    private void NodePaletteScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer) return;

        scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta);
        e.Handled = true;
    }

    #endregion

    #region 引脚交互

    private void PinButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not PinViewModel pin)
            return;

        _isCanvasFocusActive = false;
        SelectNode(pin.Owner);

        // Alt+点击断开连接
        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0)
        {
            _editorService.ClearConnectionsForPin(pin);
            SetStatus($"已断开 {pin.Owner.Title}.{pin.DisplayName} 的所有连接。");
            e.Handled = true;
            return;
        }

        // 从输出引脚开始拖拽连线
        if (pin.Direction == PinDirection.Output)
        {
            _pendingOutputPin = pin;
            _isConnecting = true;
            _wireWasDragged = false;
            GraphViewport.CaptureMouse();
            var pos = ViewportToGraph(e.GetPosition(GraphViewport));
            UpdatePreviewConnectionGeometry(pin, pos);
            PreviewConnectionPath.Visibility = Visibility.Visible;
            SetStatus($"从{pin.Owner.Title}.{pin.DisplayName} 拖拽连线...");
            e.Handled = true;
            return;
        }

        // 点击输入引脚完成连线
        if (pin.Direction == PinDirection.Input && _pendingOutputPin is not null && _isConnecting)
        {
            TryConnectPending(pin);
            e.Handled = true;
        }
    }

    private void PinButton_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not PinViewModel pin)
            return;
        if (!_isConnecting || _pendingOutputPin is null) return;
        if (pin == _pendingOutputPin)
        {
            e.Handled = true;
            return;
        }

        TryConnectPending(pin);
        e.Handled = true;
    }

    private void TryConnectPending(PinViewModel targetPin)
    {
        if (_pendingOutputPin is null || !_isConnecting) return;

        if (!_editorService.CanConnect(_pendingOutputPin, targetPin, out var reason))
        {
            SetStatus(reason);
            CancelPendingConnection(null);
            return;
        }

        _editorService.CreateConnection(_pendingOutputPin, targetPin);
        SetStatus($"已连接：{_pendingOutputPin.Owner.Title}.{_pendingOutputPin.DisplayName} -> {targetPin.Owner.Title}.{targetPin.DisplayName}");
        CancelPendingConnection(null);
    }

    private void CancelPendingConnection(string? statusMessage)
    {
        _pendingOutputPin = null;
        _isConnecting = false;
        ReleasePreviewWire();

        if (!string.IsNullOrWhiteSpace(statusMessage))
            SetStatus(statusMessage);
    }

    private void ReleasePreviewWire()
    {
        PreviewConnectionPath.Visibility = Visibility.Collapsed;
        PreviewConnectionPath.Data = null;
        GraphViewport.ReleaseMouseCapture();
    }

    private void UpdatePreviewConnectionGeometry(PinViewModel sourcePin, Point currentPoint)
    {
        var startAnchor = sourcePin.Owner.GetPinAnchor(sourcePin);
        var start = new Point(sourcePin.Owner.X + startAnchor.X, sourcePin.Owner.Y + startAnchor.Y);
        var end = currentPoint;

        var tangent = Math.Max(80, Math.Abs(end.X - start.X) * 0.45);
        var control1 = new Point(start.X + tangent, start.Y);
        var control2 = new Point(end.X - tangent, end.Y);

        var figure = new PathFigure
        {
            StartPoint = start,
            IsClosed = false,
            IsFilled = false,
        };
        figure.Segments.Add(new BezierSegment(control1, control2, end, true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        PreviewConnectionPath.Data = geometry;
    }

    #endregion

    #region 连线交互

    private void ConnectionPath_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var connection = FindConnectionFromSource(e.OriginalSource as DependencyObject);
        if (connection is null) return;

        _isCanvasFocusActive = false;
        var clickPos = ViewportToGraph(e.GetPosition(GraphViewport));
        var routedKind = connection.SourcePin.Kind;

        var reroute = _nodeFactory.CreateRerouteNode(routedKind, clickPos.X - 10, clickPos.Y - 10);
        _editorService.AddNode(reroute);

        var sourcePin = connection.SourcePin;
        var targetPin = connection.TargetPin;

        _editorService.RemoveConnection(connection);

        var rerouteIn = reroute.FindPin("in");
        var rerouteOut = reroute.FindPin("out");
        if (rerouteIn is not null && rerouteOut is not null)
        {
            _editorService.CreateConnection(sourcePin, rerouteIn);
            _editorService.CreateConnection(rerouteOut, targetPin);
        }

        _editorService.UpdatePinConnectionStates();
        SetStatus("已添加路由节点。");
        e.Handled = true;
    }

    private void ConnectionPath_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Alt) == 0) return;
        if (sender is not FrameworkElement element || element.DataContext is not ConnectionViewModel connection)
            return;

        _isCanvasFocusActive = false;
        _editorService.RemoveConnection(connection);
        _editorService.UpdatePinConnectionStates();
        SetStatus("已断开连接。");
        e.Handled = true;
    }

    private ConnectionViewModel? FindConnectionFromSource(DependencyObject? source)
    {
        var current = source;
        while (current is not null)
        {
            if (current is FrameworkElement fe && fe.DataContext is ConnectionViewModel c)
                return c;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    #endregion

    #region 选择操作

    private void SelectNode(NodeBaseViewModel? node)
    {
        ClearSelection();
        if (node is not null)
            node.IsSelected = true;

        LoadNodeToInspector(node);
    }

    private void ClearSelection()
    {
        foreach (var node in _editorService.Nodes)
            node.IsSelected = false;
    }

    private void ApplySelection(Rect selectionBounds)
    {
        var selectedNodes = new List<NodeBaseViewModel>();
        foreach (var node in _editorService.Nodes)
        {
            var nodeBounds = new Rect(node.X, node.Y, node.Width, node.Height);
            var isSelected = selectionBounds.IntersectsWith(nodeBounds);
            node.IsSelected = isSelected;
            if (isSelected)
                selectedNodes.Add(node);
        }

        LoadNodeToInspector(selectedNodes.FirstOrDefault());
        SetStatus(selectedNodes.Count == 0 ? "未选中任何节点。" : $"已框选 {selectedNodes.Count} 个节点。");
    }

    #endregion

    #region 键盘快捷键

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Esc：取消执行或取消连线
        if (e.Key == Key.Escape)
        {
            if (_executionCts is not null)
            {
                Logger.Info("===== 用户取消执行 (ESC) =====");
                _executionCts.Cancel();
                _runtimeExecutor.ReleaseAllKeys();
                SetStatus("正在停止执行...");
            }
            e.Handled = true;
            return;
        }

        if (IsFocusInside(GraphListBox))
        {
            if (Keyboard.FocusedElement is not TextBox)
            {
                if (e.Key == Key.Delete)
                {
                    DeleteSelectedGraphItem();
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.F2)
                {
                    if (SelectedGraphItem is not null)
                        StartRenameGraphItem(SelectedGraphItem);
                    e.Handled = true;
                    return;
                }
            }

            return;
        }

        // 文本框中不处理快捷键
        if (Keyboard.FocusedElement is TextBox) return;

        // Delete：删除选中节点
        if (e.Key == Key.Delete)
        {
            _editorService.RemoveSelectedNodes();
            SelectNode(null);
            e.Handled = true;
            return;
        }

        // Ctrl+C/V：复制粘贴
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            if (e.Key == Key.C)
            {
                CopySelectedNodes();
                e.Handled = true;
            }
            else if (e.Key == Key.V)
            {
                PasteNodesAtMouse();
                e.Handled = true;
            }
            return;
        }

        // Q：横向对齐
        if (e.Key == Key.F && _isCanvasFocusActive)
        {
            FitGraphToView();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Q)
        {
            AlignSelectedNodesHorizontal();
            e.Handled = true;
            return;
        }

        // Shift+Alt+S：纵向对齐
        var effectiveKey = e.Key == Key.System ? e.SystemKey : e.Key;
        if (effectiveKey == Key.S &&
            (Keyboard.Modifiers & (ModifierKeys.Shift | ModifierKeys.Alt)) == (ModifierKeys.Shift | ModifierKeys.Alt))
        {
            AlignSelectedNodesVertical();
            e.Handled = true;
        }
    }

    private void CopySelectedNodes()
    {
        _clipboardService.CopySelectedNodes(_editorService.Nodes, _editorService.Connections);
        SetStatus($"已复制 {_clipboardService.ClipboardNodeCount} 个节点。");
    }

    private void PasteNodesAtMouse()
    {
        if (!_clipboardService.HasClipboardContent) return;

        ClearSelection();

        var pastedNodes = _clipboardService.PasteNodesAt(
            _lastMousePosition,
            _nodeFactory.CreateNodeId,
            out var connections);

        foreach (var node in pastedNodes)
        {
            _editorService.AddNode(node);
        }

        // 恢复连接
        foreach (var (sourceId, sourcePin, targetId, targetPin) in connections)
        {
            var sourceNode = _editorService.Nodes.FirstOrDefault(n => n.Id == sourceId);
            var targetNode = _editorService.Nodes.FirstOrDefault(n => n.Id == targetId);
            if (sourceNode is null || targetNode is null) continue;

            var sPin = sourceNode.OutputPins.FirstOrDefault(p => p.Name == sourcePin);
            var tPin = targetNode.InputPins.FirstOrDefault(p => p.Name == targetPin);
            if (sPin is not null && tPin is not null)
            {
                _editorService.CreateConnection(sPin, tPin);
            }
        }

        EnsureCanvasLargeEnough();
        SetStatus($"已粘贴 {pastedNodes.Count} 个节点。");
    }

    private void AlignSelectedNodesHorizontal()
    {
        var selectedNodes = _editorService.Nodes.Where(n => n.IsSelected).ToList();
        if (selectedNodes.Count < 2) return;

        var avgY = selectedNodes.Average(n => n.Y);
        foreach (var node in selectedNodes)
            node.Y = avgY;

        if (_activeGraphItem is not null)
            _activeGraphItem.IsDirty = true;

        SetStatus($"已将 {selectedNodes.Count} 个节点横向对齐。");
    }

    private void AlignSelectedNodesVertical()
    {
        var selectedNodes = _editorService.Nodes.Where(n => n.IsSelected).ToList();
        if (selectedNodes.Count < 2) return;

        var avgX = selectedNodes.Average(n => n.X);
        foreach (var node in selectedNodes)
            node.X = avgX;

        if (_activeGraphItem is not null)
            _activeGraphItem.IsDirty = true;

        SetStatus($"已将 {selectedNodes.Count} 个节点纵向对齐。");
    }

    #endregion

    #region 属性面板

    private void LoadNodeToInspector(NodeBaseViewModel? node)
    {
        if (node is null)
        {
            _isLoadingInspector = true;
            NodeTitleTextBox.Text = string.Empty;
            HideAllInspectorPanels();
            InspectorHintTextBlock.Text = "请选择一个节点进行编辑。";
            RefreshInspectorLocks(node);
            _isLoadingInspector = false;
            return;
        }

        _isLoadingInspector = true;
        NodeTitleTextBox.Text = node.Title;
        InspectorHintTextBlock.Text = $"当前选中：{node.Title}";

        HideAllInspectorPanels();

        switch (node)
        {
            case FindImageNodeViewModel findImage:
                FindImageInspectorPanel.Visibility = Visibility.Visible;
                FindImagePathTextBox.Text = findImage.ImagePath;
                FindImageThresholdTextBox.Text = findImage.SimilarityThresholdPercent.ToString();
                break;

            case FindTextNodeViewModel findTextNode:
                FindTextInspectorPanel.Visibility = Visibility.Visible;
                bool hasTextInput = IsInputPinConnected(findTextNode, "text");
                FindTextTextBox.Text = hasTextInput ? "前置输入" : findTextNode.Text;
                FindTextThresholdTextBox.Text = findTextNode.SimilarityThresholdPercent.ToString();
                break;

            case MouseClickNodeViewModel mouseNode:
                MouseLeftInspectorPanel.Visibility = Visibility.Visible;
                MousePositionXTextBox.Text = mouseNode.PositionX.ToString("0.##");
                MousePositionYTextBox.Text = mouseNode.PositionY.ToString("0.##");
                MouseClickOperationModeComboBox.SelectedIndex = (int)mouseNode.OperationMode;
                MouseButtonComboBox.SelectedIndex = (int)mouseNode.MouseButton;
                break;

            case KeyboardNodeViewModel keyboardNode:
                KeyboardInspectorPanel.Visibility = Visibility.Visible;
                PopulateKeyboardKeyComboBox(keyboardNode.Key);
                KeyboardOperationModeComboBox.SelectedIndex = keyboardNode.OperationMode switch
                {
                    PressReleaseMode.Press => 0,
                    PressReleaseMode.Release => 1,
                    PressReleaseMode.Click => 2,
                    _ => 0,
                };
                break;

            case IfNodeViewModel ifNode:
                IfInspectorPanel.Visibility = Visibility.Visible;
                IfConditionComboBox.SelectedIndex = ifNode.ConditionValue ? 1 : 0;
                break;

            case WhileLoopNodeViewModel whileNode:
                WhileLoopInspectorPanel.Visibility = Visibility.Visible;
                WhileLoopConditionComboBox.SelectedIndex = whileNode.ConditionValue ? 1 : 0;
                WhileLoopModeComboBox.SelectedIndex = whileNode.LoopMode == WhileLoopMode.Infinite ? 1 : 0;
                WhileMaxIterationsTextBox.Text = whileNode.MaxIterations.ToString();
                WhileMaxIterationsLabel.Visibility = whileNode.LoopMode == WhileLoopMode.Infinite ? Visibility.Collapsed : Visibility.Visible;
                WhileMaxIterationsTextBox.Visibility = whileNode.LoopMode == WhileLoopMode.Infinite ? Visibility.Collapsed : Visibility.Visible;
                break;

            case ForLoopNodeViewModel forLoopNode:
                ForLoopInspectorPanel.Visibility = Visibility.Visible;
                ForLoopCountTextBox.Text = forLoopNode.LoopCount.ToString();
                ForLoopEndConditionComboBox.SelectedIndex = forLoopNode.EndConditionValue ? 1 : 0;
                break;

            case ScrollWheelNodeViewModel scrollNode:
                ScrollWheelInspectorPanel.Visibility = Visibility.Visible;
                ScrollWheelActionComboBox.SelectedIndex = (int)scrollNode.ScrollAction;
                ScrollWheelSpeedTextBox.Text = scrollNode.ScrollSpeed.ToString();
                ScrollWheelIntervalTextBox.Text = scrollNode.ScrollInterval.ToString();
                ScrollWheelDurationTextBox.Text = scrollNode.ScrollDuration.ToString();
                break;

            case DelayNodeViewModel delayNode:
                DelayInspectorPanel.Visibility = Visibility.Visible;
                DelayMsTextBox.Text = delayNode.DelayMs.ToString();
                break;

            case MouseMoveNodeViewModel moveNode:
                MouseMoveInspectorPanel.Visibility = Visibility.Visible;
                MouseMovePositionXTextBox.Text = moveNode.PositionX.ToString("0.##");
                MouseMovePositionYTextBox.Text = moveNode.PositionY.ToString("0.##");
                break;

            case StartProgramNodeViewModel startProg:
                StartProgramInspectorPanel.Visibility = Visibility.Visible;
                StartProgramPathTextBox.Text = startProg.ProgramPath;
                StartProgramWaitTimeoutTextBox.Text = startProg.WaitTimeoutMs.ToString();
                StartProgramFailureActionComboBox.SelectedIndex = startProg.FailureAction == ProgramStartFailureAction.Retry ? 1 : 0;
                StartProgramRetryCountTextBox.Text = startProg.RetryCount.ToString();
                break;

            case PrintLogNodeViewModel printNode:
                PrintLogInspectorPanel.Visibility = Visibility.Visible;
                bool hasMsgInput = IsInputPinConnected(printNode, "message");
                PrintLogMessageTextBox.Text = hasMsgInput ? "前置输入" : printNode.Message;
                break;

            case SelectWindowNodeViewModel selectWindowNode:
                SelectWindowInspectorPanel.Visibility = Visibility.Visible;
                bool hasProcessNameInput = IsInputPinConnected(selectWindowNode, "process_name");
                SelectWindowProcessNameTextBox.Text = hasProcessNameInput ? "前置输入" : selectWindowNode.ProcessName;
                break;
        }

        RefreshInspectorLocks(node);
        _isLoadingInspector = false;
    }

    private void HideAllInspectorPanels()
    {
        FindImageInspectorPanel.Visibility = Visibility.Collapsed;
        FindTextInspectorPanel.Visibility = Visibility.Collapsed;
        MouseLeftInspectorPanel.Visibility = Visibility.Collapsed;
        KeyboardInspectorPanel.Visibility = Visibility.Collapsed;
        ScrollWheelInspectorPanel.Visibility = Visibility.Collapsed;
        IfInspectorPanel.Visibility = Visibility.Collapsed;
        ForLoopInspectorPanel.Visibility = Visibility.Collapsed;
        WhileLoopInspectorPanel.Visibility = Visibility.Collapsed;
        DelayInspectorPanel.Visibility = Visibility.Collapsed;
        MouseMoveInspectorPanel.Visibility = Visibility.Collapsed;
        StartProgramInspectorPanel.Visibility = Visibility.Collapsed;
        PrintLogInspectorPanel.Visibility = Visibility.Collapsed;
        SelectWindowInspectorPanel.Visibility = Visibility.Collapsed;
    }

    private void ApplyInspectorChanges()
    {
        if (_editorService.Nodes.FirstOrDefault(n => n.IsSelected) is not { } node || _isLoadingInspector)
            return;

        node.Title = NodeTitleTextBox.Text.Trim();

        switch (node)
        {
            case FindImageNodeViewModel findImage:
                findImage.ImagePath = FindImagePathTextBox.Text.Trim();
                if (int.TryParse(FindImageThresholdTextBox.Text.Trim(), out var threshold))
                    findImage.SimilarityThresholdPercent = threshold;
                break;

            case FindTextNodeViewModel findTextNode:
                if (!IsInputPinConnected(findTextNode, "text"))
                    findTextNode.Text = FindTextTextBox.Text;
                if (int.TryParse(FindTextThresholdTextBox.Text.Trim(), out var tt))
                    findTextNode.SimilarityThresholdPercent = tt;
                break;

            case MouseClickNodeViewModel mouseNode:
                mouseNode.OperationMode = (PressReleaseMode)MouseClickOperationModeComboBox.SelectedIndex;
                mouseNode.MouseButton = (Graph.MouseButton)MouseButtonComboBox.SelectedIndex;
                if (double.TryParse(MousePositionXTextBox.Text.Trim(), out var x))
                    mouseNode.PositionX = x;
                if (double.TryParse(MousePositionYTextBox.Text.Trim(), out var y))
                    mouseNode.PositionY = y;
                break;

            case KeyboardNodeViewModel keyboardNode:
                keyboardNode.OperationMode = KeyboardOperationModeComboBox.SelectedIndex switch
                {
                    1 => PressReleaseMode.Release,
                    2 => PressReleaseMode.Click,
                    _ => PressReleaseMode.Press,
                };
                if (KeyboardKeyComboBox.SelectedItem is ComboBoxItem keyItem && keyItem.Tag is string keyStr)
                    keyboardNode.Key = keyStr;
                break;

            case IfNodeViewModel ifNode:
                ifNode.ConditionValue = IfConditionComboBox.SelectedIndex == 1;
                break;

            case WhileLoopNodeViewModel whileNode:
                whileNode.ConditionValue = WhileLoopConditionComboBox.SelectedIndex == 1;
                whileNode.LoopMode = WhileLoopModeComboBox.SelectedIndex == 1
                    ? WhileLoopMode.Infinite : WhileLoopMode.Finite;
                if (int.TryParse(WhileMaxIterationsTextBox.Text.Trim(), out var wm))
                    whileNode.MaxIterations = Math.Max(1, wm);
                // Toggle max iterations visibility based on loop mode
                bool isInfinite = whileNode.LoopMode == WhileLoopMode.Infinite;
                WhileMaxIterationsLabel.Visibility = isInfinite ? Visibility.Collapsed : Visibility.Visible;
                WhileMaxIterationsTextBox.Visibility = isInfinite ? Visibility.Collapsed : Visibility.Visible;
                break;

            case ForLoopNodeViewModel forLoopNode:
                if (int.TryParse(ForLoopCountTextBox.Text.Trim(), out var count))
                    forLoopNode.LoopCount = Math.Max(1, count);
                forLoopNode.EndConditionValue = ForLoopEndConditionComboBox.SelectedIndex == 1;
                break;

            case ScrollWheelNodeViewModel scrollNode:
                scrollNode.ScrollAction = (ScrollWheelAction)ScrollWheelActionComboBox.SelectedIndex;
                if (int.TryParse(ScrollWheelSpeedTextBox.Text.Trim(), out var speed))
                    scrollNode.ScrollSpeed = Math.Max(0, speed);
                if (int.TryParse(ScrollWheelIntervalTextBox.Text.Trim(), out var interval))
                    scrollNode.ScrollInterval = Math.Max(1, interval);
                if (int.TryParse(ScrollWheelDurationTextBox.Text.Trim(), out var duration))
                    scrollNode.ScrollDuration = Math.Max(0, duration);
                break;

            case DelayNodeViewModel delayNode:
                if (int.TryParse(DelayMsTextBox.Text.Trim(), out var delayMs))
                    delayNode.DelayMs = delayMs;
                break;

            case MouseMoveNodeViewModel moveNode:
                if (double.TryParse(MouseMovePositionXTextBox.Text.Trim(), out var moveX))
                    moveNode.PositionX = moveX;
                if (double.TryParse(MouseMovePositionYTextBox.Text.Trim(), out var moveY))
                    moveNode.PositionY = moveY;
                break;

            case StartProgramNodeViewModel startProg:
                startProg.ProgramPath = StartProgramPathTextBox.Text.Trim();
                if (int.TryParse(StartProgramWaitTimeoutTextBox.Text.Trim(), out var wt))
                    startProg.WaitTimeoutMs = Math.Max(0, wt);
                startProg.FailureAction = StartProgramFailureActionComboBox.SelectedIndex == 1
                    ? ProgramStartFailureAction.Retry : ProgramStartFailureAction.None;
                if (int.TryParse(StartProgramRetryCountTextBox.Text.Trim(), out var rc))
                    startProg.RetryCount = Math.Max(0, rc);
                break;

            case PrintLogNodeViewModel printNode:
                if (!IsInputPinConnected(printNode, "message"))
                    printNode.Message = PrintLogMessageTextBox.Text;
                break;

            case SelectWindowNodeViewModel selectWindowNode:
                if (!IsInputPinConnected(selectWindowNode, "process_name"))
                    selectWindowNode.ProcessName = SelectWindowProcessNameTextBox.Text.Trim();
                break;
        }

        node.RefreshDescription();
        if (_activeGraphItem is not null)
        {
            _activeGraphItem.IsDirty = true;
        }
        InspectorHintTextBlock.Text = $"当前选中：{node.Title}（已自动保存）";
        SetStatus($"节点已自动保存：{node.Title}");
    }

    private void InspectorField_TextChanged(object sender, TextChangedEventArgs e) => ApplyInspectorChanges();
    private void InspectorField_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyInspectorChanges();

    private void BrowseFindImagePath_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择图片文件",
            Filter = "图片文件 (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|所有文件(*.*)|*.*",
        };

        if (dialog.ShowDialog(this) == true)
        {
            FindImagePathTextBox.Text = dialog.FileName;
            ApplyInspectorChanges();
        }
    }

    private void BrowseStartProgramPath_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择应用程序",
            Filter = "可执行文件 (*.exe;*.bat;*.cmd)|*.exe;*.bat;*.cmd|所有文件(*.*)|*.*",
        };

        if (dialog.ShowDialog(this) == true)
        {
            StartProgramPathTextBox.Text = dialog.FileName;
            ApplyInspectorChanges();
        }
    }

    #endregion

    #region 日志面板

    private void RefreshLogList()
    {
        if (LogListBox is null) return;

        var filtered = LoggingModule.Filter(Logger.Entries).ToList();
        LogListBox.ItemsSource = filtered;
        if (filtered.Count > 0)
            LogListBox.ScrollIntoView(filtered[^1]);
    }

    private void FilterRadio_Checked(object sender, RoutedEventArgs e)
    {
        LoggingModule.FilterLevel = FilterAllRadio.IsChecked == true ? null :
                                    FilterInfoRadio.IsChecked == true ? LogLevel.Info :
                                    FilterWarnRadio.IsChecked == true ? LogLevel.Warn :
                                    FilterErrorRadio.IsChecked == true ? LogLevel.Error : null;
        RefreshLogList();
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e)
    {
        Logger.Entries.Clear();
        LogListBox.ItemsSource = null;
    }

    #endregion

    #region 拖拽导入

    private void Window_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Length == 1 && Path.GetExtension(files[0]).Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                e.Effects = DragDropEffects.Copy;
                return;
            }
        }
        e.Effects = DragDropEffects.None;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        if (files.Length != 1) return;

        var filePath = files[0];
        if (!Path.GetExtension(filePath).Equals(".json", StringComparison.OrdinalIgnoreCase)) return;

        var result = WpfMessageBox.Show(
            this,
            $"是否导入图谱？\n\n{Path.GetFileName(filePath)}",
            "导入图谱",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            ImportGraphFileToList(filePath);
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show(this, ex.Message, "导入失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #endregion

    #region 辅助方法

    private void OnGraphChanged()
    {
        _editorService.UpdatePinConnectionStates();
        if (!_isLoadingGraph && _activeGraphItem is not null)
        {
            _activeGraphItem.IsDirty = true;
        }

        if (_editorService.Nodes.FirstOrDefault(n => n.IsSelected) is { } selected)
        {
            LoadNodeToInspector(selected);
        }
    }

    private void EnsureCanvasLargeEnough()
    {
        // Infinite canvas mode no longer resizes the surface by viewport bounds.
    }

    private void SyncNodeFactorySequence()
    {
        var maxSeq = _editorService.Nodes
            .Select(n => n.Id)
            .Select(id => id.StartsWith("node_") && int.TryParse(id[5..], out var num) ? num : 0)
            .DefaultIfEmpty(0)
            .Max();
        _nodeFactory.ResetCounter(maxSeq);
    }

    private bool IsInputPinConnected(NodeBaseViewModel node, string pinName)
    {
        return node.InputPins.FirstOrDefault(p => p.Name == pinName)?.HasConnection ?? false;
    }

    private Point ViewportToGraph(Point viewportPoint)
    {
        return new Point(
            (viewportPoint.X - GraphPanTransform.X) / _zoomLevel,
            (viewportPoint.Y - GraphPanTransform.Y) / _zoomLevel);
    }

    private void FitGraphToView()
    {
        if (_editorService.Nodes.Count == 0 || GraphViewport.ActualWidth <= 0 || GraphViewport.ActualHeight <= 0)
            return;

        double left = _editorService.Nodes.Min(n => n.X);
        double top = _editorService.Nodes.Min(n => n.Y);
        double right = _editorService.Nodes.Max(n => n.X + n.Width);
        double bottom = _editorService.Nodes.Max(n => n.Y + n.Height);

        double width = Math.Max(1, right - left);
        double height = Math.Max(1, bottom - top);
        double padding = 0.08;
        double viewWidth = GraphViewport.ActualWidth * (1.0 - padding * 2.0);
        double viewHeight = GraphViewport.ActualHeight * (1.0 - padding * 2.0);

        double zoomX = viewWidth / width;
        double zoomY = viewHeight / height;
        _zoomLevel = Math.Clamp(Math.Min(zoomX, zoomY), 0.1, 2.5);
        GraphZoomTransform.ScaleX = _zoomLevel;
        GraphZoomTransform.ScaleY = _zoomLevel;

        GraphPanTransform.X = (GraphViewport.ActualWidth - width * _zoomLevel) / 2.0 - left * _zoomLevel;
        GraphPanTransform.Y = (GraphViewport.ActualHeight - height * _zoomLevel) / 2.0 - top * _zoomLevel;
        SetStatus("已缩放到节点全览。");
    }

    private void RefreshInspectorLocks(NodeBaseViewModel? node)
    {
        if (node is MouseClickNodeViewModel cn)
        {
            bool locked = IsInputPinConnected(cn, "position");
            LockTextBox(MousePositionXTextBox, locked, cn.PositionX.ToString("0.##"));
            LockTextBox(MousePositionYTextBox, locked, cn.PositionY.ToString("0.##"));
        }
        else
        {
            LockTextBox(MousePositionXTextBox, false, "");
            LockTextBox(MousePositionYTextBox, false, "");
        }

        if (node is MouseMoveNodeViewModel mn)
        {
            bool locked = IsInputPinConnected(mn, "position");
            LockTextBox(MouseMovePositionXTextBox, locked, mn.PositionX.ToString("0.##"));
            LockTextBox(MouseMovePositionYTextBox, locked, mn.PositionY.ToString("0.##"));
        }
        else
        {
            LockTextBox(MouseMovePositionXTextBox, false, "");
            LockTextBox(MouseMovePositionYTextBox, false, "");
        }

        if (node is IfNodeViewModel ifNode)
        {
            bool locked = IsInputPinConnected(ifNode, "condition");
            LockConditionCombo(IfConditionComboBox, locked, ifNode.ConditionValue);
        }
        else
        {
            LockConditionCombo(IfConditionComboBox, false, false);
        }

        if (node is WhileLoopNodeViewModel wNode)
        {
            bool locked = IsInputPinConnected(wNode, "condition");
            LockConditionCombo(WhileLoopConditionComboBox, locked, wNode.ConditionValue);
        }
        else
        {
            LockConditionCombo(WhileLoopConditionComboBox, false, false);
        }

        if (node is ForLoopNodeViewModel flNode)
        {
            bool locked = IsInputPinConnected(flNode, "end_condition");
            LockConditionCombo(ForLoopEndConditionComboBox, locked, flNode.EndConditionValue);
        }
        else
        {
            LockConditionCombo(ForLoopEndConditionComboBox, false, false);
        }

        if (node is PrintLogNodeViewModel plNode)
        {
            bool locked = IsInputPinConnected(plNode, "message");
            LockTextBox(PrintLogMessageTextBox, locked, plNode.Message);
        }
        else
        {
            LockTextBox(PrintLogMessageTextBox, false, "");
        }

        if (node is SelectWindowNodeViewModel swNode)
        {
            bool locked = IsInputPinConnected(swNode, "process_name");
            LockTextBox(SelectWindowProcessNameTextBox, locked, swNode.ProcessName);
        }
        else
        {
            LockTextBox(SelectWindowProcessNameTextBox, false, "");
        }

        if (node is FindTextNodeViewModel ftNode)
        {
            bool locked = IsInputPinConnected(ftNode, "text");
            LockTextBox(FindTextTextBox, locked, ftNode.Text);
        }
        else
        {
            LockTextBox(FindTextTextBox, false, "");
        }
    }

    private static void LockTextBox(TextBox tb, bool locked, string restoreValue)
    {
        tb.IsEnabled = !locked;
        if (locked)
        {
            tb.Text = "前置输入";
            tb.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x7A, 0x87, 0x97));
            tb.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x25, 0x29, 0x30));
            tb.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3A, 0x40, 0x4A));
        }
        else
        {
            tb.Text = restoreValue;
            tb.ClearValue(System.Windows.Controls.Control.ForegroundProperty);
            tb.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
            tb.ClearValue(System.Windows.Controls.Control.BorderBrushProperty);
        }
    }

    private static void LockConditionCombo(System.Windows.Controls.ComboBox cb, bool locked, bool restoreValue)
    {
        cb.IsEnabled = !locked;
        if (locked)
        {
            cb.Foreground = System.Windows.Media.Brushes.Gray;
            cb.Items.Clear();
            cb.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = "前置输入" });
            cb.SelectedIndex = 0;
        }
        else
        {
            cb.ClearValue(System.Windows.Controls.Control.ForegroundProperty);
            if (cb.Items.Count != 2)
            {
                cb.Items.Clear();
                cb.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = "False" });
                cb.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = "True" });
                cb.SelectedIndex = restoreValue ? 1 : 0;
            }
        }
    }

    private bool TryGetPinAtPosition(Point position, out PinViewModel? pin)
    {
        var hit = GraphSurface.InputHitTest(position) as DependencyObject;
        return TryGetPinFromSource(hit, out pin);
    }

    private static bool TryGetPinFromSource(DependencyObject? source, out PinViewModel? pin)
    {
        var current = source;
        while (current is not null)
        {
            if (current is FrameworkElement element && element.DataContext is PinViewModel dataPin)
            {
                pin = dataPin;
                return true;
            }
            current = VisualTreeHelper.GetParent(current);
        }
        pin = null;
        return false;
    }

    private static bool IsPinInteractionSource(DependencyObject? source)
    {
        var current = source;
        while (current is not null)
        {
            if (current is FrameworkElement element && element.DataContext is PinViewModel)
                return true;
            current = VisualTreeHelper.GetParent(current);
        }
        return false;
    }

    private bool IsGraphBlankSource(DependencyObject? source)
    {
        var current = source;
        while (current is not null)
        {
            if (current is FrameworkElement element)
            {
                if (element.DataContext is NodeBaseViewModel or PinViewModel or ConnectionViewModel)
                    return false;

                if (ReferenceEquals(element, GraphSurface) || ReferenceEquals(element, GraphViewport))
                    return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private void PopulateKeyboardKeyComboBox(string selectedKey)
    {
        KeyboardKeyComboBox.Items.Clear();
        string[] keys =
        {
            "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M",
            "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z",
            "D0", "D1", "D2", "D3", "D4", "D5", "D6", "D7", "D8", "D9",
            "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12",
            "Enter", "Escape", "Space", "Tab", "Backspace",
            "Shift", "Control", "Alt",
            "Left", "Up", "Right", "Down",
            "Insert", "DeleteKey", "Home", "End", "PageUp", "PageDown",
            "NumPad0", "NumPad1", "NumPad2", "NumPad3", "NumPad4",
            "NumPad5", "NumPad6", "NumPad7", "NumPad8", "NumPad9",
            "Add", "Subtract", "Multiply", "Divide",
            "LWin", "RWin",
        };

        foreach (var key in keys)
        {
            var item = new ComboBoxItem { Content = key, Tag = key };
            KeyboardKeyComboBox.Items.Add(item);
            if (key == selectedKey)
                KeyboardKeyComboBox.SelectedItem = item;
        }
    }

    private void SetStatus(string message)
    {
        StatusTextBlock.Text = message;
    }

    #endregion

    #region 引脚锚点更新

    private void PinAnchor_Loaded(object sender, RoutedEventArgs e)
    {
        UpdatePinAnchorFromElement(sender as FrameworkElement);
    }

    private void PinAnchor_LayoutUpdated(object? sender, EventArgs e)
    {
        UpdatePinAnchorFromElement(sender as FrameworkElement);
    }

    private static void UpdatePinAnchorFromElement(FrameworkElement? element)
    {
        if (element is null || element.DataContext is not PinViewModel pin)
            return;

        var nodeRoot = FindNodeRootElement(element);
        if (nodeRoot is null || element.ActualWidth <= 0 || element.ActualHeight <= 0)
            return;

        try
        {
            var transform = element.TransformToAncestor(nodeRoot);
            var localTopLeft = transform.Transform(new Point(0, 0));
            pin.AnchorPoint = new Point(
                localTopLeft.X + element.ActualWidth / 2.0,
                localTopLeft.Y + element.ActualHeight / 2.0);
        }
        catch (InvalidOperationException)
        {
            // Layout tree can be transient while WPF is remeasuring. Safe to ignore.
        }
    }

    private static FrameworkElement? FindNodeRootElement(DependencyObject? source)
    {
        DependencyObject? current = source;
        FrameworkElement? lastMatch = null;
        while (current is not null)
        {
            if (current is Border border && border.DataContext is NodeBaseViewModel)
                lastMatch = border;
            current = VisualTreeHelper.GetParent(current);
        }
        return lastMatch;
    }

    #endregion

    #region 节点右键菜单

    private record NodePaletteItem(string Category, string Name, Func<double, double, NodeBaseViewModel> Factory);

    private List<NodePaletteItem> _paletteItems = [];

    private void InitializeNodePalette()
    {
        _paletteItems =
        [
            new("输入节点", "鼠标点击", (x, y) => _nodeFactory.CreateMouseClickNode(x, y)),
            new("输入节点", "键盘", (x, y) => _nodeFactory.CreateKeyboardNode(x, y)),
            new("输入节点", "鼠标滚轮", (x, y) => _nodeFactory.CreateScrollWheelNode(x, y)),
            new("输入节点", "鼠标移动", (x, y) => _nodeFactory.CreateMouseMoveNode(x, y)),
            new("逻辑节点", "延迟", (x, y) => _nodeFactory.CreateDelayNode(x, y)),
            new("逻辑节点", "分支", (x, y) => _nodeFactory.CreateIfNode(x, y)),
            new("逻辑节点", "For循环", (x, y) => _nodeFactory.CreateForLoopNode(x, y)),
            new("逻辑节点", "While循环", (x, y) => _nodeFactory.CreateWhileLoopNode(x, y)),
            new("功能节点", "启动程序", (x, y) => _nodeFactory.CreateStartProgramNode(x, y)),
            new("功能节点", "选中窗口", (x, y) => _nodeFactory.CreateSelectWindowNode(x, y)),
            new("调试", "打印log", (x, y) => _nodeFactory.CreatePrintLogNode(x, y)),
            new("插件节点", "找图", (x, y) => _nodeFactory.CreateFindImageNode(x, y)),
            new("插件节点", "找字", (x, y) => _nodeFactory.CreateFindTextNode(x, y)),
        ];
    }

    private void OpenNodePalette(Point viewportPos)
    {
        NodePaletteSearchBox.Text = string.Empty;
        BuildNodePaletteContent(string.Empty);

        Canvas.SetLeft(NodePalette, viewportPos.X);
        Canvas.SetTop(NodePalette, viewportPos.Y);
        NodePalette.Visibility = Visibility.Visible;

        Dispatcher.BeginInvoke(new Action(() => NodePaletteSearchBox.Focus()), DispatcherPriority.Render);
    }

    private void CloseNodePalette()
    {
        NodePalette.Visibility = Visibility.Collapsed;
    }

    private void BuildNodePaletteContent(string filter)
    {
        NodePaletteContent.Children.Clear();
        var filtered = _paletteItems.Where(i => string.IsNullOrWhiteSpace(filter) || i.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

        if (filtered.Count == 0)
        {
            NodePaletteContent.Children.Add(new TextBlock
            {
                Text = "未找到匹配节点",
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x7A, 0x87, 0x97)),
                Margin = new Thickness(12, 8, 12, 8),
                FontSize = 12,
            });
            return;
        }

        var groups = filtered.GroupBy(i => i.Category).ToList();
        foreach (var group in groups)
        {
            // 分类标题
            var categoryText = new TextBlock
            {
                Text = group.Key,
                Foreground = System.Windows.Media.Brushes.White,
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                Margin = new Thickness(12, 10, 12, 4),
            };
            NodePaletteContent.Children.Add(categoryText);

            foreach (var item in group)
            {
                var button = new System.Windows.Controls.Button
                {
                    Content = item.Name,
                    Background = System.Windows.Media.Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xD0, 0xD7, 0xE2)),
                    FontSize = 13,
                    Padding = new Thickness(12, 6, 12, 6),
                    HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Tag = item,
                };
                button.Click += NodePaletteItem_Click;
                NodePaletteContent.Children.Add(button);
            }
        }
    }

    private void NodePaletteItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is NodePaletteItem item)
        {
            var graphPos = ViewportToGraph(_rightClickStartPos);
            var node = item.Factory(0, 0);
            node.X = graphPos.X;
            node.Y = graphPos.Y;
            _editorService.AddNode(node);
            SelectNode(node);
            CloseNodePalette();
        }
    }

    private void NodePaletteSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        BuildNodePaletteContent(NodePaletteSearchBox.Text.Trim());
    }

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (NodePalette.Visibility != Visibility.Visible) return;

        var pos = e.GetPosition(NodePalette);
        bool inside = pos.X >= 0 && pos.X <= NodePalette.ActualWidth && pos.Y >= 0 && pos.Y <= NodePalette.ActualHeight;
        if (!inside)
        {
            CloseNodePalette();
        }
    }

    #endregion
}
