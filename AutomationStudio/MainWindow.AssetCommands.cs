using System.Windows;
using AutomationStudioWpf.Interaction;
using AutomationStudioWpf.Services;

namespace AutomationStudioWpf;

public partial class MainWindow
{
    private void SaveGraph_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureCompiledBeforeSave())
            return;

        SaveAllAssets();
    }

    private void SaveGraphAs_Click(object sender, RoutedEventArgs e)
    {
        _graphListController.SaveAs();
    }

    private void OpenGraph_Click(object sender, RoutedEventArgs e)
    {
        _graphListController.ImportFromDialog();
    }

    private void CompileGraph_Click(object sender, RoutedEventArgs e)
    {
        CompileActiveAsset(showPrompt: false);
    }

    private void MousePick_Click(object sender, RoutedEventArgs e)
    {
        _mousePickController.Toggle();
    }

    private void ShowFinalCode_Click(object sender, RoutedEventArgs e)
    {
        ShowFinalCodePreview();
    }

    private async void RunGraph_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureCompiledBeforeRun())
            return;

        var activeSessionController = _activeEditorSession is null
            ? _activeAssetController
            : GetSessionActiveAssetController(_activeEditorSession);
        if (_activeContentAsset?.Kind != ContentAssetKind.Script || !ReferenceEquals(activeSessionController, _graphListController))
        {
            ThemedDialog.Show(this, "只有脚本里的事件图可以直接执行。请从内容浏览器打开脚本，并进入事件图。", "不能执行", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        CommitInspectorAndSnapshotAllSessions();
        await _executionController.RunAsync();
    }
}
