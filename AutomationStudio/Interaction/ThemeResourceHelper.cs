using System.Windows;
using System.Windows.Media;
using WpfApplication = System.Windows.Application;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;

namespace AutomationStudioWpf.Interaction;

internal static class ThemeResourceHelper
{
    public static void SetResource(DependencyObject target, DependencyProperty property, string key)
    {
        if (target is FrameworkElement element)
        {
            element.SetResourceReference(property, key);
        }
        else if (target is FrameworkContentElement contentElement)
        {
            contentElement.SetResourceReference(property, key);
        }
    }

    public static WpfBrush Brush(string key)
    {
        return WpfApplication.Current?.TryFindResource(key) as WpfBrush ?? WpfBrushes.Transparent;
    }
}
