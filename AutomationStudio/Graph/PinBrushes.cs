using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using Color = System.Windows.Media.Color;

namespace AutomationStudioWpf.Graph;

public static class PinBrushes
{
    public static SolidColorBrush ForKind(PinKind kind)
    {
        SolidColorBrush brush = kind switch
        {
            PinKind.Execution => new SolidColorBrush(Color.FromRgb(244, 244, 244)),
            PinKind.Boolean => new SolidColorBrush(Color.FromRgb(184, 45, 48)),
            PinKind.Vector2D => new SolidColorBrush(Color.FromRgb(80, 196, 114)),
            PinKind.String => new SolidColorBrush(Color.FromRgb(202, 46, 165)),
            _ => new SolidColorBrush(Color.FromRgb(190, 190, 190)),
        };
        brush.Freeze();
        return brush;
    }
}
