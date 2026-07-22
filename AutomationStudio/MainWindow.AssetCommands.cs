using System.Windows;
using System.IO;
using AutomationStudioWpf.Graph;
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
        var controller = _activeEditorSession is null
            ? _activeAssetController
            : GetSessionActiveAssetController(_activeEditorSession);
        if (controller is null || controller.ActiveItem is null)
        {
            SetStatus("没有可导出的当前图表。");
            return;
        }
        controller.SaveAs();
    }

    private void OpenGraph_Click(object sender, RoutedEventArgs e)
    {
        ImportExternalGraph();
    }

    private void ImportExternalGraph()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "外部导入",
            Filter = "图谱文件 (*.json)|*.json|所有文件(*.*)|*.*",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var graph = GraphListController.ReadGraphFile(dialog.FileName);
            if (_activeContentAsset is null)
            {
                CreateAssetFromImportedGraph(graph, dialog.FileName);
                return;
            }
            if (_activeContentAsset.Kind == ContentAssetKind.FunctionLibrary && graph.AssetKind != GraphAssetKind.Function)
                throw new InvalidOperationException("函数库只能导入函数图。事件图请导入脚本资产。");
            if (_activeContentAsset.Kind is not (ContentAssetKind.Script or ContentAssetKind.FunctionLibrary))
                throw new InvalidOperationException("请选择脚本或函数库资产后再导入图表。");

            OpenOrActivateAsset(_activeContentAsset);
            var controller = graph.AssetKind == GraphAssetKind.Function ? _functionListController : _graphListController;
            controller.ImportGraph(graph, dialog.FileName);
        }
        catch (Exception ex)
        {
            ThemedDialog.Show(this, ex.Message, "导入失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CreateAssetFromImportedGraph(GraphFileModel graph, string sourcePath)
    {
        ContentAssetKind assetKind = graph.AssetKind == GraphAssetKind.Function
            ? ContentAssetKind.FunctionLibrary
            : ContentAssetKind.Script;
        string baseName = Path.GetFileNameWithoutExtension(sourcePath);
        var asset = CreateContentAsset(assetKind, CreateUniqueContentName(baseName, _currentContentFolderId));
        asset.ParentFolderId = _currentContentFolderId;
        var item = new GraphListItemViewModel
        {
            Kind = graph.AssetKind,
            Name = string.IsNullOrWhiteSpace(graph.Name) ? baseName : graph.Name,
            Graph = graph,
            EntryRole = GraphEntryRole.MainEvent,
            IsDirty = true,
            IsCompileDirty = true,
        };
        if (graph.AssetKind == GraphAssetKind.EventGraph)
        {
            asset.EventGraphs.Clear();
            asset.EventGraphs.Add(item);
        }
        else
        {
            asset.Functions.Add(item);
        }
        GraphStructureNormalizer.NormalizeContentAsset(asset);
        ContentBrowserItems.Add(asset);
        RefreshContentBrowserViews();
        PersistAssetLibrary();
        OpenOrActivateAsset(asset, item, graph.AssetKind);
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
        await RunActiveScriptFromToolbarAsync();
    }

    private async Task RunActiveScriptFromToolbarAsync()
    {
        var startResult = await _scriptExecutionCoordinator.RunActiveAsync();
        if (startResult == ScriptExecutionStartResult.NoScriptAsset)
        {
            ThemedDialog.Show(this, "只有脚本资产可以执行。", "不能执行", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
