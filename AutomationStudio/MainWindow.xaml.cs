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
    private MousePickController _mousePickController = null!;
    private ScriptHotkeyService _scriptHotkeyService = null!;
    private ScriptRunManager _scriptRunManager = null!;
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
        DataContext = this;
        _graphCompileService = new GraphCompileService(_callableGraphResolver);
        InitializeComponent();
        InitializeControllers();
        InitializeServices();
        InitializeEditor();
        SetupNotifyIcon();
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
            IsExecuting = _scriptRunManager.IsAnyRunning || (_executionController?.IsRunning ?? false);
            StopExecutionButton.Visibility = IsExecuting ? Visibility.Visible : Visibility.Collapsed;
        });
    }

    private void StopExecution_Click(object sender, RoutedEventArgs e)
    {
        _scriptRunManager.StopAll();
        _executionController?.Cancel();
    }

    private void OnExecutionStateChanged(bool isRunning) { UpdateExecutionUI(); }
    private void OnScriptRunningStateChanged()
    {
        Dispatcher.InvokeAsync(() =>
        {
            IsExecuting = _scriptRunManager.IsAnyRunning;
            StopExecutionButton.Visibility = IsExecuting ? Visibility.Visible : Visibility.Collapsed;
        });
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
