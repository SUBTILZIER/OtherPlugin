using System.Windows;
using System.Windows.Controls;

namespace AutomationStudioWpf.Interaction;

/// <summary>
/// Centralizes ownership transfer for editor surface controls.
///
/// This controller is the migration boundary between the current shared
/// MainWindow.EditorGrid model and the target model where every
/// EditorSessionViewModel owns one independent EditorSurfaceControl.
///
/// It is intentionally not wired into MainWindow yet. The next migration step
/// can replace direct EditorGrid re-parenting with calls to AttachToHost while
/// keeping host ownership changes explicit and testable.
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

    private static void RemoveFromCurrentParent(FrameworkElement element)
    {
        switch (element.Parent)
        {
            case ContentControl contentControl when ReferenceEquals(contentControl.Content, element):
                contentControl.Content = null;
                break;
            case Panel panel:
                panel.Children.Remove(element);
                break;
            case Decorator decorator when ReferenceEquals(decorator.Child, element):
                decorator.Child = null;
                break;
        }
    }
}
