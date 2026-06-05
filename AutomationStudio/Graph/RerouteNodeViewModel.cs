using Point = System.Windows.Point;
using Brush = System.Windows.Media.Brush;

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

    public Brush CircleFill => PinBrushes.ForKind(RoutedKind);

    public override Point GetPinAnchor(PinViewModel pin)
    {
        return new Point(Width / 2.0, Height / 2.0);
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
