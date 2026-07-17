using AutomationStudioWpf.Services;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using Color = System.Windows.Media.Color;
using WpfApplication = System.Windows.Application;

namespace AutomationStudioWpf.Graph;

public static class PinBrushes
{
    private static readonly SolidColorBrush ExecutionBrush = MutableBrush(244, 244, 244);
    private static readonly SolidColorBrush CompletionExecutionBrush = MutableBrush(255, 184, 72);
    private static readonly SolidColorBrush BooleanBrush = MutableBrush(184, 45, 48);
    private static readonly SolidColorBrush Vector2DBrush = MutableBrush(80, 196, 114);
    private static readonly SolidColorBrush StringBrush = MutableBrush(202, 46, 165);
    private static readonly SolidColorBrush DefaultBrush = MutableBrush(190, 190, 190);

    static PinBrushes()
    {
        RefreshThemeBrushes();
        AppThemeService.ThemeChanged += (_, _) => RefreshThemeBrushes();
    }

    public static SolidColorBrush CompletionExecution => CompletionExecutionBrush;

    public static SolidColorBrush ForKind(PinKind kind) => kind switch
    {
        PinKind.Execution => ExecutionBrush,
        PinKind.Boolean => BooleanBrush,
        PinKind.Vector2D => Vector2DBrush,
        PinKind.String => StringBrush,
        _ => DefaultBrush,
    };

    internal static void RefreshThemeBrushes()
    {
        SyncBrush(ExecutionBrush, "EditorExecutionPinBrush");
        SyncBrush(CompletionExecutionBrush, "EditorCompletionPinBrush");
        SyncBrush(BooleanBrush, "EditorBooleanPinBrush");
        SyncBrush(Vector2DBrush, "EditorVectorPinBrush");
        SyncBrush(StringBrush, "EditorStringPinBrush");
        SyncBrush(DefaultBrush, "EditorDefaultPinBrush");
    }

    private static SolidColorBrush MutableBrush(byte r, byte g, byte b) =>
        new(Color.FromRgb(r, g, b));

    private static void SyncBrush(SolidColorBrush target, string key)
    {
        if (WpfApplication.Current?.TryFindResource(key) is SolidColorBrush source)
            target.Color = source.Color;
    }
}
