using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace AutomationStudioWpf;

public partial class MainWindow
{
    private static bool IsVisualAncestor(DependencyObject ancestor, DependencyObject source)
    {
        var current = source;
        while (current is not null)
        {
            if (ReferenceEquals(current, ancestor))
                return true;

            current = GetSafeVisualOrLogicalParent(current);
        }

        return false;
    }

    private static bool HasVisualAncestor<T>(DependencyObject source) where T : DependencyObject
    {
        var current = source;
        while (current is not null)
        {
            if (current is T)
                return true;

            current = GetSafeVisualOrLogicalParent(current);
        }

        return false;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T target)
                return target;

            var nested = FindVisualChild<T>(child);
            if (nested is not null)
                return nested;
        }

        return null;
    }

    private static bool IsFocusInside(DependencyObject root)
    {
        if (Keyboard.FocusedElement is not DependencyObject focused)
            return false;

        var current = focused;
        while (current is not null)
        {
            if (ReferenceEquals(current, root))
                return true;

            current = GetSafeVisualOrLogicalParent(current);
        }

        return false;
    }

    private static DependencyObject? GetSafeVisualOrLogicalParent(DependencyObject current)
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
