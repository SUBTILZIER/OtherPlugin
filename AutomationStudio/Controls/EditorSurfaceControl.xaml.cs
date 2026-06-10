namespace AutomationStudioWpf.Controls;

/// <summary>
/// Hosts one graph editor surface instance.
///
/// This control is intentionally not wired into MainWindow yet. It is the first
/// step toward replacing the current shared EditorGrid re-parenting model with
/// one independent editor surface per editor session/window.
/// </summary>
public partial class EditorSurfaceControl : System.Windows.Controls.UserControl
{
    public EditorSurfaceControl()
    {
        InitializeComponent();
    }
}
