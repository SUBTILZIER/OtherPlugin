namespace AutomationStudioWpf.Controls;

/// <summary>
/// Hosts one graph editor surface instance.
/// </summary>
public partial class EditorSurfaceControl : System.Windows.Controls.UserControl
{
    public EditorSurfaceControl()
    {
        InitializeComponent();
    }

    public System.Windows.Controls.ContentControl SidebarHost => GraphSidebarHost;

    public System.Windows.Controls.ContentControl CanvasHost => GraphCanvasHost;

    public System.Windows.Controls.ContentControl InspectorRegionHost => InspectorHost;
}
