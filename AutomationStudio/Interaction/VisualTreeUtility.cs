using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace AutomationStudioWpf.Interaction;

internal static class VisualTreeUtility
{
    public static DependencyObject? GetParent(DependencyObject current)
    {
        if (current is Visual or Visual3D)
            return VisualTreeHelper.GetParent(current);

        if (current is FrameworkContentElement contentElement)
            return contentElement.Parent;

        if (current is FrameworkElement element)
            return element.Parent;

        return LogicalTreeHelper.GetParent(current);
    }
}
