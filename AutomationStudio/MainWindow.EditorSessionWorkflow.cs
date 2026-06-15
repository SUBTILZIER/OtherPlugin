using System.Collections.ObjectModel;
using System.Windows;
using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Interaction;
using AutomationStudioWpf.Services;

namespace AutomationStudioWpf;

public partial class MainWindow
{
    private void LoadGraphLibrary()
    {
        CloseAllEditorSessions();
        ContentBrowserItems.Clear();
        foreach (var item in _graphLibraryService.LoadContentLibrary())
            ContentBrowserItems.Add(item);

        _currentContentFolderId = null;
        RefreshContentBrowserViews();
        if (_editorSessions.Count == 0)
            ClearEditorSurface();
    }

    private void OpenOrActivateAsset(ContentAssetViewModel asset, GraphListItemViewModel? targetGraph = null, GraphAssetKind? targetKind = null)
    {
        var session = _editorSessions.FirstOrDefault(item => string.Equals(item.ContentAsset.Id, asset.Id, StringComparison.Ordinal))
            ?? CreateEditorSession(asset);

        if (!_editorSessions.Contains(session))
            AddEditorSession(session);

        if (session.DockMode == EditorDockMode.Detached)
            ActivateEditorSession(session, targetGraph, targetKind);
        else
            ActivateEditorSessionFromMainTab(session, targetGraph, targetKind);
    }

    private EditorSessionViewModel CreateEditorSession(ContentAssetViewModel asset)
    {
        var session = new EditorSessionViewModel(asset);
        session.Left = 36 + _editorSessions.Count * 28;
        session.Top = 28 + _editorSessions.Count * 24;
        return session;
    }

    private void AddEditorSession(EditorSessionViewModel session)
    {
        _editorSessions.Add(session);
        session.PropertyChanged += EditorSession_PropertyChanged;
        RefreshMainEditorSessions();
    }

    private void RemoveEditorSession(EditorSessionViewModel session)
    {
        session.PropertyChanged -= EditorSession_PropertyChanged;
        _editorSessions.Remove(session);
        _mainEditorSessions.Remove(session);
        RefreshMainEditorSessions();
    }

    private void EditorSession_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not EditorSessionViewModel session)
            return;

        if (e.PropertyName is nameof(EditorSessionViewModel.DockMode))
            RefreshMainEditorSessions();

        if (e.PropertyName is nameof(EditorSessionViewModel.DisplayTitle)
            or nameof(EditorSessionViewModel.IsDirty)
            or nameof(EditorSessionViewModel.IsActive))
        {
            session.DetachedWindow?.RefreshChrome();
        }
    }

    private void ActivateEditorSession(
        EditorSessionViewModel session,
        GraphListItemViewModel? targetGraph = null,
        GraphAssetKind? targetKind = null,
        bool showInHost = true)
    {
        if (_executionController?.IsRunning == true)
        {
            SetStatus("执行中，不能切换编辑窗口。");
            return;
        }

        CommitCurrentSessionToAsset();

        if (_activeEditorSession is not null)
            _activeEditorSession.IsActive = false;

        _activeEditorSession = session;
        _activeEditorSession.IsActive = true;
        _activeContentAsset = session.ContentAsset;
        _editorService = session.EditorService;
        _nodeFactory = session.NodeFactory;
        if (showInHost)
            ShowEditorSurfaceForSession(session);
        ContentBrowserListBox.SelectedItem = session.ContentAsset;

        AttachActiveEditorService(_editorService);
        RaiseEditorBindingProperties();
        RebuildEditorControllers();
        AttachGraphCollectionChangeHandlers();

        _graphListController.ClearActive();
        _functionListController.ClearActive();
        SetSessionActiveGraphController(session, null, remember: false);

        _graphListController.LoadSectionExpansion(session.ContentAsset.EventGraphSectionExpanded, session.ContentAsset.EventGraphSectionHasState);
        _functionListController.LoadSectionExpansion(session.ContentAsset.FunctionSectionExpanded, session.ContentAsset.FunctionSectionHasState);

        ApplyEditorModeForContent(session.ContentAsset);
        LoadTargetOrRememberedGraphForSession(session, targetGraph, targetKind);
        session.RefreshDirtyState();
        UpdateEditorSessionChrome();
        SetStatus($"已打开：{session.ContentAsset.Name}");
    }

    private void LoadTargetOrRememberedGraphForSession(EditorSessionViewModel session, GraphListItemViewModel? targetGraph, GraphAssetKind? targetKind)
    {
        if (targetGraph is not null && targetKind is not null)
        {
            LoadGraphItem(GetControllerForKind(targetKind.Value), targetGraph, snapshotCurrent: false);
            return;
        }

        if (!string.IsNullOrWhiteSpace(session.ActiveGraphItemId) && session.ActiveGraphKind is { } rememberedKind)
        {
            var remembered = GetCollectionForKind(session, rememberedKind)
                .FirstOrDefault(item => string.Equals(item.Id, session.ActiveGraphItemId, StringComparison.Ordinal));
            if (remembered is not null)
            {
                LoadGraphItem(GetControllerForKind(rememberedKind), remembered, snapshotCurrent: false);
                return;
            }
        }

        LoadFirstGraphForContent(session.ContentAsset);
    }

    private GraphListController GetControllerForKind(GraphAssetKind kind) => kind switch
    {
        GraphAssetKind.Function => _functionListController,
        _ => _graphListController,
    };

    private static ObservableCollection<GraphListItemViewModel> GetCollectionForKind(EditorSessionViewModel session, GraphAssetKind kind) => kind switch
    {
        GraphAssetKind.Function => session.FunctionListItems,
        _ => session.GraphListItems,
    };

    private void CommitCurrentSessionToAsset()
    {
        if (_activeEditorSession is null)
            return;

        CommitSessionToAsset(_activeEditorSession, applyInspector: true);
    }

    private void CommitAllSessionsToAssets(bool applyInspectorForActive = false)
    {
        foreach (var session in _editorSessions)
            CommitSessionToAsset(session, applyInspectorForActive && ReferenceEquals(session, _activeEditorSession));
    }

    private void CommitSessionToAsset(EditorSessionViewModel session, bool applyInspector = false)
    {
        if (applyInspector && ReferenceEquals(session, _activeEditorSession))
            ApplyInspectorChanges();
        SnapshotSession(session);
        session.SaveToAsset();
    }

    private void ApplyEditorModeForContent(ContentAssetViewModel asset)
    {
        EmptyEditorPanel.Visibility = Visibility.Collapsed;
        EnsureEditorSurfaceHost();
        UpdateGraphSectionVisibility();
    }

    private void LoadFirstGraphForContent(ContentAssetViewModel asset)
    {
        if (asset.Kind == ContentAssetKind.Script)
        {
            if (GraphListItems.Count > 0)
            {
                LoadGraphItem(_graphListController, GraphListItems[0], snapshotCurrent: false);
                return;
            }
        }
        else if (asset.Kind == ContentAssetKind.FunctionLibrary)
        {
            if (FunctionListItems.Count > 0)
            {
                LoadGraphItem(_functionListController, FunctionListItems[0], snapshotCurrent: false);
                return;
            }
        }
        SetSessionActiveGraphController(GetOperationEditorSession(), null, remember: false);
        _suppressGraphChangedDirty = true;
        try
        {
            _editorService.ClearGraph();
            _graphCommandService.Clear();
        }
        finally
        {
            _suppressGraphChangedDirty = false;
        }
        if (TryGetActiveEditorSurface() is { } surface)
            surface.InspectorHintTextBlock.Visibility = Visibility.Visible;
    }

    private void CloseActiveEditor()
    {
        if (_activeEditorSession is not null)
        {
            CloseEditorSession(_activeEditorSession);
            return;
        }

        ClearEditorSurface();
    }

    private void CloseEditorSession(EditorSessionViewModel session)
    {
        CommitSessionToAsset(session, applyInspector: ReferenceEquals(session, _activeEditorSession));

        if (ReferenceEquals(session, _activeEditorSession))
            HideMainEditorSurfaceHostOnly();
        session.DetachedWindow?.CloseFromOwner();
        session.DetachedWindow = null;
        RemoveEditorSession(session);

        if (ReferenceEquals(session, _activeEditorSession))
        {
            _activeEditorSession = null;
            _activeContentAsset = null;
            _activeAssetController = null;

            var next = _mainEditorSessions.LastOrDefault() ?? _editorSessions.LastOrDefault();
            if (next is not null)
            {
                ActivateEditorSession(next);
            }
            else
            {
                ClearEditorSurface();
            }
        }

        UpdateEditorSessionChrome();
        PersistAssetLibrary();
    }

    private void CloseAllEditorSessions()
    {
        foreach (var session in _editorSessions.ToList())
            CloseEditorSession(session);
    }

    private void CloseMainEditorSessions()
    {
        foreach (var session in _mainEditorSessions.ToList())
            CloseEditorSession(session);
    }

    private void CloseEditorSessionsToRight(EditorSessionViewModel session)
    {
        int index = _mainEditorSessions.IndexOf(session);
        if (index < 0)
            return;

        foreach (var rightSession in _mainEditorSessions.Skip(index + 1).ToList())
            CloseEditorSession(rightSession);
    }

    private void CloseEditorSessionsForAssetIds(ISet<string> assetIds)
    {
        foreach (var session in _editorSessions
                     .Where(item => assetIds.Contains(item.ContentAsset.Id))
                     .ToList())
        {
            CloseEditorSession(session);
        }
    }

    private void ClearEditorSurface()
    {
        _activeContentAsset = null;
        _activeEditorSession = null;
        _activeAssetController = null;
        _editorService.ClearGraph();
        _graphCommandService.Clear();
        RaiseEditorBindingProperties();
        HideEditorSurfaceHost();
        EmptyEditorPanel.Visibility = Visibility.Visible;
        UpdateEditorSessionChrome();
    }

    private void SaveVisibleGraphsToActiveContent()
    {
        var session = GetOperationEditorSession();
        if (session is null)
            return;

        SnapshotSession(session);
        session.SaveToAsset();
    }

    private void MarkCurrentContentDirty()
    {
        var session = GetOperationEditorSession();
        if (session is not null)
        {
            session.ContentAsset.IsDirty = true;
            session.RefreshDirtyState();
        }
        else if (_activeContentAsset is not null)
        {
            _activeContentAsset.IsDirty = true;
        }
        UpdateEditorSessionChrome();
    }

    private IEnumerable<CallableGraphItem> GetCallableFunctions()
    {
        CommitAllSessionsToAssets(applyInspectorForActive: true);
        return _callableGraphResolver.ResolveFunctions(ContentBrowserItems, _activeContentAsset);
    }

    private IEnumerable<CallableGraphItem> GetRuntimeCallableFunctions()
    {
        CommitAllSessionsToAssets(applyInspectorForActive: true);
        return _callableGraphResolver.ResolveFunctions(ContentBrowserItems, _activeContentAsset);
    }

    private IEnumerable<CallableCustomEventItem> GetCallableCustomEvents()
    {
        SnapshotActiveAsset();
        if (_activeContentAsset?.Kind != ContentAssetKind.Script ||
            !ReferenceEquals(_activeAssetController, _graphListController) ||
            _graphListController.ActiveItem?.Graph is not { } graph)
        {
            yield break;
        }

        foreach (var node in graph.Nodes.Where(node => node.NodeTypeKey == "custom_event"))
        {
            string id = string.IsNullOrWhiteSpace(node.CustomEventId) ? node.Id : node.CustomEventId!;
            string name = string.IsNullOrWhiteSpace(node.Title) ? "自定义事件" : node.Title;
            yield return new CallableCustomEventItem(
                id,
                name,
                "本脚本事件",
                node.Parameters.Select(CloneParameterFile).ToList());
        }
    }
}
