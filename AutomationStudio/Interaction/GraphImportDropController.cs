using System.IO;
using System.Windows;
using WpfDataFormats = System.Windows.DataFormats;
using WpfDragEventArgs = System.Windows.DragEventArgs;
using WpfDragDropEffects = System.Windows.DragDropEffects;
using WpfMessageBox = System.Windows.MessageBox;

namespace AutomationStudioWpf.Interaction;

public sealed class GraphImportDropController
{
    private readonly Window _owner;
    private readonly GraphListController _graphListController;

    public GraphImportDropController(Window owner, GraphListController graphListController)
    {
        _owner = owner;
        _graphListController = graphListController;
    }

    public void HandleDragEnter(WpfDragEventArgs e)
    {
        if (TryGetSingleJsonPath(e, out _))
        {
            e.Effects = WpfDragDropEffects.Copy;
            return;
        }

        e.Effects = WpfDragDropEffects.None;
    }

    public void HandleDrop(WpfDragEventArgs e)
    {
        if (!TryGetSingleJsonPath(e, out var filePath)) return;

        var result = WpfMessageBox.Show(
            _owner,
            $"是否导入图谱？\n\n{Path.GetFileName(filePath)}",
            "导入图谱",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            _graphListController.ImportFile(filePath);
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show(_owner, ex.Message, "导入失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static bool TryGetSingleJsonPath(WpfDragEventArgs e, out string filePath)
    {
        filePath = string.Empty;
        if (!e.Data.GetDataPresent(WpfDataFormats.FileDrop)) return false;

        var files = (string[])e.Data.GetData(WpfDataFormats.FileDrop);
        if (files.Length != 1) return false;
        if (!Path.GetExtension(files[0]).Equals(".json", StringComparison.OrdinalIgnoreCase)) return false;

        filePath = files[0];
        return true;
    }
}
