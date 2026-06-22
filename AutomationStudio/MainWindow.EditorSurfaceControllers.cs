using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AutomationStudioWpf.Controls;
using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Interaction;
using AutomationStudioWpf.Logging;
using AutomationStudioWpf.Services;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfSize = System.Windows.Size;

namespace AutomationStudioWpf;

public partial class MainWindow
{
    private void InitializeServices()
    {
        AttachActiveEditorService(_editorService);

        Logger.Entries.CollectionChanged += (_, e) => _logPanelController.HandleEntriesChanged(e);

        Closing += Window_Closing;
        PreviewMouseDown += Window_PreviewMouseDown;
    }

    private void AttachActiveEditorService(GraphEditorService editorService)
    {
        if (_attachedEditorService is not null)
        {
            _attachedEditorService.GraphChanged -= OnGraphChanged;
            _attachedEditorService.StatusChanged -= SetStatus;
            _attachedEditorService.GraphChanged -= NavigationFeatures_GraphChanged;
        }

        _attachedEditorService = editorService;
        _attachedEditorService.GraphChanged += OnGraphChanged;
        _attachedEditorService.StatusChanged += SetStatus;
        if (_navigationFeaturesInstalled)
            _attachedEditorService.GraphChanged += NavigationFeatures_GraphChanged;
    }

    private void InitializeControllers()
    {
        RebuildEditorControllers();
        _mousePickController = new MousePickController(this, SetStatus);

        _logPanelController = new LogPanelController(
            LogRichTextBox,
            FilterAllRadio,
            FilterInfoRadio,
            FilterWarnRadio,
            FilterErrorRadio);
    }

    private void RebuildEditorControllers()
    {
        if (_activeEditorSession?.SurfaceContext is { IsConfigured: true } activeContext)
        {
            _executionController = new ExecutionController(
                this,
                _editorService,
                new Runtime.GraphRuntimeExecutor(nodeRegistry: _nodeRegistry, adapters: new Adapters.RuntimeAdapters()),
                new GraphCore.GraphValidator(),
                RunGraphButton,
                GetRuntimeCallableFunctions,
                SetStatus);

            ApplyEditorSurfaceContext(activeContext);
            return;
        }

        _graphCommandService = GetOrCreateActiveCommandService();
        var surface = GetEditorSurfaceForControllerSetup();

        _executionController = new ExecutionController(
            this,
            _editorService,
            new Runtime.GraphRuntimeExecutor(nodeRegistry: _nodeRegistry, adapters: new Adapters.RuntimeAdapters()),
            new GraphCore.GraphValidator(),
            RunGraphButton,
            GetRuntimeCallableFunctions,
            SetStatus);

        _graphListController = new GraphListController(
            this,
            _editorService,
            _graphLibraryService,
            _nodeFactory,
            surface.GraphListBox,
            GraphListItems,
            SyncNodeFactorySequence,
            PersistAssetLibrary,
            GraphAssetKind.EventGraph,
            "事件图",
            "事件图",
            SetStatus);

        _functionListController = new GraphListController(
            this,
            _editorService,
            _graphLibraryService,
            _nodeFactory,
            surface.FunctionListBox,
            FunctionListItems,
            SyncNodeFactorySequence,
            PersistAssetLibrary,
            GraphAssetKind.Function,
            "函数",
            "新函数_",
            SetStatus);

        RebuildInteractionControllers();
    }

    private GraphCommandService GetOrCreateActiveCommandService()
    {
        if (_activeEditorSession is null)
        {
            return new GraphCommandService(
                _editorService,
                () => GetActiveGraphKind() ?? GraphAssetKind.EventGraph,
                SyncNodeFactorySequence,
                SetStatus);
        }

        var session = _activeEditorSession;
        return session.CommandService ??= new GraphCommandService(
            session.EditorService,
            () => GetActiveGraphKind() ?? session.ActiveGraphKind ?? GraphAssetKind.EventGraph,
            SyncNodeFactorySequence,
            SetStatus);
    }

    private void ApplyEditorSurfaceContext(EditorSurfaceContext context)
    {
        _graphCommandService = context.CommandService;
        _graphListController = context.GraphListController;
        _functionListController = context.FunctionListController;
        _activeAssetController = context.ActiveAssetController;
        _canvasPanZoomController = context.CanvasPanZoomController;
        _nodeDragSelectionController = context.NodeDragSelectionController;
        _inspectorController = context.InspectorController;
        _pinConnectionController = context.PinConnectionController;
        _nodePaletteController = context.NodePaletteController;
        _graphImportDropController = context.GraphImportDropController;
    }

    private void RebuildInteractionControllers()
    {
        var surface = GetEditorSurfaceForControllerSetup();

        _nodePaletteController = new NodePaletteController(
            surface.NodePalette,
            surface.NodePaletteSearchBox,
            surface.NodePaletteContent,
            _nodeFactory,
            _editorService,
            _graphCommandService,
            _nodeRegistry,
            GetCallableFunctions,
            GetCallableCustomEvents,
            GetActiveGraphKind,
            SnapshotActiveAsset,
            ViewportToGraph,
            () => new WpfSize(surface.GraphViewport.ActualWidth, surface.GraphViewport.ActualHeight),
            SelectNode,
            node => _pinConnectionController.TryAutoConnectNewNode(node));

        _canvasPanZoomController = new CanvasPanZoomController(
            surface.GraphViewport,
            surface.GraphZoomTransform,
            surface.GraphPanTransform,
            _editorService,
            SetStatus);

        _nodeDragSelectionController = new NodeDragSelectionController(
            _editorService,
            _graphCommandService,
            _clipboardService,
            _nodeFactory,
            surface.GraphViewport,
            surface.SelectionRectangle,
            ViewportToGraph,
            LoadNodeToInspector,
            FitGraphToView,
            EnsureCanvasLargeEnough,
            MarkActiveAssetLayoutDirty,
            () => _pinConnectionController?.ClearSelectedConnectionPath(),
            SetStatus);

        _inspectorController = new InspectorController(
            this,
            _editorService,
            new Adapters.Win32WindowAdapter(),
            MarkActiveAssetDirty,
            SetStatus,
            surface.InspectorHintTextBlock,
            surface.NodeTitleTextBox,
            surface.NodeNumberTextBlock,
            surface.ParameterInspectorPanel,
            surface.AddParameterButton,
            surface.ParameterInspectorTitle,
            surface.ParameterRowsPanel,
            surface.FindImageInspectorPanel,
            surface.FindImageSourceModeComboBox,
            surface.FindImageSourcePathLabel,
            surface.FindImageSourcePathPanel,
            surface.FindImageSourcePathTextBox,
            surface.FindImagePathTextBox,
            surface.FindImageThresholdTextBox,
            surface.FindImageUseRegionCheckBox,
            surface.FindImageRegionXTextBox,
            surface.FindImageRegionYTextBox,
            surface.FindImageRegionWidthTextBox,
            surface.FindImageRegionHeightTextBox,
            surface.MouseLeftInspectorPanel,
            surface.MousePositionXTextBox,
            surface.MousePositionYTextBox,
            surface.MouseClickOperationModeComboBox,
            surface.MouseButtonComboBox,
            surface.KeyboardInspectorPanel,
            surface.KeyboardKeyComboBox,
            surface.KeyboardOperationModeComboBox,
            surface.ScrollWheelInspectorPanel,
            surface.ScrollWheelActionComboBox,
            surface.ScrollWheelSpeedTextBox,
            surface.ScrollWheelIntervalTextBox,
            surface.ScrollWheelDurationTextBox,
            surface.IfInspectorPanel,
            surface.IfConditionComboBox,
            surface.ForLoopInspectorPanel,
            surface.ForLoopCountTextBox,
            surface.ForLoopEndConditionComboBox,
            surface.WhileLoopInspectorPanel,
            surface.WhileLoopConditionComboBox,
            surface.WhileLoopModeComboBox,
            surface.WhileMaxIterationsLabel,
            surface.WhileMaxIterationsTextBox,
            surface.DelayInspectorPanel,
            surface.DelayMsTextBox,
            surface.MouseMoveInspectorPanel,
            surface.MouseMovePositionXTextBox,
            surface.MouseMovePositionYTextBox,
            surface.StartProgramInspectorPanel,
            surface.StartProgramPathTextBox,
            surface.StartProgramWaitTimeoutTextBox,
            surface.StartProgramFailureActionComboBox,
            surface.StartProgramRetryCountTextBox,
            surface.PrintLogInspectorPanel,
            surface.PrintLogMessageTextBox,
            surface.SelectWindowInspectorPanel,
            surface.SelectWindowInputModeComboBox,
            surface.SelectWindowManualPanel,
            surface.SelectWindowProcessNameTextBox,
            surface.SelectWindowAutoPanel,
            surface.SelectWindowAutoComboBox,
            surface.ToDoInspectorPanel,
            surface.ToDoSearchBox,
            surface.ToDoTargetListBox,
            surface.ToDoTargetTitleTextBox,
            surface.ToDoTargetNumberTextBox,
            surface.ToDoReturnAfterTargetCheckBox,
            surface.CommonInspectorPanel,
            surface.CommonKeyChordAddPanel,
            surface.CommonKeyChordKeyComboBox,
            surface.CommonModePanel,
            surface.CommonModeComboBox,
            surface.CommonWindowPickerPanel,
            surface.CommonWindowComboBox,
            surface.CommonEnumPanel,
            surface.CommonEnumLabel,
            surface.CommonEnumComboBox,
            surface.CommonBrowseFileButton,
            surface.CommonTextLabel,
            surface.CommonTextBox,
            surface.CommonText2Label,
            surface.CommonText2Box,
            surface.CommonText3Label,
            surface.CommonText3Box,
            surface.CommonNumberLabel,
            surface.CommonNumberBox,
            surface.CommonNumber2Label,
            surface.CommonNumber2Box,
            surface.CommonNumber3Label,
            surface.CommonNumber3Box,
            surface.CommonNumber4Label,
            surface.CommonNumber4Box,
            surface.CommonFlagCheckBox,
            surface.CommonHelpTextBlock);

        _pinConnectionController = new PinConnectionController(
            _editorService,
            _graphCommandService,
            _nodeFactory,
            surface.GraphViewport,
            surface.PreviewConnectionPath,
            ViewportToGraph,
            position => TryGetPinAtPosition(position, out var pin) ? pin : null,
            OpenNodePaletteForConnection,
            SelectNode,
            ClearNodeSelectionForConnection,
            _nodeDragSelectionController.SetCanvasFocusActive,
            SetStatus);

        _graphImportDropController = new GraphImportDropController(this, _graphListController);
    }

    private EditorSurfaceControl? TryGetActiveEditorSurface()
    {
        if (_eventSurfaceSessionOverride?.Surface is { } overrideSurface)
            return overrideSurface;

        if (_activeEditorSession?.Surface is { } activeSurface)
            return activeSurface;

        if (_lastMainEditorSession?.Surface is { } mainSurface &&
            _editorSessions.Contains(_lastMainEditorSession))
            return mainSurface;

        return null;
    }

    private EditorSurfaceControl GetEditorSurfaceForControllerSetup()
    {
        if (_eventSurfaceSessionOverride?.Surface is { } overrideSurface)
            return overrideSurface;

        if (_activeEditorSession?.Surface is { } activeSurface)
            return activeSurface;

        _bootstrapEditorSurface ??= new EditorSurfaceControl
        {
            DataContext = this,
        };
        return _bootstrapEditorSurface;
    }

    internal void ActivateEditorSurface(EditorSurfaceControl surface)
    {
        var session = _editorSessions.FirstOrDefault(candidate => ReferenceEquals(candidate.Surface, surface));
        if (session is not null)
            ActivateEditorSessionForSurfaceInteraction(session);
    }

    private bool ActivateEditorSessionForSurfaceInteraction(EditorSessionViewModel session)
    {
        if (_executionController?.IsRunning == true)
        {
            SetStatus("执行中，不能切换编辑窗口。");
            return false;
        }

        ConfigureEditorSurface(session);
        if (ReferenceEquals(session, _activeEditorSession))
        {
            if (session.SurfaceContext is { } activeContext)
                ApplyEditorSurfaceContext(activeContext);
            return true;
        }

        if (!ReferenceEquals(session, _activeEditorSession))
        {
            CommitCurrentSessionToAsset();
            if (_activeEditorSession is not null)
                _activeEditorSession.IsActive = false;

            _activeEditorSession = session;
            _activeEditorSession.IsActive = true;
            _activeContentAsset = session.ContentAsset;
            _editorService = session.EditorService;
            _nodeFactory = session.NodeFactory;
            AttachActiveEditorService(_editorService);
        }

        RebuildEditorControllers();
        AttachGraphCollectionChangeHandlers();
        UpdateCompileButtonState();
        return true;
    }

    internal void HandleEditorSurfaceEvent(EditorSessionViewModel session, EditorSurfaceEvent surfaceEvent, object sender, EventArgs e, bool promoteToActive)
    {
        if (promoteToActive && !ActivateEditorSessionForSurfaceInteraction(session))
            return;

        if (ReferenceEquals(session, _activeEditorSession))
        {
            if (session.SurfaceContext is { } activeContext)
                ApplyEditorSurfaceContext(activeContext);
            HandleEditorSurfaceEvent(surfaceEvent, sender, e);
            if (session.SurfaceContext is { } updatedContext)
                ApplyEditorSurfaceContext(updatedContext);
            return;
        }

        RunWithSurfaceContext(session, () => HandleEditorSurfaceEvent(surfaceEvent, sender, e));
    }

    private void RunWithSurfaceContext(EditorSessionViewModel session, Action action)
    {
        ConfigureEditorSurface(session);
        if (session.SurfaceContext is not { IsConfigured: true } context)
            return;

        var previousOverride = _eventSurfaceSessionOverride;
        var previousContentAsset = _activeContentAsset;
        var previousEditorService = _editorService;
        var previousNodeFactory = _nodeFactory;
        var previousCommandService = _graphCommandService;
        var previousGraphListController = _graphListController;
        var previousFunctionListController = _functionListController;
        var previousActiveAssetController = _activeAssetController;
        var previousCanvasPanZoomController = _canvasPanZoomController;
        var previousNodeDragSelectionController = _nodeDragSelectionController;
        var previousInspectorController = _inspectorController;
        var previousPinConnectionController = _pinConnectionController;
        var previousNodePaletteController = _nodePaletteController;
        var previousGraphImportDropController = _graphImportDropController;

        try
        {
            _eventSurfaceSessionOverride = session;
            _activeContentAsset = session.ContentAsset;
            _editorService = session.EditorService;
            _nodeFactory = session.NodeFactory;
            ApplyEditorSurfaceContext(context);
            action();
        }
        finally
        {
            _eventSurfaceSessionOverride = previousOverride;
            _activeContentAsset = previousContentAsset;
            _editorService = previousEditorService;
            _nodeFactory = previousNodeFactory;
            _graphCommandService = previousCommandService;
            _graphListController = previousGraphListController;
            _functionListController = previousFunctionListController;
            _activeAssetController = previousActiveAssetController;
            _canvasPanZoomController = previousCanvasPanZoomController;
            _nodeDragSelectionController = previousNodeDragSelectionController;
            _inspectorController = previousInspectorController;
            _pinConnectionController = previousPinConnectionController;
            _nodePaletteController = previousNodePaletteController;
            _graphImportDropController = previousGraphImportDropController;
        }
    }

    internal void HandleEditorSurfaceEvent(EditorSurfaceEvent surfaceEvent, object sender, EventArgs e)
    {
        switch (surfaceEvent)
        {
            case EditorSurfaceEvent.AddGraphListItemClick: AddGraphListItem_Click(sender, (RoutedEventArgs)e); break;
            case EditorSurfaceEvent.GraphListBoxMouseDoubleClick: GraphListBox_MouseDoubleClick(sender, (MouseButtonEventArgs)e); break;
            case EditorSurfaceEvent.GraphListBoxKeyDown: GraphListBox_KeyDown(sender, (WpfKeyEventArgs)e); break;
            case EditorSurfaceEvent.RenameGraphMenuItemClick: RenameGraphMenuItem_Click(sender, (RoutedEventArgs)e); break;
            case EditorSurfaceEvent.DeleteGraphMenuItemClick: DeleteGraphMenuItem_Click(sender, (RoutedEventArgs)e); break;
            case EditorSurfaceEvent.AddFunctionListItemClick: AddFunctionListItem_Click(sender, (RoutedEventArgs)e); break;
            case EditorSurfaceEvent.FunctionListBoxMouseDoubleClick: FunctionListBox_MouseDoubleClick(sender, (MouseButtonEventArgs)e); break;
            case EditorSurfaceEvent.FunctionListBoxKeyDown: FunctionListBox_KeyDown(sender, (WpfKeyEventArgs)e); break;
            case EditorSurfaceEvent.FunctionListItemPreviewMouseRightButtonDown: FunctionListItem_PreviewMouseRightButtonDown(sender, (MouseButtonEventArgs)e); break;
            case EditorSurfaceEvent.FunctionListItemPreviewMouseLeftButtonDown: FunctionListItem_PreviewMouseLeftButtonDown(sender, (MouseButtonEventArgs)e); break;
            case EditorSurfaceEvent.GraphListItemPreviewMouseRightButtonDown: GraphListItem_PreviewMouseRightButtonDown(sender, (MouseButtonEventArgs)e); break;
            case EditorSurfaceEvent.GraphListItemPreviewMouseLeftButtonDown: GraphListItem_PreviewMouseLeftButtonDown(sender, (MouseButtonEventArgs)e); break;
            case EditorSurfaceEvent.GraphNameTextBoxKeyDown: GraphNameTextBox_KeyDown(sender, (WpfKeyEventArgs)e); break;
            case EditorSurfaceEvent.GraphNameTextBoxLostFocus: GraphNameTextBox_LostFocus(sender, (RoutedEventArgs)e); break;
            case EditorSurfaceEvent.LibraryPublishCheckBoxClick: LibraryPublishCheckBox_Click(sender, (RoutedEventArgs)e); break;
            case EditorSurfaceEvent.ToggleEventGraphSectionClick: ToggleEventGraphSection_Click(sender, (RoutedEventArgs)e); break;
            case EditorSurfaceEvent.ToggleFunctionSectionClick: ToggleFunctionSection_Click(sender, (RoutedEventArgs)e); break;
            case EditorSurfaceEvent.NodeCardMouseLeftButtonDown: NodeCard_MouseLeftButtonDown(sender, (MouseButtonEventArgs)e); break;
            case EditorSurfaceEvent.NodeHeaderPreviewMouseLeftButtonDown: NodeHeader_PreviewMouseLeftButtonDown(sender, (MouseButtonEventArgs)e); break;
            case EditorSurfaceEvent.NodeHeaderPreviewMouseMove: NodeHeader_PreviewMouseMove(sender, (WpfMouseEventArgs)e); break;
            case EditorSurfaceEvent.NodeHeaderPreviewMouseLeftButtonUp: NodeHeader_PreviewMouseLeftButtonUp(sender, (MouseButtonEventArgs)e); break;
            case EditorSurfaceEvent.CommonVariadicAddButtonClick: CommonVariadicAddButton_Click(sender, (RoutedEventArgs)e); break;
            case EditorSurfaceEvent.CommonVariadicRemoveButtonClick: CommonVariadicRemoveButton_Click(sender, (RoutedEventArgs)e); break;
            case EditorSurfaceEvent.GraphViewportPreviewMouseLeftButtonDown: GraphViewport_PreviewMouseLeftButtonDown(sender, (MouseButtonEventArgs)e); break;
            case EditorSurfaceEvent.GraphViewportPreviewMouseMove: GraphViewport_PreviewMouseMove(sender, (WpfMouseEventArgs)e); break;
            case EditorSurfaceEvent.GraphViewportPreviewMouseLeftButtonUp: GraphViewport_PreviewMouseLeftButtonUp(sender, (MouseButtonEventArgs)e); break;
            case EditorSurfaceEvent.GraphViewportPreviewMouseRightButtonDown: GraphViewport_PreviewMouseRightButtonDown(sender, (MouseButtonEventArgs)e); break;
            case EditorSurfaceEvent.GraphViewportPreviewMouseRightButtonUp: GraphViewport_PreviewMouseRightButtonUp(sender, (MouseButtonEventArgs)e); break;
            case EditorSurfaceEvent.GraphViewportPreviewMouseWheel: GraphViewport_PreviewMouseWheel(sender, (MouseWheelEventArgs)e); break;
            case EditorSurfaceEvent.NodePaletteScrollViewerPreviewMouseWheel: NodePaletteScrollViewer_PreviewMouseWheel(sender, (MouseWheelEventArgs)e); break;
            case EditorSurfaceEvent.PinButtonPreviewMouseLeftButtonDown: PinButton_PreviewMouseLeftButtonDown(sender, (MouseButtonEventArgs)e); break;
            case EditorSurfaceEvent.PinButtonPreviewMouseLeftButtonUp: PinButton_PreviewMouseLeftButtonUp(sender, (MouseButtonEventArgs)e); break;
            case EditorSurfaceEvent.ConnectionPathMouseDoubleClick: ConnectionPath_MouseDoubleClick(sender, (MouseButtonEventArgs)e); break;
            case EditorSurfaceEvent.ConnectionPathMouseLeftButtonDown: ConnectionPath_MouseLeftButtonDown(sender, (MouseButtonEventArgs)e); break;
            case EditorSurfaceEvent.ConnectionPathPreviewMouseLeftButtonDown: ConnectionPath_PreviewMouseLeftButtonDown(sender, (MouseButtonEventArgs)e); break;
            case EditorSurfaceEvent.ConnectionPathPreviewMouseRightButtonDown: ConnectionPath_PreviewMouseRightButtonDown(sender, (MouseButtonEventArgs)e); break;
            case EditorSurfaceEvent.DeleteConnectionPathClick: DeleteConnectionPath_Click(sender, (RoutedEventArgs)e); break;
            case EditorSurfaceEvent.AddRerouteToConnectionPathClick: AddRerouteToConnectionPath_Click(sender, (RoutedEventArgs)e); break;
            case EditorSurfaceEvent.InspectorFieldTextChanged: InspectorField_TextChanged(sender, (TextChangedEventArgs)e); break;
            case EditorSurfaceEvent.InspectorFieldSelectionChanged: InspectorField_SelectionChanged(sender, (SelectionChangedEventArgs)e); break;
            case EditorSurfaceEvent.InspectorFieldCheckedChanged: InspectorField_CheckedChanged(sender, (RoutedEventArgs)e); break;
            case EditorSurfaceEvent.ToDoSearchBoxTextChanged: ToDoSearchBox_TextChanged(sender, (TextChangedEventArgs)e); break;
            case EditorSurfaceEvent.ToDoTargetListBoxSelectionChanged: ToDoTargetListBox_SelectionChanged(sender, (SelectionChangedEventArgs)e); break;
            case EditorSurfaceEvent.BrowseFindImagePathClick: BrowseFindImagePath_Click(sender, (RoutedEventArgs)e); break;
            case EditorSurfaceEvent.BrowseStartProgramPathClick: BrowseStartProgramPath_Click(sender, (RoutedEventArgs)e); break;
            case EditorSurfaceEvent.RefreshWindowListClick: RefreshWindowList_Click(sender, (RoutedEventArgs)e); break;
            case EditorSurfaceEvent.BrowseFindImageSourcePathClick: BrowseFindImageSourcePath_Click(sender, (RoutedEventArgs)e); break;
            case EditorSurfaceEvent.CommonKeyChordAddButtonClick: CommonKeyChordAddButton_Click(sender, (RoutedEventArgs)e); break;
            case EditorSurfaceEvent.AddParameterButtonClick: AddParameterButton_Click(sender, (RoutedEventArgs)e); break;
            case EditorSurfaceEvent.CommonModeComboBoxSelectionChanged: CommonModeComboBox_SelectionChanged(sender, (SelectionChangedEventArgs)e); break;
            case EditorSurfaceEvent.CommonWindowComboBoxSelectionChanged: CommonWindowComboBox_SelectionChanged(sender, (SelectionChangedEventArgs)e); break;
            case EditorSurfaceEvent.CommonWindowRefreshButtonClick: CommonWindowRefreshButton_Click(sender, (RoutedEventArgs)e); break;
            case EditorSurfaceEvent.CommonEnumComboBoxSelectionChanged: CommonEnumComboBox_SelectionChanged(sender, (SelectionChangedEventArgs)e); break;
            case EditorSurfaceEvent.CommonBrowseFileButtonClick: CommonBrowseFileButton_Click(sender, (RoutedEventArgs)e); break;
            case EditorSurfaceEvent.SelectWindowInputModeSelectionChanged: SelectWindowInputMode_SelectionChanged(sender, (SelectionChangedEventArgs)e); break;
            case EditorSurfaceEvent.SelectWindowAutoComboBoxSelectionChanged: SelectWindowAutoComboBox_SelectionChanged(sender, (SelectionChangedEventArgs)e); break;
            case EditorSurfaceEvent.FindImageSourceModeComboBoxSelectionChanged: FindImageSourceModeComboBox_SelectionChanged(sender, (SelectionChangedEventArgs)e); break;
            case EditorSurfaceEvent.PinAnchorLoaded: PinAnchor_Loaded(sender, (RoutedEventArgs)e); break;
            case EditorSurfaceEvent.PinAnchorLayoutUpdated: PinAnchor_LayoutUpdated(sender, e); break;
            case EditorSurfaceEvent.NodePaletteSearchBoxTextChanged: NodePaletteSearchBox_TextChanged(sender, (TextChangedEventArgs)e); break;
        }
    }

    private void ConfigureEditorSurface(EditorSessionViewModel session)
    {
        var context = session.EnsureSurfaceContext();
        context.Configure(this, CreateEditorSurfaceHostServices(session));
        if (_themedDialogOverridesInstalled)
            InstallGraphListHandlersForSurface(context.Surface, context);
        if (ReferenceEquals(session, _activeEditorSession))
            ApplyEditorSurfaceContext(context);
    }

    private EditorSurfaceHostServices CreateEditorSurfaceHostServices(EditorSessionViewModel session) => new(
        this,
        _graphLibraryService,
        _clipboardService,
        _nodeRegistry,
        PersistAssetLibrary,
        () => SnapshotSession(session),
        () => MarkSessionDirty(session),
        () => MarkSessionLayoutDirty(session),
        EnsureCanvasLargeEnough,
        SetStatus,
        GetCallableFunctions,
        GetCallableCustomEvents);

    private GraphAssetKind? GetActiveGraphKind() => _activeAssetController?.AssetKind;

    private void InitializeEditor()
    {
        LoadGraphLibrary();
        InitializeNodePalette();
        EnsureCanvasLargeEnough();
    }
}
