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

    private enum ContentDropAction
    {
        Cancel,
        Move,
        Copy,
    }

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

    private EditorSurfaceControl GetActiveEditorSurface()
    {
        if (TryGetActiveEditorSurface() is { } surface)
            return surface;
        throw new InvalidOperationException("No active editor surface is available.");
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

    #region 工具栏事件 - 文件操作

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
        var surface = GetActiveEditorSurface();

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

    private void NewContentFolder_Click(object sender, RoutedEventArgs e) => AddContentAsset(ContentAssetKind.Folder, "新文件夹");

    private void NewScriptAsset_Click(object sender, RoutedEventArgs e) => AddContentAsset(ContentAssetKind.Script, "新脚本");

    private void NewFunctionLibraryAsset_Click(object sender, RoutedEventArgs e) => AddContentAsset(ContentAssetKind.FunctionLibrary, "新函数库");

    private void ContentBrowserListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        _contentFolderSelectionActive = false;
        if (GetContentAssetFromMouseEvent(e) is not { } asset)
            return;

        if (asset.Kind == ContentAssetKind.Folder)
            EnterContentFolder(asset);
        else
            OpenContentAsset(asset);
    }

    private void ContentFolderListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        _contentFolderSelectionActive = true;
        if (GetContentAssetFromMouseEvent(e) is not { IsFolder: true } folder)
            return;

        EnterContentFolder(ReferenceEquals(folder, _rootContentFolder) ? null : folder);
        e.Handled = true;
    }

    private void ContentFolderItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source &&
            (HasVisualAncestor<System.Windows.Controls.Button>(source) || HasVisualAncestor<TextBox>(source)))
            return;

        if (sender is not ListBoxItem { DataContext: ContentAssetViewModel { IsFolder: true } folder })
            return;

        _contentFolderSelectionActive = true;
        ContentFolderListBox.SelectedItem = folder;
        ContentBrowserListBox.SelectedItem = null;
        EnterContentFolder(ReferenceEquals(folder, _rootContentFolder) ? null : folder);
        e.Handled = true;
    }

    private void ContentFolderToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { DataContext: ContentAssetViewModel { IsFolder: true } folder })
            return;

        folder.IsTreeExpanded = !folder.IsTreeExpanded;
        RefreshContentBrowserViews();
        e.Handled = true;
    }

    private void ContentFolderListBox_KeyDown(object sender, KeyEventArgs e)
    {
        _contentFolderSelectionActive = true;
        HandleContentKeyDown(e);
    }

    private void ContentBrowserListBox_KeyDown(object sender, KeyEventArgs e)
    {
        _contentFolderSelectionActive = false;
        HandleContentKeyDown(e);
    }

    private void ContentAsset_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _contentFolderSelectionActive = false;
        _contentBrowserContextTargetsAsset = true;
        if (sender is ListBoxItem { DataContext: ContentAssetViewModel item })
            ContentBrowserListBox.SelectedItem = item;
    }

    private void ContentBrowserListBox_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _contentFolderSelectionActive = false;
        _contentBrowserContextTargetsAsset = GetContentAssetFromMouseEvent(e) is not null;
        if (!_contentBrowserContextTargetsAsset)
        {
            ContentBrowserListBox.SelectedItem = null;
            ContentFolderListBox.SelectedItem = null;
            ContentBrowserListBox.Focus();
        }
    }

    private void ContentBrowserContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        var assetVisibility = _contentBrowserContextTargetsAsset ? Visibility.Visible : Visibility.Collapsed;
        var newVisibility = _contentBrowserContextTargetsAsset ? Visibility.Collapsed : Visibility.Visible;

        ContentBrowserRenameMenuItem.Visibility = assetVisibility;
        ContentBrowserDeleteMenuItem.Visibility = assetVisibility;
        ContentBrowserAssetMenuSeparator.Visibility = assetVisibility;
        ContentBrowserNewScriptMenuItem.Visibility = newVisibility;
        ContentBrowserNewFolderMenuItem.Visibility = newVisibility;
        ContentBrowserNewLibraryMenuSeparator.Visibility = newVisibility;
        ContentBrowserNewFunctionLibraryMenuItem.Visibility = newVisibility;
    }

    private void ContentFolder_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem { DataContext: ContentAssetViewModel item })
        {
            ContentFolderListBox.SelectedItem = item;
            ContentBrowserListBox.SelectedItem = null;
            _contentFolderSelectionActive = true;
            if (!ReferenceEquals(item, _rootContentFolder))
                ContentFolderListBox.Focus();
        }
    }

    private void ContentBrowserListBox_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _contentDragStartPoint = e.GetPosition(ContentBrowserListBox);
            return;
        }

        var current = e.GetPosition(ContentBrowserListBox);
        if (Math.Abs(current.X - _contentDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _contentDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        if (ContentBrowserListBox.SelectedItem is ContentAssetViewModel asset && !ReferenceEquals(asset, _rootContentFolder))
            DragDrop.DoDragDrop(ContentBrowserListBox, new System.Windows.DataObject(typeof(ContentAssetViewModel), asset), DragDropEffects.Move | DragDropEffects.Copy);
    }

    private void ContentFolder_DragEnter(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(ContentAssetViewModel))
            ? DragDropEffects.Move | DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void ContentFolder_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(ContentAssetViewModel)) ||
            e.Data.GetData(typeof(ContentAssetViewModel)) is not ContentAssetViewModel source)
            return;

        var target = sender is ListBoxItem { DataContext: ContentAssetViewModel item } ? item : null;
        if (target is null || !target.IsFolder)
            return;

        MoveOrCopyContentAsset(source, ReferenceEquals(target, _rootContentFolder) ? null : target.Id);
        e.Handled = true;
    }

    private void RenameContentAssetMenuItem_Click(object sender, RoutedEventArgs e) => StartRenameSelectedContentAsset();

    private void DeleteContentAssetMenuItem_Click(object sender, RoutedEventArgs e) => DeleteSelectedContentAsset();

    private void ContentAssetNameTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: ContentAssetViewModel item }) return;

        if (e.Key == Key.Enter)
        {
            CommitContentAssetRename(item);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            item.IsEditing = false;
            e.Handled = true;
        }
    }

    private void ContentAssetNameTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: ContentAssetViewModel item })
            CommitContentAssetRename(item);
    }

    private void ContentBrowserArea_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox focusedTextBox &&
            focusedTextBox.DataContext is ContentAssetViewModel focusedItem &&
            focusedItem.IsEditing &&
            e.OriginalSource is DependencyObject source &&
            !IsVisualAncestor(focusedTextBox, source))
        {
            CommitContentAssetRename(focusedItem);
        }
    }

    private void AddContentAsset(ContentAssetKind kind, string namePrefix)
    {
        var asset = CreateContentAsset(kind, CreateUniqueContentName(namePrefix, _currentContentFolderId));
        asset.ParentFolderId = _currentContentFolderId;
        ContentBrowserItems.Add(asset);
        asset.IsEditing = true;
        RefreshContentBrowserViews();
        ContentBrowserListBox.SelectedItem = asset;
        _contentFolderSelectionActive = false;
        FocusContentRenameTextBox(asset);
        PersistAssetLibrary();
    }

    private ContentAssetViewModel CreateContentAsset(ContentAssetKind kind, string name)
    {
        var asset = new ContentAssetViewModel
        {
            Kind = kind,
            Name = name,
            IsDirty = true,
        };

        return asset;
    }

    private GraphListItemViewModel CreateGraphItem(GraphAssetKind kind, string name)
    {
        var graph = CreateDefaultGraphModel(kind, name);
        return new GraphListItemViewModel
        {
            Kind = kind,
            Name = name,
            Graph = graph,
            IsDirty = true,
            IsCompileDirty = true,
        };
    }

    private GraphFileModel CreateDefaultGraphModel(GraphAssetKind kind, string name)
    {
        if (kind == GraphAssetKind.Function)
            _editorService.NewFunctionGraph();
        else
            _editorService.NewGraph();

        ApplyEntryNodeTitle(_editorService.Nodes, kind, name);
        SyncNodeFactorySequence();
        return _editorService.ExportGraphModel(name, kind);
    }

    private string CreateUniqueContentName(string prefix, string? parentFolderId, ContentAssetViewModel? exclude = null)
    {
        int index = 1;
        string name;
        do
        {
            name = $"{prefix}{index++}";
        }
        while (HasSameLevelContentName(name, parentFolderId, exclude));

        return name;
    }

    private void OpenContentAsset(ContentAssetViewModel asset)
    {
        OpenOrActivateAsset(asset);
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
        GetActiveEditorSurface().InspectorHintTextBlock.Visibility = Visibility.Visible;
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

    private void RefreshContentBrowserViews()
    {
        var assetById = BuildContentAssetLookup();
        if (_currentContentFolderId is not null &&
            (!assetById.TryGetValue(_currentContentFolderId, out var currentFolder) || !currentFolder.IsFolder))
        {
            _currentContentFolderId = null;
        }

        ExpandFolderPath(_currentContentFolderId, assetById);
        var folderChildrenByParent = BuildContentChildrenLookup(foldersOnly: true);
        var childrenByParent = BuildContentChildrenLookup(foldersOnly: false);

        _rootContentFolder.ViewDepth = 0;
        _rootContentFolder.IsTreeExpanded = true;
        _rootContentFolder.HasFolderChildren = folderChildrenByParent[null].Any();
        ContentFolderItems.ReplaceAll(new[] { _rootContentFolder }
            .Concat(BuildFolderTree(null, 1, new HashSet<string>(), folderChildrenByParent)));
        ContentVisibleItems.ReplaceAll(SortContentChildren(childrenByParent[_currentContentFolderId]));

        ContentFolderListBox.SelectedItem = _currentContentFolderId is null
            ? _rootContentFolder
            : ContentFolderItems.FirstOrDefault(item => item.Id == _currentContentFolderId);
    }

    private IEnumerable<ContentAssetViewModel> BuildFolderTree(string? parentId, int depth, HashSet<string> visited)
    {
        var folderChildrenByParent = BuildContentChildrenLookup(foldersOnly: true);
        return BuildFolderTree(parentId, depth, visited, folderChildrenByParent);
    }

    private static IEnumerable<ContentAssetViewModel> BuildFolderTree(
        string? parentId,
        int depth,
        HashSet<string> visited,
        ILookup<string?, ContentAssetViewModel> folderChildrenByParent)
    {
        foreach (var folder in folderChildrenByParent[parentId].OrderBy(item => item.Name))
        {
            if (!visited.Add(folder.Id))
                continue;

            folder.ViewDepth = depth;
            folder.HasFolderChildren = folderChildrenByParent[folder.Id].Any();
            yield return folder;

            if (!folder.IsTreeExpanded)
                continue;

            foreach (var child in BuildFolderTree(folder.Id, depth + 1, visited, folderChildrenByParent))
                yield return child;
        }
    }

    private Dictionary<string, ContentAssetViewModel> BuildContentAssetLookup()
    {
        var lookup = new Dictionary<string, ContentAssetViewModel>(StringComparer.Ordinal);
        foreach (var item in ContentBrowserItems)
            lookup[item.Id] = item;
        return lookup;
    }

    private ILookup<string?, ContentAssetViewModel> BuildContentChildrenLookup(bool foldersOnly)
    {
        var source = foldersOnly
            ? ContentBrowserItems.Where(item => item.IsFolder)
            : ContentBrowserItems;
        return source.ToLookup(item => item.ParentFolderId, StringComparer.Ordinal);
    }

    private static IEnumerable<ContentAssetViewModel> SortContentChildren(IEnumerable<ContentAssetViewModel> items) =>
        items.OrderByDescending(item => item.IsFolder).ThenBy(item => item.Name);

    private void EnterContentFolder(ContentAssetViewModel? folder)
    {
        _currentContentFolderId = folder?.Id;
        ExpandFolderPath(_currentContentFolderId);
        RefreshContentBrowserViews();
        SetStatus(folder is null ? "已进入内容根目录。" : $"已进入文件夹：{folder.Name}");
    }

    private void HandleContentKeyDown(KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox)
            return;

        if (e.Key == Key.Delete)
        {
            DeleteSelectedContentAsset();
            e.Handled = true;
        }
        else if (e.Key == Key.F2)
        {
            StartRenameSelectedContentAsset();
            e.Handled = true;
        }
    }

    private ContentAssetViewModel? GetSelectedContentAsset()
    {
        if (_contentFolderSelectionActive || IsFocusInside(ContentFolderListBox))
        {
            return ContentFolderListBox.SelectedItem is ContentAssetViewModel focusedFolder &&
                   !ReferenceEquals(focusedFolder, _rootContentFolder)
                ? focusedFolder
                : null;
        }

        if (ContentBrowserListBox.SelectedItem is ContentAssetViewModel asset)
            return asset;

        if (ContentFolderListBox.SelectedItem is ContentAssetViewModel folder && !ReferenceEquals(folder, _rootContentFolder))
            return folder;

        return null;
    }

    private void StartRenameSelectedContentAsset()
    {
        if (GetSelectedContentAsset() is ContentAssetViewModel item)
        {
            item.IsEditing = true;
            FocusContentRenameTextBox(item);
        }
    }

    private void CommitContentAssetRename(ContentAssetViewModel item)
    {
        if (_isCommittingContentAssetRename)
            return;

        _isCommittingContentAssetRename = true;
        try
        {
            string baseName = string.IsNullOrWhiteSpace(item.Name) ? "未命名资产" : item.Name.Trim();
            item.Name = MakeUniqueContentName(baseName, item.ParentFolderId, item);
            item.IsEditing = false;
            item.IsDirty = true;
            RefreshContentBrowserViews();
            PersistAssetLibrary();
        }
        finally
        {
            _isCommittingContentAssetRename = false;
        }
    }

    private void DeleteSelectedContentAsset()
    {
        if (GetSelectedContentAsset() is not ContentAssetViewModel item)
            return;

        var result = WpfMessageBox.Show(this, $"是否删除：{item.Name}？", "删除资产", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
            return;

        var deletingIds = new HashSet<string> { item.Id };
        foreach (var child in ContentBrowserItems.Where(child => child.ParentFolderId == item.Id).ToList())
            child.ParentFolderId = item.ParentFolderId;
        ContentBrowserItems.Remove(item);
        CloseEditorSessionsForAssetIds(deletingIds);
        RefreshContentBrowserViews();
        PersistAssetLibrary();
    }

    private void MoveOrCopyContentAsset(ContentAssetViewModel source, string? targetFolderId)
    {
        if (source.ParentFolderId == targetFolderId || IsDescendantFolder(targetFolderId, source.Id))
            return;

        var choice = ShowContentDropActionDialog(source.Name);
        ApplyContentDropAction(source, targetFolderId, choice);
    }

    private void ApplyContentDropAction(ContentAssetViewModel source, string? targetFolderId, ContentDropAction choice)
    {
        if (choice == ContentDropAction.Cancel)
            return;

        if (choice == ContentDropAction.Move)
            MoveContentAsset(source, targetFolderId);
        else if (choice == ContentDropAction.Copy)
            ContentBrowserItems.Add(CloneContentAssetForCopy(source, targetFolderId));

        RefreshContentBrowserViews();
        PersistAssetLibrary();
    }

    private void MoveContentAsset(ContentAssetViewModel source, string? targetFolderId)
    {
        source.Name = MakeUniqueContentName(source.Name, targetFolderId, source);
        source.ParentFolderId = targetFolderId;
        source.IsDirty = true;
    }

    private ContentDropAction ShowContentDropActionDialog(string assetName)
    {
        var result = ContentDropAction.Cancel;
        var dialog = new Window
        {
            Owner = this,
            Title = "拖拽资产",
            Width = 360,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(27, 32, 40)),
            Foreground = System.Windows.Media.Brushes.White,
            Content = CreateContentDropDialogContent(assetName, action =>
            {
                result = action;
            }),
        };

        if (dialog.Content is FrameworkElement root)
        {
            foreach (var button in FindVisualChildren<System.Windows.Controls.Button>(root))
            {
                button.Click += (_, _) => dialog.Close();
            }
        }

        dialog.ShowDialog();
        return result;
    }

    private static FrameworkElement CreateContentDropDialogContent(string assetName, Action<ContentDropAction> setResult)
    {
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock
        {
            Text = $"选择对资产“{assetName}”的操作：",
            Foreground = System.Windows.Media.Brushes.White,
            Margin = new Thickness(0, 0, 0, 16),
        });

        var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        AddDropDialogButton(buttons, "移动到此处", ContentDropAction.Move, setResult);
        AddDropDialogButton(buttons, "复制到此处", ContentDropAction.Copy, setResult);
        AddDropDialogButton(buttons, "取消", ContentDropAction.Cancel, setResult);
        panel.Children.Add(buttons);
        return panel;
    }

    private static void AddDropDialogButton(System.Windows.Controls.Panel panel, string text, ContentDropAction action, Action<ContentDropAction> setResult)
    {
        var button = new System.Windows.Controls.Button
        {
            Content = text,
            Margin = new Thickness(6, 0, 0, 0),
            Padding = new Thickness(10, 5, 10, 5),
            MinWidth = 78,
        };
        button.Click += (_, _) => setResult(action);
        panel.Children.Add(button);
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T target)
                yield return target;

            foreach (var nested in FindVisualChildren<T>(child))
                yield return nested;
        }
    }

    private bool IsDescendantFolder(string? candidateFolderId, string sourceFolderId)
    {
        while (candidateFolderId is not null)
        {
            if (candidateFolderId == sourceFolderId)
                return true;

            candidateFolderId = ContentBrowserItems.FirstOrDefault(item => item.Id == candidateFolderId)?.ParentFolderId;
        }

        return false;
    }

    private ContentAssetViewModel CloneContentAssetForCopy(ContentAssetViewModel source, string? targetFolderId)
    {
        var clone = CreateContentAsset(source.Kind, CreateUniqueContentName($"{source.Name}_Copy", targetFolderId));
        clone.ParentFolderId = targetFolderId;
        clone.EventGraphs = new ObservableCollection<GraphListItemViewModel>(source.EventGraphs.Select(CloneGraphItem));
        clone.Functions = new ObservableCollection<GraphListItemViewModel>(source.Functions.Select(CloneGraphItem));
        return clone;
    }

    private static GraphListItemViewModel CloneGraphItem(GraphListItemViewModel source) => new()
    {
        Kind = source.Kind,
        Name = source.Name,
        Graph = new GraphFileModel
        {
            Name = source.Graph.Name,
            AssetKind = source.Graph.AssetKind,
            Nodes = source.Graph.Nodes.Select(CloneNodeFile).ToList(),
            Connections = source.Graph.Connections.Select(conn => new ConnectionFileModel
            {
                SourceNodeId = conn.SourceNodeId,
                SourcePinName = conn.SourcePinName,
                TargetNodeId = conn.TargetNodeId,
                TargetPinName = conn.TargetPinName,
            }).ToList(),
        },
        IsDirty = true,
        IsCompileDirty = true,
        IsPublicToLibrary = source.IsPublicToLibrary,
    };

    private string MakeUniqueContentName(string baseName, string? parentFolderId, ContentAssetViewModel? exclude = null)
    {
        if (!HasSameLevelContentName(baseName, parentFolderId, exclude))
            return baseName;

        int index = 1;
        string name;
        do
        {
            name = $"{baseName}{index++}";
        }
        while (HasSameLevelContentName(name, parentFolderId, exclude));

        return name;
    }

    private bool HasSameLevelContentName(string name, string? parentFolderId, ContentAssetViewModel? exclude = null) =>
        ContentBrowserItems.Any(item =>
            !ReferenceEquals(item, exclude) &&
            item.ParentFolderId == parentFolderId &&
            string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));

    private void ExpandFolderPath(string? folderId)
    {
        ExpandFolderPath(folderId, BuildContentAssetLookup());
    }

    private static void ExpandFolderPath(string? folderId, IReadOnlyDictionary<string, ContentAssetViewModel> assetById)
    {
        if (folderId is null ||
            !assetById.TryGetValue(folderId, out var current) ||
            !current.IsFolder)
        {
            return;
        }

        folderId = current.Id;
        while (folderId is not null)
        {
            if (!assetById.TryGetValue(folderId, out var folder) || !folder.IsFolder)
                return;

            folder.IsTreeExpanded = true;
            folderId = folder.ParentFolderId;
        }
    }

    private void FocusContentRenameTextBox(ContentAssetViewModel item)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            System.Windows.Controls.ListBox targetList = item.IsFolder && _contentFolderSelectionActive
                ? ContentFolderListBox
                : ContentBrowserListBox;
            if (targetList.ItemContainerGenerator.ContainerFromItem(item) is not ListBoxItem container)
                return;

            var tb = FindVisualChild<TextBox>(container);
            if (tb is null)
                return;

            tb.Focus();
            tb.SelectAll();
        }), DispatcherPriority.Render);
    }

    private ContentAssetViewModel? GetContentAssetFromMouseEvent(MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
            return null;

        while (source is not null)
        {
            if (source is FrameworkElement { DataContext: ContentAssetViewModel item })
                return item;

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private static bool IsVisualAncestor(DependencyObject ancestor, DependencyObject source)
    {
        var current = source;
        while (current is not null)
        {
            if (ReferenceEquals(current, ancestor))
                return true;

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private static bool HasVisualAncestor<T>(DependencyObject source) where T : DependencyObject
    {
        var current = source;
        while (current is not null)
        {
            if (current is T)
                return true;

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

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

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T target)
                return target;

            var nested = FindVisualChild<T>(child);
            if (nested is not null)
                return nested;
        }

        return null;
    }

    private static bool IsFocusInside(DependencyObject root)
    {
        if (Keyboard.FocusedElement is not DependencyObject focused)
            return false;

        var current = focused;
        while (current is not null)
        {
            if (ReferenceEquals(current, root))
                return true;

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    #endregion

    #region 窗口生命周期

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
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
        foreach (var session in _editorSessions.ToList())
        {
            session.DetachedWindow?.CloseFromOwner();
            session.DetachedWindow = null;
        }
    }

    #endregion

    #region 工具栏事件 - 执行

    private async void RunGraph_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureCompiledBeforeRun())
            return;

        var activeSessionController = _activeEditorSession is null ? _activeAssetController : GetSessionActiveAssetController(_activeEditorSession);
        if (_activeContentAsset?.Kind != ContentAssetKind.Script || !ReferenceEquals(activeSessionController, _graphListController))
        {
            WpfMessageBox.Show(this, "只有脚本里的事件图可以直接执行。请从内容浏览器打开脚本，并进入事件图。", "不能执行", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        CommitInspectorAndSnapshotAllSessions();
        await _executionController.RunAsync();
    }

    #endregion

    #region 画布交互 - 鼠标事件

    private void NodeCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _nodeDragSelectionController.HandleNodeCardMouseDown(sender, e);
    }

    private void NodeHeader_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _nodeDragSelectionController.BeginNodeDrag(sender, e, _pinConnectionController.IsConnecting);
    }

    private void NodeHeader_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        _nodeDragSelectionController.MoveNodeDrag(e, _pinConnectionController.IsConnecting);
    }

    private void NodeHeader_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _nodeDragSelectionController.EndNodeDrag(sender);
    }

    private void GraphViewport_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var surface = GetActiveEditorSurface();
        // 节点菜单打开时，点击菜单内部不处理画布事件
        if (surface.NodePalette.Visibility == Visibility.Visible)
        {
            var posInPalette = e.GetPosition(surface.NodePalette);
            if (posInPalette.X >= 0 && posInPalette.X <= surface.NodePalette.ActualWidth &&
                posInPalette.Y >= 0 && posInPalette.Y <= surface.NodePalette.ActualHeight)
            {
                return;
            }
        }

        if (!IsGraphBlankSource(e.OriginalSource as DependencyObject)) return;

        if (_pinConnectionController.IsConnecting)
        {
            _pinConnectionController.Cancel("已取消连线。");
            e.Handled = true;
            return;
        }

        _nodeDragSelectionController.BeginSelection(e);
    }

    private void GraphViewport_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        var surface = GetActiveEditorSurface();
        var viewportPos = e.GetPosition(surface.GraphViewport);
        _nodeDragSelectionController.UpdateMousePosition(e);

        if (_nodeDragSelectionController.IsDragging || _pinConnectionController.IsConnecting)
            _canvasPanZoomController.EdgePan(viewportPos);

        // 右键拖动检测：移动超过阈值则转为平移
        if (_rightClickPending && e.RightButton == MouseButtonState.Pressed)
        {
            var delta = viewportPos - _rightClickStartPos;
            if (Math.Abs(delta.X) > 3 || Math.Abs(delta.Y) > 3)
            {
                _rightClickPending = false;
                _canvasPanZoomController.BeginPan(viewportPos);
            }
        }

        if (_canvasPanZoomController.IsPanning)
        {
            if (e.RightButton != MouseButtonState.Pressed)
            {
                _canvasPanZoomController.EndPan();
            }
            _canvasPanZoomController.MovePan(e);
        }

        _pinConnectionController.Move(_nodeDragSelectionController.LastMousePosition);
        _nodeDragSelectionController.UpdateSelectionRectangle();
    }

    private void GraphViewport_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_pinConnectionController.HandleViewportMouseLeftButtonUp(e))
            return;

        _nodeDragSelectionController.CompleteSelection(e);
    }

    private void GraphViewport_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsGraphBlankSource(e.OriginalSource as DependencyObject)) return;

        var surface = GetActiveEditorSurface();
        _nodeDragSelectionController.CancelSelection();
        _nodeDragSelectionController.CancelDrag();
        surface.GraphViewport.ReleaseMouseCapture();
        _nodeDragSelectionController.SetCanvasFocusActive(true);
        surface.GraphViewport.Focus();

        // 判断是点击还是拖动：记录起始位置，尝试移动后决定
        _rightClickPending = true;
        _rightClickStartPos = e.GetPosition(surface.GraphViewport);
        e.Handled = true;
    }

    private void GraphViewport_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_rightClickPending)
        {
            // 右键点击（非拖动），弹出节点菜单
            _rightClickPending = false;
            OpenNodePalette(_rightClickStartPos);
            e.Handled = true;
            return;
        }

        if (!_canvasPanZoomController.IsPanning)
        {
            GetActiveEditorSurface().GraphViewport.ReleaseMouseCapture();
            return;
        }

        _canvasPanZoomController.EndPan();
        e.Handled = true;
    }

    private void GraphViewport_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var surface = GetActiveEditorSurface();
        if (surface.NodePalette.Visibility == Visibility.Visible)
        {
            var pos = e.GetPosition(surface.NodePalette);
            if (pos.X >= 0 && pos.X <= surface.NodePalette.ActualWidth &&
                pos.Y >= 0 && pos.Y <= surface.NodePalette.ActualHeight)
            {
                return;
            }
        }

        _canvasPanZoomController.Zoom(e);
        e.Handled = true;
    }

    private void NodePaletteScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer) return;

        scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta);
        e.Handled = true;
    }

    #endregion

    #region 引脚交互

    private void PinButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pinConnectionController.HandlePinMouseLeftButtonDown(sender, e);
    }

    private void PinButton_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _pinConnectionController.HandlePinMouseLeftButtonUp(sender, e);
    }

    #endregion

    #region 连线交互

    private void ConnectionPath_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        _pinConnectionController.HandleConnectionDoubleClick(sender, e);
    }

    private void ConnectionPath_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2)
            _pinConnectionController.HandleConnectionDoubleClick(sender, e);
    }

    private void ConnectionPath_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pinConnectionController.HandleConnectionMouseLeftButtonDown(sender, e);
    }

    private void ConnectionPath_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pinConnectionController.HandleConnectionMouseRightButtonDown(sender, e);
    }

    private void DeleteConnectionPath_Click(object sender, RoutedEventArgs e)
    {
        _pinConnectionController.DeleteSelectedConnectionPath();
    }

    private void AddRerouteToConnectionPath_Click(object sender, RoutedEventArgs e)
    {
        _pinConnectionController.InsertRerouteOnSelectedPath();
    }

    #endregion

    #region 选择操作

    private void SelectNode(NodeBaseViewModel? node)
    {
        _nodeDragSelectionController.SelectNode(node);
    }

    private void ClearNodeSelectionForConnection()
    {
        foreach (var node in _editorService.Nodes)
            node.IsSelected = false;

        LoadNodeToInspector(null);
    }

    #endregion

    #region 键盘快捷键

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var surface = GetActiveEditorSurface();
        // Esc：取消执行或取消连线
        if (e.Key == Key.Escape)
        {
            if (_executionController.IsRunning)
            {
                _executionController.Cancel();
            }
            else if (_pinConnectionController.IsConnecting)
            {
                _pinConnectionController.Cancel("已取消连线。");
            }
            e.Handled = true;
            return;
        }

        if (IsFocusInside(surface.GraphListBox) || IsFocusInside(surface.FunctionListBox))
        {
            if (Keyboard.FocusedElement is not TextBoxBase && GetFocusedGraphController() is { } controller)
            {
                controller.HandleKeyDown(e);
                if (e.Handled) return;
            }

            return;
        }

        if (IsFocusInside(ContentBrowserListBox) || IsFocusInside(ContentFolderListBox))
        {
            HandleContentKeyDown(e);
            return;
        }

        // TextBox/RichTextBox keep their own copy/select shortcuts.
        if (Keyboard.FocusedElement is TextBoxBase) return;

        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            {
                _graphCommandService.Undo();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Y || (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Shift) != 0))
            {
                _graphCommandService.Redo();
                e.Handled = true;
                return;
            }
        }

        if (_pinConnectionController.HandleKeyDown(e))
        {
            e.Handled = true;
            return;
        }

        if (_nodeDragSelectionController.HandleKeyDown(e))
        {
            e.Handled = true;
        }
    }

    private GraphListController? GetFocusedGraphController()
    {
        var surface = GetActiveEditorSurface();
        if (IsFocusInside(surface.FunctionListBox))
            return _functionListController;
        if (IsFocusInside(surface.GraphListBox))
            return _graphListController;
        return null;
    }

    #endregion

    #region 属性面板

    private void LoadNodeToInspector(NodeBaseViewModel? node)
    {
        _inspectorController.LoadNode(node);
    }

    private void ApplyInspectorChanges()
    {
        _inspectorController.ApplyChanges();
    }

    private void CommitInspectorAndSnapshotActive()
    {
        if (GetOperationEditorSession() is { } session)
            CommitSessionToAsset(session, applyInspector: ReferenceEquals(session, _activeEditorSession));
    }

    private void CommitInspectorAndSnapshotAllSessions()
    {
        CommitAllSessionsToAssets(applyInspectorForActive: true);
    }

    private void InspectorField_TextChanged(object sender, TextChangedEventArgs e) => ApplyInspectorChanges();
    private void InspectorField_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyInspectorChanges();
    private void InspectorField_CheckedChanged(object sender, RoutedEventArgs e) => ApplyInspectorChanges();

    private void ToDoSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _inspectorController.ToDoSearchChanged();
    }

    private void ToDoTargetListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_inspectorController.ToDoTargetSelected())
            CommitInspectorAndSnapshotActive();
    }

    private void BrowseFindImagePath_Click(object sender, RoutedEventArgs e)
    {
        _inspectorController.BrowseFindImagePath();
    }

    private void BrowseStartProgramPath_Click(object sender, RoutedEventArgs e)
    {
        _inspectorController.BrowseStartProgramPath();
    }

    private void SelectWindowInputMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_inspectorController is null) return;
        _inspectorController.SelectWindowInputModeChanged();
    }

    private void SelectWindowAutoComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_inspectorController is null) return;
        _inspectorController.SelectWindowAutoChanged();
    }

    private void RefreshWindowList_Click(object sender, RoutedEventArgs e)
    {
        _inspectorController.RefreshWindowList();
    }

    private void FindImageSourceModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_inspectorController is null) return;
        _inspectorController.FindImageSourceModeChanged();
    }

    private void BrowseFindImageSourcePath_Click(object sender, RoutedEventArgs e)
    {
        _inspectorController.BrowseFindImageSourcePath();
    }

    private void CommonKeyChordAddButton_Click(object sender, RoutedEventArgs e)
    {
        _inspectorController.AddCommonKeyChordKey();
    }

    private void AddParameterButton_Click(object sender, RoutedEventArgs e)
    {
        _inspectorController.AddParameter();
    }

    private void CommonModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_inspectorController is null) return;
        _inspectorController.CommonModeChanged();
    }

    private void CommonWindowComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_inspectorController is null) return;
        _inspectorController.CommonWindowChanged();
    }

    private void CommonWindowRefreshButton_Click(object sender, RoutedEventArgs e)
    {
        _inspectorController.RefreshCommonWindowList();
    }

    private void CommonEnumComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_inspectorController is null) return;
        _inspectorController.CommonEnumChanged();
    }

    private void CommonBrowseFileButton_Click(object sender, RoutedEventArgs e)
    {
        _inspectorController.BrowseCommonFile();
    }

    #endregion

    #region 日志面板

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

    #endregion

    #region 拖拽导入

    private void Window_DragEnter(object sender, DragEventArgs e)
    {
        _graphImportDropController.HandleDragEnter(e);
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        _graphImportDropController.HandleDrop(e);
    }

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

    private Point ViewportToGraph(Point viewportPoint)
    {
        return _canvasPanZoomController.ViewportToGraph(viewportPoint);
    }

    private void FitGraphToView()
    {
        _canvasPanZoomController.FitGraphToView();
    }

    private bool TryGetPinAtPosition(Point position, out PinViewModel? pin)
    {
        var hit = GetActiveEditorSurface().GraphSurface.InputHitTest(position) as DependencyObject;
        if (TryGetPinFromSource(hit, out pin))
            return true;

        return TryGetNearestPinAtPosition(position, out pin);
    }

    private bool TryGetNearestPinAtPosition(Point position, out PinViewModel? pin)
    {
        const double hitRadius = 24.0;
        double bestDistanceSquared = hitRadius * hitRadius;
        PinViewModel? bestPin = null;

        foreach (var candidate in _editorService.Nodes.SelectMany(node => node.InputPins.Concat(node.OutputPins)))
        {
            var anchor = candidate.Owner.GetPinAnchor(candidate);
            double x = candidate.Owner.X + anchor.X;
            double y = candidate.Owner.Y + anchor.Y;
            double dx = position.X - x;
            double dy = position.Y - y;
            double distanceSquared = dx * dx + dy * dy;
            if (distanceSquared > bestDistanceSquared)
                continue;

            bestDistanceSquared = distanceSquared;
            bestPin = candidate;
        }

        pin = bestPin;
        return pin is not null;
    }

    private static bool TryGetPinFromSource(DependencyObject? source, out PinViewModel? pin)
    {
        var current = source;
        while (current is not null)
        {
            if (current is FrameworkElement element && element.DataContext is PinViewModel dataPin)
            {
                pin = dataPin;
                return true;
            }
            current = VisualTreeHelper.GetParent(current);
        }
        pin = null;
        return false;
    }

    private bool IsGraphBlankSource(DependencyObject? source)
    {
        var current = source;
        while (current is not null)
        {
            if (current is FrameworkElement element)
            {
                if (element.DataContext is NodeBaseViewModel or PinViewModel or ConnectionViewModel or ConnectionPathViewModel)
                    return false;

                var surface = GetActiveEditorSurface();
                if (ReferenceEquals(element, surface.GraphSurface) || ReferenceEquals(element, surface.GraphViewport))
                    return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
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

    #region 引脚锚点更新

    private void PinAnchor_Loaded(object sender, RoutedEventArgs e)
    {
        UpdatePinAnchorFromElement(sender as FrameworkElement);
    }

    private void PinAnchor_LayoutUpdated(object? sender, EventArgs e)
    {
        UpdatePinAnchorFromElement(sender as FrameworkElement);
    }

    private static void UpdatePinAnchorFromElement(FrameworkElement? element)
    {
        if (element is null || element.DataContext is not PinViewModel pin)
            return;

        var nodeRoot = FindNodeRootElement(element);
        if (nodeRoot is null || element.ActualWidth <= 0 || element.ActualHeight <= 0)
            return;

        try
        {
            var transform = element.TransformToAncestor(nodeRoot);
            var localTopLeft = transform.Transform(new Point(0, 0));
            pin.AnchorPoint = new Point(
                localTopLeft.X + element.ActualWidth / 2.0,
                localTopLeft.Y + element.ActualHeight / 2.0);
        }
        catch (InvalidOperationException)
        {
            // Layout tree can be transient while WPF is remeasuring. Safe to ignore.
        }
    }

    private static FrameworkElement? FindNodeRootElement(DependencyObject? source)
    {
        DependencyObject? current = source;
        FrameworkElement? lastMatch = null;
        while (current is not null)
        {
            if (current is Border border && border.DataContext is NodeBaseViewModel)
                lastMatch = border;
            current = VisualTreeHelper.GetParent(current);
        }
        return lastMatch;
    }

    #endregion

    #region 节点右键菜单

    private void InitializeNodePalette()
    {
        // NodePaletteController builds menu entries from NodeRegistry definitions on demand.
    }

    private void OpenNodePalette(Point viewportPos)
    {
        _pinConnectionController.CancelPendingPaletteConnection();
        _nodePaletteController.Open(viewportPos);
    }

    private void OpenNodePaletteForConnection(Point viewportPos)
    {
        _nodePaletteController.Open(viewportPos);
    }

    private void CloseNodePalette()
    {
        _pinConnectionController.CancelPendingPaletteConnection();
        _nodePaletteController.Close();
    }

    private void NodePaletteSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _nodePaletteController.Filter(GetActiveEditorSurface().NodePaletteSearchBox.Text.Trim());
    }

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var sourceSurface = FindAncestor<EditorSurfaceControl>(e.OriginalSource as DependencyObject);
        HandleEditorSurfacePreviewMouseDown(sourceSurface, e);
    }

    internal void HandleEditorSurfacePreviewMouseDown(EditorSurfaceControl? sourceSurface, MouseButtonEventArgs e)
    {
        foreach (var session in _editorSessions.ToList())
        {
            var context = session.SurfaceContext;
            if (context is null || !context.IsConfigured || !context.NodePaletteController.IsOpen)
                continue;

            if (!ReferenceEquals(context.Surface, sourceSurface))
            {
                context.CloseNodePalette();
                continue;
            }

            var pos = e.GetPosition(context.Surface.NodePalette);
            if (!context.NodePaletteController.IsPointInside(pos))
                context.CloseNodePalette();
        }
    }

    #endregion
}
