using System.ComponentModel;
using System.Windows;
using WpfMessageBox = System.Windows.MessageBox;

namespace AutomationStudioWpf;

public partial class MainWindow
{
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _mousePickController.Stop();
        _executionController.ReleaseAllKeys();
        if (_isClosing) return;

        CommitAllSessionsToAssets(applyInspectorForActive: true);
        if (ContentBrowserItems.Any(item => item.IsDirty) ||
            GraphListItems.Concat(FunctionListItems).Any(item => item.IsDirty))
        {
            var result = WpfMessageBox.Show(
                this,
                "存在未保存资产，是否保存？",
                "是否保存",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);
            if (result == MessageBoxResult.Cancel)
            {
                e.Cancel = true;
                return;
            }

            if (result == MessageBoxResult.Yes)
                SaveAllAssets();
        }

        if (e.Cancel)
            return;

        _isClosing = true;
        _finalCodePreviewWindow?.Close();
        _finalCodePreviewWindow = null;
        foreach (var session in _editorSessions.ToList())
        {
            session.DetachedWindow?.CloseFromOwner();
            session.DetachedWindow = null;
        }

        _mousePickController.Dispose();
    }
}
