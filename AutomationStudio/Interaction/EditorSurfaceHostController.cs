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
        if (ReferenceEquals(host.Content, context.Surface))
        {
            host.Visibility = Visibility.Visible;
            return;
        }

        DetachFromCurrentParent(context.Surface);
        host.Content = context.Surface;
        host.Visibility = Visibility.Visible;
    }

    public void ClearHost(ContentControl host)
    {
        ArgumentNullException.ThrowIfNull(host);
        host.Content = null;
        host.Visibility = Visibility.Collapsed;
    }

    public static void DetachFromCurrentParent(FrameworkElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

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
