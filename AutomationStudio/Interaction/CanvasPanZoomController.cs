using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Services;
using WpfMouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfMouseWheelEventArgs = System.Windows.Input.MouseWheelEventArgs;
using Point = System.Windows.Point;

namespace AutomationStudioWpf.Interaction;

public sealed class CanvasPanZoomController
{
    private readonly FrameworkElement _viewport;
    private readonly ScaleTransform _zoomTransform;
    private readonly TranslateTransform _panTransform;
    private readonly GraphEditorService _editorService;
    private readonly Action<string> _setStatus;

    private bool _isPanning;
    private Point _panStart;
    private Point _panStartOffset;
    private double _zoomLevel = 1.0;

    public CanvasPanZoomController(
        FrameworkElement viewport,
        ScaleTransform zoomTransform,
        TranslateTransform panTransform,
        GraphEditorService editorService,
        Action<string> setStatus)
    {
        _viewport = viewport;
        _zoomTransform = zoomTransform;
        _panTransform = panTransform;
        _editorService = editorService;
        _setStatus = setStatus;
    }

    public bool IsPanning => _isPanning;

    public Point ViewportToGraph(Point viewportPoint)
    {
        return new Point(
            (viewportPoint.X - _panTransform.X) / _zoomLevel,
            (viewportPoint.Y - _panTransform.Y) / _zoomLevel);
    }

    public bool BeginPan(WpfMouseButtonEventArgs e)
    {
        return BeginPan(e.GetPosition(_viewport));
    }

    public bool BeginPan(Point startPoint)
    {
        _isPanning = true;
        _panStart = startPoint;
        _panStartOffset = new Point(_panTransform.X, _panTransform.Y);
        _viewport.Focus();
        Mouse.Capture(_viewport);
        return true;
    }

    public bool MovePan(WpfMouseEventArgs e)
    {
        if (!_isPanning)
            return false;

        var current = e.GetPosition(_viewport);
        _panTransform.X = _panStartOffset.X + current.X - _panStart.X;
        _panTransform.Y = _panStartOffset.Y + current.Y - _panStart.Y;
        return true;
    }

    public bool EndPan()
    {
        if (!_isPanning)
            return false;

        _isPanning = false;
        Mouse.Capture(null);
        return true;
    }

    public void Zoom(WpfMouseWheelEventArgs e)
    {
        var mousePos = e.GetPosition(_viewport);
        var graphBefore = ViewportToGraph(mousePos);
        double factor = e.Delta > 0 ? 1.12 : 0.88;
        _zoomLevel = Math.Clamp(_zoomLevel * factor, 0.1, 2.5);
        _zoomTransform.ScaleX = _zoomLevel;
        _zoomTransform.ScaleY = _zoomLevel;

        _panTransform.X = mousePos.X - graphBefore.X * _zoomLevel;
        _panTransform.Y = mousePos.Y - graphBefore.Y * _zoomLevel;
    }

    public void FitGraphToView()
    {
        if (_editorService.Nodes.Count == 0 || _viewport.ActualWidth <= 0 || _viewport.ActualHeight <= 0)
            return;

        double left = _editorService.Nodes.Min(n => n.X);
        double top = _editorService.Nodes.Min(n => n.Y);
        double right = _editorService.Nodes.Max(n => n.X + n.Width);
        double bottom = _editorService.Nodes.Max(n => n.Y + n.Height);

        double width = Math.Max(1, right - left);
        double height = Math.Max(1, bottom - top);
        const double padding = 0.08;
        double viewWidth = _viewport.ActualWidth * (1.0 - padding * 2.0);
        double viewHeight = _viewport.ActualHeight * (1.0 - padding * 2.0);

        _zoomLevel = Math.Clamp(Math.Min(viewWidth / width, viewHeight / height), 0.1, 2.5);
        _zoomTransform.ScaleX = _zoomLevel;
        _zoomTransform.ScaleY = _zoomLevel;

        _panTransform.X = (_viewport.ActualWidth - width * _zoomLevel) / 2.0 - left * _zoomLevel;
        _panTransform.Y = (_viewport.ActualHeight - height * _zoomLevel) / 2.0 - top * _zoomLevel;
        _setStatus("已缩放到节点全览。");
    }
}
