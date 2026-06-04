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
    private readonly NodeRegistry _nodeRegistry = NodeRegistry.CreateDefault();
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
    // 运行状态
    private bool _isClosing;

    // 右键菜单状态
    private bool _rightClickPending;
    private Point _rightClickStartPos;

    public MainWindow()
    {
        DataContext = this;
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
        _executionController = new ExecutionController(
            this,
            _editorService,
            new Runtime.GraphRuntimeExecutor(nodeRegistry: _nodeRegistry, adapters: new Adapters.RuntimeAdapters()),
            new GraphCore.GraphValidator(),
            RunGraphButton,
            GetCallableFunctions,
            GetCallableMacros,
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
            _nodeRegistry,
            GetCallableFunctions,
            GetCallableMacros,
            SnapshotActiveAsset,
            ViewportToGraph,
            SelectNode);

        _canvasPanZoomController = new CanvasPanZoomController(
            GraphViewport,
            GraphZoomTransform,
            GraphPanTransform,
            _editorService,
            SetStatus);

        _nodeDragSelectionController = new NodeDragSelectionController(
            _editorService,
            _clipboardService,
            _nodeFactory,
            GraphViewport,
            SelectionRectangle,
            ViewportToGraph,
            LoadNodeToInspector,
            FitGraphToView,
            EnsureCanvasLargeEnough,
            MarkActiveAssetDirty,
            SetStatus);

        _inspectorController = new InspectorController(
            this,
            _editorService,
            new Adapters.Win32WindowAdapter(),
            MarkActiveAssetDirty,
            SetStatus,
            InspectorHintTextBlock,
            NodeTitleTextBox,
            ParameterInspectorPanel,
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
            _nodeFactory,
            GraphViewport,
            PreviewConnectionPath,
            ViewportToGraph,
            position => TryGetPinAtPosition(position, out var pin) ? pin : null,
            SelectNode,
            _nodeDragSelectionController.SetCanvasFocusActive,
            SetStatus);

        _logPanelController = new LogPanelController(
            LogListBox,
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
    public System.Collections.IEnumerable Connections => _editorService.Connections;
    public ObservableCollection<GraphListItemViewModel> GraphListItems { get; } = [];
    public ObservableCollection<GraphListItemViewModel> FunctionListItems { get; } = [];
    public ObservableCollection<GraphListItemViewModel> MacroListItems { get; } = [];
    public ObservableCollection<ContentAssetViewModel> ContentBrowserItems { get; } = [];

    #endregion

    #region 工具栏事件 - 文件操作

    private void NewGraph_Click(object sender, RoutedEventArgs e)
    {
        if (_activeContentAsset is null)
        {
            var asset = CreateContentAsset(ContentAssetKind.Script, CreateUniqueContentName("新脚本"));
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

    #endregion

    #region 图谱列表

    private void LoadGraphLibrary()
    {
        ContentBrowserItems.Clear();
        foreach (var item in _graphLibraryService.LoadContentLibrary())
            ContentBrowserItems.Add(item);

        if (ContentBrowserItems.Count == 0)
            ContentBrowserItems.Add(CreateContentAsset(ContentAssetKind.Script, "默认脚本"));

        CloseActiveEditor();
    }

    private void AddGraphListItem_Click(object sender, RoutedEventArgs e)
    {
        SnapshotActiveAsset();
        _activeAssetController = _graphListController;
        _graphListController.AddAndRename(snapshotCurrent: false);
        MarkCurrentContentDirty();
    }

    private void GraphListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        SnapshotActiveAsset();
        if (_graphListController.SelectedItem is { } item)
        {
            _graphListController.LoadItem(item, snapshotCurrent: false);
            _activeAssetController = _graphListController;
            e.Handled = true;
        }
    }

    private void GraphListBox_KeyDown(object sender, KeyEventArgs e)
    {
        _graphListController.HandleKeyDown(e);
    }

    private void RenameGraphMenuItem_Click(object sender, RoutedEventArgs e)
    {
        (_activeAssetController ?? _graphListController).RenameSelected();
    }

    private void DeleteGraphMenuItem_Click(object sender, RoutedEventArgs e)
    {
        (_activeAssetController ?? _graphListController).DeleteSelected();
    }

    private void AddFunctionListItem_Click(object sender, RoutedEventArgs e)
    {
        SnapshotActiveAsset();
        _activeAssetController = _functionListController;
        _functionListController.AddAndRename(snapshotCurrent: false);
        MarkCurrentContentDirty();
    }

    private void FunctionListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        SnapshotActiveAsset();
        if (_functionListController.SelectedItem is { } item)
        {
            _functionListController.LoadItem(item, snapshotCurrent: false);
            _activeAssetController = _functionListController;
            e.Handled = true;
        }
    }

    private void FunctionListBox_KeyDown(object sender, KeyEventArgs e)
    {
        _functionListController.HandleKeyDown(e);
    }

    private void FunctionListItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _functionListController.SelectRightClickedItem(sender);
        _activeAssetController = _functionListController;
    }

    private void AddMacroListItem_Click(object sender, RoutedEventArgs e)
    {
        SnapshotActiveAsset();
        _activeAssetController = _macroListController;
        _macroListController.AddAndRename(snapshotCurrent: false);
        MarkCurrentContentDirty();
    }

    private void MacroListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        SnapshotActiveAsset();
        if (_macroListController.SelectedItem is { } item)
        {
            _macroListController.LoadItem(item, snapshotCurrent: false);
            _activeAssetController = _macroListController;
            e.Handled = true;
        }
    }

    private void MacroListBox_KeyDown(object sender, KeyEventArgs e)
    {
        _macroListController.HandleKeyDown(e);
    }

    private void MacroListItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _macroListController.SelectRightClickedItem(sender);
        _activeAssetController = _macroListController;
    }

    private void GraphListItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _graphListController.SelectRightClickedItem(sender);
        _activeAssetController = _graphListController;
        e.Handled = false;
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

    private GraphListController GetControllerFor(GraphListItemViewModel item) =>
        item.Kind switch
        {
            GraphAssetKind.Function => _functionListController,
            GraphAssetKind.Macro => _macroListController,
            _ => _graphListController,
        };

    #endregion

    #region 内容浏览器

    private void NewContentFolder_Click(object sender, RoutedEventArgs e) => AddContentAsset(ContentAssetKind.Folder, "新文件夹");

    private void NewScriptAsset_Click(object sender, RoutedEventArgs e) => AddContentAsset(ContentAssetKind.Script, "新脚本");

    private void NewFunctionLibraryAsset_Click(object sender, RoutedEventArgs e) => AddContentAsset(ContentAssetKind.FunctionLibrary, "新函数库");

    private void NewMacroLibraryAsset_Click(object sender, RoutedEventArgs e) => AddContentAsset(ContentAssetKind.MacroLibrary, "新宏库");

    private void ContentBrowserListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ContentBrowserListBox.SelectedItem is ContentAssetViewModel asset && asset.Kind != ContentAssetKind.Folder)
            OpenContentAsset(asset);
    }

    private void ContentBrowserListBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox) return;

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

    private void ContentAsset_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem { DataContext: ContentAssetViewModel item })
            ContentBrowserListBox.SelectedItem = item;
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

    private void AddContentAsset(ContentAssetKind kind, string namePrefix)
    {
        var asset = CreateContentAsset(kind, CreateUniqueContentName(namePrefix));
        ContentBrowserItems.Add(asset);
        ContentBrowserListBox.SelectedItem = asset;
        asset.IsEditing = true;
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

        if (kind == ContentAssetKind.Script)
            asset.EventGraphs.Add(CreateGraphItem(GraphAssetKind.EventGraph, "事件图1"));
        else if (kind == ContentAssetKind.FunctionLibrary)
            asset.Functions.Add(CreateGraphItem(GraphAssetKind.Function, "新函数_1"));
        else if (kind == ContentAssetKind.MacroLibrary)
            asset.Macros.Add(CreateGraphItem(GraphAssetKind.Macro, "新宏_1"));

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

        _nodeFactory.ResetCounter(1);
        return _editorService.ExportGraphModel(name, kind);
    }

    private string CreateUniqueContentName(string prefix)
    {
        int index = 1;
        string name;
        do
        {
            name = $"{prefix}{index++}";
        }
        while (ContentBrowserItems.Any(item => item.Name == name));

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

        ApplyEditorModeForContent(asset);
        LoadFirstGraphForContent(asset);
        SetStatus($"已打开：{asset.Name}");
    }

    private void ApplyEditorModeForContent(ContentAssetViewModel asset)
    {
        EmptyEditorPanel.Visibility = Visibility.Collapsed;
        EditorGrid.Visibility = Visibility.Visible;
        EventGraphSection.Visibility = asset.Kind == ContentAssetKind.Script ? Visibility.Visible : Visibility.Collapsed;
        GraphListBox.Visibility = asset.Kind == ContentAssetKind.Script ? Visibility.Visible : Visibility.Collapsed;
        FunctionSection.Visibility = asset.Kind is ContentAssetKind.Script or ContentAssetKind.FunctionLibrary ? Visibility.Visible : Visibility.Collapsed;
        FunctionListBox.Visibility = asset.Kind is ContentAssetKind.Script or ContentAssetKind.FunctionLibrary ? Visibility.Visible : Visibility.Collapsed;
        MacroSection.Visibility = asset.Kind is ContentAssetKind.Script or ContentAssetKind.MacroLibrary ? Visibility.Visible : Visibility.Collapsed;
        MacroListBox.Visibility = asset.Kind is ContentAssetKind.Script or ContentAssetKind.MacroLibrary ? Visibility.Visible : Visibility.Collapsed;
        InspectorPanel.Visibility = Visibility.Visible;
    }

    private void LoadFirstGraphForContent(ContentAssetViewModel asset)
    {
        if (asset.Kind == ContentAssetKind.Script)
        {
            if (GraphListItems.Count == 0)
                GraphListItems.Add(CreateGraphItem(GraphAssetKind.EventGraph, "事件图1"));
            _graphListController.LoadItem(GraphListItems[0], snapshotCurrent: false);
            _activeAssetController = _graphListController;
        }
        else if (asset.Kind == ContentAssetKind.FunctionLibrary)
        {
            if (FunctionListItems.Count == 0)
                FunctionListItems.Add(CreateGraphItem(GraphAssetKind.Function, "新函数_1"));
            _functionListController.LoadItem(FunctionListItems[0], snapshotCurrent: false);
            _activeAssetController = _functionListController;
        }
        else if (asset.Kind == ContentAssetKind.MacroLibrary)
        {
            if (MacroListItems.Count == 0)
                MacroListItems.Add(CreateGraphItem(GraphAssetKind.Macro, "新宏_1"));
            _macroListController.LoadItem(MacroListItems[0], snapshotCurrent: false);
            _activeAssetController = _macroListController;
        }
    }

    private void CloseActiveEditor()
    {
        _activeContentAsset = null;
        _activeAssetController = null;
        GraphListItems.Clear();
        FunctionListItems.Clear();
        MacroListItems.Clear();
        _editorService.Nodes.Clear();
        _editorService.Connections.Clear();
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
        if (_activeContentAsset?.Kind == ContentAssetKind.Script)
        {
            foreach (var function in _activeContentAsset.Functions)
                yield return new CallableGraphItem(function.Id, function.Name, "本脚本函数", function.Graph);
        }

        foreach (var library in ContentBrowserItems.Where(asset => asset.Kind == ContentAssetKind.FunctionLibrary))
        foreach (var function in library.Functions)
            yield return new CallableGraphItem(function.Id, $"{library.Name}/{function.Name}", "函数库", function.Graph);
    }

    private IEnumerable<CallableGraphItem> GetCallableMacros()
    {
        SaveVisibleGraphsToActiveContent();
        if (_activeContentAsset?.Kind == ContentAssetKind.Script)
        {
            foreach (var macro in _activeContentAsset.Macros)
                yield return new CallableGraphItem(macro.Id, macro.Name, "本脚本宏", macro.Graph);
        }

        foreach (var library in ContentBrowserItems.Where(asset => asset.Kind == ContentAssetKind.MacroLibrary))
        foreach (var macro in library.Macros)
            yield return new CallableGraphItem(macro.Id, $"{library.Name}/{macro.Name}", "宏库", macro.Graph);
    }

    private void StartRenameSelectedContentAsset()
    {
        if (ContentBrowserListBox.SelectedItem is ContentAssetViewModel item)
            item.IsEditing = true;
    }

    private void CommitContentAssetRename(ContentAssetViewModel item)
    {
        item.Name = string.IsNullOrWhiteSpace(item.Name) ? "未命名资产" : item.Name.Trim();
        item.IsEditing = false;
        item.IsDirty = true;
        PersistAssetLibrary();
    }

    private void DeleteSelectedContentAsset()
    {
        if (ContentBrowserListBox.SelectedItem is not ContentAssetViewModel item)
            return;

        var result = WpfMessageBox.Show(this, $"是否删除：{item.Name}？", "删除资产", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
            return;

        bool deletingActive = ReferenceEquals(item, _activeContentAsset);
        ContentBrowserItems.Remove(item);
        if (deletingActive)
            CloseActiveEditor();
        PersistAssetLibrary();
    }

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

    private void ConnectionPath_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pinConnectionController.HandleConnectionMouseLeftButtonDown(sender, e);
    }

    #endregion

    #region 选择操作

    private void SelectNode(NodeBaseViewModel? node)
    {
        _nodeDragSelectionController.SelectNode(node);
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

        if (IsFocusInside(GraphListBox))
        {
            if (Keyboard.FocusedElement is not TextBox)
            {
                _graphListController.HandleKeyDown(e);
                if (e.Handled) return;
            }

            return;
        }

        // 文本框中不处理快捷键
        if (Keyboard.FocusedElement is TextBox) return;

        if (_nodeDragSelectionController.HandleKeyDown(e))
        {
            e.Handled = true;
        }
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

    private void InspectorField_TextChanged(object sender, TextChangedEventArgs e) => ApplyInspectorChanges();
    private void InspectorField_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyInspectorChanges();
    private void InspectorField_CheckedChanged(object sender, RoutedEventArgs e) => ApplyInspectorChanges();

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
        MarkActiveAssetDirty();

        if (_editorService.Nodes.FirstOrDefault(n => n.IsSelected) is { } selected)
        {
            LoadNodeToInspector(selected);
        }
    }

    private void MarkActiveAssetDirty()
    {
        _activeAssetController?.MarkDirty();
        MarkCurrentContentDirty();
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
        foreach (var item in GraphListItems.Concat(FunctionListItems).Concat(MacroListItems))
            item.IsDirty = false;
        foreach (var item in ContentBrowserItems)
            item.IsDirty = false;
        PersistAssetLibrary();
        SetStatus($"已保存全部内容资产：{ContentBrowserItems.Count} 个。");
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
        return TryGetPinFromSource(hit, out pin);
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
                if (element.DataContext is NodeBaseViewModel or PinViewModel or ConnectionViewModel)
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
        _nodePaletteController.Open(viewportPos);
    }

    private void CloseNodePalette()
    {
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
