using System.Windows;
using AutomationStudioWpf.Services;
using WpfMessageBox = System.Windows.MessageBox;

namespace AutomationStudioWpf;

public partial class MainWindow
{
    private void NewGraph_Click(object sender, RoutedEventArgs e)
    {
        if (_activeContentAsset is null)
        {
            var asset = CreateContentAsset(ContentAssetKind.Script, CreateUniqueContentName("新脚本", _currentContentFolderId));
            asset.ParentFolderId = _currentContentFolderId;
            ContentBrowserItems.Add(asset);
            OpenContentAsset(asset);
        }
        else if (_activeContentAsset.Kind == ContentAssetKind.Script)
        {
            AddGraphListItem_Click(sender, e);
        }
    }

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

    private async void RunGraph_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureCompiledBeforeRun())
            return;

        var activeSessionController = _activeEditorSession is null
            ? _activeAssetController
            : GetSessionActiveAssetController(_activeEditorSession);
        if (_activeContentAsset?.Kind != ContentAssetKind.Script || !ReferenceEquals(activeSessionController, _graphListController))
        {
            WpfMessageBox.Show(this, "只有脚本里的事件图可以直接执行。请从内容浏览器打开脚本，并进入事件图。", "不能执行", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        CommitInspectorAndSnapshotAllSessions();
        await _executionController.RunAsync();
    }
}
