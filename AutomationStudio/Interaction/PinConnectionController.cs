using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Services;
using Point = System.Windows.Point;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace AutomationStudioWpf.Interaction;

public sealed class PinConnectionController
{
    private readonly GraphEditorService _editorService;
    private readonly GraphCommandService _commandService;
    private readonly NodeFactory _nodeFactory;
    private readonly FrameworkElement _captureElement;
    private readonly Path _previewPath;
    private readonly Func<Point, Point> _viewportToGraph;
    private readonly Func<Point, PinViewModel?> _pinAtGraphPosition;
    private readonly Action<Point> _openNodePaletteForConnection;
    private readonly Action<NodeBaseViewModel?> _selectNode;
    private readonly Action _clearNodeSelection;
    private readonly Action<bool> _setCanvasFocusActive;
    private readonly Action<string> _setStatus;

    private PinViewModel? _pendingStartPin;
    private PinViewModel? _pendingPalettePin;
    private ConnectionPathViewModel? _selectedConnectionPath;
    private Point _lastConnectionClickGraphPoint;
    private bool _isConnecting;
    private bool _wireWasDragged;

    public PinConnectionController(
        GraphEditorService editorService,
        GraphCommandService commandService,
        NodeFactory nodeFactory,
        FrameworkElement captureElement,
        Path previewPath,
        Func<Point, Point> viewportToGraph,
        Func<Point, PinViewModel?> pinAtGraphPosition,
        Action<Point> openNodePaletteForConnection,
        Action<NodeBaseViewModel?> selectNode,
        Action clearNodeSelection,
        Action<bool> setCanvasFocusActive,
        Action<string> setStatus)
    {
        _editorService = editorService;
        _commandService = commandService;
        _nodeFactory = nodeFactory;
        _captureElement = captureElement;
        _previewPath = previewPath;
        _viewportToGraph = viewportToGraph;
        _pinAtGraphPosition = pinAtGraphPosition;
        _openNodePaletteForConnection = openNodePaletteForConnection;
        _selectNode = selectNode;
        _clearNodeSelection = clearNodeSelection;
        _setCanvasFocusActive = setCanvasFocusActive;
        _setStatus = setStatus;
    }

    public bool IsConnecting => _isConnecting;

    public void HandlePinMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not PinViewModel pin)
            return;

        _setCanvasFocusActive(false);
        _selectNode(pin.Owner);

        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0)
        {
            DisconnectPin(pin);
            e.Handled = true;
            return;
        }

        if (IsConnecting)
        {
            Complete(pin);
            e.Handled = true;
            return;
        }

        Begin(pin, _viewportToGraph(e.GetPosition(_captureElement)));
        e.Handled = true;
    }

    public void HandlePinMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not PinViewModel pin)
            return;
        if (!IsConnecting) return;

        Complete(pin);
        e.Handled = true;
    }

    public bool HandleViewportMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!IsConnecting) return false;

        var targetPin = _pinAtGraphPosition(_viewportToGraph(e.GetPosition(_captureElement)));
        CompleteOrCancel(targetPin);
        e.Handled = true;
        return true;
    }

    public void HandleConnectionDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var clickPos = _viewportToGraph(e.GetPosition(_captureElement));
        var connection = FindConnectionFromSource(e.OriginalSource as DependencyObject, clickPos);
        if (connection is null) return;

        _setCanvasFocusActive(false);
        var reroute = InsertReroute(connection, clickPos);
        if (reroute is not null)
            _selectNode(reroute);
        e.Handled = true;
    }

    public void HandleConnectionMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var clickPos = _viewportToGraph(e.GetPosition(_captureElement));
        _lastConnectionClickGraphPoint = clickPos;

        if ((Keyboard.Modifiers & ModifierKeys.Alt) == 0)
        {
            SelectConnectionPath(e.OriginalSource as DependencyObject);
            _setCanvasFocusActive(false);
            return;
        }

        var connection = FindConnectionFromSource(e.OriginalSource as DependencyObject, clickPos);
        if (connection is null)
            return;

        _setCanvasFocusActive(false);
        RemoveConnection(connection);
        e.Handled = true;
    }

    public void HandleConnectionMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _lastConnectionClickGraphPoint = _viewportToGraph(e.GetPosition(_captureElement));
        SelectConnectionPath(e.OriginalSource as DependencyObject);
        _setCanvasFocusActive(false);
    }

    public void DisconnectPin(PinViewModel pin)
    {
        _commandService.Execute("Disconnect pin", () => _editorService.ClearConnectionsForPin(pin));
        _setStatus($"已断开 {pin.Owner.Title}.{pin.DisplayName} 的所有连接。");
    }

    public void Begin(PinViewModel startPin, Point currentPoint)
    {
        _pendingStartPin = startPin;
        _pendingPalettePin = null;
        _isConnecting = true;
        _wireWasDragged = false;
        _captureElement.CaptureMouse();
        UpdatePreviewGeometry(startPin, currentPoint);
        _previewPath.Visibility = Visibility.Visible;
        _setStatus($"从{startPin.Owner.Title}.{startPin.DisplayName} 拖拽连线...");
    }

    public void Move(Point currentPoint)
    {
        if (!_isConnecting || _pendingStartPin is null)
            return;

        _wireWasDragged = true;
        UpdatePreviewGeometry(_pendingStartPin, currentPoint);
    }

    public bool Complete(PinViewModel targetPin)
    {
        if (_pendingStartPin is null || !_isConnecting)
            return false;

        if (targetPin == _pendingStartPin)
        {
            Cancel(null);
            return true;
        }

        if (!TryNormalizeConnection(_pendingStartPin, targetPin, out var sourcePin, out var inputPin, out var reason) ||
            !_editorService.CanConnect(sourcePin, inputPin, out reason))
        {
            _setStatus(reason);
            Cancel(null);
            return true;
        }

        _commandService.Execute("Create connection", () => _editorService.CreateConnection(sourcePin, inputPin));
        _setStatus($"已连接：{sourcePin.Owner.Title}.{sourcePin.DisplayName} -> {inputPin.Owner.Title}.{inputPin.DisplayName}");
        Cancel(null);
        return true;
    }

    public void CompleteOrCancel(PinViewModel? targetPin)
    {
        if (!_isConnecting)
            return;

        if (targetPin is not null && targetPin != _pendingStartPin)
        {
            Complete(targetPin);
            return;
        }

        if (_wireWasDragged)
        {
            OpenPaletteFromPendingWire();
            return;
        }

        Cancel(null);
    }

    public void Cancel(string? statusMessage)
    {
        _pendingStartPin = null;
        _isConnecting = false;
        ReleasePreviewWire();

        if (!string.IsNullOrWhiteSpace(statusMessage))
            _setStatus(statusMessage);
    }

    public void CancelPendingPaletteConnection()
    {
        _pendingPalettePin = null;
    }

    public bool TryAutoConnectNewNode(NodeBaseViewModel newNode)
    {
        if (_pendingPalettePin is null)
            return false;

        var startPin = _pendingPalettePin;
        _pendingPalettePin = null;
        var candidates = startPin.Direction == PinDirection.Output
            ? newNode.InputPins
            : newNode.OutputPins;

        foreach (var candidate in candidates)
        {
            if (!TryNormalizeConnection(startPin, candidate, out var sourcePin, out var inputPin, out _) ||
                !_editorService.CanConnect(sourcePin, inputPin, out _))
            {
                continue;
            }

            _commandService.Execute("Auto-connect node", () => _editorService.CreateConnection(sourcePin, inputPin));
            _editorService.UpdatePinConnectionStates();
            _setStatus($"已创建节点并连接：{sourcePin.Owner.Title}.{sourcePin.DisplayName} -> {inputPin.Owner.Title}.{inputPin.DisplayName}");
            return true;
        }

        _setStatus("已创建节点，但没有找到可自动连接的匹配引脚。");
        return false;
    }

    public void RemoveConnection(ConnectionViewModel connection)
    {
        _commandService.Execute("Remove connection", () => _editorService.RemoveConnection(connection));
        _editorService.UpdatePinConnectionStates();
        _setStatus("已断开连接。");
    }

    public bool HandleKeyDown(WpfKeyEventArgs e)
    {
        if (e.Key is not (Key.Delete or Key.Back))
            return false;

        if (!DeleteSelectedConnectionPath())
            return false;

        e.Handled = true;
        return true;
    }

    public void ClearSelectedConnectionPath()
    {
        foreach (var path in _editorService.ConnectionPaths)
            path.IsSelected = false;

        _selectedConnectionPath = null;
    }

    public bool DeleteSelectedConnectionPath()
    {
        var path = GetLiveSelectedPath();
        if (path is null)
            return false;

        var connections = path.Connections.ToList();
        _commandService.Execute("Delete connection path", () =>
        {
            _editorService.RemoveConnections(connections);
        });

        ClearSelectedConnectionPath();
        _editorService.UpdatePinConnectionStates();
        _setStatus("Deleted selected connection path.");
        return true;
    }

    public bool InsertRerouteOnSelectedPath()
    {
        var path = GetLiveSelectedPath();
        if (path is null)
            return false;

        var connection = path.FindNearestConnection(_lastConnectionClickGraphPoint);
        var reroute = InsertReroute(connection, _lastConnectionClickGraphPoint);
        if (reroute is not null)
            _selectNode(reroute);

        return reroute is not null;
    }

    private RerouteNodeViewModel? InsertReroute(ConnectionViewModel connection, Point graphPoint)
    {
        RerouteNodeViewModel? reroute = null;
        _commandService.Execute("Add reroute", () =>
        {
            _editorService.RunBatchedEdit(() =>
            {
                reroute = _nodeFactory.CreateRerouteNode(connection.SourcePin.Kind, graphPoint.X - 10, graphPoint.Y - 10);
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
            });
        });

        _editorService.UpdatePinConnectionStates();
        _setStatus("Added reroute point.");
        return reroute;
    }

    private void SelectConnectionPath(DependencyObject? source)
    {
        var path = FindConnectionPathFromSource(source);
        if (path is null)
            return;

        _clearNodeSelection();
        foreach (var item in _editorService.ConnectionPaths)
            item.IsSelected = ReferenceEquals(item, path);

        _selectedConnectionPath = path;
    }

    private ConnectionPathViewModel? GetLiveSelectedPath()
    {
        if (_selectedConnectionPath is not null && _editorService.ConnectionPaths.Contains(_selectedConnectionPath))
            return _selectedConnectionPath;

        _selectedConnectionPath = _editorService.ConnectionPaths.FirstOrDefault(path => path.IsSelected);
        return _selectedConnectionPath;
    }

    private void ReleasePreviewWire()
    {
        _previewPath.Visibility = Visibility.Collapsed;
        _previewPath.Data = null;
        _captureElement.ReleaseMouseCapture();
    }

    private void UpdatePreviewGeometry(PinViewModel sourcePin, Point currentPoint)
    {
        var startAnchor = sourcePin.Owner.GetPinAnchor(sourcePin);
        var start = new Point(sourcePin.Owner.X + startAnchor.X, sourcePin.Owner.Y + startAnchor.Y);
        var end = currentPoint;

        double direction = sourcePin.Direction == PinDirection.Output ? 1.0 : -1.0;
        var tangent = Math.Abs(end.X - start.X) * 0.45;
        var control1 = new Point(start.X + tangent * direction, start.Y);
        var control2 = new Point(end.X - tangent * direction, end.Y);

        var figure = new PathFigure
        {
            StartPoint = start,
            IsClosed = false,
            IsFilled = false,
        };
        figure.Segments.Add(new BezierSegment(control1, control2, end, true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        _previewPath.Data = geometry;
    }

    private void OpenPaletteFromPendingWire()
    {
        if (_pendingStartPin is null)
            return;

        _pendingPalettePin = _pendingStartPin;
        _pendingStartPin = null;
        _isConnecting = false;
        ReleasePreviewWire();
        _openNodePaletteForConnection(Mouse.GetPosition(_captureElement));
        _setStatus("选择节点后将自动连接引脚。");
    }

    private static bool TryNormalizeConnection(
        PinViewModel first,
        PinViewModel second,
        out PinViewModel sourcePin,
        out PinViewModel inputPin,
        out string reason)
    {
        if (first.Direction == PinDirection.Output && second.Direction == PinDirection.Input)
        {
            sourcePin = first;
            inputPin = second;
            reason = string.Empty;
            return true;
        }

        if (first.Direction == PinDirection.Input && second.Direction == PinDirection.Output)
        {
            sourcePin = second;
            inputPin = first;
            reason = string.Empty;
            return true;
        }

        sourcePin = first;
        inputPin = second;
        reason = "连线方向不正确，必须连接输出引脚和输入引脚。";
        return false;
    }

    private static ConnectionViewModel? FindConnectionFromSource(DependencyObject? source, Point graphPoint)
    {
        var current = source;
        while (current is not null)
        {
            if (current is FrameworkElement fe && fe.DataContext is ConnectionViewModel c)
                return c;
            if (current is FrameworkElement pathElement && pathElement.DataContext is ConnectionPathViewModel path)
                return path.FindNearestConnection(graphPoint);
            current = VisualTreeUtility.GetParent(current);
        }
        return null;
    }

    private static ConnectionPathViewModel? FindConnectionPathFromSource(DependencyObject? source)
    {
        var current = source;
        while (current is not null)
        {
            if (current is FrameworkElement { DataContext: ConnectionPathViewModel path })
                return path;

            current = VisualTreeUtility.GetParent(current);
        }

        return null;
    }
}
