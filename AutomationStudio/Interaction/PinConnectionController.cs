using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Services;
using Point = System.Windows.Point;

namespace AutomationStudioWpf.Interaction;

public sealed class PinConnectionController
{
    private readonly GraphEditorService _editorService;
    private readonly FrameworkElement _captureElement;
    private readonly Path _previewPath;
    private readonly Action<string> _setStatus;

    private PinViewModel? _pendingOutputPin;
    private bool _isConnecting;
    private bool _wireWasDragged;

    public PinConnectionController(
        GraphEditorService editorService,
        FrameworkElement captureElement,
        Path previewPath,
        Action<string> setStatus)
    {
        _editorService = editorService;
        _captureElement = captureElement;
        _previewPath = previewPath;
        _setStatus = setStatus;
    }

    public bool IsConnecting => _isConnecting;

    public void DisconnectPin(PinViewModel pin)
    {
        _editorService.ClearConnectionsForPin(pin);
        _setStatus($"已断开 {pin.Owner.Title}.{pin.DisplayName} 的所有连接。");
    }

    public void Begin(PinViewModel outputPin, Point currentPoint)
    {
        _pendingOutputPin = outputPin;
        _isConnecting = true;
        _wireWasDragged = false;
        _captureElement.CaptureMouse();
        UpdatePreviewGeometry(outputPin, currentPoint);
        _previewPath.Visibility = Visibility.Visible;
        _setStatus($"从{outputPin.Owner.Title}.{outputPin.DisplayName} 拖拽连线...");
    }

    public void Move(Point currentPoint)
    {
        if (!_isConnecting || _pendingOutputPin is null)
            return;

        _wireWasDragged = true;
        UpdatePreviewGeometry(_pendingOutputPin, currentPoint);
    }

    public bool Complete(PinViewModel targetPin)
    {
        if (_pendingOutputPin is null || !_isConnecting)
            return false;

        if (targetPin == _pendingOutputPin)
            return true;

        if (!_editorService.CanConnect(_pendingOutputPin, targetPin, out var reason))
        {
            _setStatus(reason);
            Cancel(null);
            return true;
        }

        _editorService.CreateConnection(_pendingOutputPin, targetPin);
        _setStatus($"已连接：{_pendingOutputPin.Owner.Title}.{_pendingOutputPin.DisplayName} -> {targetPin.Owner.Title}.{targetPin.DisplayName}");
        Cancel(null);
        return true;
    }

    public void CompleteOrCancel(PinViewModel? targetPin)
    {
        if (!_isConnecting)
            return;

        if (targetPin is not null && targetPin != _pendingOutputPin)
        {
            Complete(targetPin);
        }
        else if (_wireWasDragged)
        {
            Cancel("已取消连线。");
        }

        ReleasePreviewWire();
    }

    public void Cancel(string? statusMessage)
    {
        _pendingOutputPin = null;
        _isConnecting = false;
        ReleasePreviewWire();

        if (!string.IsNullOrWhiteSpace(statusMessage))
            _setStatus(statusMessage);
    }

    public void RemoveConnection(ConnectionViewModel connection)
    {
        _editorService.RemoveConnection(connection);
        _editorService.UpdatePinConnectionStates();
        _setStatus("已断开连接。");
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
        _previewPath.Data = geometry;
    }
}
