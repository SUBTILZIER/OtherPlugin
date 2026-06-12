using System.Windows;
using DragEventArgs = System.Windows.DragEventArgs;

namespace AutomationStudioWpf;

public partial class MainWindow
{
    private void RefreshLogList()
    {
        _logPanelController.Refresh();
    }

    private void FilterRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (_logPanelController is null)
            return;

        _logPanelController.ApplyFilterFromUi();
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e)
    {
        _logPanelController.Clear();
    }

    private void Window_DragEnter(object sender, DragEventArgs e)
    {
        _graphImportDropController.HandleDragEnter(e);
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        _graphImportDropController.HandleDrop(e);
    }
}
