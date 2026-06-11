using System.Windows;
using AutomationStudioWpf.Interaction;

namespace AutomationStudioWpf;

public partial class MainWindow
{
    private sealed record LegacyEditorSurfaceRegions(UIElement? Sidebar, UIElement? Canvas, UIElement? Inspector);

    private UIElement? _legacyEditorSidebarRegion;
    private UIElement? _legacyEditorCanvasRegion;
    private UIElement? _legacyEditorInspectorRegion;

    private LegacyEditorSurfaceRegions GetLegacyEditorSurfaceRegions()
    {
        _legacyEditorSidebarRegion ??= GetEditorGridRegionByColumn(0);
        _legacyEditorCanvasRegion ??= GetEditorGridRegionByColumn(2);
        _legacyEditorInspectorRegion ??= InspectorPanel ?? GetEditorGridRegionByColumn(4);

        return new LegacyEditorSurfaceRegions(
            _legacyEditorSidebarRegion,
            _legacyEditorCanvasRegion,
            _legacyEditorInspectorRegion);
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

    private void RestoreLegacyEditorRegionsToEditorGrid()
    {
        RestoreLegacyEditorRegion(_legacyEditorSidebarRegion, 0);
        RestoreLegacyEditorRegion(_legacyEditorCanvasRegion, 2);
        RestoreLegacyEditorRegion(_legacyEditorInspectorRegion, 4);
    }

    private void RestoreLegacyEditorRegion(UIElement? region, int column)
    {
        if (region is null || EditorGrid.Children.Contains(region))
            return;

        System.Windows.Controls.Grid.SetColumn(region, column);
        EditorGrid.Children.Add(region);
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
