using System.Windows;

namespace AutomationStudioWpf;

public partial class MainWindow
{
    private sealed record LegacyEditorSurfaceRegions(UIElement? Sidebar, UIElement? Canvas, UIElement? Inspector);

    private LegacyEditorSurfaceRegions GetLegacyEditorSurfaceRegions()
    {
        return new LegacyEditorSurfaceRegions(
            GetEditorGridRegionByColumn(0),
            GetEditorGridRegionByColumn(2),
            InspectorPanel ?? GetEditorGridRegionByColumn(4));
    }

    private UIElement? GetEditorGridRegionByColumn(int column)
    {
        foreach (UIElement child in EditorGrid.Children)
        {
            if (System.Windows.Controls.Grid.GetColumn(child) == column)
                return child;
        }

        return null;
    }
}
