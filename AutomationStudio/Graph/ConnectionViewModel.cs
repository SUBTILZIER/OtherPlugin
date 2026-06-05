using System.ComponentModel;
using Point = System.Windows.Point;
using Brush = System.Windows.Media.Brush;
using Geometry = System.Windows.Media.Geometry;
using PathFigure = System.Windows.Media.PathFigure;
using BezierSegment = System.Windows.Media.BezierSegment;
using PathGeometry = System.Windows.Media.PathGeometry;
using DispatcherPriority = System.Windows.Threading.DispatcherPriority;

namespace AutomationStudioWpf.Graph;

/// <summary>
/// Represents one graph connection line.
/// The path is recalculated whenever either endpoint node moves.
/// </summary>
public sealed class ConnectionViewModel : ObservableObject, IDisposable
{
    private bool _disposed;
    private bool _pathUpdateQueued;
    private Geometry? _pathGeometry;

    public ConnectionViewModel(PinViewModel sourcePin, PinViewModel targetPin)
    {
        SourcePin = sourcePin;
        TargetPin = targetPin;
        StrokeBrush = sourcePin.PinBrush;

        SourcePin.Owner.PropertyChanged += NodePropertyChanged;
        TargetPin.Owner.PropertyChanged += NodePropertyChanged;
        SourcePin.PropertyChanged += PinPropertyChanged;
        TargetPin.PropertyChanged += PinPropertyChanged;
    }

    public PinViewModel SourcePin { get; }

    public PinViewModel TargetPin { get; }

    public Brush StrokeBrush { get; }

    public Geometry PathGeometry => _pathGeometry ??= BuildPathGeometry();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        SourcePin.Owner.PropertyChanged -= NodePropertyChanged;
        TargetPin.Owner.PropertyChanged -= NodePropertyChanged;
        SourcePin.PropertyChanged -= PinPropertyChanged;
        TargetPin.PropertyChanged -= PinPropertyChanged;
        _disposed = true;
    }

    private void NodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NodeBaseViewModel.X) or nameof(NodeBaseViewModel.Y))
        {
            QueuePathGeometryRefresh();
        }
    }

    private void PinPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PinViewModel.AnchorPoint))
        {
            QueuePathGeometryRefresh();
        }
    }

    private void QueuePathGeometryRefresh()
    {
        if (_pathUpdateQueued)
        {
            return;
        }

        _pathUpdateQueued = true;
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            RefreshPathGeometry();
            return;
        }

        dispatcher.BeginInvoke(RefreshPathGeometry, DispatcherPriority.Render);
    }

    private void RefreshPathGeometry()
    {
        _pathUpdateQueued = false;
        _pathGeometry = BuildPathGeometry();
        OnPropertyChanged(nameof(PathGeometry));
    }

    private Geometry BuildPathGeometry()
    {
        Point start = GetAbsolutePinAnchor(SourcePin);
        Point end = GetAbsolutePinAnchor(TargetPin);

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
        if (geometry.CanFreeze)
        {
            geometry.Freeze();
        }

        return geometry;
    }

    private static Point GetAbsolutePinAnchor(PinViewModel pin)
    {
        Point anchor = pin.Owner.GetPinAnchor(pin);
        return new Point(pin.Owner.X + anchor.X, pin.Owner.Y + anchor.Y);
    }
}
