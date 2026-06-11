using System.Windows;
using AutomationStudioWpf.Interaction;

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

    private void AttachLegacyEditorRegionsToSessionSurface(EditorSessionViewModel session)
    {
        var context = session.EnsureSurfaceContext();
        var regions = GetLegacyEditorSurfaceRegions();

        _editorSurfaceHostController.AttachRegions(
            context.Surface,
            regions.Sidebar,
            regions.Canvas,
            regions.Inspector);
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
