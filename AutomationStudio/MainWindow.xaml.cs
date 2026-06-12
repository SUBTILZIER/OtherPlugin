using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AutomationStudioWpf.Collections;
using AutomationStudioWpf.Controls;
using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Interaction;
using AutomationStudioWpf.Logging;
using AutomationStudioWpf.Nodes;
using AutomationStudioWpf.Services;
using Microsoft.Win32;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using DragEventArgs = System.Windows.DragEventArgs;
using TextBox = System.Windows.Controls.TextBox;
using MouseButton = System.Windows.Input.MouseButton;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;
using TextBoxBase = System.Windows.Controls.Primitives.TextBoxBase;
using WpfMessageBox = System.Windows.MessageBox;

namespace AutomationStudioWpf;

/// <summary>
/// 主窗口 - 节点编辑器
/// 重构后：职责精简为协调各服务，具体逻辑下沉到 Services 层
/// </summary>
public partial class MainWindow : Window, INotifyPropertyChanged
{
    // 核心服务
    private GraphEditorService _editorService = new();
    private readonly NodeClipboardService _clipboardService = new();
    private NodeFactory _nodeFactory = new();
    private readonly GraphLibraryService _graphLibraryService = new();
    private readonly CallableGraphResolver _callableGraphResolver = new();
    private readonly GraphCompileService _graphCompileService;
    private readonly NodeRegistry _nodeRegistry = NodeRegistry.CreateDefault();
    private readonly ObservableCollection<EditorSessionViewModel> _editorSessions = [];
    private readonly ObservableCollection<EditorSessionViewModel> _mainEditorSessions = [];
    private readonly ObservableCollection<GraphListItemViewModel> _emptyGraphListItems = [];
    private readonly ObservableCollection<GraphListItemViewModel> _emptyFunctionListItems = [];
    private readonly ObservableCollection<NodeBaseViewModel> _emptyNodes = [];
    private readonly ObservableCollection<ConnectionPathViewModel> _emptyConnectionPaths = [];
    private EditorSessionViewModel? _activeEditorSession;
    private GraphEditorService? _attachedEditorService;
    private EditorSessionViewModel? _draggedEditorSession;
    private Point _editorSessionDragStart;
    private bool _isEditorSessionDrag;
    private System.Windows.Controls.Primitives.Popup? _editorSessionDragPreviewPopup;
    private EditorSurfaceControl? _bootstrapEditorSurface;
    private EditorSessionViewModel? _eventSurfaceSessionOverride;
    private GraphCommandService _graphCommandService = null!;
    private ExecutionController _executionController = null!;
    private NodePaletteController _nodePaletteController = null!;
    private GraphListController _graphListController = null!;
    private GraphListController _functionListController = null!;
    private GraphListController? _activeAssetController;
    private CanvasPanZoomController _canvasPanZoomController = null!;
    private NodeDragSelectionController _nodeDragSelectionController = null!;
    private InspectorController _inspectorController = null!;
    private PinConnectionController _pinConnectionController = null!;
    private LogPanelController _logPanelController = null!;
    private GraphImportDropController _graphImportDropController = null!;
    private ContentAssetViewModel? _activeContentAsset;
    private string? _currentContentFolderId;
    private Point _contentDragStartPoint;
    private bool _contentFolderSelectionActive;
    private bool _contentBrowserContextTargetsAsset;
    private bool _suppressGraphChangedDirty;
    private bool _isCommittingContentAssetRename;
    private readonly ContentAssetViewModel _rootContentFolder = new()
    {
        Kind = ContentAssetKind.Folder,
        Name = "内容",
    };
    // 运行状态
    private bool _isClosing;

    // 右键菜单状态
    private bool _rightClickPending;
    private Point _rightClickStartPos;

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainWindow()
    {
        DataContext = this;
        _graphCompileService = new GraphCompileService(_callableGraphResolver);
        InitializeComponent();
        InitializeControllers();
        InitializeServices();
        InitializeEditor();
    }

    #region 初始化

    private void InitializeServices()
    {

        // 绑定服务事件
        AttachActiveEditorService(_editorService);

        // 日志更新
        Logger.Entries.CollectionChanged += (_, e) => _logPanelController.HandleEntriesChanged(e);

        // 窗口关闭时释放按键，并处理未保存图谱。
        Closing += Window_Closing;

        // 全局鼠标点击检测：点击节点菜单外部时关闭菜单
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
            () => new System.Windows.Size(surface.GraphViewport.ActualWidth, surface.GraphViewport.ActualHeight),
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
            case EditorSurfaceEvent.GraphListBoxKeyDown: GraphListBox_KeyDown(sender, (KeyEventArgs)e); break;
            case EditorSurfaceEvent.RenameGraphMenuItemClick: RenameGraphMenuItem_Click(sender, (RoutedEventArgs)e); break;
            case EditorSurfaceEvent.DeleteGraphMenuItemClick: DeleteGraphMenuItem_Click(sender, (RoutedEventArgs)e); break;
            case EditorSurfaceEvent.AddFunctionListItemClick: AddFunctionListItem_Click(sender, (RoutedEventArgs)e); break;
            case EditorSurfaceEvent.FunctionListBoxMouseDoubleClick: FunctionListBox_MouseDoubleClick(sender, (MouseButtonEventArgs)e); break;
            case EditorSurfaceEvent.FunctionListBoxKeyDown: FunctionListBox_KeyDown(sender, (KeyEventArgs)e); break;
            case EditorSurfaceEvent.FunctionListItemPreviewMouseRightButtonDown: FunctionListItem_PreviewMouseRightButtonDown(sender, (MouseButtonEventArgs)e); break;
            case EditorSurfaceEvent.FunctionListItemPreviewMouseLeftButtonDown: FunctionListItem_PreviewMouseLeftButtonDown(sender, (MouseButtonEventArgs)e); break;
            case EditorSurfaceEvent.GraphListItemPreviewMouseRightButtonDown: GraphListItem_PreviewMouseRightButtonDown(sender, (MouseButtonEventArgs)e); break;
            case EditorSurfaceEvent.GraphListItemPreviewMouseLeftButtonDown: GraphListItem_PreviewMouseLeftButtonDown(sender, (MouseButtonEventArgs)e); break;
            case EditorSurfaceEvent.GraphNameTextBoxKeyDown: GraphNameTextBox_KeyDown(sender, (KeyEventArgs)e); break;
            case EditorSurfaceEvent.GraphNameTextBoxLostFocus: GraphNameTextBox_LostFocus(sender, (RoutedEventArgs)e); break;
            case EditorSurfaceEvent.LibraryPublishCheckBoxClick: LibraryPublishCheckBox_Click(sender, (RoutedEventArgs)e); break;
            case EditorSurfaceEvent.ToggleEventGraphSectionClick: ToggleEventGraphSection_Click(sender, (RoutedEventArgs)e); break;
            case EditorSurfaceEvent.ToggleFunctionSectionClick: ToggleFunctionSection_Click(sender, (RoutedEventArgs)e); break;
            case EditorSurfaceEvent.NodeCardMouseLeftButtonDown: NodeCard_MouseLeftButtonDown(sender, (MouseButtonEventArgs)e); break;
            case EditorSurfaceEvent.NodeHeaderPreviewMouseLeftButtonDown: NodeHeader_PreviewMouseLeftButtonDown(sender, (MouseButtonEventArgs)e); break;
            case EditorSurfaceEvent.NodeHeaderPreviewMouseMove: NodeHeader_PreviewMouseMove(sender, (MouseEventArgs)e); break;
            case EditorSurfaceEvent.NodeHeaderPreviewMouseLeftButtonUp: NodeHeader_PreviewMouseLeftButtonUp(sender, (MouseButtonEventArgs)e); break;
            case EditorSurfaceEvent.GraphViewportPreviewMouseLeftButtonDown: GraphViewport_PreviewMouseLeftButtonDown(sender, (MouseButtonEventArgs)e); break;
            case EditorSurfaceEvent.GraphViewportPreviewMouseMove: GraphViewport_PreviewMouseMove(sender, (MouseEventArgs)e); break;
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

    private void InitializeEditor()
    {
        LoadGraphLibrary();
        InitializeNodePalette();
        EnsureCanvasLargeEnough();
    }

    #endregion

    #region 属性绑定

    public System.Collections.IEnumerable Nodes => _activeEditorSession?.EditorService.Nodes ?? _emptyNodes;
    public System.Collections.IEnumerable ConnectionPaths => _activeEditorSession?.EditorService.ConnectionPaths ?? _emptyConnectionPaths;
    public ObservableCollection<GraphListItemViewModel> GraphListItems => _activeEditorSession?.GraphListItems ?? _emptyGraphListItems;
    public ObservableCollection<GraphListItemViewModel> FunctionListItems => _activeEditorSession?.FunctionListItems ?? _emptyFunctionListItems;
    public ObservableCollection<EditorSessionViewModel> EditorSessions => _editorSessions;
    public ObservableCollection<EditorSessionViewModel> MainEditorSessions => _mainEditorSessions;
    public ObservableCollection<ContentAssetViewModel> ContentBrowserItems { get; } = [];
    public RangeObservableCollection<ContentAssetViewModel> ContentFolderItems { get; } = [];
    public RangeObservableCollection<ContentAssetViewModel> ContentVisibleItems { get; } = [];

    #endregion

    #region 图谱列表

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

    private void AddGraphListItem_Click(object sender, RoutedEventArgs e)
    {
        SnapshotActiveAsset();
        SetSessionActiveGraphController(GetOperationEditorSession(), _graphListController, remember: false);
        _graphListController.AddAndRename(snapshotCurrent: false);
        SetSessionActiveGraphController(GetOperationEditorSession(), _graphListController);
        SaveSectionExpansionForActiveAsset(_graphListController);
        MarkCurrentContentDirty();
        UpdateGraphSectionVisibility();
    }

    private void GraphListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_graphListController.TryGetItemFromMouse(e, out var item))
        {
            LoadGraphItem(_graphListController, item, snapshotCurrent: true);
            e.Handled = true;
            UpdateGraphSectionVisibility();
        }
    }

    private void GraphListBox_KeyDown(object sender, KeyEventArgs e)
    {
        _graphListController.HandleKeyDown(e);
        SaveSectionExpansionForActiveAsset(_graphListController);
        UpdateGraphSectionVisibility();
    }

    private void RenameGraphMenuItem_Click(object sender, RoutedEventArgs e)
    {
        (_activeAssetController ?? _graphListController).RenameSelected();
    }

    private void DeleteGraphMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var controller = _activeAssetController ?? _graphListController;
        controller.DeleteSelected();
        SaveSectionExpansionForActiveAsset(controller);
        UpdateGraphSectionVisibility();
    }

    private void AddFunctionListItem_Click(object sender, RoutedEventArgs e)
    {
        SnapshotActiveAsset();
        SetSessionActiveGraphController(GetOperationEditorSession(), _functionListController, remember: false);
        _functionListController.AddAndRename(snapshotCurrent: false);
        SetSessionActiveGraphController(GetOperationEditorSession(), _functionListController);
        SaveSectionExpansionForActiveAsset(_functionListController);
        MarkCurrentContentDirty();
        UpdateGraphSectionVisibility();
    }

    private void FunctionListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_functionListController.TryGetItemFromMouse(e, out var item))
        {
            LoadGraphItem(_functionListController, item, snapshotCurrent: true);
            e.Handled = true;
            UpdateGraphSectionVisibility();
        }
    }

    private void FunctionListBox_KeyDown(object sender, KeyEventArgs e)
    {
        _functionListController.HandleKeyDown(e);
        SaveSectionExpansionForActiveAsset(_functionListController);
        UpdateGraphSectionVisibility();
    }

    private void FunctionListItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _functionListController.SelectRightClickedItem(sender);
        ActivateGraphListItem(_functionListController, sender, e);
    }

    private void FunctionListItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        ActivateGraphListItem(_functionListController, sender, e);
    }

    private void GraphListItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _graphListController.SelectRightClickedItem(sender);
        ActivateGraphListItem(_graphListController, sender, e);
        e.Handled = false;
    }

    private void GraphListItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        ActivateGraphListItem(_graphListController, sender, e);
    }

    private void ActivateGraphListItem(GraphListController controller, object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBoxItem { DataContext: GraphListItemViewModel item } ||
            item.IsEditing)
        {
            return;
        }

        var source = e.OriginalSource as DependencyObject ?? sender as DependencyObject;
        if (source is not null && HasVisualAncestor<TextBox>(source))
            return;
        if (source is not null && HasVisualAncestor<System.Windows.Controls.Primitives.ToggleButton>(source))
            return;

        if (ReferenceEquals(_activeAssetController, controller) &&
            ReferenceEquals(controller.ActiveItem, item))
        {
            return;
        }

        LoadGraphItem(controller, item, snapshotCurrent: true);
        UpdateGraphSectionVisibility();
    }

    private void LoadGraphItem(GraphListController controller, GraphListItemViewModel item, bool snapshotCurrent)
    {
        if (snapshotCurrent)
            SnapshotActiveAsset();

        SetSessionActiveGraphController(GetOperationEditorSession(), controller, remember: false);
        controller.LoadItem(item, snapshotCurrent: false);
        var session = GetOperationEditorSession();
        SetSessionActiveGraphController(session, controller);
        _graphCommandService.Clear();
    }

    private void GraphNameTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is TextBox { DataContext: GraphListItemViewModel item } tb)
            GetControllerFor(item).HandleRenameKeyDown(tb, e);
    }

    private void GraphNameTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: GraphListItemViewModel item } tb)
            GetControllerFor(item).HandleRenameLostFocus(tb);
    }

    private void LibraryPublishCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.CheckBox { DataContext: GraphListItemViewModel item })
            return;
        if (_activeContentAsset?.Kind is not ContentAssetKind.FunctionLibrary)
            return;

        item.IsDirty = true;
        MarkCurrentContentDirty();
        PersistAssetLibrary();
        SetStatus(item.IsPublicToLibrary
            ? $"已公开到库：{item.Name}"
            : $"已取消公开到库：{item.Name}");
    }

    private GraphListController GetControllerFor(GraphListItemViewModel item) =>
        item.Kind switch
        {
            GraphAssetKind.Function => _functionListController,
            _ => _graphListController,
        };

    private void ToggleEventGraphSection_Click(object sender, RoutedEventArgs e)
    {
        _graphListController.SetSectionExpanded(!_graphListController.IsSectionExpanded);
        SaveSectionExpansionForActiveAsset(_graphListController);
        UpdateGraphSectionVisibility();
    }

    private void ToggleFunctionSection_Click(object sender, RoutedEventArgs e)
    {
        _functionListController.SetSectionExpanded(!_functionListController.IsSectionExpanded);
        SaveSectionExpansionForActiveAsset(_functionListController);
        UpdateGraphSectionVisibility();
    }

    private void SaveSectionExpansionForActiveAsset(GraphListController controller)
    {
        if (_activeContentAsset is null)
            return;

        if (ReferenceEquals(controller, _graphListController))
        {
            _activeContentAsset.EventGraphSectionExpanded = controller.IsSectionExpanded;
            _activeContentAsset.EventGraphSectionHasState = true;
        }
        else if (ReferenceEquals(controller, _functionListController))
        {
            _activeContentAsset.FunctionSectionExpanded = controller.IsSectionExpanded;
            _activeContentAsset.FunctionSectionHasState = true;
        }
    }

    private void UpdateGraphSectionVisibility()
    {
        if (_graphListController is null || _functionListController is null)
            return;

        bool showEvent = _activeContentAsset?.Kind == ContentAssetKind.Script;
        bool showFunction = _activeContentAsset?.Kind is ContentAssetKind.Script or ContentAssetKind.FunctionLibrary;
        if (TryGetActiveEditorSurface() is not { } surface)
        {
            UpdateCompileButtonState();
            return;
        }

        UpdateGraphSection(_graphListController, surface.EventGraphPanel, surface.EventGraphSection, surface.EventGraphSectionToggle, surface.GraphListBox, showEvent);
        UpdateGraphSection(_functionListController, surface.FunctionPanel, surface.FunctionSection, surface.FunctionSectionToggle, surface.FunctionListBox, showFunction);
        surface.EventGraphDirtyBadge.Visibility = showEvent && _graphListController.HasCompileDirtyItems ? Visibility.Visible : Visibility.Collapsed;
        surface.FunctionDirtyBadge.Visibility = showFunction && _functionListController.HasCompileDirtyItems ? Visibility.Visible : Visibility.Collapsed;
        UpdateLibraryPublishOptionVisibility();
        UpdateCompileButtonState();
    }

    private void UpdateLibraryPublishOptionVisibility()
    {
        bool showFunctions = _activeContentAsset?.Kind == ContentAssetKind.FunctionLibrary;

        foreach (var item in FunctionListItems)
            item.ShowLibraryPublishOption = showFunctions;
    }

    private static void UpdateGraphSection(
        GraphListController controller,
        FrameworkElement panel,
        FrameworkElement header,
        System.Windows.Controls.Button toggle,
        FrameworkElement list,
        bool showSection)
    {
        controller.RefreshSectionExpansion();
        panel.Visibility = showSection ? Visibility.Visible : Visibility.Collapsed;
        header.Visibility = showSection ? Visibility.Visible : Visibility.Collapsed;
        toggle.Content = controller.IsSectionExpanded ? "v" : ">";
        list.Visibility = showSection && controller.IsSectionExpanded && controller.ItemCount > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    #endregion

    #region 内容浏览器

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

    private void EditorSession_PropertyChanged(object? sender, PropertyChangedEventArgs e)
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

    private void ActivateEditorSession(EditorSessionViewModel session, GraphListItemViewModel? targetGraph = null, GraphAssetKind? targetKind = null)
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

    private GraphAssetKind? GetActiveGraphKind() => _activeAssetController?.AssetKind;

    private static void ApplyEntryNodeTitle(IEnumerable<NodeBaseViewModel> nodes, GraphAssetKind kind, string graphName)
    {
        string title = $"{graphName}开始";
        foreach (var node in nodes)
        {
            if (kind == GraphAssetKind.Function && node is FunctionEntryNodeViewModel)
            {
                node.Title = title;
            }
        }
    }

    private static void ApplyEntryNodeTitle(GraphFileModel graph, GraphAssetKind kind, string graphName)
    {
        string? entryKey = kind switch
        {
            GraphAssetKind.Function => "function_entry",
            _ => null,
        };
        if (entryKey is null)
            return;

        foreach (var node in graph.Nodes.Where(node => node.NodeTypeKey == entryKey))
            node.Title = $"{graphName}开始";
    }

    private static NodeFileModel CloneNodeFile(NodeFileModel node) => new()
    {
        Id = node.Id,
        NodeTypeKey = node.NodeTypeKey,
        Title = node.Title,
        X = node.X,
        Y = node.Y,
        ImagePath = node.ImagePath,
        SourceImagePath = node.SourceImagePath,
        ImageSearchSourceMode = node.ImageSearchSourceMode,
        SimilarityThresholdPercent = node.SimilarityThresholdPercent,
        UseFindImageRegion = node.UseFindImageRegion,
        FindImageRegionX = node.FindImageRegionX,
        FindImageRegionY = node.FindImageRegionY,
        FindImageRegionWidth = node.FindImageRegionWidth,
        FindImageRegionHeight = node.FindImageRegionHeight,
        ProgramPath = node.ProgramPath,
        WaitTimeoutMs = node.WaitTimeoutMs,
        FailureAction = node.FailureAction,
        RetryCount = node.RetryCount,
        PrintLogMessage = node.PrintLogMessage,
        ClickMode = node.ClickMode,
        PositionX = node.PositionX,
        PositionY = node.PositionY,
        HoldDurationMs = node.HoldDurationMs,
        MouseButton = node.MouseButton,
        OperationMode = node.OperationMode,
        Key = node.Key,
        ScrollAction = node.ScrollAction,
        ScrollSpeed = node.ScrollSpeed,
        ScrollInterval = node.ScrollInterval,
        ScrollDuration = node.ScrollDuration,
        DelayMs = node.DelayMs,
        LoopCount = node.LoopCount,
        ConditionValue = node.ConditionValue,
        WhileLoopMode = node.WhileLoopMode,
        MaxIterations = node.MaxIterations,
        RoutedKind = node.RoutedKind,
        ProcessName = node.ProcessName,
        WindowInputMode = node.WindowInputMode,
        Text = node.Text,
        Text2 = node.Text2,
        Text3 = node.Text3,
        Number = node.Number,
        Number2 = node.Number2,
        Number3 = node.Number3,
        Number4 = node.Number4,
        Flag = node.Flag,
        FunctionId = node.FunctionId,
        CustomEventId = node.CustomEventId,
        ExitName = node.ExitName,
        Parameters = node.Parameters.Select(CloneParameterFile).ToList(),
        InputParameters = node.InputParameters.Select(CloneParameterFile).ToList(),
        OutputParameters = node.OutputParameters.Select(CloneParameterFile).ToList(),
    };

    private static GraphParameterFileModel CloneParameterFile(GraphParameterFileModel parameter) => new()
    {
        Id = parameter.Id,
        Name = parameter.Name,
        Type = parameter.Type,
    };

    #endregion

    #region 辅助方法

    private void EnsureCanvasLargeEnough()
    {
        // Infinite canvas mode no longer resizes the surface by viewport bounds.
    }

    private void SyncNodeFactorySequence()
    {
        var maxSeq = _editorService.Nodes
            .Select(n => n.Id)
            .Select(id => id.StartsWith("node_") && int.TryParse(id[5..], out var num) ? num : 0)
            .DefaultIfEmpty(0)
            .Max();
        _nodeFactory.ResetCounter(maxSeq);
    }

    private void SetStatus(string message)
    {
        StatusTextBlock.Text = message;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void RaiseEditorBindingProperties()
    {
        OnPropertyChanged(nameof(Nodes));
        OnPropertyChanged(nameof(ConnectionPaths));
        OnPropertyChanged(nameof(GraphListItems));
        OnPropertyChanged(nameof(FunctionListItems));
        OnPropertyChanged(nameof(EditorSessions));
        OnPropertyChanged(nameof(MainEditorSessions));
    }

    private void UpdateEditorSessionChrome()
    {
        foreach (var session in _editorSessions)
        {
            session.RefreshDirtyState();
            session.DetachedWindow?.RefreshChrome();
        }

        RefreshMainEditorSessions();
        EditorWindowBar.Visibility = _mainEditorSessions.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshMainEditorSessions()
    {
        var visibleSessions = _editorSessions
            .Where(session => session.DockMode != EditorDockMode.Detached)
            .ToList();
        if (_mainEditorSessions.SequenceEqual(visibleSessions))
            return;

        _mainEditorSessions.Clear();
        foreach (var session in visibleSessions)
            _mainEditorSessions.Add(session);
        OnPropertyChanged(nameof(MainEditorSessions));
    }


    private static void RemoveElementFromParent(UIElement element)
    {
        if (element is null)
            return;

        if (element is FrameworkElement { Parent: System.Windows.Controls.Panel parent })
            parent.Children.Remove(element);
        else if (element is FrameworkElement { Parent: ContentControl contentControl } &&
                 ReferenceEquals(contentControl.Content, element))
            contentControl.Content = null;
    }

    #endregion

}
