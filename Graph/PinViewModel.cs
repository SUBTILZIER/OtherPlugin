using Point = System.Windows.Point;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using Color = System.Windows.Media.Color;

namespace AutomationStudioWpf.Graph;

public sealed class PinViewModel : ObservableObject
{
    public const double PinRowHeight = 28;
    public const double PinIconSize = 12;

    private bool _hasConnection;
    private Point _anchorPoint;

    public PinViewModel(NodeBaseViewModel owner, string name, string displayName, PinDirection direction, PinKind kind)
    {
        Owner = owner;
        Name = name;
        DisplayName = displayName;
        Direction = direction;
        Kind = kind;
        PinBrush = CreateBrush(kind);
    }

    public NodeBaseViewModel Owner { get; }

    public string Name { get; }

    public string DisplayName { get; }

    public PinDirection Direction { get; }

    public PinKind Kind { get; }

    public Brush PinBrush { get; }

    public bool IsExecution => Kind == PinKind.Execution;

    public bool IsData => Kind != PinKind.Execution;

    public bool IsInput => Direction == PinDirection.Input;

    public bool IsOutput => Direction == PinDirection.Output;

    public string TriangleGeometry => "M 2 0 L 12 6 L 2 12 Z";

    public Brush PinFillBrush => HasConnection ? PinBrush : Brushes.Transparent;

    public bool HasConnection
    {
        get => _hasConnection;
        set
        {
            if (SetProperty(ref _hasConnection, value))
            {
                OnPropertyChanged(nameof(PinFillBrush));
            }
        }
    }

    public Point AnchorPoint
    {
        get => _anchorPoint;
        set => SetProperty(ref _anchorPoint, value);
    }

    public string KindLabel => Kind switch
    {
        PinKind.Execution => "执行",
        PinKind.Boolean => "布尔",
        PinKind.Vector2D => "Vector2D",
        _ => Kind.ToString(),
    };

    private static Brush CreateBrush(PinKind kind)
    {
        SolidColorBrush brush = kind switch
        {
            PinKind.Execution => new SolidColorBrush(Color.FromRgb(244, 244, 244)),
            PinKind.Boolean => new SolidColorBrush(Color.FromRgb(89, 197, 117)),
            PinKind.Vector2D => new SolidColorBrush(Color.FromRgb(88, 188, 255)),
            _ => new SolidColorBrush(Color.FromRgb(190, 190, 190)),
        };
        brush.Freeze();
        return brush;
    }
}
