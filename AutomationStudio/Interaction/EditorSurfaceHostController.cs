using System.Windows;
using System.Windows.Controls;
using AutomationStudioWpf.Controls;

namespace AutomationStudioWpf.Interaction;

/// <summary>
/// Centralizes ownership transfer for editor surface controls.
/// </summary>
public sealed class EditorSurfaceHostController
{
    public void AttachToHost(EditorSessionViewModel session, ContentControl host)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(host);

        var context = session.EnsureSurfaceContext();
        RemoveFromCurrentParent(context.Surface);
        host.Content = context.Surface;
        host.Visibility = Visibility.Visible;
    }

    public void ClearHost(ContentControl host)
    {
        ArgumentNullException.ThrowIfNull(host);
        host.Content = null;
        host.Visibility = Visibility.Collapsed;
    }

    public void AttachRegions(EditorSurfaceControl surface, UIElement? sidebar, UIElement? canvas, UIElement? inspector)
    {
        ArgumentNullException.ThrowIfNull(surface);

        SetRegionContent(surface.SidebarHost, sidebar);
        SetRegionContent(surface.CanvasHost, canvas);
        SetRegionContent(surface.InspectorRegionHost, inspector);
    }

    public void ClearRegions(EditorSurfaceControl surface)
    {
        ArgumentNullException.ThrowIfNull(surface);

        ClearRegion(surface.SidebarHost);
        ClearRegion(surface.CanvasHost);
        ClearRegion(surface.InspectorRegionHost);
    }

    private static void SetRegionContent(ContentControl host, UIElement? content)
    {
        ArgumentNullException.ThrowIfNull(host);

        if (content is null)
        {
            ClearRegion(host);
            return;
        }

        RemoveFromCurrentParent(content);
        host.Content = content;
        host.Visibility = Visibility.Visible;
    }

    private static void ClearRegion(ContentControl host)
    {
        ArgumentNullException.ThrowIfNull(host);
        host.Content = null;
        host.Visibility = Visibility.Collapsed;
    }

    private static void RemoveFromCurrentParent(FrameworkElement element)
    {
        switch (element.Parent)
        {
            case ContentControl contentControl when ReferenceEquals(contentControl.Content, element):
                contentControl.Content = null;
                break;
            case System.Windows.Controls.Panel panel:
                panel.Children.Remove(element);
                break;
            case Decorator decorator when ReferenceEquals(decorator.Child, element):
                decorator.Child = null;
                break;
        }
    }
}
