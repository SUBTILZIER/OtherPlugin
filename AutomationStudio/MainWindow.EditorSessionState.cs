using System.Windows;
using AutomationStudioWpf.Graph;
using AutomationStudioWpf.GraphCore;
using AutomationStudioWpf.Interaction;
using AutomationStudioWpf.Logging;
using AutomationStudioWpf.Services;

namespace AutomationStudioWpf;

public partial class MainWindow
{
    private void OnGraphChanged()
    {
        _editorService.UpdatePinConnectionStates();
        var session = GetOperationEditorSession();
        var controller = session is null ? null : GetSessionActiveAssetController(session);
        if (!_suppressGraphChangedDirty && controller?.IsLoadingGraph != true)
            MarkActiveAssetDirty();

        if (_inspectorController.IsUserInteractionActive)
            return;

        if (_editorService.Nodes.FirstOrDefault(n => n.IsSelected) is { } selected)
        {
            LoadNodeToInspector(selected);
        }
        else
        {
            LoadNodeToInspector(null);
        }
    }

    private void MarkActiveAssetDirty()
    {
        if (GetOperationEditorSession() is { } session)
            MarkSessionDirty(session);
    }

    private void MarkActiveAssetLayoutDirty()
    {
        if (GetOperationEditorSession() is { } session)
            MarkSessionLayoutDirty(session);
    }

    private void SnapshotActiveAsset()
    {
        if (GetOperationEditorSession() is { } session)
            SnapshotSession(session);
    }

    private EditorSessionViewModel? GetOperationEditorSession() =>
        _eventSurfaceSessionOverride ?? _activeEditorSession;

    private void MarkSessionDirty(EditorSessionViewModel session)
    {
        var controller = GetSessionActiveAssetController(session);
        controller?.MarkLogicDirty();
        session.ContentAsset.IsDirty = true;
        session.RefreshDirtyState();
        if (ReferenceEquals(session, _activeEditorSession))
            UpdateGraphSectionVisibility();
        UpdateEditorSessionChrome();
    }

    private void MarkSessionLayoutDirty(EditorSessionViewModel session)
    {
        var controller = GetSessionActiveAssetController(session);
        controller?.MarkLayoutDirty();
        session.ContentAsset.IsDirty = true;
        session.RefreshDirtyState();
        if (ReferenceEquals(session, _activeEditorSession))
            UpdateGraphSectionVisibility();
        UpdateEditorSessionChrome();
    }

    private void SnapshotSession(EditorSessionViewModel session)
    {
        var controller = GetSessionActiveAssetController(session);
        controller?.SnapshotActive();
        session.RememberActive(controller);
        session.RefreshDirtyState();
    }

    private GraphListController? GetSessionActiveAssetController(EditorSessionViewModel session)
    {
        if (session.SurfaceContext is { IsConfigured: true } context)
        {
            if (context.ActiveAssetController is { } controller)
                return controller;

            if (session.ActiveGraphKind is GraphAssetKind rememberedKind)
            {
                var rememberedController = rememberedKind == GraphAssetKind.Function
                    ? context.FunctionListController
                    : context.GraphListController;
                if (rememberedController.ActiveItem is not null)
                    return rememberedController;
            }

            if (session.ContentAsset.Kind == ContentAssetKind.FunctionLibrary)
            {
                if (context.FunctionListController.ActiveItem is not null)
                    return context.FunctionListController;
                if (context.GraphListController.ActiveItem is not null)
                    return context.GraphListController;
            }
            else
            {
                if (context.GraphListController.ActiveItem is not null)
                    return context.GraphListController;
                if (context.FunctionListController.ActiveItem is not null)
                    return context.FunctionListController;
            }
        }

        if (ReferenceEquals(session, _activeEditorSession))
            return _activeAssetController;
        return null;
    }

    private void SetSessionActiveGraphController(EditorSessionViewModel? session, GraphListController? controller, bool remember = true)
    {
        if (session is not null)
        {
            if (session.SurfaceContext is { } context)
            {
                // The two list boxes have independent SelectedItem state. Keep only
                // the controller that owns the active graph selected.
                if (controller is null)
                {
                    context.GraphListController.ClearActive();
                    context.FunctionListController.ClearActive();
                }
                else if (ReferenceEquals(controller, context.GraphListController))
                {
                    context.FunctionListController.ClearActive();
                }
                else if (ReferenceEquals(controller, context.FunctionListController))
                {
                    context.GraphListController.ClearActive();
                }

                context.ActiveAssetController = controller;
            }

            if (remember)
                session.RememberActive(controller);
        }

        if (session is null || ReferenceEquals(session, _activeEditorSession) || ReferenceEquals(session, _eventSurfaceSessionOverride))
            _activeAssetController = controller;
    }

    private void PersistAssetLibrary()
    {
        TryPersistAssetLibrary(showPrompt: false);
    }

    private bool TryPersistAssetLibrary(bool showPrompt)
    {
        if (_workspacePersistenceService.TryPersist(out var error))
            return true;

        if (error is not null)
        {
            Logger.Error($"保存资产库失败：{error.Message}");
            SetStatus("保存资产库失败，未清除脏标记。");
            if (showPrompt)
                ThemedDialog.Show(this, error.Message, "保存失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        return false;
    }

    private bool SaveAllAssets()
    {
        CommitAllSessionsToAssets(applyInspectorForActive: true);
        if (!TryPersistAssetLibrary(showPrompt: true))
            return false;

        foreach (var item in ContentBrowserItems
                     .Where(asset => asset.Kind != ContentAssetKind.Folder)
                     .SelectMany(asset => asset.EventGraphs.Concat(asset.Functions))
                     .Concat(GraphListItems)
                     .Concat(FunctionListItems))
        {
            item.IsDirty = false;
        }

        foreach (var item in ContentBrowserItems)
            item.IsDirty = false;
        foreach (var session in _editorSessions)
            SyncSessionGraphStateFromAsset(session);
        UpdateGraphSectionVisibility();
        UpdateEditorSessionChrome();
        SetStatus($"已保存全部内容资产：{ContentBrowserItems.Count} 个。");
        return true;
    }

    private bool EnsureCompiledBeforeSave()
    {
        CommitInspectorAndSnapshotAllSessions();
        if (!HasCompileDirtyAssets())
            return true;

        var result = ThemedDialog.ShowCustom(
            this,
            "存在未编译修改，是否先编译再保存？",
            "需要编译",
            MessageBoxImage.Question,
            new ThemedDialogButton("先编译", MessageBoxResult.Yes, true),
            new ThemedDialogButton("直接保存", MessageBoxResult.No),
            new ThemedDialogButton("取消", MessageBoxResult.Cancel));
        if (result == MessageBoxResult.Cancel)
            return false;
        if (result == MessageBoxResult.Yes)
            return CompileAllAssets(showPrompt: false);
        return true;
    }

    private GraphWorkspaceReadModel? PrepareActiveScriptForRun(ContentAssetViewModel asset)
    {
        CommitInspectorAndSnapshotAllSessions();
        if (!HasCompileDirtyAssets())
        {
            GraphWorkspaceReadModel cleanReadModel = BuildGraphWorkspaceReadModel();
            return cleanReadModel.DependencyIndex.FindAsset(asset.Id) is null ? null : cleanReadModel;
        }

        SetStatus("执行前检测到未编译修改，正在自动编译...");
        return CompileAllAssets(showPrompt: true, out GraphWorkspaceReadModel? compiledReadModel)
            ? compiledReadModel
            : null;
    }

    private bool CompileActiveAsset(bool showPrompt)
    {
        var targetSession = _activeEditorSession;
        var targetAsset = _assetCompileCoordinator.ResolveActiveAsset();
        if (targetAsset is null || targetAsset.Kind == ContentAssetKind.Folder)
        {
            if (showPrompt)
                ThemedDialog.Show(this, "没有打开可编译的资产。", "无法编译", MessageBoxButton.OK, MessageBoxImage.Warning);
            SetStatus("编译失败：没有打开可编译的资产。");
            return false;
        }

        var targetContext = targetSession?.SurfaceContext;
        var targetController = targetSession is null ? _activeAssetController : GetSessionActiveAssetController(targetSession);
        var targetCommandService = targetContext?.CommandService ?? _graphCommandService;

        var result = _assetCompileCoordinator.CompileAsset(targetAsset, commitSessions: true);
        foreach (var item in ContentBrowserItems.Where(item => result.ChangedAssetIds.Contains(item.Id)))
            item.IsDirty = true;
        SyncAffectedEditorSessions(result);

        if (!HandleCompileResult(result, showPrompt))
            return false;

        if (targetController?.ActiveItem is { } active)
        {
            if (targetSession is not null)
            {
                RunWithSurfaceContext(targetSession, () =>
                {
                    RunWithoutAutoFitOnGraphLoad(() => targetController.ReloadItemWithoutPersist(active));
                    targetCommandService.Clear();
                });
            }
            else
            {
                RunWithoutAutoFitOnGraphLoad(() => targetController.ReloadItemWithoutPersist(active));
                targetCommandService.Clear();
            }
        }

        targetSession?.RefreshDirtyState();
        PersistAssetLibrary();
        if (targetSession is null || ReferenceEquals(targetSession, _activeEditorSession))
            UpdateGraphSectionVisibility();
        UpdateEditorSessionChrome();
        SetStatus($"编译完成：{targetAsset.Name}，同步 {result.UpdatedCallNodes} 个调用节点，移除 {result.RemovedConnections} 条无效连线。");
        return true;
    }

    private bool CompileAllAssets(bool showPrompt) =>
        CompileAllAssets(showPrompt, out _);

    private bool CompileAllAssets(bool showPrompt, out GraphWorkspaceReadModel? readModel)
    {
        var result = _assetCompileCoordinator.CompileAll(commitSessions: false);
        readModel = result.ReadModel;
        foreach (var item in ContentBrowserItems.Where(item => result.ChangedAssetIds.Contains(item.Id)))
            item.IsDirty = true;

        if (!HandleCompileResult(result, showPrompt))
            return false;

        foreach (var session in _editorSessions)
            SyncSessionGraphStateFromAsset(session);

        if (_activeAssetController?.ActiveItem is { } active)
        {
            RunWithoutAutoFitOnGraphLoad(() => _activeAssetController.ReloadItemWithoutPersist(active));
            _graphCommandService.Clear();
        }
        PersistAssetLibrary();
        UpdateGraphSectionVisibility();
        UpdateEditorSessionChrome();
        SetStatus($"编译完成：同步 {result.UpdatedCallNodes} 个调用节点，移除 {result.RemovedConnections} 条无效连线。");
        return result.Success;
    }

    private bool HandleCompileResult(GraphCompileResult result, bool showPrompt)
    {
        foreach (string repairMessage in result.RepairMessages)
            Logger.Info($"编译修复：{repairMessage}");

        if (result.Success)
            return true;

        foreach (var issue in result.Issues)
        {
            if (issue.Severity == GraphValidationSeverity.Error)
                Logger.Error($"编译：{issue.Message}");
            else
                Logger.Warn($"编译：{issue.Message}");
        }

        string message = string.Join(Environment.NewLine, result.Issues
            .Where(issue => issue.Severity == GraphValidationSeverity.Error)
            .Take(6)
            .Select(issue => issue.Message));
        if (showPrompt && !string.IsNullOrWhiteSpace(message))
            ThemedDialog.Show(this, message, "编译失败", MessageBoxButton.OK, MessageBoxImage.Error);
        SetStatus($"编译失败：{result.Issues.Count(issue => issue.Severity == GraphValidationSeverity.Error)} 个错误。");
        UpdateGraphSectionVisibility();
        return false;
    }

    private static void SyncSessionGraphStateFromAsset(EditorSessionViewModel session)
    {
        SyncGraphItems(session.GraphListItems, session.ContentAsset.EventGraphs);
        SyncGraphItems(session.FunctionListItems, session.ContentAsset.Functions);
        session.RefreshDirtyState();
    }

    private void SyncAffectedEditorSessions(GraphCompileResult result)
    {
        var affectedAssetIds = new HashSet<string>(result.AffectedAssetIds, StringComparer.Ordinal);
        affectedAssetIds.UnionWith(result.ChangedAssetIds);
        foreach (EditorSessionViewModel session in _editorSessions.Where(
                     session => affectedAssetIds.Contains(session.ContentAsset.Id)))
        {
            SyncSessionGraphStateFromAsset(session);
        }
    }

    private static void SyncGraphItems(
        IEnumerable<GraphListItemViewModel> sessionItems,
        IEnumerable<GraphListItemViewModel> assetItems)
    {
        var assetById = assetItems
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        foreach (var sessionItem in sessionItems)
        {
            if (!assetById.TryGetValue(sessionItem.Id, out var assetItem))
                continue;

            sessionItem.Graph = assetItem.Graph;
            sessionItem.IsDirty = assetItem.IsDirty;
            sessionItem.IsCompileDirty = assetItem.IsCompileDirty;
            sessionItem.IsPublicToLibrary = assetItem.IsPublicToLibrary;
            sessionItem.Name = assetItem.Name;
        }
    }

    private bool HasCompileDirtyAssets()
    {
        return ContentBrowserItems
            .Where(asset => asset.Kind != ContentAssetKind.Folder)
            .SelectMany(asset => asset.EventGraphs.Concat(asset.Functions))
            .Concat(GraphListItems)
            .Concat(FunctionListItems)
            .Any(item => item.IsCompileDirty);
    }

    private void UpdateCompileButtonState()
    {
        QueueAssetCompileButtonStateUpdate();
    }
}
