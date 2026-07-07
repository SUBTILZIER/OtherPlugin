using System.Windows.Media;
using WpfBrush = System.Windows.Media.Brush;

namespace AutomationStudioWpf.Interaction;

public partial class InspectorController
{
    private static WpfBrush ResourceBrush(string key) => ThemeResourceHelper.Brush(key);
}
