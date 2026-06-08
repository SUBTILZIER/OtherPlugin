using System.ComponentModel;
using Brush = System.Windows.Media.Brush;
using Geometry = System.Windows.Media.Geometry;
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
        return ConnectionSplinePlanner.BuildPinConnectionGeometry(SourcePin, TargetPin);
    }
}
