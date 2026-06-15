using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Services;
using Point = System.Windows.Point;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace AutomationStudioWpf.Interaction;

public sealed class NodeDragSelectionController
{
    private const double GridSize = 20.0;

    private readonly GraphEditorService _editorService;
    private readonly GraphCommandService _commandService;
    private readonly NodeClipboardService _clipboardService;
    private readonly NodeFactory _nodeFactory;
    private readonly FrameworkElement _graphViewport;
    private readonly FrameworkElement _selectionRectangle;
    private readonly Func<Point, Point> _viewportToGraph;
    private readonly Action<NodeBaseViewModel?> _loadInspector;
    private readonly Action _fitGraphToView;
    private readonly Action _ensureCanvasLargeEnough;
    private readonly Action _markDirty;
    private readonly Action _clearConnectionSelection;
    private readonly Action<string> _setStatus;

    private NodeBaseViewModel? _dragNode;
    private List<(NodeBaseViewModel Node, double OffsetX, double OffsetY)> _dragGroup = [];
    private GraphFileModel? _dragBeforeSnapshot;
    private bool _isSelecting;
    private bool _dragFrameQueued;
    private bool _dragChanged;
    private Point _pendingDragPoint;
    private Point _selectionStart;

    public NodeDragSelectionController(
        GraphEditorService editorService,
        GraphCommandService commandService,
        NodeClipboardService clipboardService,
        NodeFactory nodeFactory,
        FrameworkElement graphViewport,
        FrameworkElement selectionRectangle,
        Func<Point, Point> viewportToGraph,
        Action<NodeBaseViewModel?> loadInspector,
        Action fitGraphToView,
        Action ensureCanvasLargeEnough,
        Action markDirty,
        Action clearConnectionSelection,
        Action<string> setStatus)
    {
        _editorService = editorService;
        _commandService = commandService;
        _clipboardService = clipboardService;
        _nodeFactory = nodeFactory;
        _graphViewport = graphViewport;
        _selectionRectangle = selectionRectangle;
        _viewportToGraph = viewportToGraph;
        _loadInspector = loadInspector;
        _fitGraphToView = fitGraphToView;
        _ensureCanvasLargeEnough = ensureCanvasLargeEnough;
        _markDirty = markDirty;
        _clearConnectionSelection = clearConnectionSelection;
        _setStatus = setStatus;
    }

    public Point LastMousePosition { get; private set; }

    public bool IsCanvasFocusActive { get; private set; }

    public bool IsDragging => _dragNode is not null;

    public bool IsSelecting => _isSelecting;

    public void SetCanvasFocusActive(bool active) => IsCanvasFocusActive = active;

    public void SelectNode(NodeBaseViewModel? node)
    {
        _clearConnectionSelection();
        ClearSelection();
        if (node is not null)
            node.IsSelected = true;

        _loadInspector(node);
    }

    public void ClearSelection()
    {
        foreach (var node in _editorService.Nodes)
            node.IsSelected = false;
    }

    public bool HandleNodeCardMouseDown(object sender, WpfMouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not NodeBaseViewModel node)
            return false;

        IsCanvasFocusActive = false;
        SelectNode(node);
        e.Handled = true;
        return true;
    }

    public bool BeginNodeDrag(object sender, WpfMouseButtonEventArgs e, bool isConnecting)
    {
        if (isConnecting) return false;
        if (IsPinInteractionSource(e.OriginalSource as DependencyObject)) return false;
        if (sender is not FrameworkElement element || element.DataContext is not NodeBaseViewModel node)
            return false;

        IsCanvasFocusActive = false;
        if (!node.IsSelected)
            SelectNode(node);
        else
            _loadInspector(node);

        var point = _viewportToGraph(e.GetPosition(_graphViewport));
        _dragNode = node;
        _dragBeforeSnapshot = _commandService.Capture();
        _pendingDragPoint = point;
        _dragFrameQueued = false;
        _dragChanged = false;

        var selectedCount = _editorService.Nodes.Count(n => n.IsSelected);
        _dragGroup = selectedCount > 1 && node.IsSelected
            ? _editorService.Nodes
                .Where(n => n.IsSelected)
                .Select(n => (n, point.X - n.X, point.Y - n.Y))
                .ToList()
            : [(node, point.X - node.X, point.Y - node.Y)];

        element.CaptureMouse();
        e.Handled = true;
        return true;
    }

    public void MoveNodeDrag(WpfMouseEventArgs e, bool isConnecting)
    {
        if (isConnecting) return;
        if (_dragNode is null || e.LeftButton != MouseButtonState.Pressed) return;

        _pendingDragPoint = _viewportToGraph(e.GetPosition(_graphViewport));
        QueueDragFrame();
    }

    public void EndNodeDrag(object sender)
    {
        ApplyPendingDragFrame();

        if (sender is UIElement element)
            element.ReleaseMouseCapture();

        if (_dragChanged)
        {
            if (_dragBeforeSnapshot is not null)
                _commandService.RecordApplied("Move nodes", _dragBeforeSnapshot, _commandService.Capture());
            _markDirty();
        }

        _dragNode = null;
        _dragBeforeSnapshot = null;
        _dragGroup.Clear();
        _dragFrameQueued = false;
        _dragChanged = false;
    }

    public void BeginSelection(WpfMouseButtonEventArgs e)
    {
        IsCanvasFocusActive = true;
        _graphViewport.Focus();
        SelectNode(null);

        _isSelecting = true;
        _selectionStart = _viewportToGraph(e.GetPosition(_graphViewport));
        Canvas.SetLeft(_selectionRectangle, _selectionStart.X);
        Canvas.SetTop(_selectionRectangle, _selectionStart.Y);
        _selectionRectangle.Width = 0;
        _selectionRectangle.Height = 0;
        _selectionRectangle.Visibility = Visibility.Visible;
        _graphViewport.CaptureMouse();
        e.Handled = true;
    }

    public void UpdateMousePosition(WpfMouseEventArgs e)
    {
        LastMousePosition = _viewportToGraph(e.GetPosition(_graphViewport));
    }

    public void UpdateSelectionRectangle()
    {
        if (!_isSelecting) return;

        var current = LastMousePosition;
        var left = Math.Min(current.X, _selectionStart.X);
        var top = Math.Min(current.Y, _selectionStart.Y);
        var width = Math.Abs(current.X - _selectionStart.X);
        var height = Math.Abs(current.Y - _selectionStart.Y);

        Canvas.SetLeft(_selectionRectangle, left);
        Canvas.SetTop(_selectionRectangle, top);
        _selectionRectangle.Width = width;
        _selectionRectangle.Height = height;
    }

    public bool CompleteSelection(WpfMouseButtonEventArgs e)
    {
        if (!_isSelecting) return false;

        _isSelecting = false;
        _graphViewport.ReleaseMouseCapture();

        var selectionBounds = new Rect(
            Canvas.GetLeft(_selectionRectangle),
            Canvas.GetTop(_selectionRectangle),
            _selectionRectangle.Width,
            _selectionRectangle.Height);

        _selectionRectangle.Visibility = Visibility.Collapsed;
        ApplySelection(selectionBounds);
        e.Handled = true;
        return true;
    }

    public void CancelSelection()
    {
        if (!_isSelecting) return;

        _isSelecting = false;
        _selectionRectangle.Visibility = Visibility.Collapsed;
    }

    public void CancelDrag()
    {
        _dragNode = null;
        _dragBeforeSnapshot = null;
        _dragGroup.Clear();
        _dragFrameQueued = false;
        _dragChanged = false;
    }

    public bool HandleKeyDown(WpfKeyEventArgs e)
    {
        if (e.Key == Key.Delete)
        {
            _commandService.Execute("Delete selected nodes", _editorService.RemoveSelectedNodes);
            SelectNode(null);
            e.Handled = true;
            return true;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            if (e.Key == Key.C)
            {
                CopySelectedNodes();
                e.Handled = true;
                return true;
            }

            if (e.Key == Key.V)
            {
                PasteNodesAtMouse();
                e.Handled = true;
                return true;
            }
        }

        if (e.Key == Key.F && IsCanvasFocusActive)
        {
            _fitGraphToView();
            e.Handled = true;
            return true;
        }

        if (e.Key == Key.Q)
        {
            AlignSelectedNodesHorizontal();
            e.Handled = true;
            return true;
        }

        var effectiveKey = e.Key == Key.System ? e.SystemKey : e.Key;
        if (effectiveKey == Key.S &&
            (Keyboard.Modifiers & (ModifierKeys.Shift | ModifierKeys.Alt)) == (ModifierKeys.Shift | ModifierKeys.Alt))
        {
            AlignSelectedNodesVertical();
            e.Handled = true;
            return true;
        }

        if (TryMoveSelectedNodesByKeyboard(e))
            return true;

        return false;
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

        _loadInspector(selectedNodes.FirstOrDefault());
        _setStatus(selectedNodes.Count == 0 ? "未选中任何节点。" : $"已框选 {selectedNodes.Count} 个节点。");
    }

    private void CopySelectedNodes()
    {
        _clipboardService.CopySelectedNodes(_editorService.Nodes, _editorService.Connections);
        _setStatus($"已复制 {_clipboardService.ClipboardNodeCount} 个节点。");
    }

    private void PasteNodesAtMouse()
    {
        if (!_clipboardService.HasClipboardContent) return;

        ClearSelection();

        var pastedNodes = _clipboardService.PasteNodesAt(
            LastMousePosition,
            _nodeFactory.CreateNodeId,
            out var connections);

        _commandService.Execute("Paste nodes", () =>
        {
            foreach (var node in pastedNodes)
                _editorService.AddNode(node);

            foreach (var (sourceId, sourcePin, targetId, targetPin) in connections)
            {
                var sourceNode = _editorService.Nodes.FirstOrDefault(n => n.Id == sourceId);
                var targetNode = _editorService.Nodes.FirstOrDefault(n => n.Id == targetId);
                if (sourceNode is null || targetNode is null) continue;

                var sPin = sourceNode.OutputPins.FirstOrDefault(p => p.Name == sourcePin);
                var tPin = targetNode.InputPins.FirstOrDefault(p => p.Name == targetPin);
                if (sPin is not null && tPin is not null)
                    _editorService.CreateConnection(sPin, tPin);
            }
        });

        _ensureCanvasLargeEnough();
        _setStatus($"已粘贴 {pastedNodes.Count} 个节点。");
    }

    private void AlignSelectedNodesHorizontal()
    {
        var selectedNodes = _editorService.Nodes.Where(n => n.IsSelected).ToList();
        if (selectedNodes.Count < 2) return;

        var before = _commandService.Capture();
        var avgY = selectedNodes.Average(n => n.Y);
        foreach (var node in selectedNodes)
            node.Y = avgY;

        _commandService.RecordApplied("Align nodes horizontal", before, _commandService.Capture());
        _markDirty();
        _setStatus($"已将 {selectedNodes.Count} 个节点横向对齐。");
    }

    private void AlignSelectedNodesVertical()
    {
        var selectedNodes = _editorService.Nodes.Where(n => n.IsSelected).ToList();
        if (selectedNodes.Count < 2) return;

        var before = _commandService.Capture();
        var avgX = selectedNodes.Average(n => n.X);
        foreach (var node in selectedNodes)
            node.X = avgX;

        _commandService.RecordApplied("Align nodes vertical", before, _commandService.Capture());
        _markDirty();
        _setStatus($"已将 {selectedNodes.Count} 个节点纵向对齐。");
    }

    private bool TryMoveSelectedNodesByKeyboard(WpfKeyEventArgs e)
    {
        double dx = 0;
        double dy = 0;
        switch (e.Key)
        {
            case Key.Left:
                dx = -KeyboardMoveStep();
                break;
            case Key.Right:
                dx = KeyboardMoveStep();
                break;
            case Key.Up:
                dy = -KeyboardMoveStep();
                break;
            case Key.Down:
                dy = KeyboardMoveStep();
                break;
            default:
                return false;
        }

        var selectedNodes = _editorService.Nodes.Where(n => n.IsSelected).ToList();
        if (selectedNodes.Count == 0)
            return false;

        var before = _commandService.Capture();
        foreach (var node in selectedNodes)
        {
            node.X = SnapIfNeeded(node.X + dx);
            node.Y = SnapIfNeeded(node.Y + dy);
        }

        _commandService.RecordApplied("Move nodes", before, _commandService.Capture());
        _markDirty();
        _setStatus($"Moved {selectedNodes.Count} node(s).");
        e.Handled = true;
        return true;
    }

    private static double KeyboardMoveStep()
    {
        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0)
            return 1.0;

        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
            return GridSize * 2.0;

        return GridSize;
    }

    private static double SnapIfNeeded(double value)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0)
            return value;

        return Math.Round(value / GridSize) * GridSize;
    }

    private void QueueDragFrame()
    {
        if (_dragFrameQueued)
            return;

        _dragFrameQueued = true;
        _graphViewport.Dispatcher.BeginInvoke(ApplyPendingDragFrame, DispatcherPriority.Render);
    }

    private void ApplyPendingDragFrame()
    {
        _dragFrameQueued = false;
        if (_dragNode is null)
            return;

        foreach (var (item, offsetX, offsetY) in _dragGroup)
        {
            double nextX = SnapIfNeeded(_pendingDragPoint.X - offsetX);
            double nextY = SnapIfNeeded(_pendingDragPoint.Y - offsetY);

            if (Math.Abs(item.X - nextX) > 0.01)
            {
                item.X = nextX;
                _dragChanged = true;
            }

            if (Math.Abs(item.Y - nextY) > 0.01)
            {
                item.Y = nextY;
                _dragChanged = true;
            }
        }
    }

    private static bool IsPinInteractionSource(DependencyObject? source)
    {
        var current = source;
        while (current is not null)
        {
            if (current is FrameworkElement { DataContext: PinViewModel })
                return true;

            current = VisualTreeUtility.GetParent(current);
        }

        return false;
    }
}
