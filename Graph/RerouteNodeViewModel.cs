using System.Windows.Media;
using Point = System.Windows.Point;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace AutomationStudioWpf.Graph;

public sealed class RerouteNodeViewModel : NodeBaseViewModel
{
    public RerouteNodeViewModel(string id, PinKind pinKind) : base(id, string.Empty)
    {
        RoutedKind = pinKind;
        AddInput("in", string.Empty, pinKind);
        AddOutput("out", string.Empty, pinKind);
    }

    public override NodeKind NodeKind => NodeKind.Reroute;
    public override string NodeTypeKey => "reroute";
    public override bool CanDelete => true;
    public override double Width => 20;
    public override double Height => 20;

    public PinKind RoutedKind { get; }

    public Brush CircleFill => RoutedKind switch
    {
        PinKind.Execution => new SolidColorBrush(Color.FromRgb(244, 244, 244)),
        PinKind.Boolean => new SolidColorBrush(Color.FromRgb(180, 60, 60)),
        PinKind.Vector2D => new SolidColorBrush(Color.FromRgb(88, 188, 255)),
        _ => new SolidColorBrush(Color.FromRgb(190, 190, 190)),
    };

    public new Point GetPinAnchor(PinViewModel pin)
    {
        if (pin.AnchorPoint != default)
            return pin.AnchorPoint;

        return new Point(10, 10);
    }

    public override void RefreshDescription()
    {
        Description = RoutedKind switch
        {
            PinKind.Execution => "执行路由",
            PinKind.Boolean => "布尔路由",
            PinKind.Vector2D => "Vector2D 路由",
            _ => "路由",
        };
    }
}
