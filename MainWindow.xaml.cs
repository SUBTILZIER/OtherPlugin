using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

    // 运行时状态
    private CancellationTokenSource? _executionCts;
    private Point _lastMousePosition;
    private bool _isLoadingInspector;

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
    private double _panHorizontalOffset;
    private double _panVerticalOffset;

    // 缩放
    private double _zoomLevel = 1.0;
    private readonly ScaleTransform _zoomTransform = new(1.0, 1.0);

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
        GraphSurface.LayoutTransform = _zoomTransform;

        // 绑定服务事件
        _editorService.GraphChanged += OnGraphChanged;
        _editorService.StatusChanged += SetStatus;

        // 日志更新
        Logger.Entries.CollectionChanged += (_, _) => RefreshLogList();

        // 窗口关闭时释放按键
        Closing += (_, _) => _runtimeExecutor.ReleaseAllKeys();
    }

    private void InitializeEditor()
    {
        _editorService.NewGraph();
        _nodeFactory.ResetCounter(1);
        EnsureCanvasLargeEnough();
    }

    #endregion

    #region 属性绑定

    public System.Collections.IEnumerable Nodes => _editorService.Nodes;
    public System.Collections.IEnumerable Connections => _editorService.Connections;

    #endregion

    #region 工具栏事件 - 文件操作

    private void NewGraph_Click(object sender, RoutedEventArgs e)
    {
        _editorService.NewGraph();
        _nodeFactory.ResetCounter(1);
        EnsureCanvasLargeEnough();
    }

    private void SaveGraph_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_editorService.CurrentGraphPath))
            {
                SaveGraphAs();
            }
            else
            {
                _editorService.SaveGraph();
            }
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show(this, ex.Message, "保存失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveGraphAs()
    {
        var dialog = new SaveFileDialog
        {
            Title = "保存图谱",
            Filter = "图谱文件 (*.json)|*.json|所有文件 (*.*)|*.*",
            FileName = "graph.json",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        };

        if (dialog.ShowDialog(this) == true)
        {
            _editorService.SaveGraph(dialog.FileName);
        }
    }

    private void OpenGraph_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "打开图谱",
            Filter = "图谱文件 (*.json)|*.json|所有文件 (*.*)|*.*",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        };

        if (dialog.ShowDialog(this) != true) return;

        try
        {
            _editorService.LoadGraph(dialog.FileName);
            SyncNodeFactorySequence();
            EnsureCanvasLargeEnough();
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show(this, ex.Message, "打开失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #endregion

    #region 工具栏事件 - 添加节点

    private void AddFindImageNode_Click(object sender, RoutedEventArgs e) => AddNode(_nodeFactory.CreateFindImageNode(_editorService.Nodes.Count * 40, _editorService.Nodes.Count * 30));
    private void AddMouseLeftNode_Click(object sender, RoutedEventArgs e) => AddNode(_nodeFactory.CreateMouseClickNode(_editorService.Nodes.Count * 40, _editorService.Nodes.Count * 30));
    private void AddMouseMoveNode_Click(object sender, RoutedEventArgs e) => AddNode(_nodeFactory.CreateMouseMoveNode(_editorService.Nodes.Count * 40, _editorService.Nodes.Count * 30));
    private void AddKeyboardNode_Click(object sender, RoutedEventArgs e) => AddNode(_nodeFactory.CreateKeyboardNode(_editorService.Nodes.Count * 40, _editorService.Nodes.Count * 30));
    private void AddScrollWheelNode_Click(object sender, RoutedEventArgs e) => AddNode(_nodeFactory.CreateScrollWheelNode(_editorService.Nodes.Count * 40, _editorService.Nodes.Count * 30));
    private void AddDelayNode_Click(object sender, RoutedEventArgs e) => AddNode(_nodeFactory.CreateDelayNode(_editorService.Nodes.Count * 40, _editorService.Nodes.Count * 30));
    private void AddIfNode_Click(object sender, RoutedEventArgs e) => AddNode(_nodeFactory.CreateIfNode(_editorService.Nodes.Count * 40, _editorService.Nodes.Count * 30));
    private void AddForLoopNode_Click(object sender, RoutedEventArgs e) => AddNode(_nodeFactory.CreateForLoopNode(_editorService.Nodes.Count * 40, _editorService.Nodes.Count * 30));
    private void AddWhileLoopNode_Click(object sender, RoutedEventArgs e) => AddNode(_nodeFactory.CreateWhileLoopNode(_editorService.Nodes.Count * 40, _editorService.Nodes.Count * 30));

    private void AddNode(NodeBaseViewModel node)
    {
        _editorService.AddNode(node);
        SelectNode(node);
        EnsureCanvasLargeEnough();
        SetStatus($"已添加 {node.Title}。");
    }

    #endregion

    #region 工具栏事件 - 执行

    private async void RunGraph_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _executionCts = new CancellationTokenSource();
            var plan = _editorService.BuildExecutionPlan();
            var baseDirectory = !string.IsNullOrWhiteSpace(_editorService.CurrentGraphPath)
                ? Path.GetDirectoryName(_editorService.CurrentGraphPath) ?? Environment.CurrentDirectory
                : Environment.CurrentDirectory;

            SetStatus("执行开始...");
            var ct = _executionCts.Token;
            var result = await Task.Run(() => _runtimeExecutor.Execute(plan, baseDirectory, ct), ct);
            _executionCts = null;
            SetStatus(result.Message);
        }
        catch (OperationCanceledException)
        {
            Logger.Info("===== 执行已取消 =====");
            _runtimeExecutor.ReleaseAllKeys();
            SetStatus("执行已取消。");
        }
        catch (Exception ex)
        {
            _executionCts = null;
            WpfMessageBox.Show(this, ex.Message, "执行失败", MessageBoxButton.OK, MessageBoxImage.Error);
            SetStatus("执行失败。");
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

    private void OpenLogWindow_Click(object sender, RoutedEventArgs e)
    {
        var window = new LogWindow { Owner = this };
        window.Show();
    }

    #endregion

    #region 画布交互 - 鼠标事件

    private void NodeCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not NodeBaseViewModel node)
            return;

        SelectNode(node);
        e.Handled = true;
    }

    private void NodeHeader_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isConnecting) return;
        if (IsPinInteractionSource(e.OriginalSource as DependencyObject)) return;
        if (sender is not FrameworkElement element || element.DataContext is not NodeBaseViewModel node)
            return;

        if (!node.IsSelected)
            SelectNode(node);
        else
            LoadNodeToInspector(node);

        var point = e.GetPosition(GraphSurface);
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

        var point = e.GetPosition(GraphSurface);
        foreach (var (item, offsetX, offsetY) in _dragGroup)
        {
            item.X = Math.Max(0, point.X - offsetX);
            item.Y = Math.Max(0, point.Y - offsetY);
        }
    }

    private void NodeHeader_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is UIElement element)
            element.ReleaseMouseCapture();

        _dragNode = null;
        _dragGroup.Clear();
    }

    private void GraphSurface_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource != GraphSurface) return;

        if (_isConnecting)
        {
            CancelPendingConnection("已取消连线。");
            e.Handled = true;
            return;
        }

        ClearSelection();
        SelectNode(null);
        _isSelecting = true;
        _selectionStart = e.GetPosition(GraphSurface);
        Canvas.SetLeft(SelectionRectangle, _selectionStart.X);
        Canvas.SetTop(SelectionRectangle, _selectionStart.Y);
        SelectionRectangle.Width = 0;
        SelectionRectangle.Height = 0;
        SelectionRectangle.Visibility = Visibility.Visible;
        GraphSurface.CaptureMouse();
        e.Handled = true;
    }

    private void GraphSurface_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        _lastMousePosition = e.GetPosition(GraphSurface);

        if (_isPanning)
        {
            var currentPan = e.GetPosition(GraphScrollViewer);
            var delta = currentPan - _panStart;
            GraphScrollViewer.ScrollToHorizontalOffset(Math.Max(0, _panHorizontalOffset - delta.X));
            GraphScrollViewer.ScrollToVerticalOffset(Math.Max(0, _panVerticalOffset - delta.Y));
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

    private void GraphSurface_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isConnecting)
        {
            if (_pendingOutputPin is not null &&
                TryGetPinAtPosition(e.GetPosition(GraphSurface), out var targetPin) &&
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
        GraphSurface.ReleaseMouseCapture();

        var selectionBounds = new Rect(
            Canvas.GetLeft(SelectionRectangle),
            Canvas.GetTop(SelectionRectangle),
            SelectionRectangle.Width,
            SelectionRectangle.Height);

        SelectionRectangle.Visibility = Visibility.Collapsed;
        ApplySelection(selectionBounds);
        e.Handled = true;
    }

    private void GraphSurface_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource != GraphSurface) return;

        _isPanning = true;
        _panStart = e.GetPosition(GraphScrollViewer);
        _panHorizontalOffset = GraphScrollViewer.HorizontalOffset;
        _panVerticalOffset = GraphScrollViewer.VerticalOffset;
        GraphSurface.CaptureMouse();
        e.Handled = true;
    }

    private void GraphSurface_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isPanning) return;

        _isPanning = false;
        GraphSurface.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void GraphScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var mouseInViewport = e.GetPosition(GraphScrollViewer);

        var oldZoom = _zoomLevel;
        var zoomDelta = e.Delta > 0 ? 0.1 : -0.1;
        var newZoom = Math.Clamp(oldZoom + zoomDelta, 0.1, 2.0);

        if (Math.Abs(newZoom - oldZoom) < 0.001) return;

        _zoomLevel = newZoom;
        _zoomTransform.ScaleX = newZoom;
        _zoomTransform.ScaleY = newZoom;

        EnsureCanvasLargeEnough();

        var canvasX = (GraphScrollViewer.HorizontalOffset + mouseInViewport.X) / oldZoom;
        var canvasY = (GraphScrollViewer.VerticalOffset + mouseInViewport.Y) / oldZoom;

        GraphScrollViewer.ScrollToHorizontalOffset(Math.Max(0, canvasX * newZoom - mouseInViewport.X));
        GraphScrollViewer.ScrollToVerticalOffset(Math.Max(0, canvasY * newZoom - mouseInViewport.Y));

        e.Handled = true;
    }

    #endregion

    #region 引脚交互

    private void PinButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not PinViewModel pin)
            return;

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
            GraphSurface.CaptureMouse();
            var pos = e.GetPosition(GraphSurface);
            UpdatePreviewConnectionGeometry(pin, pos);
            PreviewConnectionPath.Visibility = Visibility.Visible;
            SetStatus($"从 {pin.Owner.Title}.{pin.DisplayName} 拖出连线...");
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
        GraphSurface.ReleaseMouseCapture();
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

        var clickPos = e.GetPosition(GraphSurface);
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

        SetStatus($"已将 {selectedNodes.Count} 个节点横向对齐。");
    }

    private void AlignSelectedNodesVertical()
    {
        var selectedNodes = _editorService.Nodes.Where(n => n.IsSelected).ToList();
        if (selectedNodes.Count < 2) return;

        var avgX = selectedNodes.Average(n => n.X);
        foreach (var node in selectedNodes)
            node.X = avgX;

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

            case MouseClickNodeViewModel mouseNode:
                MouseLeftInspectorPanel.Visibility = Visibility.Visible;
                var hasPositionInput = IsInputPinConnected(mouseNode, "position");
                MousePositionXTextBox.IsEnabled = !hasPositionInput;
                MousePositionYTextBox.IsEnabled = !hasPositionInput;
                MousePositionXTextBox.Text = hasPositionInput ? "来自前置节点" : mouseNode.PositionX.ToString("0.##");
                MousePositionYTextBox.Text = hasPositionInput ? "来自前置节点" : mouseNode.PositionY.ToString("0.##");
                MouseClickOperationModeComboBox.SelectedIndex = (int)mouseNode.OperationMode;
                MouseButtonComboBox.SelectedIndex = (int)mouseNode.MouseButton;
                break;

            case KeyboardNodeViewModel keyboardNode:
                KeyboardInspectorPanel.Visibility = Visibility.Visible;
                PopulateKeyboardKeyComboBox(keyboardNode.Key);
                KeyboardOperationModeComboBox.SelectedIndex = keyboardNode.OperationMode == PressReleaseMode.Press ? 0 : 1;
                break;

            case IfNodeViewModel ifNode:
                IfInspectorPanel.Visibility = Visibility.Visible;
                var hasCondInput = IsInputPinConnected(ifNode, "condition");
                IfConditionComboBox.IsEnabled = !hasCondInput;
                IfConditionComboBox.SelectedIndex = ifNode.ConditionValue ? 1 : 0;
                break;

            case WhileLoopNodeViewModel whileNode:
                WhileLoopInspectorPanel.Visibility = Visibility.Visible;
                var hasWhileCondInput = IsInputPinConnected(whileNode, "condition");
                WhileLoopConditionComboBox.IsEnabled = !hasWhileCondInput;
                WhileLoopConditionComboBox.SelectedIndex = whileNode.ConditionValue ? 1 : 0;
                break;

            case ForLoopNodeViewModel forLoopNode:
                ForLoopInspectorPanel.Visibility = Visibility.Visible;
                ForLoopCountTextBox.Text = forLoopNode.LoopCount.ToString();
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
                var hasMovePositionInput = IsInputPinConnected(moveNode, "position");
                MouseMovePositionXTextBox.IsEnabled = !hasMovePositionInput;
                MouseMovePositionYTextBox.IsEnabled = !hasMovePositionInput;
                MouseMovePositionXTextBox.Text = hasMovePositionInput ? "来自前置节点" : moveNode.PositionX.ToString("0.##");
                MouseMovePositionYTextBox.Text = hasMovePositionInput ? "来自前置节点" : moveNode.PositionY.ToString("0.##");
                break;
        }

        _isLoadingInspector = false;
    }

    private void HideAllInspectorPanels()
    {
        FindImageInspectorPanel.Visibility = Visibility.Collapsed;
        MouseLeftInspectorPanel.Visibility = Visibility.Collapsed;
        KeyboardInspectorPanel.Visibility = Visibility.Collapsed;
        ScrollWheelInspectorPanel.Visibility = Visibility.Collapsed;
        IfInspectorPanel.Visibility = Visibility.Collapsed;
        ForLoopInspectorPanel.Visibility = Visibility.Collapsed;
        WhileLoopInspectorPanel.Visibility = Visibility.Collapsed;
        DelayInspectorPanel.Visibility = Visibility.Collapsed;
        MouseMoveInspectorPanel.Visibility = Visibility.Collapsed;
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

            case MouseClickNodeViewModel mouseNode:
                mouseNode.OperationMode = (PressReleaseMode)MouseClickOperationModeComboBox.SelectedIndex;
                mouseNode.MouseButton = (Graph.MouseButton)MouseButtonComboBox.SelectedIndex;
                if (double.TryParse(MousePositionXTextBox.Text.Trim(), out var x))
                    mouseNode.PositionX = x;
                if (double.TryParse(MousePositionYTextBox.Text.Trim(), out var y))
                    mouseNode.PositionY = y;
                break;

            case KeyboardNodeViewModel keyboardNode:
                keyboardNode.OperationMode = KeyboardOperationModeComboBox.SelectedIndex == 1
                    ? PressReleaseMode.Release : PressReleaseMode.Press;
                if (KeyboardKeyComboBox.SelectedItem is ComboBoxItem keyItem && keyItem.Tag is string keyStr)
                    keyboardNode.Key = keyStr;
                break;

            case IfNodeViewModel ifNode:
                ifNode.ConditionValue = IfConditionComboBox.SelectedIndex == 1;
                break;

            case WhileLoopNodeViewModel whileNode:
                whileNode.ConditionValue = WhileLoopConditionComboBox.SelectedIndex == 1;
                break;

            case ForLoopNodeViewModel forLoopNode:
                if (int.TryParse(ForLoopCountTextBox.Text.Trim(), out var count))
                    forLoopNode.LoopCount = Math.Max(1, count);
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
        }

        node.RefreshDescription();
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
            Filter = "图片文件 (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|所有文件 (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) == true)
        {
            FindImagePathTextBox.Text = dialog.FileName;
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

    #region 拖放导入

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
            _editorService.LoadGraph(filePath);
            SyncNodeFactorySequence();
            EnsureCanvasLargeEnough();
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
    }

    private void EnsureCanvasLargeEnough()
    {
        var minWidth = GraphScrollViewer.ViewportWidth / _zoomLevel + 2000;
        var minHeight = GraphScrollViewer.ViewportHeight / _zoomLevel + 2000;

        if (_editorService.Nodes.Count > 0)
        {
            var maxNodeRight = _editorService.Nodes.Max(n => n.X + n.Width) + 2000;
            var maxNodeBottom = _editorService.Nodes.Max(n => n.Y + 180) + 2000;
            minWidth = Math.Max(minWidth, maxNodeRight);
            minHeight = Math.Max(minHeight, maxNodeBottom);
        }

        if (GraphSurface.Width < minWidth) GraphSurface.Width = minWidth;
        if (GraphSurface.Height < minHeight) GraphSurface.Height = minHeight;
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
}
