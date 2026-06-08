using System.ComponentModel;
using Point = System.Windows.Point;
using Brush = System.Windows.Media.Brush;
using Geometry = System.Windows.Media.Geometry;
using DispatcherPriority = System.Windows.Threading.DispatcherPriority;

namespace AutomationStudioWpf.Graph;

public sealed class ConnectionPathViewModel : ObservableObject, IDisposable
{
    private bool _disposed;
    private bool _pathUpdateQueued;
    private bool _isSelected;
    private Geometry? _pathGeometry;

    public ConnectionPathViewModel(IReadOnlyList<ConnectionViewModel> connections)
    {
        Connections = connections;
        StrokeBrush = connections.Count > 0 ? connections[0].StrokeBrush : PinBrushes.ForKind(PinKind.Execution);

        foreach (var pin in Connections.SelectMany(connection => new[] { connection.SourcePin, connection.TargetPin }).Distinct())
        {
            pin.PropertyChanged += PinPropertyChanged;
            pin.Owner.PropertyChanged += NodePropertyChanged;
        }
    }

    public IReadOnlyList<ConnectionViewModel> Connections { get; }

    public Brush StrokeBrush { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                OnPropertyChanged(nameof(SelectionOpacity));
                OnPropertyChanged(nameof(StrokeThickness));
            }
        }
    }

    public double SelectionOpacity => IsSelected ? 1.0 : 0.0;

    public double StrokeThickness => IsSelected ? 5.5 : 4.0;

    public Geometry PathGeometry => _pathGeometry ??= BuildPathGeometry();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var pin in Connections.SelectMany(connection => new[] { connection.SourcePin, connection.TargetPin }).Distinct())
        {
            pin.PropertyChanged -= PinPropertyChanged;
            pin.Owner.PropertyChanged -= NodePropertyChanged;
        }

        _disposed = true;
    }

    public ConnectionViewModel FindNearestConnection(Point graphPoint)
    {
        if (Connections.Count == 0)
        {
            throw new InvalidOperationException("Connection path has no backing connections.");
        }

        ConnectionViewModel nearest = Connections[0];
        double nearestDistance = double.MaxValue;
        foreach (var connection in Connections)
        {
            Point start = ConnectionSplinePlanner.GetAbsolutePinAnchor(connection.SourcePin);
            Point end = ConnectionSplinePlanner.GetAbsolutePinAnchor(connection.TargetPin);
            double distance = ConnectionSplinePlanner.DistanceToSegment(graphPoint, start, end);
            if (distance < nearestDistance)
            {
                nearest = connection;
                nearestDistance = distance;
            }
        }

        return nearest;
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
        IReadOnlyList<Point> points = BuildPathPoints();
        return ConnectionSplinePlanner.BuildGeometry(points);
    }

    private IReadOnlyList<Point> BuildPathPoints()
    {
        if (Connections.Count == 0)
        {
            return [];
        }

        if (Connections.Count == 1)
        {
            var connection = Connections[0];
            return
            [
                ConnectionSplinePlanner.GetAbsolutePinAnchor(connection.SourcePin),
                ConnectionSplinePlanner.GetAbsolutePinAnchor(connection.TargetPin),
            ];
        }

        var first = Connections[0];
        var last = Connections[^1];
        Point start = ConnectionSplinePlanner.GetAbsolutePinAnchor(first.SourcePin);
        Point end = ConnectionSplinePlanner.GetAbsolutePinAnchor(last.TargetPin);
        List<NodeBaseViewModel> routeNodes = [];

        foreach (var connection in Connections)
        {
            AddRerouteNode(routeNodes, connection.SourcePin.Owner);
            AddRerouteNode(routeNodes, connection.TargetPin.Owner);
        }

        IEnumerable<Point> routePoints = routeNodes.Select(node => new Point(node.X + node.Width / 2.0, node.Y + node.Height / 2.0));
        return [start, .. routePoints, end];
    }

    private static void AddRerouteNode(List<NodeBaseViewModel> routeNodes, NodeBaseViewModel node)
    {
        if (node.NodeKind == NodeKind.Reroute && !routeNodes.Contains(node))
        {
            routeNodes.Add(node);
        }
    }
}
