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
using AutomationStudioWpf.GraphCore;
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
    private readonly AssetCloneService _assetCloneService = new();
    private NodeFactory _nodeFactory = new();
    private readonly GraphLibraryService _graphLibraryService = new();
    private readonly CallableGraphResolver _callableGraphResolver = new();
    private readonly CustomEventResolver _customEventResolver = new();
    private readonly GraphCompileService _graphCompileService;
    private readonly WorkspaceCommitService _workspaceCommitService;
    private readonly WorkspacePersistenceService _workspacePersistenceService;
    private readonly WorkspaceReadModelService _workspaceReadModelService;
    private readonly AssetCompileCoordinator _assetCompileCoordinator;
    private readonly ScriptExecutionCoordinator _scriptExecutionCoordinator;
    private readonly FinalCodePreviewService _finalCodePreviewService = new();
    private readonly PythonEnvironmentService _pythonEnvironmentService = new();
    private readonly NodeRegistry _nodeRegistry = NodeRegistry.CreateDefault();
    private readonly Adapters.RuntimeAdapters _runtimeAdapters;
    private readonly Runtime.GraphRuntimeExecutor _runtimeExecutor;
    private readonly EditorWorkspace _workspace = new();
    private readonly ObservableCollection<GraphListItemViewModel> _emptyGraphListItems = [];
    private readonly ObservableCollection<GraphListItemViewModel> _emptyFunctionListItems = [];
    private readonly ObservableCollection<NodeBaseViewModel> _emptyNodes = [];
    private readonly ObservableCollection<ConnectionPathViewModel> _emptyConnectionPaths = [];
    private ObservableCollection<EditorSessionViewModel> _editorSessions => _workspace.Sessions;
    private ObservableCollection<EditorSessionViewModel> _mainEditorSessions => _workspace.MainSessions;
    private EditorSessionViewModel? _activeEditorSession
    {
        get => _workspace.ActiveSession;
        set => _workspace.ActiveSession = value;
    }
    private EditorSessionViewModel? _lastMainEditorSession
    {
        get => _workspace.LastMainSession;
        set => _workspace.LastMainSession = value;
    }
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
    private MousePickController _mousePickController = null!;
    private ScriptHotkeyService _scriptHotkeyService = null!;
    private HotkeyCaptureCoordinator _hotkeyCaptureCoordinator = null!;
    private ScriptRunManager _scriptRunManager = null!;
    private readonly HashSet<string> _reportedHookFailures = new(StringComparer.Ordinal);
    private FinalCodePreviewWindow? _finalCodePreviewWindow;
    private ContentAssetViewModel? _activeContentAsset;
    private string? _currentContentFolderId;
    private Point _contentDragStartPoint;
    private bool _contentFolderSelectionActive;
    private bool _contentBrowserContextTargetsAsset;
    private ContentAssetViewModel? _contentBrowserContextTargetAsset;
    private bool _suppressGraphChangedDirty;
    private bool _isCommittingContentAssetRename;
    private readonly ContentAssetViewModel _rootContentFolder = new()
    {
        Kind = ContentAssetKind.Folder,
        Name = "内容",
    };
    // 运行状态
    private bool _isClosing;
    private bool _isExecuting;
    private bool _isActiveAssetCompileDirty;

    public bool IsExecuting
    {
        get => _isExecuting;
        set { if (_isExecuting != value) { _isExecuting = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExecuting))); } }
    }

    // 右键菜单状态
    private bool _rightClickPending;
    private Point _rightClickStartPos;

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainWindow()
    {
        LoadAppSettings();
        DataContext = this;
        _graphCompileService = new GraphCompileService(_callableGraphResolver);
        var wpfRuntimeUi = new WpfRuntimeUiAdapter(this);
        _pythonEnvironmentService.SetNotificationSink(wpfRuntimeUi);
        _runtimeAdapters = new Adapters.RuntimeAdapters(_pythonEnvironmentService, wpfRuntimeUi);
        _runtimeExecutor = new Runtime.GraphRuntimeExecutor(_runtimeAdapters, _nodeRegistry);
        InitializeComponent();
        _workspaceCommitService = new WorkspaceCommitService(GetSessionActiveAssetController);
        _workspaceReadModelService = new WorkspaceReadModelService(() => ContentBrowserItems);
        _workspacePersistenceService = new WorkspacePersistenceService(
            _graphLibraryService,
            () => ContentBrowserItems,
            () => _activeContentAsset?.Id,
            () =>
            {
                if (_scriptHotkeyService is not null)
                    RefreshScriptHotkeys();
            });
        _assetCompileCoordinator = new AssetCompileCoordinator(
            _graphCompileService,
            _workspaceCommitService,
            () => _editorSessions,
            () => _activeEditorSession,
            () => _activeContentAsset,
            () => ContentBrowserItems,
            ApplyInspectorChanges);
        _scriptExecutionCoordinator = new ScriptExecutionCoordinator(
            () => _executionController,
            _assetCompileCoordinator.ResolveActiveAsset,
            PrepareActiveScriptForRun);
        Icon = WindowIconHelper.AppIcon;
        InitializeControllers();
        InitializeServices();
        InitializeEditor();
        SetupNotifyIcon();
        UpdateChromeState();
    }

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

    #region 辅助方法

    
    private void UpdateExecutionUI()
    {
        Dispatcher.InvokeAsync(() =>
        {
            bool isManualDebugRunning = _executionController?.IsManualDebugRunning ?? false;
            bool isHotkeyRunActive = _scriptRunManager.IsAnyHotkeyRunActive;
            IsExecuting = isManualDebugRunning || isHotkeyRunActive;
            UpdateExecutionFreezeState();
            StopExecutionButton.Visibility = IsExecuting ? Visibility.Visible : Visibility.Collapsed;
            StopExecutionButton.ToolTip = isManualDebugRunning && isHotkeyRunActive
                ? "停止手动调试和全部热键脚本"
                : isHotkeyRunActive
                    ? "停止全部热键脚本"
                    : "停止手动调试";
            UpdateEditorToolbarVisibility();
        });
    }

    public bool IsActiveAssetCompileDirty
    {
        get => _isActiveAssetCompileDirty;
        private set
        {
            if (_isActiveAssetCompileDirty == value)
                return;

            _isActiveAssetCompileDirty = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActiveAssetCompileDirty)));
        }
    }

    private void StopExecution_Click(object sender, RoutedEventArgs e)
    {
        _scriptRunManager.StopAll(ScriptRunStopReason.Toolbar);
        _executionController?.Cancel(ExecutionStopReason.Toolbar);
    }

    private void OnExecutionStateChanged(bool isRunning) { UpdateExecutionUI(); }
    private void OnScriptRunningStateChanged()
    {
        if (!_isClosing)
            RefreshScriptHotkeys();
        UpdateExecutionUI();
    }

    private void UpdateExecutionFreezeState()
    {
        bool isManualDebugRunning = _executionController?.IsManualDebugRunning ?? false;
        bool isHotkeyRunActive = _scriptRunManager.IsAnyHotkeyRunActive;
        string message = isManualDebugRunning && isHotkeyRunActive
            ? "Esc 仅停止手动调试；终止热键停止对应脚本；顶部停止按钮停止全部。"
            : isHotkeyRunActive
                ? "请使用对应终止热键或顶部停止按钮结束脚本。"
                : "按 Esc 或顶部停止按钮结束调试。";

        foreach (var session in _editorSessions)
        {
            if (session.Surface is { } surface)
            {
                surface.IsExecutionFrozen = IsExecuting;
                surface.ExecutionFreezeMessage = message;
            }
        }

        if (_bootstrapEditorSurface is { } bootstrapSurface)
        {
            bootstrapSurface.IsExecutionFrozen = IsExecuting;
            bootstrapSurface.ExecutionFreezeMessage = message;
        }
    }
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

    private void UpdateChromeState()
    {
        if (WindowMaximizeGlyph is null)
            return;

        WindowMaximizeGlyph.Text = WindowState == WindowState.Maximized ? "❐" : "▢";
    }

    private void Window_StateChanged(object sender, EventArgs e)
    {
        UpdateChromeState();
    }

    private void WindowMinimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void WindowMaximizeRestore_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void WindowClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
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
            session.IsActive = ReferenceEquals(session, _activeEditorSession);
            session.RefreshDirtyState();
            session.DetachedWindow?.RefreshChrome();
        }

        RefreshMainEditorSessions();
        EditorWindowBar.Visibility = _mainEditorSessions.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateEditorToolbarVisibility();
    }

    private void UpdateEditorToolbarVisibility()
    {
        if (EditorActionToolbarGroup is null || RunGraphButton is null)
            return;

        bool hasEditableAsset = _activeContentAsset?.Kind is ContentAssetKind.Script or ContentAssetKind.FunctionLibrary;
        EditorActionToolbarGroup.Visibility = hasEditableAsset ? Visibility.Visible : Visibility.Collapsed;

        bool canRunScript = _activeContentAsset?.Kind == ContentAssetKind.Script;
        RunGraphButton.IsEnabled = canRunScript && !IsExecuting;
        RunGraphButton.ToolTip = canRunScript
            ? "执行当前脚本的主事件图。"
            : "只有脚本资产可执行。";
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
