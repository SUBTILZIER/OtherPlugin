using System.Windows;

namespace AutomationStudioWpf;

public partial class MainWindow
{
    private readonly Interaction.EditorSurfaceHostController _editorSurfaceHostController = new();
    private System.Windows.Controls.ContentControl? _editorSurfaceHost;

    private System.Windows.Controls.ContentControl EnsureEditorSurfaceHost()
    {
        if (_editorSurfaceHost is not null)
            return _editorSurfaceHost;

        _editorSurfaceHost = new System.Windows.Controls.ContentControl
        {
            Visibility = Visibility.Collapsed,
            Focusable = false,
        };
        System.Windows.Controls.Grid.SetRow(_editorSurfaceHost, 0);

        if (_editorGridHomeParent is not null)
        {
            var insertIndex = _editorGridHomeIndex >= 0
                ? Math.Min(_editorGridHomeIndex + 1, _editorGridHomeParent.Children.Count)
                : _editorGridHomeParent.Children.Count;
            _editorGridHomeParent.Children.Insert(insertIndex, _editorSurfaceHost);
        }

        return _editorSurfaceHost;
    }

    private void AttachSessionSurfaceToMainHost(Interaction.EditorSessionViewModel session)
    {
        var host = EnsureEditorSurfaceHost();
        _editorSurfaceHostController.AttachToHost(session, host);
    }

    private void HideEditorSurfaceHost()
    {
        if (_editorSurfaceHost is null)
            return;

        _editorSurfaceHostController.ClearHost(_editorSurfaceHost);
    }
}
