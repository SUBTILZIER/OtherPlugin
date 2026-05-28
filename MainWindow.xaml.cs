using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Runtime;
using Microsoft.Win32;
using Point = System.Windows.Point;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MouseWheelEventArgs = System.Windows.Input.MouseWheelEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Key = System.Windows.Input.Key;
using Keyboard = System.Windows.Input.Keyboard;
using ModifierKeys = System.Windows.Input.ModifierKeys;
using MouseButtonState = System.Windows.Input.MouseButtonState;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WpfSaveFileDialog = Microsoft.Win32.SaveFileDialog;
using WpfMessageBox = System.Windows.MessageBox;
using TextBox = System.Windows.Controls.TextBox;

namespace AutomationStudioWpf;

/// <summary>
/// Main node editor window.
/// This first version focuses on two blueprint-style nodes and a maintainable code structure.
/// </summary>
public partial class MainWindow : Window
{
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private readonly GraphRuntimeExecutor _runtimeExecutor = new();
    private string? _currentGraphPath;
    private NodeBaseViewModel? _selectedNode;
    private PinViewModel? _pendingOutputPin;
    private bool _isConnecting;
    private NodeBaseViewModel? _dragNode;
    private Point _dragOffset;
    private List<(NodeBaseViewModel Node, double OffsetX, double OffsetY)> _dragGroup = [];
    private int _nodeSequence = 1;
    private bool _isSelecting;
    private Point _selectionStart;
    private bool _isPanning;
    private Point _panStart;
    private double _panHorizontalOffset;
    private double _panVerticalOffset;
    private double _zoomLevel = 1.0;
    private readonly ScaleTransform _zoomTransform = new(1.0, 1.0);
    private List<NodeFileModel> _clipboardNodes = [];
    private List<ConnectionFileModel> _clipboardConnections = [];
    private Point _lastMousePosition;
    private bool _isLoadingInspector;

    public MainWindow()
    {
        DataContext = this;
        InitializeComponent();
        GraphSurface.LayoutTransform = _zoomTransform;
        SeedDemoGraph();
    }

    public ObservableCollection<NodeBaseViewModel> Nodes { get; } = [];

    public ObservableCollection<ConnectionViewModel> Connections { get; } = [];

    private void SeedDemoGraph()
    {
        Nodes.Clear();
        Connections.Clear();
        _nodeSequence = 1;
        StartNodeViewModel startNode = CreateDefaultStartNode();
        FindImageNodeViewModel findNode = new(CreateNodeId())
        {
            Title = "找图：继续游戏",
            ImagePath = @"D:\Images\continue_game.png",
            SimilarityThresholdPercent = 80,
            X = 360,
            Y = 180,
        };

        MouseLeftClickNodeViewModel mouseNode = new(CreateNodeId())
        {
            Title = "鼠标点击：点击目标",
            ClickMode = MouseClickMode.SingleClick,
            PositionX = 1280,
            PositionY = 720,
            X = 790,
            Y = 210,
        };

        Nodes.Add(startNode);
        Nodes.Add(findNode);
        Nodes.Add(mouseNode);

        CreateConnection(startNode.FindPin("exec_out")!, findNode.FindPin("exec_in")!);
        CreateConnection(findNode.FindPin("exec_out")!, mouseNode.FindPin("exec_in")!);
        CreateConnection(findNode.FindPin("center")!, mouseNode.FindPin("position")!);
        SelectNode(startNode);
        EnsureCanvasLargeEnough();
        SetStatus("已创建示例图谱。左键空白处框选，右键空白处拖动画布。");
    }

    private StartNodeViewModel CreateDefaultStartNode()
    {
        return new StartNodeViewModel(CreateNodeId())
        {
            Title = "事件开始运行",
            X = 80,
            Y = 210,
        };
    }

    private string CreateNodeId()
    {
        return $"node_{_nodeSequence++:000}";
    }

    private void AddFindImageNode_Click(object sender, RoutedEventArgs e)
    {
        FindImageNodeViewModel node = new(CreateNodeId())
        {
            Title = "找图节点",
            X = 260 + Nodes.Count * 40,
            Y = 180 + Nodes.Count * 30,
        };
        Nodes.Add(node);
        SelectNode(node);
        EnsureCanvasLargeEnough();
        SetStatus("已添加找图节点。");
    }

    private void AddMouseLeftNode_Click(object sender, RoutedEventArgs e)
    {
        MouseLeftClickNodeViewModel node = new(CreateNodeId())
        {
            Title = "鼠标左键节点",
            X = 320 + Nodes.Count * 40,
            Y = 220 + Nodes.Count * 30,
        };
        Nodes.Add(node);
        SelectNode(node);
        EnsureCanvasLargeEnough();
        SetStatus("已添加鼠标点击节点。");
    }

    private void AddDelayNode_Click(object sender, RoutedEventArgs e)
    {
        DelayNodeViewModel node = new(CreateNodeId())
        {
            Title = "延迟节点",
            X = 360 + Nodes.Count * 40,
            Y = 260 + Nodes.Count * 30,
        };
        Nodes.Add(node);
        SelectNode(node);
        EnsureCanvasLargeEnough();
        SetStatus("已添加延迟节点。");
    }

    private void AddMouseMoveNode_Click(object sender, RoutedEventArgs e)
    {
        MouseMoveNodeViewModel node = new(CreateNodeId())
        {
            Title = "鼠标移动节点",
            X = 420 + Nodes.Count * 40,
            Y = 300 + Nodes.Count * 30,
        };
        Nodes.Add(node);
        SelectNode(node);
        EnsureCanvasLargeEnough();
        SetStatus("已添加鼠标移动节点。");
    }

    private void DeleteSelectedNode_Click(object sender, RoutedEventArgs e)
    {
        DeleteSelectedNodes();
    }

    private void ClearPendingConnection_Click(object sender, RoutedEventArgs e)
    {
        CancelPendingConnection("已清除待连接引脚。");
    }

    private void NewGraph_Click(object sender, RoutedEventArgs e)
    {
        Nodes.Clear();
        Connections.Clear();
        _currentGraphPath = null;
        _nodeSequence = 1;
        StartNodeViewModel startNode = CreateDefaultStartNode();
        Nodes.Add(startNode);
        SelectNode(startNode);
        EnsureCanvasLargeEnough();
        SetStatus("已新建图谱，并创建开始节点。");
    }

    private void SaveGraph_Click(object sender, RoutedEventArgs e)
    {
        string? path = _currentGraphPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            WpfSaveFileDialog dialog = new()
            {
                Title = "保存图谱",
                Filter = "图谱文件 (*.json)|*.json",
                FileName = "graph.json",
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            path = dialog.FileName;
        }

        GraphFileModel file = new()
        {
            Name = "自动化蓝图图谱",
            Nodes = Nodes.Select(ToFileModel).ToList(),
            Connections = Connections.Select(connection => new ConnectionFileModel
            {
                SourceNodeId = connection.SourcePin.Owner.Id,
                SourcePinName = connection.SourcePin.Name,
                TargetNodeId = connection.TargetPin.Owner.Id,
                TargetPinName = connection.TargetPin.Name,
            }).ToList(),
        };

        File.WriteAllText(path, JsonSerializer.Serialize(file, _jsonOptions));
        _currentGraphPath = path;
        SetStatus($"图谱已保存：{Path.GetFileName(path)}");
    }

    private void OpenGraph_Click(object sender, RoutedEventArgs e)
    {
        WpfOpenFileDialog dialog = new()
        {
            Title = "打开图谱",
            Filter = "图谱文件 (*.json)|*.json",
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        GraphFileModel? file = JsonSerializer.Deserialize<GraphFileModel>(File.ReadAllText(dialog.FileName));
        if (file is null)
        {
            WpfMessageBox.Show(this, "图谱文件解析失败。", "打开失败", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        LoadGraph(file);
        _currentGraphPath = dialog.FileName;
        SetStatus($"图谱已加载：{Path.GetFileName(dialog.FileName)}");
    }

    private async void RunGraph_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            GraphExecutionPlan plan = BuildExecutionPlan();
            string baseDirectory = !string.IsNullOrWhiteSpace(_currentGraphPath)
                ? Path.GetDirectoryName(_currentGraphPath) ?? Environment.CurrentDirectory
                : Environment.CurrentDirectory;

            SetStatus("执行开始...");
            GraphExecutionResult result = await Task.Run(() => _runtimeExecutor.Execute(plan, baseDirectory));
            SetStatus(result.Message);
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show(this, ex.Message, "执行失败", MessageBoxButton.OK, MessageBoxImage.Error);
            SetStatus("执行失败。");
        }
    }

    private void LoadGraph(GraphFileModel file)
    {
        Nodes.Clear();
        Connections.Clear();
        _pendingOutputPin = null;

        Dictionary<string, NodeBaseViewModel> nodesById = new();
        foreach (NodeFileModel nodeFile in file.Nodes)
        {
            NodeBaseViewModel? node = nodeFile.NodeTypeKey switch
            {
                "start" => new StartNodeViewModel(nodeFile.Id)
                {
                    Title = nodeFile.Title,
                    X = nodeFile.X,
                    Y = nodeFile.Y,
                },
                "find_image" => new FindImageNodeViewModel(nodeFile.Id)
                {
                    Title = nodeFile.Title,
                    X = nodeFile.X,
                    Y = nodeFile.Y,
                    ImagePath = nodeFile.ImagePath ?? string.Empty,
                    SimilarityThresholdPercent = nodeFile.SimilarityThresholdPercent,
                },
                "mouse_left_click" or "mouse_click" => new MouseLeftClickNodeViewModel(nodeFile.Id)
                {
                    Title = nodeFile.Title,
                    X = nodeFile.X,
                    Y = nodeFile.Y,
                    ClickMode = Enum.TryParse(nodeFile.ClickMode, true, out MouseClickMode mode) ? mode : MouseClickMode.SingleClick,
                    MouseButton = Enum.TryParse(nodeFile.MouseButton, true, out MouseButton button) ? button : MouseButton.Left,
                    PositionX = nodeFile.PositionX,
                    PositionY = nodeFile.PositionY,
                },
                "delay" => new DelayNodeViewModel(nodeFile.Id)
                {
                    Title = nodeFile.Title,
                    X = nodeFile.X,
                    Y = nodeFile.Y,
                    DelayMs = nodeFile.DelayMs,
                },
                "mouse_move" => new MouseMoveNodeViewModel(nodeFile.Id)
                {
                    Title = nodeFile.Title,
                    X = nodeFile.X,
                    Y = nodeFile.Y,
                    PositionX = nodeFile.PositionX,
                    PositionY = nodeFile.PositionY,
                },
                _ => null,
            };

            if (node is null)
            {
                continue;
            }

            Nodes.Add(node);
            nodesById[node.Id] = node;
        }

        foreach (ConnectionFileModel connectionFile in file.Connections)
        {
            if (!nodesById.TryGetValue(connectionFile.SourceNodeId, out NodeBaseViewModel? sourceNode) ||
                !nodesById.TryGetValue(connectionFile.TargetNodeId, out NodeBaseViewModel? targetNode))
            {
                continue;
            }

            PinViewModel? sourcePin = sourceNode.FindPin(connectionFile.SourcePinName);
            PinViewModel? targetPin = targetNode.FindPin(connectionFile.TargetPinName);
            if (sourcePin is not null && targetPin is not null)
            {
                CreateConnection(sourcePin, targetPin);
            }
        }

        UpdatePinConnectionStates();

        _nodeSequence = Nodes
            .Select(node => node.Id)
            .Select(id => id.StartsWith("node_") && int.TryParse(id[5..], out int numericId) ? numericId : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;

        SelectNode(Nodes.FirstOrDefault());
    }

    private static NodeFileModel ToFileModel(NodeBaseViewModel node)
    {
        NodeFileModel file = new()
        {
            Id = node.Id,
            NodeTypeKey = node.NodeTypeKey,
            Title = node.Title,
            X = node.X,
            Y = node.Y,
        };

        if (node is FindImageNodeViewModel findImage)
        {
            file.ImagePath = findImage.ImagePath;
            file.SimilarityThresholdPercent = findImage.SimilarityThresholdPercent;
        }
        else if (node is MouseLeftClickNodeViewModel mouseNode)
        {
            file.ClickMode = mouseNode.ClickMode.ToString();
            file.PositionX = mouseNode.PositionX;
            file.PositionY = mouseNode.PositionY;
            file.MouseButton = mouseNode.MouseButton.ToString();
        }
        else if (node is DelayNodeViewModel delayNode)
        {
            file.DelayMs = delayNode.DelayMs;
        }
        else if (node is MouseMoveNodeViewModel mouseMoveNode)
        {
            file.PositionX = mouseMoveNode.PositionX;
            file.PositionY = mouseMoveNode.PositionY;
        }

        return file;
    }

    private void NodeCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not NodeBaseViewModel node)
        {
            return;
        }

        SelectNode(node);
        e.Handled = true;
    }

    private void NodeHeader_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isConnecting)
        {
            return;
        }

        if (IsPinInteractionSource(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (sender is not FrameworkElement element || element.DataContext is not NodeBaseViewModel node)
        {
            return;
        }

        if (!node.IsSelected)
        {
            SelectNode(node);
        }
        else
        {
            _selectedNode = node;
            LoadNodeToInspector(node);
        }

        Point point = e.GetPosition(GraphSurface);
        _dragNode = node;
        _dragOffset = new Point(point.X - node.X, point.Y - node.Y);

        int selectedCount = Nodes.Count(n => n.IsSelected);
        if (selectedCount > 1 && node.IsSelected)
        {
            _dragGroup = Nodes
                .Where(n => n.IsSelected)
                .Select(n => (Node: n, OffsetX: point.X - n.X, OffsetY: point.Y - n.Y))
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
        if (_isConnecting)
        {
            return;
        }

        if (_dragNode is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        Point point = e.GetPosition(GraphSurface);
        foreach (var (item, offsetX, offsetY) in _dragGroup)
        {
            item.X = Math.Max(0, point.X - offsetX);
            item.Y = Math.Max(0, point.Y - offsetY);
        }
    }

    private void NodeHeader_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is UIElement element)
        {
            element.ReleaseMouseCapture();
        }

        _dragNode = null;
        _dragGroup.Clear();
    }

    private void GraphSurface_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource != GraphSurface)
        {
            return;
        }

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
        SetStatus("框选开始。拖动鼠标选择多个节点。");
        e.Handled = true;
    }

    private void GraphSurface_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        _lastMousePosition = e.GetPosition(GraphSurface);

        if (_isPanning)
        {
            Point currentPan = e.GetPosition(GraphScrollViewer);
            Vector delta = currentPan - _panStart;
            GraphScrollViewer.ScrollToHorizontalOffset(Math.Max(0, _panHorizontalOffset - delta.X));
            GraphScrollViewer.ScrollToVerticalOffset(Math.Max(0, _panVerticalOffset - delta.Y));
        }

        if (_isConnecting && _pendingOutputPin is not null)
        {
            UpdatePreviewConnectionGeometry(_pendingOutputPin, _lastMousePosition);
        }

        if (!_isSelecting)
        {
            return;
        }

        Point current = _lastMousePosition;
        double left = Math.Min(current.X, _selectionStart.X);
        double top = Math.Min(current.Y, _selectionStart.Y);
        double width = Math.Abs(current.X - _selectionStart.X);
        double height = Math.Abs(current.Y - _selectionStart.Y);

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
                TryGetPinAtPosition(e.GetPosition(GraphSurface), out PinViewModel? targetPin) &&
                targetPin is not null &&
                targetPin != _pendingOutputPin)
            {
                TryConnectPending(targetPin);
            }

            ReleasePreviewWire();
            e.Handled = true;
            return;
        }

        if (!_isSelecting)
        {
            return;
        }

        _isSelecting = false;
        GraphSurface.ReleaseMouseCapture();

        Rect selectionBounds = new(
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
        if (e.OriginalSource != GraphSurface)
        {
            return;
        }

        _isPanning = true;
        _panStart = e.GetPosition(GraphScrollViewer);
        _panHorizontalOffset = GraphScrollViewer.HorizontalOffset;
        _panVerticalOffset = GraphScrollViewer.VerticalOffset;
        GraphSurface.CaptureMouse();
        SetStatus("画布拖动中。");
        e.Handled = true;
    }

    private void GraphSurface_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isPanning)
        {
            return;
        }

        _isPanning = false;
        GraphSurface.ReleaseMouseCapture();
        SetStatus("画布拖动结束。");
        e.Handled = true;
    }

    private void GraphScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        Point mouseInViewport = e.GetPosition(GraphScrollViewer);

        double oldZoom = _zoomLevel;
        double zoomDelta = e.Delta > 0 ? 0.1 : -0.1;
        double newZoom = Math.Clamp(oldZoom + zoomDelta, 0.1, 2.0);

        if (Math.Abs(newZoom - oldZoom) < 0.001)
        {
            return;
        }

        _zoomLevel = newZoom;
        _zoomTransform.ScaleX = newZoom;
        _zoomTransform.ScaleY = newZoom;

        EnsureCanvasLargeEnough();

        double canvasX = (GraphScrollViewer.HorizontalOffset + mouseInViewport.X) / oldZoom;
        double canvasY = (GraphScrollViewer.VerticalOffset + mouseInViewport.Y) / oldZoom;

        GraphScrollViewer.ScrollToHorizontalOffset(Math.Max(0, canvasX * newZoom - mouseInViewport.X));
        GraphScrollViewer.ScrollToVerticalOffset(Math.Max(0, canvasY * newZoom - mouseInViewport.Y));

        e.Handled = true;
    }

    private void EnsureCanvasLargeEnough()
    {
        double minWidth = GraphScrollViewer.ViewportWidth / _zoomLevel + 2000;
        double minHeight = GraphScrollViewer.ViewportHeight / _zoomLevel + 2000;

        if (Nodes.Count > 0)
        {
            double maxNodeRight = Nodes.Max(n => n.X + n.Width) + 2000;
            double maxNodeBottom = Nodes.Max(n => n.Y + 180) + 2000;
            minWidth = Math.Max(minWidth, maxNodeRight);
            minHeight = Math.Max(minHeight, maxNodeBottom);
        }

        if (GraphSurface.Width < minWidth)
        {
            GraphSurface.Width = minWidth;
        }

        if (GraphSurface.Height < minHeight)
        {
            GraphSurface.Height = minHeight;
        }
    }

    private void PinButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not PinViewModel pin)
        {
            return;
        }

        SelectNode(pin.Owner);

        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0)
        {
            BreakPinConnections(pin);
            SetStatus($"已断开 {pin.Owner.Title}.{pin.DisplayName} 的所有连接。");
            e.Handled = true;
            return;
        }

        if (pin.Direction == PinDirection.Output)
        {
            _pendingOutputPin = pin;
            _isConnecting = true;
            GraphSurface.CaptureMouse();
            Point pos = e.GetPosition(GraphSurface);
            UpdatePreviewConnectionGeometry(pin, pos);
            PreviewConnectionPath.Visibility = Visibility.Visible;
            SetStatus($"从 {pin.Owner.Title}.{pin.DisplayName} 拖出连线（或松开后点击目标输入引脚完成连线）。");
            e.Handled = true;
            return;
        }

        if (pin.Direction == PinDirection.Input && _pendingOutputPin is not null && _isConnecting)
        {
            TryConnectPending(pin);
            e.Handled = true;
            return;
        }

        e.Handled = true;
    }

    private void PinButton_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not PinViewModel pin)
        {
            return;
        }

        if (!_isConnecting || _pendingOutputPin is null)
        {
            return;
        }

        if (pin == _pendingOutputPin)
        {
            e.Handled = true;
            return;
        }

        TryConnectPending(pin);
        e.Handled = true;
    }

    private bool CanConnect(PinViewModel sourcePin, PinViewModel targetPin, out string reason)
    {
        if (sourcePin.Owner == targetPin.Owner)
        {
            reason = "暂不支持把节点连接到自身。";
            return false;
        }

        if (sourcePin.Direction != PinDirection.Output || targetPin.Direction != PinDirection.Input)
        {
            reason = "连线方向不正确，必须从输出引脚连到输入引脚。";
            return false;
        }

        if (sourcePin.Kind != targetPin.Kind)
        {
            reason = $"引脚类型不匹配：{sourcePin.KindLabel} 不能连接到 {targetPin.KindLabel}。";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private void CreateConnection(PinViewModel sourcePin, PinViewModel targetPin)
    {
        // Each input pin keeps at most one incoming connection.
        for (int i = Connections.Count - 1; i >= 0; i--)
        {
            if (Connections[i].TargetPin == targetPin)
            {
                RemoveConnectionAt(i);
            }
        }

        Connections.Add(new ConnectionViewModel(sourcePin, targetPin));
        UpdatePinConnectionStates();
    }

    private void UpdatePreviewConnectionGeometry(PinViewModel sourcePin, Point currentPoint)
    {
        Point startAnchor = sourcePin.Owner.GetPinAnchor(sourcePin);
        Point start = new(sourcePin.Owner.X + startAnchor.X, sourcePin.Owner.Y + startAnchor.Y);
        Point end = currentPoint;

        double tangent = Math.Max(80, Math.Abs(end.X - start.X) * 0.45);
        Point control1 = new(start.X + tangent, start.Y);
        Point control2 = new(end.X - tangent, end.Y);

        PathFigure figure = new()
        {
            StartPoint = start,
            IsClosed = false,
            IsFilled = false,
        };
        figure.Segments.Add(new BezierSegment(control1, control2, end, true));

        PathGeometry geometry = new();
        geometry.Figures.Add(figure);
        PreviewConnectionPath.Data = geometry;
    }

    private void TryConnectPending(PinViewModel targetPin)
    {
        if (_pendingOutputPin is null || !_isConnecting)
        {
            return;
        }

        if (!CanConnect(_pendingOutputPin, targetPin, out string reason))
        {
            SetStatus(reason);
            CancelPendingConnection(null);
            return;
        }

        CreateConnection(_pendingOutputPin, targetPin);
        SetStatus($"已连接：{_pendingOutputPin.Owner.Title}.{_pendingOutputPin.DisplayName} -> {targetPin.Owner.Title}.{targetPin.DisplayName}");
        CancelPendingConnection(null);
    }

    private void ReleasePreviewWire()
    {
        PreviewConnectionPath.Visibility = Visibility.Collapsed;
        PreviewConnectionPath.Data = null;
        GraphSurface.ReleaseMouseCapture();
    }

    private void BreakPinConnections(PinViewModel pin)
    {
        for (int i = Connections.Count - 1; i >= 0; i--)
        {
            if (Connections[i].SourcePin == pin || Connections[i].TargetPin == pin)
            {
                RemoveConnectionAt(i);
            }
        }

        UpdatePinConnectionStates();
    }

    private void CancelPendingConnection(string? statusMessage)
    {
        _pendingOutputPin = null;
        _isConnecting = false;
        ReleasePreviewWire();

        if (!string.IsNullOrWhiteSpace(statusMessage))
        {
            SetStatus(statusMessage);
        }
    }

    private void UpdatePinConnectionStates()
    {
        foreach (NodeBaseViewModel node in Nodes)
        {
            foreach (PinViewModel pin in node.InputPins.Concat(node.OutputPins))
            {
                pin.HasConnection = Connections.Any(connection => connection.SourcePin == pin || connection.TargetPin == pin);
            }
        }
    }

    private void ClearSelection()
    {
        foreach (NodeBaseViewModel node in Nodes)
        {
            node.IsSelected = false;
        }
    }

    private void ApplySelection(Rect selectionBounds)
    {
        List<NodeBaseViewModel> selectedNodes = [];
        foreach (NodeBaseViewModel node in Nodes)
        {
            Rect nodeBounds = new(node.X, node.Y, node.Width, node.Height);
            bool isSelected = selectionBounds.IntersectsWith(nodeBounds);
            node.IsSelected = isSelected;
            if (isSelected)
            {
                selectedNodes.Add(node);
            }
        }

        _selectedNode = selectedNodes.FirstOrDefault();
        LoadNodeToInspector(_selectedNode);
        SetStatus(selectedNodes.Count == 0
            ? "未选中任何节点。"
            : $"已框选 {selectedNodes.Count} 个节点。");
    }

    private void SelectNode(NodeBaseViewModel? node)
    {
        ClearSelection();
        _selectedNode = node;

        if (_selectedNode is not null)
        {
            _selectedNode.IsSelected = true;
        }

        LoadNodeToInspector(node);
    }

    private void LoadNodeToInspector(NodeBaseViewModel? node)
    {
        if (node is null)
        {
            _isLoadingInspector = true;
            NodeIdTextBox.Text = string.Empty;
            NodeTitleTextBox.Text = string.Empty;
            FindImageInspectorPanel.Visibility = Visibility.Collapsed;
            MouseLeftInspectorPanel.Visibility = Visibility.Collapsed;
            DelayInspectorPanel.Visibility = Visibility.Collapsed;
            MouseMoveInspectorPanel.Visibility = Visibility.Collapsed;
            InspectorHintTextBlock.Text = "请选择一个节点进行编辑。";
            _isLoadingInspector = false;
            return;
        }

        _isLoadingInspector = true;
        NodeIdTextBox.Text = node.Id;
        NodeTitleTextBox.Text = node.Title;
        InspectorHintTextBlock.Text = $"当前选中：{node.Title}";

        FindImageInspectorPanel.Visibility = node is FindImageNodeViewModel ? Visibility.Visible : Visibility.Collapsed;
        MouseLeftInspectorPanel.Visibility = node is MouseLeftClickNodeViewModel ? Visibility.Visible : Visibility.Collapsed;
        DelayInspectorPanel.Visibility = node is DelayNodeViewModel ? Visibility.Visible : Visibility.Collapsed;
        MouseMoveInspectorPanel.Visibility = node is MouseMoveNodeViewModel ? Visibility.Visible : Visibility.Collapsed;

        if (node is FindImageNodeViewModel findImage)
        {
            FindImagePathTextBox.Text = findImage.ImagePath;
            FindImageThresholdTextBox.Text = findImage.SimilarityThresholdPercent.ToString();
        }

        if (node is MouseLeftClickNodeViewModel mouseNode)
        {
            MousePositionXTextBox.Text = mouseNode.PositionX.ToString("0.##");
            MousePositionYTextBox.Text = mouseNode.PositionY.ToString("0.##");
            MouseClickModeComboBox.SelectedIndex = mouseNode.ClickMode == MouseClickMode.SingleClick ? 0 : 1;
            MouseButtonComboBox.SelectedIndex = mouseNode.MouseButton switch
            {
                MouseButton.Left => 0,
                MouseButton.Right => 1,
                MouseButton.XButton1 => 2,
                MouseButton.XButton2 => 3,
                _ => 0,
            };
        }

        if (node is DelayNodeViewModel delayNode)
        {
            DelayMsTextBox.Text = delayNode.DelayMs.ToString();
        }

        if (node is MouseMoveNodeViewModel moveNode)
        {
            MouseMovePositionXTextBox.Text = moveNode.PositionX.ToString("0.##");
            MouseMovePositionYTextBox.Text = moveNode.PositionY.ToString("0.##");
        }

        _isLoadingInspector = false;
    }

    private void ApplyInspectorChanges()
    {
        if (_selectedNode is null || _isLoadingInspector)
        {
            return;
        }

        _selectedNode.Title = NodeTitleTextBox.Text.Trim();

        if (_selectedNode is FindImageNodeViewModel findImage)
        {
            findImage.ImagePath = FindImagePathTextBox.Text.Trim();
            if (int.TryParse(FindImageThresholdTextBox.Text.Trim(), out int threshold))
            {
                findImage.SimilarityThresholdPercent = threshold;
            }
        }
        else if (_selectedNode is MouseLeftClickNodeViewModel mouseNode)
        {
            mouseNode.ClickMode = MouseClickModeComboBox.SelectedIndex == 1 ? MouseClickMode.Hold : MouseClickMode.SingleClick;
            mouseNode.MouseButton = MouseButtonComboBox.SelectedIndex switch
            {
                1 => MouseButton.Right,
                2 => MouseButton.XButton1,
                3 => MouseButton.XButton2,
                _ => MouseButton.Left,
            };
            if (double.TryParse(MousePositionXTextBox.Text.Trim(), out double x))
            {
                mouseNode.PositionX = x;
            }
            if (double.TryParse(MousePositionYTextBox.Text.Trim(), out double y))
            {
                mouseNode.PositionY = y;
            }
        }
        else if (_selectedNode is DelayNodeViewModel delayNode)
        {
            if (int.TryParse(DelayMsTextBox.Text.Trim(), out int delayMs))
            {
                delayNode.DelayMs = delayMs;
            }
        }
        else if (_selectedNode is MouseMoveNodeViewModel moveNode)
        {
            if (double.TryParse(MouseMovePositionXTextBox.Text.Trim(), out double moveX))
            {
                moveNode.PositionX = moveX;
            }
            if (double.TryParse(MouseMovePositionYTextBox.Text.Trim(), out double moveY))
            {
                moveNode.PositionY = moveY;
            }
        }

        _selectedNode.RefreshDescription();
        InspectorHintTextBlock.Text = $"当前选中：{_selectedNode.Title}（已自动保存）";
        SetStatus($"节点已自动保存：{_selectedNode.Title}");
    }

    private void InspectorField_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyInspectorChanges();
    }

    private void InspectorField_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplyInspectorChanges();
    }

    private void BrowseFindImagePath_Click(object sender, RoutedEventArgs e)
    {
        WpfOpenFileDialog dialog = new()
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

    private void ConnectionPath_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Alt) == 0)
        {
            return;
        }

        if (sender is not FrameworkElement element || element.DataContext is not ConnectionViewModel connection)
        {
            return;
        }

        RemoveConnection(connection);
        UpdatePinConnectionStates();
        SetStatus("已断开连接。");
        e.Handled = true;
    }

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
        {
            return;
        }

        FrameworkElement? nodeRoot = FindNodeRootElement(element);
        if (nodeRoot is null || element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            return;
        }

        try
        {
            GeneralTransform transform = element.TransformToAncestor(nodeRoot);
            Point localTopLeft = transform.Transform(new Point(0, 0));
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
            {
                lastMatch = border;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return lastMatch;
    }

    private static bool IsPinInteractionSource(DependencyObject? source)
    {
        DependencyObject? current = source;
        while (current is not null)
        {
            if (current is FrameworkElement element && element.DataContext is PinViewModel)
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private bool TryGetPinAtPosition(Point position, out PinViewModel? pin)
    {
        DependencyObject? hit = GraphSurface.InputHitTest(position) as DependencyObject;
        return TryGetPinFromSource(hit, out pin);
    }

    private static bool TryGetPinFromSource(DependencyObject? source, out PinViewModel? pin)
    {
        DependencyObject? current = source;
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

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox)
        {
            return;
        }

        if (e.Key == Key.Delete)
        {
            DeleteSelectedNodes();
            e.Handled = true;
            return;
        }

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

        if (e.Key == Key.Q)
        {
            AlignSelectedNodesHorizontal();
            e.Handled = true;
            return;
        }

        Key effectiveKey = e.Key == Key.System ? e.SystemKey : e.Key;
        if (effectiveKey == Key.S &&
            (Keyboard.Modifiers & (ModifierKeys.Shift | ModifierKeys.Alt)) == (ModifierKeys.Shift | ModifierKeys.Alt))
        {
            AlignSelectedNodesVertical();
            e.Handled = true;
        }
    }

    private void AlignSelectedNodesHorizontal()
    {
        var selectedNodes = Nodes.Where(n => n.IsSelected).ToList();
        if (selectedNodes.Count < 2)
        {
            return;
        }

        double avgY = selectedNodes.Average(n => n.Y);
        foreach (var node in selectedNodes)
        {
            node.Y = avgY;
        }

        SetStatus($"已将 {selectedNodes.Count} 个节点横向对齐。");
    }

    private void AlignSelectedNodesVertical()
    {
        var selectedNodes = Nodes.Where(n => n.IsSelected).ToList();
        if (selectedNodes.Count < 2)
        {
            return;
        }

        double avgX = selectedNodes.Average(n => n.X);
        foreach (var node in selectedNodes)
        {
            node.X = avgX;
        }

        SetStatus($"已将 {selectedNodes.Count} 个节点纵向对齐。");
    }

    private void CopySelectedNodes()
    {
        var selectedNodes = Nodes.Where(n => n.IsSelected && n.CanDelete).ToList();
        if (selectedNodes.Count == 0)
        {
            return;
        }

        _clipboardNodes = selectedNodes.Select(ToFileModel).ToList();
        HashSet<string> selectedIds = selectedNodes.Select(n => n.Id).ToHashSet();
        _clipboardConnections = Connections
            .Where(c => selectedIds.Contains(c.SourcePin.Owner.Id) && selectedIds.Contains(c.TargetPin.Owner.Id))
            .Select(c => new ConnectionFileModel
            {
                SourceNodeId = c.SourcePin.Owner.Id,
                SourcePinName = c.SourcePin.Name,
                TargetNodeId = c.TargetPin.Owner.Id,
                TargetPinName = c.TargetPin.Name,
            })
            .ToList();
        SetStatus($"已复制 {_clipboardNodes.Count} 个节点，{_clipboardConnections.Count} 条内部连线。");
    }

    private void PasteNodesAtMouse()
    {
        if (_clipboardNodes.Count == 0)
        {
            return;
        }

        double centerX = _clipboardNodes.Average(n => n.X);
        double centerY = _clipboardNodes.Average(n => n.Y);

        ClearSelection();
        Dictionary<string, NodeBaseViewModel> pastedNodeMap = [];

        foreach (NodeFileModel source in _clipboardNodes)
        {
            string newId = CreateNodeId();
            double offsetX = source.X - centerX;
            double offsetY = source.Y - centerY;

            NodeBaseViewModel node = CreateNodeFromFileModel(source, newId);
            node.X = _lastMousePosition.X + offsetX;
            node.Y = _lastMousePosition.Y + offsetY;
            node.IsSelected = true;

            Nodes.Add(node);
            pastedNodeMap[source.Id] = node;
        }

        foreach (ConnectionFileModel sourceConnection in _clipboardConnections)
        {
            if (!pastedNodeMap.TryGetValue(sourceConnection.SourceNodeId, out NodeBaseViewModel? sourceNode) ||
                !pastedNodeMap.TryGetValue(sourceConnection.TargetNodeId, out NodeBaseViewModel? targetNode))
            {
                continue;
            }

            PinViewModel? sourcePin = sourceNode.FindPin(sourceConnection.SourcePinName);
            PinViewModel? targetPin = targetNode.FindPin(sourceConnection.TargetPinName);
            if (sourcePin is not null && targetPin is not null)
            {
                CreateConnection(sourcePin, targetPin);
            }
        }

        EnsureCanvasLargeEnough();
        SetStatus($"已粘贴 {_clipboardNodes.Count} 个节点，恢复 {_clipboardConnections.Count} 条内部连线。");
    }

    private NodeBaseViewModel CreateNodeFromFileModel(NodeFileModel source, string newId)
    {
        NodeBaseViewModel node = source.NodeTypeKey switch
        {
            "start" => throw new InvalidOperationException("不允许复制开始节点。"),
            "find_image" => new FindImageNodeViewModel(newId)
            {
                ImagePath = source.ImagePath ?? string.Empty,
                SimilarityThresholdPercent = source.SimilarityThresholdPercent,
            },
            "mouse_left_click" or "mouse_click" => new MouseLeftClickNodeViewModel(newId)
            {
                ClickMode = Enum.TryParse(source.ClickMode, true, out MouseClickMode mode) ? mode : MouseClickMode.SingleClick,
                MouseButton = Enum.TryParse(source.MouseButton, true, out MouseButton button) ? button : MouseButton.Left,
                PositionX = source.PositionX,
                PositionY = source.PositionY,
            },
            "delay" => new DelayNodeViewModel(newId)
            {
                DelayMs = source.DelayMs,
            },
            "mouse_move" => new MouseMoveNodeViewModel(newId)
            {
                PositionX = source.PositionX,
                PositionY = source.PositionY,
            },
            _ => throw new InvalidOperationException($"未知节点类型: {source.NodeTypeKey}"),
        };

        node.Title = source.Title;
        return node;
    }

    private void DeleteSelectedNodes()
    {
        var nodesToDelete = Nodes.Where(n => n.IsSelected && n.CanDelete).ToList();
        if (nodesToDelete.Count == 0)
        {
            return;
        }

        foreach (NodeBaseViewModel node in nodesToDelete)
        {
            for (int i = Connections.Count - 1; i >= 0; i--)
            {
                if (Connections[i].SourcePin.Owner == node || Connections[i].TargetPin.Owner == node)
                {
                    RemoveConnectionAt(i);
                }
            }

            Nodes.Remove(node);
        }

        UpdatePinConnectionStates();
        SelectNode(null);
        SetStatus($"已删除 {nodesToDelete.Count} 个节点。");
    }

    private GraphExecutionPlan BuildExecutionPlan()
    {
        List<GraphRuntimeNode> nodes = [];
        foreach (NodeBaseViewModel node in Nodes)
        {
            GraphRuntimeNode runtimeNode = node switch
            {
                StartNodeViewModel startNode => GraphRuntimeNode.ForStart(startNode.Id, startNode.Title),
                FindImageNodeViewModel findImageNode => GraphRuntimeNode.ForFindImage(
                    findImageNode.Id,
                    findImageNode.Title,
                    findImageNode.ImagePath,
                    findImageNode.SimilarityThresholdPercent),
                MouseLeftClickNodeViewModel mouseNode => GraphRuntimeNode.ForMouseLeftClick(
                    mouseNode.Id,
                    mouseNode.Title,
                    mouseNode.ClickMode,
                    mouseNode.MouseButton,
                    mouseNode.PositionX,
                    mouseNode.PositionY),
                DelayNodeViewModel delayNode => GraphRuntimeNode.ForDelay(
                    delayNode.Id,
                    delayNode.Title,
                    delayNode.DelayMs),
                MouseMoveNodeViewModel moveNode => GraphRuntimeNode.ForMouseMove(
                    moveNode.Id,
                    moveNode.Title,
                    moveNode.PositionX,
                    moveNode.PositionY),
                _ => throw new InvalidOperationException($"不支持执行的节点类型: {node.GetType().Name}"),
            };
            nodes.Add(runtimeNode);
        }

        List<GraphRuntimeConnection> connections = Connections
            .Select(connection => new GraphRuntimeConnection(
                connection.SourcePin.Owner.Id,
                connection.SourcePin.Name,
                connection.SourcePin.Kind,
                connection.TargetPin.Owner.Id,
                connection.TargetPin.Name,
                connection.TargetPin.Kind))
            .ToList();

        return new GraphExecutionPlan(nodes, connections);
    }

    private void RemoveConnection(ConnectionViewModel connection)
    {
        int index = Connections.IndexOf(connection);
        if (index >= 0)
        {
            RemoveConnectionAt(index);
        }
    }

    private void RemoveConnectionAt(int index)
    {
        ConnectionViewModel connection = Connections[index];
        Connections.RemoveAt(index);
        connection.Dispose();
    }

    private void SetStatus(string message)
    {
        StatusTextBlock.Text = message;
    }
}
