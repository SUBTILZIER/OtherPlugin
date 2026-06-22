using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using AutomationStudioWpf.Controls;
using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Interaction;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPoint = System.Windows.Point;
using WpfButton = System.Windows.Controls.Button;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfTextBoxBase = System.Windows.Controls.Primitives.TextBoxBase;

namespace AutomationStudioWpf;

public partial class MainWindow
{
    private void NodeCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _nodeDragSelectionController.HandleNodeCardMouseDown(sender, e);
    }

    private void NodeHeader_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<WpfButton>(e.OriginalSource as DependencyObject) is not null)
            return;

        _nodeDragSelectionController.BeginNodeDrag(sender, e, _pinConnectionController.IsConnecting);
    }

    private void NodeHeader_PreviewMouseMove(object sender, WpfMouseEventArgs e)
    {
        _nodeDragSelectionController.MoveNodeDrag(e, _pinConnectionController.IsConnecting);
    }

    private void NodeHeader_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _nodeDragSelectionController.EndNodeDrag(sender);
    }

    private void CommonVariadicAddButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not NodeBaseViewModel node ||
            !node.AddDynamicPin())
        {
            return;
        }

        _editorService.UpdatePinConnectionStates();
        _editorService.RebindConnectionsToCurrentPins();
        node.RefreshDescription();
        LoadNodeToInspector(node);
        MarkActiveAssetDirty();
        SetStatus($"已添加动态引脚：{node.Title}");
        e.Handled = true;
    }

    private void CommonVariadicRemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not NodeBaseViewModel node ||
            !node.CanRemoveDynamicPin)
        {
            return;
        }

        string? pinName = node.GetLastDynamicPinName();
        if (pinName is null)
            return;
        if (node.FindPin(pinName) is { } pin)
            _editorService.ClearConnectionsForPin(pin);
        if (!node.RemoveLastDynamicPin())
            return;

        _editorService.UpdatePinConnectionStates();
        _editorService.RebindConnectionsToCurrentPins();
        node.RefreshDescription();
        LoadNodeToInspector(node);
        MarkActiveAssetDirty();
        SetStatus($"已删除动态引脚：{node.Title}");
        e.Handled = true;
    }

    private void GraphViewport_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (TryGetActiveEditorSurface() is not { } surface)
            return;

        if (surface.NodePalette.Visibility == Visibility.Visible)
        {
            var posInPalette = e.GetPosition(surface.NodePalette);
            if (posInPalette.X >= 0 && posInPalette.X <= surface.NodePalette.ActualWidth &&
                posInPalette.Y >= 0 && posInPalette.Y <= surface.NodePalette.ActualHeight)
            {
                return;
            }
        }

        if (!IsGraphBlankSource(e.OriginalSource as DependencyObject))
            return;

        if (_pinConnectionController.IsConnecting)
        {
            _pinConnectionController.Cancel("已取消连线。");
            e.Handled = true;
            return;
        }

        _nodeDragSelectionController.BeginSelection(e);
    }

    private void GraphViewport_PreviewMouseMove(object sender, WpfMouseEventArgs e)
    {
        if (TryGetActiveEditorSurface() is not { } surface)
            return;

        var viewportPos = e.GetPosition(surface.GraphViewport);
        _nodeDragSelectionController.UpdateMousePosition(e);

        if (_nodeDragSelectionController.IsDragging || _pinConnectionController.IsConnecting)
            _canvasPanZoomController.EdgePan(viewportPos);

        if (_rightClickPending && e.RightButton == MouseButtonState.Pressed)
        {
            var delta = viewportPos - _rightClickStartPos;
            if (Math.Abs(delta.X) > 3 || Math.Abs(delta.Y) > 3)
            {
                _rightClickPending = false;
                _canvasPanZoomController.BeginPan(viewportPos);
            }
        }

        if (_canvasPanZoomController.IsPanning)
        {
            if (e.RightButton != MouseButtonState.Pressed)
                _canvasPanZoomController.EndPan();

            _canvasPanZoomController.MovePan(e);
        }

        _pinConnectionController.Move(_nodeDragSelectionController.LastMousePosition);
        _nodeDragSelectionController.UpdateSelectionRectangle();
    }

    private void GraphViewport_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_pinConnectionController.HandleViewportMouseLeftButtonUp(e))
            return;

        _nodeDragSelectionController.CompleteSelection(e);
    }

    private void GraphViewport_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsGraphBlankSource(e.OriginalSource as DependencyObject))
            return;

        if (TryGetActiveEditorSurface() is not { } surface)
            return;

        _nodeDragSelectionController.CancelSelection();
        _nodeDragSelectionController.CancelDrag();
        surface.GraphViewport.ReleaseMouseCapture();
        _nodeDragSelectionController.SetCanvasFocusActive(true);
        surface.GraphViewport.Focus();

        _rightClickPending = true;
        _rightClickStartPos = e.GetPosition(surface.GraphViewport);
        e.Handled = true;
    }

    private void GraphViewport_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_rightClickPending)
        {
            _rightClickPending = false;
            OpenNodePalette(_rightClickStartPos);
            e.Handled = true;
            return;
        }

        if (!_canvasPanZoomController.IsPanning)
        {
            TryGetActiveEditorSurface()?.GraphViewport.ReleaseMouseCapture();
            return;
        }

        _canvasPanZoomController.EndPan();
        e.Handled = true;
    }

    private void GraphViewport_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (TryGetActiveEditorSurface() is not { } surface)
            return;

        if (surface.NodePalette.Visibility == Visibility.Visible)
        {
            var pos = e.GetPosition(surface.NodePalette);
            if (pos.X >= 0 && pos.X <= surface.NodePalette.ActualWidth &&
                pos.Y >= 0 && pos.Y <= surface.NodePalette.ActualHeight)
            {
                return;
            }
        }

        _canvasPanZoomController.Zoom(e);
        e.Handled = true;
    }

    private void NodePaletteScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer)
            return;

        scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta);
        e.Handled = true;
    }

    private void PinButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pinConnectionController.HandlePinMouseLeftButtonDown(sender, e);
    }

    private void PinButton_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _pinConnectionController.HandlePinMouseLeftButtonUp(sender, e);
    }

    private void ConnectionPath_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        _pinConnectionController.HandleConnectionDoubleClick(sender, e);
    }

    private void ConnectionPath_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2)
            _pinConnectionController.HandleConnectionDoubleClick(sender, e);
    }

    private void ConnectionPath_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pinConnectionController.HandleConnectionMouseLeftButtonDown(sender, e);
    }

    private void ConnectionPath_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pinConnectionController.HandleConnectionMouseRightButtonDown(sender, e);
    }

    private void DeleteConnectionPath_Click(object sender, RoutedEventArgs e)
    {
        _pinConnectionController.DeleteSelectedConnectionPath();
    }

    private void AddRerouteToConnectionPath_Click(object sender, RoutedEventArgs e)
    {
        _pinConnectionController.InsertRerouteOnSelectedPath();
    }

    private void SelectNode(NodeBaseViewModel? node)
    {
        _nodeDragSelectionController.SelectNode(node);
    }

    private void ClearNodeSelectionForConnection()
    {
        foreach (var node in _editorService.Nodes)
            node.IsSelected = false;

        LoadNodeToInspector(null);
    }

    private void Window_PreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_mousePickController.IsActive)
                _mousePickController.Stop("已退出鼠标拾取。");
            else if (_executionController.IsRunning)
                _executionController.Cancel();
            else if (_pinConnectionController.IsConnecting)
                _pinConnectionController.Cancel("已取消连线。");

            e.Handled = true;
            return;
        }

        var surface = TryGetActiveEditorSurface();
        if (surface is not null && (IsFocusInside(surface.GraphListBox) || IsFocusInside(surface.FunctionListBox)))
        {
            if (Keyboard.FocusedElement is not WpfTextBoxBase && GetFocusedGraphController() is { } controller)
            {
                controller.HandleKeyDown(e);
                if (e.Handled)
                    return;
            }

            return;
        }

        if (IsFocusInside(ContentBrowserListBox) || IsFocusInside(ContentFolderListBox))
        {
            HandleContentKeyDown(e);
            return;
        }

        if (Keyboard.FocusedElement is WpfTextBoxBase)
            return;

        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            {
                _graphCommandService.Undo();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Y || (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Shift) != 0))
            {
                _graphCommandService.Redo();
                e.Handled = true;
                return;
            }
        }

        if (_pinConnectionController.HandleKeyDown(e))
        {
            e.Handled = true;
            return;
        }

        if (_nodeDragSelectionController.HandleKeyDown(e))
            e.Handled = true;
    }

    private GraphListController? GetFocusedGraphController()
    {
        if (TryGetActiveEditorSurface() is not { } surface)
            return null;
        if (IsFocusInside(surface.FunctionListBox))
            return _functionListController;
        if (IsFocusInside(surface.GraphListBox))
            return _graphListController;
        return null;
    }

    private WpfPoint ViewportToGraph(WpfPoint viewportPoint)
    {
        return _canvasPanZoomController.ViewportToGraph(viewportPoint);
    }

    private void FitGraphToView()
    {
        _canvasPanZoomController.FitGraphToView();
    }

    private bool TryGetPinAtPosition(WpfPoint position, out PinViewModel? pin)
    {
        var hit = TryGetActiveEditorSurface()?.GraphSurface.InputHitTest(position) as DependencyObject;
        if (TryGetPinFromSource(hit, out pin))
            return true;

        return TryGetNearestPinAtPosition(position, out pin);
    }

    private bool TryGetNearestPinAtPosition(WpfPoint position, out PinViewModel? pin)
    {
        const double hitRadius = 24.0;
        double bestDistanceSquared = hitRadius * hitRadius;
        PinViewModel? bestPin = null;

        foreach (var candidate in _editorService.Nodes.SelectMany(node => node.InputPins.Concat(node.OutputPins)))
        {
            var anchor = candidate.Owner.GetPinAnchor(candidate);
            double x = candidate.Owner.X + anchor.X;
            double y = candidate.Owner.Y + anchor.Y;
            double dx = position.X - x;
            double dy = position.Y - y;
            double distanceSquared = dx * dx + dy * dy;
            if (distanceSquared > bestDistanceSquared)
                continue;

            bestDistanceSquared = distanceSquared;
            bestPin = candidate;
        }

        pin = bestPin;
        return pin is not null;
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
            current = GetSafeVisualOrLogicalParent(current);
        }
        pin = null;
        return false;
    }

    private bool IsGraphBlankSource(DependencyObject? source)
    {
        var surface = TryGetActiveEditorSurface();
        if (surface is null)
            return false;

        var current = source;
        while (current is not null)
        {
            if (current is FrameworkElement element)
            {
                if (element.DataContext is NodeBaseViewModel or PinViewModel or ConnectionViewModel or ConnectionPathViewModel)
                    return false;

                if (ReferenceEquals(element, surface.GraphSurface) || ReferenceEquals(element, surface.GraphViewport))
                    return true;
            }

            current = GetSafeVisualOrLogicalParent(current);
        }

        return false;
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
            return;

        var nodeRoot = FindNodeRootElement(element);
        if (nodeRoot is null || element.ActualWidth <= 0 || element.ActualHeight <= 0)
            return;

        try
        {
            var transform = element.TransformToAncestor(nodeRoot);
            var localTopLeft = transform.Transform(new WpfPoint(0, 0));
            pin.AnchorPoint = new WpfPoint(
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
            current = GetSafeVisualOrLogicalParent(current);
        }
        return lastMatch;
    }

    private void InitializeNodePalette()
    {
        // NodePaletteController builds menu entries from NodeRegistry definitions on demand.
    }

    private void OpenNodePalette(WpfPoint viewportPos)
    {
        _pinConnectionController.CancelPendingPaletteConnection();
        _nodePaletteController.Open(viewportPos);
    }

    private void OpenNodePaletteForConnection(WpfPoint viewportPos)
    {
        _nodePaletteController.Open(viewportPos);
    }

    private void CloseNodePalette()
    {
        _pinConnectionController.CancelPendingPaletteConnection();
        _nodePaletteController.Close();
    }

    private void NodePaletteSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        string filter = sender is WpfTextBox textBox
            ? textBox.Text.Trim()
            : TryGetActiveEditorSurface()?.NodePaletteSearchBox.Text.Trim() ?? string.Empty;
        _nodePaletteController.Filter(filter);
    }

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var sourceSurface = FindAncestor<EditorSurfaceControl>(e.OriginalSource as DependencyObject);
        HandleEditorSurfacePreviewMouseDown(sourceSurface, e);
    }

    internal void HandleEditorSurfacePreviewMouseDown(EditorSurfaceControl? sourceSurface, MouseButtonEventArgs e)
    {
        foreach (var session in _editorSessions.ToList())
        {
            var context = session.SurfaceContext;
            if (context is null || !context.IsConfigured || !context.NodePaletteController.IsOpen)
                continue;

            if (!ReferenceEquals(context.Surface, sourceSurface))
            {
                context.CloseNodePalette();
                continue;
            }

            var pos = e.GetPosition(context.Surface.NodePalette);
            if (!context.NodePaletteController.IsPointInside(pos))
                context.CloseNodePalette();
        }
    }
}
