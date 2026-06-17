using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using Color = System.Windows.Media.Color;

namespace AutomationStudioWpf.Graph;

public static class PinBrushes
{
    private static readonly SolidColorBrush ExecutionBrush = FrozenBrush(244, 244, 244);
    private static readonly SolidColorBrush BooleanBrush = FrozenBrush(184, 45, 48);
    private static readonly SolidColorBrush Vector2DBrush = FrozenBrush(80, 196, 114);
    private static readonly SolidColorBrush StringBrush = FrozenBrush(202, 46, 165);
    private static readonly SolidColorBrush DefaultBrush = FrozenBrush(190, 190, 190);

    public static SolidColorBrush ForKind(PinKind kind) => kind switch
    {
        PinKind.Execution => ExecutionBrush,
        PinKind.Boolean => BooleanBrush,
        PinKind.Vector2D => Vector2DBrush,
        PinKind.String => StringBrush,
        _ => DefaultBrush,
    };

    private static SolidColorBrush FrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
