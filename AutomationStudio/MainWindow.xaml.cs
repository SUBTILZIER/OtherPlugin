using System.IO;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
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
public partial class MainWindow : Window
{
    // 核心服务
    private readonly GraphEditorService _editorService = new();
    private readonly NodeClipboardService _clipboardService = new();
    private readonly NodeFactory _nodeFactory = new();
    private readonly GraphLibraryService _graphLibraryService = new();
    private readonly CallableGraphResolver _callableGraphResolver = new();
    private readonly GraphCompileService _graphCompileService;
    private readonly NodeRegistry _nodeRegistry = NodeRegistry.CreateDefault();
    private GraphCommandService _graphCommandService = null!;
    private ExecutionController _executionController = null!;
    private NodePaletteController _nodePaletteController = null!;
    private GraphListController _graphListController = null!;
    private GraphListController _functionListController = null!;
    private GraphListController _macroListController = null!;
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
        _editorService.GraphChanged += OnGraphChanged;
        _editorService.StatusChanged += SetStatus;

        // 日志更新
        Logger.Entries.CollectionChanged += (_, _) => _logPanelController.Refresh();

        // 窗口关闭时释放按键，并处理未保存图谱。
        Closing += Window_Closing;

        // 全局鼠标点击检测：点击节点菜单外部时关闭菜单
        PreviewMouseDown += Window_PreviewMouseDown;
    }

    private void InitializeControllers()
    {
        _graphCommandService = new GraphCommandService(
            _editorService,
            () => GetActiveGraphKind() ?? GraphAssetKind.EventGraph,
            SyncNodeFactorySequence,
            SetStatus);

        _executionController = new ExecutionController(
            this,
            _editorService,
            new Runtime.GraphRuntimeExecutor(nodeRegistry: _nodeRegistry, adapters: new Adapters.RuntimeAdapters()),
            new GraphCore.GraphValidator(),
            RunGraphButton,
            GetRuntimeCallableFunctions,
            GetRuntimeCallableMacros,
            SetStatus);

        _graphListController = new GraphListController(
            this,
            _editorService,
            _graphLibraryService,
            _nodeFactory,
            GraphListBox,
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
            FunctionListBox,
            FunctionListItems,
            SyncNodeFactorySequence,
            PersistAssetLibrary,
            GraphAssetKind.Function,
            "函数",
            "新函数_",
            SetStatus);

        _macroListController = new GraphListController(
            this,
            _editorService,
            _graphLibraryService,
            _nodeFactory,
            MacroListBox,
            MacroListItems,
            SyncNodeFactorySequence,
            PersistAssetLibrary,
            GraphAssetKind.Macro,
            "宏",
            "新宏_",
            SetStatus);

        _nodePaletteController = new NodePaletteController(
            NodePalette,
            NodePaletteSearchBox,
            NodePaletteContent,
            _nodeFactory,
            _editorService,
            _graphCommandService,
            _nodeRegistry,
            GetCallableFunctions,
            GetCallableMacros,
            GetCallableCustomEvents,
            GetActiveGraphKind,
            SnapshotActiveAsset,
            ViewportToGraph,
            () => new System.Windows.Size(GraphViewport.ActualWidth, GraphViewport.ActualHeight),
            SelectNode,
            node => _pinConnectionController.TryAutoConnectNewNode(node));

        _canvasPanZoomController = new CanvasPanZoomController(
            GraphViewport,
            GraphZoomTransform,
            GraphPanTransform,
            _editorService,
            SetStatus);

        _nodeDragSelectionController = new NodeDragSelectionController(
            _editorService,
            _graphCommandService,
            _clipboardService,
            _nodeFactory,
            GraphViewport,
            SelectionRectangle,
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
            InspectorHintTextBlock,
            NodeTitleTextBox,
            NodeNumberTextBlock,
            ParameterInspectorPanel,
            AddParameterButton,
            ParameterInspectorTitle,
            ParameterRowsPanel,
            FindImageInspectorPanel,
            FindImageSourceModeComboBox,
            FindImageSourcePathLabel,
            FindImageSourcePathPanel,
            FindImageSourcePathTextBox,
            FindImagePathTextBox,
            FindImageThresholdTextBox,
            FindImageUseRegionCheckBox,
            FindImageRegionXTextBox,
            FindImageRegionYTextBox,
            FindImageRegionWidthTextBox,
            FindImageRegionHeightTextBox,
            MouseLeftInspectorPanel,
            MousePositionXTextBox,
            MousePositionYTextBox,
            MouseClickOperationModeComboBox,
            MouseButtonComboBox,
            KeyboardInspectorPanel,
            KeyboardKeyComboBox,
            KeyboardOperationModeComboBox,
            ScrollWheelInspectorPanel,
            ScrollWheelActionComboBox,
            ScrollWheelSpeedTextBox,
            ScrollWheelIntervalTextBox,
            ScrollWheelDurationTextBox,
            IfInspectorPanel,
            IfConditionComboBox,
            ForLoopInspectorPanel,
            ForLoopCountTextBox,
            ForLoopEndConditionComboBox,
            WhileLoopInspectorPanel,
            WhileLoopConditionComboBox,
            WhileLoopModeComboBox,
            WhileMaxIterationsLabel,
            WhileMaxIterationsTextBox,
            DelayInspectorPanel,
            DelayMsTextBox,
            MouseMoveInspectorPanel,
            MouseMovePositionXTextBox,
            MouseMovePositionYTextBox,
            StartProgramInspectorPanel,
            StartProgramPathTextBox,
            StartProgramWaitTimeoutTextBox,
            StartProgramFailureActionComboBox,
            StartProgramRetryCountTextBox,
            PrintLogInspectorPanel,
            PrintLogMessageTextBox,
            SelectWindowInspectorPanel,
            SelectWindowInputModeComboBox,
            SelectWindowManualPanel,
            SelectWindowProcessNameTextBox,
            SelectWindowAutoPanel,
            SelectWindowAutoComboBox,
            ToDoInspectorPanel,
            ToDoSearchBox,
            ToDoTargetListBox,
            ToDoTargetTitleTextBox,
            ToDoTargetNumberTextBox,
            ToDoReturnAfterTargetCheckBox,
            CommonInspectorPanel,
            CommonKeyChordAddPanel,
            CommonKeyChordKeyComboBox,
            CommonModePanel,
            CommonModeComboBox,
            CommonWindowPickerPanel,
            CommonWindowComboBox,
            CommonEnumPanel,
            CommonEnumLabel,
            CommonEnumComboBox,
            CommonBrowseFileButton,
            CommonTextLabel,
            CommonTextBox,
            CommonText2Label,
            CommonText2Box,
            CommonText3Label,
            CommonText3Box,
            CommonNumberLabel,
            CommonNumberBox,
            CommonNumber2Label,
            CommonNumber2Box,
            CommonNumber3Label,
            CommonNumber3Box,
            CommonNumber4Label,
            CommonNumber4Box,
            CommonFlagCheckBox,
            CommonHelpTextBlock);

        _pinConnectionController = new PinConnectionController(
            _editorService,
            _graphCommandService,
            _nodeFactory,
            GraphViewport,
            PreviewConnectionPath,
            ViewportToGraph,
            position => TryGetPinAtPosition(position, out var pin) ? pin : null,
            OpenNodePaletteForConnection,
            SelectNode,
            ClearNodeSelectionForConnection,
            _nodeDragSelectionController.SetCanvasFocusActive,
            SetStatus);

        _logPanelController = new LogPanelController(
            LogRichTextBox,
            FilterAllRadio,
            FilterInfoRadio,
            FilterWarnRadio,
            FilterErrorRadio);

        _graphImportDropController = new GraphImportDropController(this, _graphListController);
    }

    private void InitializeEditor()
    {
        LoadGraphLibrary();
        InitializeNodePalette();
        EnsureCanvasLargeEnough();
    }

    #endregion

    #region 属性绑定

    public System.Collections.IEnumerable Nodes => _editorService.Nodes;
    public System.Collections.IEnumerable ConnectionPaths => _editorService.ConnectionPaths;
    public ObservableCollection<GraphListItemViewModel> GraphListItems { get; } = [];
    public ObservableCollection<GraphListItemViewModel> FunctionListItems { get; } = [];
    public ObservableCollection<GraphListItemViewModel> MacroListItems { get; } = [];
    public ObservableCollection<ContentAssetViewModel> ContentBrowserItems { get; } = [];
    public ObservableCollection<ContentAssetViewModel> ContentFolderItems { get; } = [];
    public ObservableCollection<ContentAssetViewModel> ContentVisibleItems { get; } = [];

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
        CommitInspectorAndSnapshotActive();
        CompileCurrentAssets(showPrompt: false);
    }

    #endregion

    #region 图谱列表

    private void LoadGraphLibrary()
    {
        ContentBrowserItems.Clear();
        foreach (var item in _graphLibraryService.LoadContentLibrary())
            ContentBrowserItems.Add(item);

        _currentContentFolderId = null;
        RefreshContentBrowserViews();
        CloseActiveEditor();
    }

    private void AddGraphListItem_Click(object sender, RoutedEventArgs e)
    {
        SnapshotActiveAsset();
        _activeAssetController = _graphListController;
        _graphListController.AddAndRename(snapshotCurrent: false);
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
        _activeAssetController = _functionListController;
        _functionListController.AddAndRename(snapshotCurrent: false);
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

    private void AddMacroListItem_Click(object sender, RoutedEventArgs e)
    {
        SnapshotActiveAsset();
        _activeAssetController = _macroListController;
        _macroListController.AddAndRename(snapshotCurrent: false);
        SaveSectionExpansionForActiveAsset(_macroListController);
        MarkCurrentContentDirty();
        UpdateGraphSectionVisibility();
    }

    private void MacroListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_macroListController.TryGetItemFromMouse(e, out var item))
        {
            LoadGraphItem(_macroListController, item, snapshotCurrent: true);
            e.Handled = true;
            UpdateGraphSectionVisibility();
        }
    }

    private void MacroListBox_KeyDown(object sender, KeyEventArgs e)
    {
        _macroListController.HandleKeyDown(e);
        SaveSectionExpansionForActiveAsset(_macroListController);
        UpdateGraphSectionVisibility();
    }

    private void MacroListItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _macroListController.SelectRightClickedItem(sender);
        ActivateGraphListItem(_macroListController, sender, e);
    }

    private void MacroListItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        ActivateGraphListItem(_macroListController, sender, e);
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

        _activeAssetController = controller;
        controller.LoadItem(item, snapshotCurrent: false);
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
        if (_activeContentAsset?.Kind is not (ContentAssetKind.FunctionLibrary or ContentAssetKind.MacroLibrary))
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
            GraphAssetKind.Macro => _macroListController,
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

    private void ToggleMacroSection_Click(object sender, RoutedEventArgs e)
    {
        _macroListController.SetSectionExpanded(!_macroListController.IsSectionExpanded);
        SaveSectionExpansionForActiveAsset(_macroListController);
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
        else if (ReferenceEquals(controller, _macroListController))
        {
            _activeContentAsset.MacroSectionExpanded = controller.IsSectionExpanded;
            _activeContentAsset.MacroSectionHasState = true;
        }
    }

    private void UpdateGraphSectionVisibility()
    {
        if (_graphListController is null || _functionListController is null || _macroListController is null)
            return;

        bool showEvent = _activeContentAsset?.Kind == ContentAssetKind.Script;
        bool showFunction = _activeContentAsset?.Kind is ContentAssetKind.Script or ContentAssetKind.FunctionLibrary;
        bool showMacro = _activeContentAsset?.Kind is ContentAssetKind.Script or ContentAssetKind.MacroLibrary;

        UpdateGraphSection(_graphListController, EventGraphPanel, EventGraphSection, EventGraphSectionToggle, GraphListBox, showEvent);
        UpdateGraphSection(_functionListController, FunctionPanel, FunctionSection, FunctionSectionToggle, FunctionListBox, showFunction);
        UpdateGraphSection(_macroListController, MacroPanel, MacroSection, MacroSectionToggle, MacroListBox, showMacro);
        EventGraphDirtyBadge.Visibility = showEvent && _graphListController.HasCompileDirtyItems ? Visibility.Visible : Visibility.Collapsed;
        FunctionDirtyBadge.Visibility = showFunction && _functionListController.HasCompileDirtyItems ? Visibility.Visible : Visibility.Collapsed;
        MacroDirtyBadge.Visibility = showMacro && _macroListController.HasCompileDirtyItems ? Visibility.Visible : Visibility.Collapsed;
        UpdateLibraryPublishOptionVisibility();
        UpdateCompileButtonState();
    }

    private void UpdateLibraryPublishOptionVisibility()
    {
        bool showFunctions = _activeContentAsset?.Kind == ContentAssetKind.FunctionLibrary;
        bool showMacros = _activeContentAsset?.Kind == ContentAssetKind.MacroLibrary;

        foreach (var item in FunctionListItems)
            item.ShowLibraryPublishOption = showFunctions;
        foreach (var item in MacroListItems)
            item.ShowLibraryPublishOption = showMacros;
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

    private void NewMacroLibraryAsset_Click(object sender, RoutedEventArgs e) => AddContentAsset(ContentAssetKind.MacroLibrary, "新宏库");

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
        ContentBrowserNewMacroLibraryMenuItem.Visibility = newVisibility;
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
        else if (kind == GraphAssetKind.Macro)
            _editorService.NewMacroGraph();
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
        SaveVisibleGraphsToActiveContent();
        _activeContentAsset = asset;
        ContentBrowserListBox.SelectedItem = asset;

        GraphListItems.Clear();
        FunctionListItems.Clear();
        MacroListItems.Clear();
        _graphListController.ClearActive();
        _functionListController.ClearActive();
        _macroListController.ClearActive();
        _activeAssetController = null;

        foreach (var item in asset.EventGraphs)
            GraphListItems.Add(item);
        foreach (var item in asset.Functions)
            FunctionListItems.Add(item);
        foreach (var item in asset.Macros)
            MacroListItems.Add(item);

        _graphListController.LoadSectionExpansion(asset.EventGraphSectionExpanded, asset.EventGraphSectionHasState);
        _functionListController.LoadSectionExpansion(asset.FunctionSectionExpanded, asset.FunctionSectionHasState);
        _macroListController.LoadSectionExpansion(asset.MacroSectionExpanded, asset.MacroSectionHasState);
        ApplyEditorModeForContent(asset);
        LoadFirstGraphForContent(asset);
        SetStatus($"已打开：{asset.Name}");
    }

    private void ApplyEditorModeForContent(ContentAssetViewModel asset)
    {
        EmptyEditorPanel.Visibility = Visibility.Collapsed;
        EditorGrid.Visibility = Visibility.Visible;
        InspectorPanel.Visibility = Visibility.Visible;
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
        else if (asset.Kind == ContentAssetKind.MacroLibrary)
        {
            if (MacroListItems.Count > 0)
            {
                LoadGraphItem(_macroListController, MacroListItems[0], snapshotCurrent: false);
                return;
            }
        }

        _activeAssetController = null;
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
        InspectorHintTextBlock.Visibility = Visibility.Visible;
    }

    private void CloseActiveEditor()
    {
        _activeContentAsset = null;
        _activeAssetController = null;
        GraphListItems.Clear();
        FunctionListItems.Clear();
        MacroListItems.Clear();
        _editorService.ClearGraph();
        _graphCommandService.Clear();
        EmptyEditorPanel.Visibility = Visibility.Visible;
        EditorGrid.Visibility = Visibility.Collapsed;
    }

    private void SaveVisibleGraphsToActiveContent()
    {
        if (_activeContentAsset is null)
            return;

        SnapshotActiveAsset();
        _activeContentAsset.EventGraphs = new ObservableCollection<GraphListItemViewModel>(GraphListItems);
        _activeContentAsset.Functions = new ObservableCollection<GraphListItemViewModel>(FunctionListItems);
        _activeContentAsset.Macros = new ObservableCollection<GraphListItemViewModel>(MacroListItems);
    }

    private void MarkCurrentContentDirty()
    {
        if (_activeContentAsset is not null)
            _activeContentAsset.IsDirty = true;
    }

    private IEnumerable<CallableGraphItem> GetCallableFunctions()
    {
        SaveVisibleGraphsToActiveContent();
        return _callableGraphResolver.ResolveFunctions(ContentBrowserItems, _activeContentAsset);
    }

    private IEnumerable<CallableGraphItem> GetRuntimeCallableFunctions()
    {
        SaveVisibleGraphsToActiveContent();
        return _callableGraphResolver.ResolveFunctions(ContentBrowserItems, _activeContentAsset);
    }

    private IEnumerable<CallableGraphItem> GetCallableMacros()
    {
        SaveVisibleGraphsToActiveContent();
        return _callableGraphResolver.ResolveMacros(ContentBrowserItems, _activeContentAsset);
    }

    private IEnumerable<CallableGraphItem> GetRuntimeCallableMacros()
    {
        SaveVisibleGraphsToActiveContent();
        return _callableGraphResolver.ResolveMacros(ContentBrowserItems, _activeContentAsset);
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
        if (_currentContentFolderId is not null && ContentBrowserItems.All(item => item.Id != _currentContentFolderId))
            _currentContentFolderId = null;
        ExpandFolderPath(_currentContentFolderId);

        ContentFolderItems.Clear();
        _rootContentFolder.ViewDepth = 0;
        _rootContentFolder.IsTreeExpanded = true;
        _rootContentFolder.HasFolderChildren = ContentBrowserItems.Any(item => item.IsFolder && item.ParentFolderId is null);
        ContentFolderItems.Add(_rootContentFolder);

        foreach (var folder in BuildFolderTree(null, 1, new HashSet<string>()))
            ContentFolderItems.Add(folder);

        ContentVisibleItems.Clear();
        foreach (var item in ContentBrowserItems.Where(item => item.ParentFolderId == _currentContentFolderId).OrderByDescending(item => item.IsFolder).ThenBy(item => item.Name))
            ContentVisibleItems.Add(item);

        ContentFolderListBox.SelectedItem = _currentContentFolderId is null
            ? _rootContentFolder
            : ContentFolderItems.FirstOrDefault(item => item.Id == _currentContentFolderId);
    }

    private IEnumerable<ContentAssetViewModel> BuildFolderTree(string? parentId, int depth, HashSet<string> visited)
    {
        foreach (var folder in ContentBrowserItems
                     .Where(item => item.IsFolder && item.ParentFolderId == parentId)
                     .OrderBy(item => item.Name))
        {
            if (!visited.Add(folder.Id))
                continue;

            folder.ViewDepth = depth;
            folder.HasFolderChildren = ContentBrowserItems.Any(item => item.IsFolder && item.ParentFolderId == folder.Id);
            yield return folder;

            if (!folder.IsTreeExpanded)
                continue;

            foreach (var child in BuildFolderTree(folder.Id, depth + 1, visited))
                yield return child;
        }
    }

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

        bool deletingActive = ReferenceEquals(item, _activeContentAsset);
        foreach (var child in ContentBrowserItems.Where(child => child.ParentFolderId == item.Id).ToList())
            child.ParentFolderId = item.ParentFolderId;
        ContentBrowserItems.Remove(item);
        if (deletingActive)
            CloseActiveEditor();
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
        clone.Macros = new ObservableCollection<GraphListItemViewModel>(source.Macros.Select(CloneGraphItem));
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
        var current = ContentBrowserItems.FirstOrDefault(item => item.Id == folderId && item.IsFolder);
        folderId = current?.Id;
        while (folderId is not null)
        {
            var folder = ContentBrowserItems.FirstOrDefault(item => item.Id == folderId && item.IsFolder);
            if (folder is null)
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
            if (kind == GraphAssetKind.Function && node is FunctionEntryNodeViewModel ||
                kind == GraphAssetKind.Macro && node is MacroEntryNodeViewModel)
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
            GraphAssetKind.Macro => "macro_entry",
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
        MacroId = node.MacroId,
        CustomEventId = node.CustomEventId,
        ExitName = node.ExitName,
        Parameters = node.Parameters.Select(CloneParameterFile).ToList(),
        InputParameters = node.InputParameters.Select(CloneParameterFile).ToList(),
        OutputParameters = node.OutputParameters.Select(CloneParameterFile).ToList(),
        MacroExits = node.MacroExits.Select(exit => new MacroExitFileModel { Id = exit.Id, Name = exit.Name }).ToList(),
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

        SaveVisibleGraphsToActiveContent();
        if (ContentBrowserItems.Any(item => item.IsDirty) ||
            GraphListItems.Concat(FunctionListItems).Concat(MacroListItems).Any(item => item.IsDirty))
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
    }

    #endregion

    #region 工具栏事件 - 执行

    private async void RunGraph_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureCompiledBeforeRun())
            return;

        if (_activeContentAsset?.Kind != ContentAssetKind.Script || !ReferenceEquals(_activeAssetController, _graphListController))
        {
            WpfMessageBox.Show(this, "只有脚本里的事件图可以直接执行。请从内容浏览器打开脚本，并进入事件图。", "不能执行", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SaveVisibleGraphsToActiveContent();
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
        // 节点菜单打开时，点击菜单内部不处理画布事件
        if (NodePalette.Visibility == Visibility.Visible)
        {
            var posInPalette = e.GetPosition(NodePalette);
            if (posInPalette.X >= 0 && posInPalette.X <= NodePalette.ActualWidth &&
                posInPalette.Y >= 0 && posInPalette.Y <= NodePalette.ActualHeight)
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
        var viewportPos = e.GetPosition(GraphViewport);
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

        _nodeDragSelectionController.CancelSelection();
        _nodeDragSelectionController.CancelDrag();
        GraphViewport.ReleaseMouseCapture();
        _nodeDragSelectionController.SetCanvasFocusActive(true);
        GraphViewport.Focus();

        // 判断是点击还是拖动：记录起始位置，尝试移动后决定
        _rightClickPending = true;
        _rightClickStartPos = e.GetPosition(GraphViewport);
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
            GraphViewport.ReleaseMouseCapture();
            return;
        }

        _canvasPanZoomController.EndPan();
        e.Handled = true;
    }

    private void GraphViewport_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (NodePalette.Visibility == Visibility.Visible)
        {
            var pos = e.GetPosition(NodePalette);
            if (pos.X >= 0 && pos.X <= NodePalette.ActualWidth &&
                pos.Y >= 0 && pos.Y <= NodePalette.ActualHeight)
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

        if (IsFocusInside(GraphListBox) || IsFocusInside(FunctionListBox) || IsFocusInside(MacroListBox))
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
        if (IsFocusInside(FunctionListBox))
            return _functionListController;
        if (IsFocusInside(MacroListBox))
            return _macroListController;
        if (IsFocusInside(GraphListBox))
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
        ApplyInspectorChanges();
        SnapshotActiveAsset();
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

    private void OnGraphChanged()
    {
        _editorService.UpdatePinConnectionStates();
        if (!_suppressGraphChangedDirty && _activeAssetController?.IsLoadingGraph != true)
            MarkActiveAssetDirty();

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
        _activeAssetController?.MarkLogicDirty();
        MarkCurrentContentDirty();
        UpdateGraphSectionVisibility();
    }

    private void MarkActiveAssetLayoutDirty()
    {
        _activeAssetController?.MarkLayoutDirty();
        MarkCurrentContentDirty();
        UpdateGraphSectionVisibility();
    }

    private void SnapshotActiveAsset()
    {
        _activeAssetController?.SnapshotActive();
    }

    private void PersistAssetLibrary()
    {
        SaveVisibleGraphsToActiveContent();
        _graphLibraryService.SaveContentLibrary(ContentBrowserItems, _activeContentAsset?.Id);
    }

    private void SaveAllAssets()
    {
        SaveVisibleGraphsToActiveContent();
        foreach (var item in ContentBrowserItems
                     .Where(asset => asset.Kind != ContentAssetKind.Folder)
                     .SelectMany(asset => asset.EventGraphs.Concat(asset.Functions).Concat(asset.Macros))
                     .Concat(GraphListItems)
                     .Concat(FunctionListItems)
                     .Concat(MacroListItems))
        {
            item.IsDirty = false;
        }

        foreach (var item in ContentBrowserItems)
            item.IsDirty = false;
        PersistAssetLibrary();
        UpdateGraphSectionVisibility();
        SetStatus($"已保存全部内容资产：{ContentBrowserItems.Count} 个。");
    }

    private bool EnsureCompiledBeforeSave()
    {
        CommitInspectorAndSnapshotActive();
        SaveVisibleGraphsToActiveContent();
        if (!HasCompileDirtyAssets())
            return true;

        var result = WpfMessageBox.Show(this, "存在未编译修改，是否先编译再保存？", "需要编译", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (result == MessageBoxResult.Cancel)
            return false;
        if (result == MessageBoxResult.Yes)
            return CompileAllAssets(showPrompt: false);
        return true;
    }

    private bool EnsureCompiledBeforeRun()
    {
        CommitInspectorAndSnapshotActive();
        SaveVisibleGraphsToActiveContent();
        if (!HasCompileDirtyAssets())
            return true;

        WpfMessageBox.Show(this, "存在未编译修改，请先点击编译。", "需要编译", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    private bool CompileCurrentAssets(bool showPrompt)
    {
        CommitInspectorAndSnapshotActive();
        SaveVisibleGraphsToActiveContent();
        if (_activeContentAsset is null || _activeAssetController?.ActiveItem is not { } active)
        {
            if (showPrompt)
                WpfMessageBox.Show(this, "没有打开的图表。", "无法编译", MessageBoxButton.OK, MessageBoxImage.Warning);
            SetStatus("编译失败：没有打开的图表。");
            return false;
        }

        var result = _graphCompileService.CompileGraph(ContentBrowserItems, _activeContentAsset, active);
        foreach (var item in ContentBrowserItems.Where(item => result.ChangedAssetIds.Contains(item.Id)))
            item.IsDirty = true;

        if (!HandleCompileResult(result, showPrompt))
            return false;

        _activeAssetController.ReloadItemWithoutPersist(active);
        _graphCommandService.Clear();
        PersistAssetLibrary();
        UpdateGraphSectionVisibility();
        SetStatus($"编译完成：{active.Name}，同步 {result.UpdatedCallNodes} 个调用节点，移除 {result.RemovedConnections} 条无效连线。");
        return true;
    }

    private bool CompileAllAssets(bool showPrompt)
    {
        CommitInspectorAndSnapshotActive();
        SaveVisibleGraphsToActiveContent();
        var result = _graphCompileService.Compile(ContentBrowserItems);
        foreach (var item in ContentBrowserItems.Where(item => result.ChangedAssetIds.Contains(item.Id)))
            item.IsDirty = true;

        if (!HandleCompileResult(result, showPrompt))
            return false;

        if (_activeAssetController?.ActiveItem is { } active)
        {
            _activeAssetController.ReloadItemWithoutPersist(active);
            _graphCommandService.Clear();
        }
        PersistAssetLibrary();
        UpdateGraphSectionVisibility();
        SetStatus($"编译完成：同步 {result.UpdatedCallNodes} 个调用节点，移除 {result.RemovedConnections} 条无效连线。");
        return result.Success;
    }

    private bool HandleCompileResult(GraphCompileResult result, bool showPrompt)
    {
        if (result.Success)
            return true;

        foreach (var issue in result.Issues)
        {
            if (issue.Severity == GraphCore.GraphValidationSeverity.Error)
                Logger.Error($"编译：{issue.Message}");
            else
                Logger.Warn($"编译：{issue.Message}");
        }

        string message = string.Join(Environment.NewLine, result.Issues
            .Where(issue => issue.Severity == GraphCore.GraphValidationSeverity.Error)
            .Take(6)
            .Select(issue => issue.Message));
        if (showPrompt && !string.IsNullOrWhiteSpace(message))
            WpfMessageBox.Show(this, message, "编译失败", MessageBoxButton.OK, MessageBoxImage.Error);
        SetStatus($"编译失败：{result.Issues.Count(issue => issue.Severity == GraphCore.GraphValidationSeverity.Error)} 个错误。");
        UpdateGraphSectionVisibility();
        return false;
    }

    private bool HasCompileDirtyAssets()
    {
        return ContentBrowserItems
            .Where(asset => asset.Kind != ContentAssetKind.Folder)
            .SelectMany(asset => asset.EventGraphs.Concat(asset.Functions).Concat(asset.Macros))
            .Concat(GraphListItems)
            .Concat(FunctionListItems)
            .Concat(MacroListItems)
            .Any(item => item.IsCompileDirty);
    }

    private void UpdateCompileButtonState()
    {
        if (CompileGraphButton is null)
            return;

        bool dirty = _activeAssetController?.ActiveItem?.IsCompileDirty == true;
        CompileButtonText.Text = dirty ? "编译*" : "编译";
        CompileDirtyIcon.Visibility = dirty ? Visibility.Visible : Visibility.Collapsed;
        CompileGraphButton.Background = dirty
            ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(75, 54, 28))
            : new SolidColorBrush(System.Windows.Media.Color.FromRgb(32, 36, 43));
        CompileGraphButton.BorderBrush = dirty
            ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(214, 138, 34))
            : new SolidColorBrush(System.Windows.Media.Color.FromRgb(46, 52, 64));
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
        var hit = GraphSurface.InputHitTest(position) as DependencyObject;
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

                if (ReferenceEquals(element, GraphSurface) || ReferenceEquals(element, GraphViewport))
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
        _nodePaletteController.Filter(NodePaletteSearchBox.Text.Trim());
    }

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_nodePaletteController.IsOpen) return;

        var pos = e.GetPosition(NodePalette);
        if (!_nodePaletteController.IsPointInside(pos))
        {
            CloseNodePalette();
        }
    }

    #endregion
}
