using System.ComponentModel;
using Brush = System.Windows.Media.Brush;
using Geometry = System.Windows.Media.Geometry;

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
    private IDisposable? _pendingPathUpdate;
    private readonly IRenderUpdateScheduler _renderUpdateScheduler;

    public ConnectionViewModel(PinViewModel sourcePin, PinViewModel targetPin)
        : this(sourcePin, targetPin, RenderUpdateScheduler.CreateDefault())
    {
    }

    internal ConnectionViewModel(
        PinViewModel sourcePin,
        PinViewModel targetPin,
        IRenderUpdateScheduler renderUpdateScheduler)
    {
        SourcePin = sourcePin;
        TargetPin = targetPin;
        _renderUpdateScheduler = renderUpdateScheduler ?? throw new ArgumentNullException(nameof(renderUpdateScheduler));
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
            return;

        _disposed = true;
        _pendingPathUpdate?.Dispose();
        _pendingPathUpdate = null;
        _pathUpdateQueued = false;

        SourcePin.Owner.PropertyChanged -= NodePropertyChanged;
        TargetPin.Owner.PropertyChanged -= NodePropertyChanged;
        SourcePin.PropertyChanged -= PinPropertyChanged;
        TargetPin.PropertyChanged -= PinPropertyChanged;
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
        if (_disposed || _pathUpdateQueued)
            return;

        _pathUpdateQueued = true;
        IDisposable? request = _renderUpdateScheduler.Schedule(RefreshPathGeometry);
        if (_pathUpdateQueued)
            _pendingPathUpdate = request;
        else
            request?.Dispose();
        if (request is null)
            _pathUpdateQueued = false;
    }

    private void RefreshPathGeometry()
    {
        _pendingPathUpdate = null;
        _pathUpdateQueued = false;
        if (_disposed)
            return;

        _pathGeometry = BuildPathGeometry();
        OnPropertyChanged(nameof(PathGeometry));
    }

    private Geometry BuildPathGeometry()
    {
        return ConnectionSplinePlanner.BuildPinConnectionGeometry(SourcePin, TargetPin);
    }
}
