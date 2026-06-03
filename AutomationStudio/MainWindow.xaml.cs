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
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
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
    private CanvasPanZoomController _canvasPanZoomController = null!;
    private NodeDragSelectionController _nodeDragSelectionController = null!;
    private InspectorController _inspectorController = null!;
    private PinConnectionController _pinConnectionController = null!;
    private LogPanelController _logPanelController = null!;
    private GraphImportDropController _graphImportDropController = null!;
    private readonly Adapters.Win32WindowAdapter _windowAdapter = new();

    // 运行状态
    private bool _isLoadingInspector;
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
            SetStatus);

        _nodePaletteController = new NodePaletteController(
            NodePalette,
            NodePaletteSearchBox,
            NodePaletteContent,
            _nodeFactory,
            _editorService,
            _nodeRegistry,
            ViewportToGraph,
            SelectNode);

        _graphListController = new GraphListController(
            this,
            _editorService,
            _graphLibraryService,
            _nodeFactory,
            GraphListBox,
            GraphListItems,
            SyncNodeFactorySequence,
            SetStatus);

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
            _graphListController.MarkDirty,
            SetStatus);

        _inspectorController = new InspectorController(
            MousePositionXTextBox,
            MousePositionYTextBox,
            MouseMovePositionXTextBox,
            MouseMovePositionYTextBox,
            IfConditionComboBox,
            WhileLoopConditionComboBox,
            ForLoopEndConditionComboBox,
            PrintLogMessageTextBox,
            SelectWindowProcessNameTextBox);

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

    #endregion

    #region 工具栏事件 - 文件操作

    private void NewGraph_Click(object sender, RoutedEventArgs e)
    {
        _graphListController.AddAndRename();
    }

    private void SaveGraph_Click(object sender, RoutedEventArgs e)
    {
        _graphListController.SaveAll();
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
        _graphListController.LoadLibrary();
    }

    private void AddGraphListItem_Click(object sender, RoutedEventArgs e)
    {
        _graphListController.AddAndRename();
    }

    private void GraphListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        _graphListController.HandleDoubleClick(e);
    }

    private void GraphListBox_KeyDown(object sender, KeyEventArgs e)
    {
        _graphListController.HandleKeyDown(e);
    }

    private void RenameGraphMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _graphListController.RenameSelected();
    }

    private void DeleteGraphMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _graphListController.DeleteSelected();
    }

    private void GraphListItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _graphListController.SelectRightClickedItem(sender);
        e.Handled = false;
    }

    private void GraphNameTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is TextBox tb)
            _graphListController.HandleRenameKeyDown(tb, e);
    }

    private void GraphNameTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb)
            _graphListController.HandleRenameLostFocus(tb);
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

        if (!_graphListController.HandleClosing(e))
            return;

        _isClosing = true;
    }

    #endregion

    #region 工具栏事件 - 执行

    private async void RunGraph_Click(object sender, RoutedEventArgs e)
    {
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
        if (node is null)
        {
            _isLoadingInspector = true;
            NodeTitleTextBox.Text = string.Empty;
            HideAllInspectorPanels();
            InspectorHintTextBlock.Text = "请选择一个节点进行编辑。";
            RefreshInspectorLocks(node);
            _isLoadingInspector = false;
            return;
        }

        _isLoadingInspector = true;
        NodeTitleTextBox.Text = node.Title;
        InspectorHintTextBlock.Text = $"当前选中：{node.Title}";

        HideAllInspectorPanels();

        switch (node)
        {
            case FindImageNodeViewModel findImage:
                FindImageInspectorPanel.Visibility = Visibility.Visible;
                FindImagePathTextBox.Text = findImage.ImagePath;
                FindImageThresholdTextBox.Text = findImage.SimilarityThresholdPercent.ToString();
                break;

            case MouseClickNodeViewModel mouseNode:
                MouseLeftInspectorPanel.Visibility = Visibility.Visible;
                MousePositionXTextBox.Text = mouseNode.PositionX.ToString("0.##");
                MousePositionYTextBox.Text = mouseNode.PositionY.ToString("0.##");
                MouseClickOperationModeComboBox.SelectedIndex = (int)mouseNode.OperationMode;
                MouseButtonComboBox.SelectedIndex = (int)mouseNode.MouseButton;
                break;

            case KeyboardNodeViewModel keyboardNode:
                KeyboardInspectorPanel.Visibility = Visibility.Visible;
                PopulateKeyboardKeyComboBox(keyboardNode.Key);
                KeyboardOperationModeComboBox.SelectedIndex = keyboardNode.OperationMode switch
                {
                    PressReleaseMode.Press => 0,
                    PressReleaseMode.Release => 1,
                    PressReleaseMode.Click => 2,
                    _ => 0,
                };
                break;

            case IfNodeViewModel ifNode:
                IfInspectorPanel.Visibility = Visibility.Visible;
                IfConditionComboBox.SelectedIndex = ifNode.ConditionValue ? 1 : 0;
                break;

            case WhileLoopNodeViewModel whileNode:
                WhileLoopInspectorPanel.Visibility = Visibility.Visible;
                WhileLoopConditionComboBox.SelectedIndex = whileNode.ConditionValue ? 1 : 0;
                WhileLoopModeComboBox.SelectedIndex = whileNode.LoopMode == WhileLoopMode.Infinite ? 1 : 0;
                WhileMaxIterationsTextBox.Text = whileNode.MaxIterations.ToString();
                WhileMaxIterationsLabel.Visibility = whileNode.LoopMode == WhileLoopMode.Infinite ? Visibility.Collapsed : Visibility.Visible;
                WhileMaxIterationsTextBox.Visibility = whileNode.LoopMode == WhileLoopMode.Infinite ? Visibility.Collapsed : Visibility.Visible;
                break;

            case ForLoopNodeViewModel forLoopNode:
                ForLoopInspectorPanel.Visibility = Visibility.Visible;
                ForLoopCountTextBox.Text = forLoopNode.LoopCount.ToString();
                ForLoopEndConditionComboBox.SelectedIndex = forLoopNode.EndConditionValue ? 1 : 0;
                break;

            case ScrollWheelNodeViewModel scrollNode:
                ScrollWheelInspectorPanel.Visibility = Visibility.Visible;
                ScrollWheelActionComboBox.SelectedIndex = (int)scrollNode.ScrollAction;
                ScrollWheelSpeedTextBox.Text = scrollNode.ScrollSpeed.ToString();
                ScrollWheelIntervalTextBox.Text = scrollNode.ScrollInterval.ToString();
                ScrollWheelDurationTextBox.Text = scrollNode.ScrollDuration.ToString();
                break;

            case DelayNodeViewModel delayNode:
                DelayInspectorPanel.Visibility = Visibility.Visible;
                DelayMsTextBox.Text = delayNode.DelayMs.ToString();
                break;

            case MouseMoveNodeViewModel moveNode:
                MouseMoveInspectorPanel.Visibility = Visibility.Visible;
                MouseMovePositionXTextBox.Text = moveNode.PositionX.ToString("0.##");
                MouseMovePositionYTextBox.Text = moveNode.PositionY.ToString("0.##");
                break;

            case StartProgramNodeViewModel startProg:
                StartProgramInspectorPanel.Visibility = Visibility.Visible;
                StartProgramPathTextBox.Text = startProg.ProgramPath;
                StartProgramWaitTimeoutTextBox.Text = startProg.WaitTimeoutMs.ToString();
                StartProgramFailureActionComboBox.SelectedIndex = startProg.FailureAction == ProgramStartFailureAction.Retry ? 1 : 0;
                StartProgramRetryCountTextBox.Text = startProg.RetryCount.ToString();
                break;

            case PrintLogNodeViewModel printNode:
                PrintLogInspectorPanel.Visibility = Visibility.Visible;
                bool hasMsgInput = IsInputPinConnected(printNode, "message");
                PrintLogMessageTextBox.Text = hasMsgInput ? "前置输入" : printNode.Message;
                break;

            case SelectWindowNodeViewModel selectWindowNode:
                SelectWindowInspectorPanel.Visibility = Visibility.Visible;
                SelectWindowInputModeComboBox.SelectedIndex = selectWindowNode.InputMode == WindowInputMode.Auto ? 1 : 0;
                bool hasProcessNameInput = IsInputPinConnected(selectWindowNode, "process_name");
                if (hasProcessNameInput)
                {
                    SelectWindowProcessNameTextBox.Text = "前置输入";
                    SelectWindowAutoComboBox.SelectedItem = null;
                }
                else if (selectWindowNode.InputMode == WindowInputMode.Auto)
                {
                    PopulateWindowListComboBox();
                    SelectWindowAutoComboBox.SelectedItem = selectWindowNode.ProcessName;
                }
                else
                {
                    SelectWindowProcessNameTextBox.Text = selectWindowNode.ProcessName;
                }
                UpdateSelectWindowModeVisibility(selectWindowNode.InputMode, hasProcessNameInput);
                break;
        }

        RefreshInspectorLocks(node);
        _isLoadingInspector = false;
    }

    private void HideAllInspectorPanels()
    {
        FindImageInspectorPanel.Visibility = Visibility.Collapsed;
        MouseLeftInspectorPanel.Visibility = Visibility.Collapsed;
        KeyboardInspectorPanel.Visibility = Visibility.Collapsed;
        ScrollWheelInspectorPanel.Visibility = Visibility.Collapsed;
        IfInspectorPanel.Visibility = Visibility.Collapsed;
        ForLoopInspectorPanel.Visibility = Visibility.Collapsed;
        WhileLoopInspectorPanel.Visibility = Visibility.Collapsed;
        DelayInspectorPanel.Visibility = Visibility.Collapsed;
        MouseMoveInspectorPanel.Visibility = Visibility.Collapsed;
        StartProgramInspectorPanel.Visibility = Visibility.Collapsed;
        PrintLogInspectorPanel.Visibility = Visibility.Collapsed;
        SelectWindowInspectorPanel.Visibility = Visibility.Collapsed;
    }

    private void ApplyInspectorChanges()
    {
        if (_editorService.Nodes.FirstOrDefault(n => n.IsSelected) is not { } node || _isLoadingInspector)
            return;

        node.Title = NodeTitleTextBox.Text.Trim();

        switch (node)
        {
            case FindImageNodeViewModel findImage:
                findImage.ImagePath = FindImagePathTextBox.Text.Trim();
                if (int.TryParse(FindImageThresholdTextBox.Text.Trim(), out var threshold))
                    findImage.SimilarityThresholdPercent = threshold;
                break;

            case MouseClickNodeViewModel mouseNode:
                mouseNode.OperationMode = (PressReleaseMode)MouseClickOperationModeComboBox.SelectedIndex;
                mouseNode.MouseButton = (Graph.MouseButton)MouseButtonComboBox.SelectedIndex;
                if (double.TryParse(MousePositionXTextBox.Text.Trim(), out var x))
                    mouseNode.PositionX = x;
                if (double.TryParse(MousePositionYTextBox.Text.Trim(), out var y))
                    mouseNode.PositionY = y;
                break;

            case KeyboardNodeViewModel keyboardNode:
                keyboardNode.OperationMode = KeyboardOperationModeComboBox.SelectedIndex switch
                {
                    1 => PressReleaseMode.Release,
                    2 => PressReleaseMode.Click,
                    _ => PressReleaseMode.Press,
                };
                if (KeyboardKeyComboBox.SelectedItem is ComboBoxItem keyItem && keyItem.Tag is string keyStr)
                    keyboardNode.Key = keyStr;
                break;

            case IfNodeViewModel ifNode:
                ifNode.ConditionValue = IfConditionComboBox.SelectedIndex == 1;
                break;

            case WhileLoopNodeViewModel whileNode:
                whileNode.ConditionValue = WhileLoopConditionComboBox.SelectedIndex == 1;
                whileNode.LoopMode = WhileLoopModeComboBox.SelectedIndex == 1
                    ? WhileLoopMode.Infinite : WhileLoopMode.Finite;
                if (int.TryParse(WhileMaxIterationsTextBox.Text.Trim(), out var wm))
                    whileNode.MaxIterations = Math.Max(1, wm);
                // Toggle max iterations visibility based on loop mode
                bool isInfinite = whileNode.LoopMode == WhileLoopMode.Infinite;
                WhileMaxIterationsLabel.Visibility = isInfinite ? Visibility.Collapsed : Visibility.Visible;
                WhileMaxIterationsTextBox.Visibility = isInfinite ? Visibility.Collapsed : Visibility.Visible;
                break;

            case ForLoopNodeViewModel forLoopNode:
                if (int.TryParse(ForLoopCountTextBox.Text.Trim(), out var count))
                    forLoopNode.LoopCount = Math.Max(1, count);
                forLoopNode.EndConditionValue = ForLoopEndConditionComboBox.SelectedIndex == 1;
                break;

            case ScrollWheelNodeViewModel scrollNode:
                scrollNode.ScrollAction = (ScrollWheelAction)ScrollWheelActionComboBox.SelectedIndex;
                if (int.TryParse(ScrollWheelSpeedTextBox.Text.Trim(), out var speed))
                    scrollNode.ScrollSpeed = Math.Max(0, speed);
                if (int.TryParse(ScrollWheelIntervalTextBox.Text.Trim(), out var interval))
                    scrollNode.ScrollInterval = Math.Max(1, interval);
                if (int.TryParse(ScrollWheelDurationTextBox.Text.Trim(), out var duration))
                    scrollNode.ScrollDuration = Math.Max(0, duration);
                break;

            case DelayNodeViewModel delayNode:
                if (int.TryParse(DelayMsTextBox.Text.Trim(), out var delayMs))
                    delayNode.DelayMs = delayMs;
                break;

            case MouseMoveNodeViewModel moveNode:
                if (double.TryParse(MouseMovePositionXTextBox.Text.Trim(), out var moveX))
                    moveNode.PositionX = moveX;
                if (double.TryParse(MouseMovePositionYTextBox.Text.Trim(), out var moveY))
                    moveNode.PositionY = moveY;
                break;

            case StartProgramNodeViewModel startProg:
                startProg.ProgramPath = StartProgramPathTextBox.Text.Trim();
                if (int.TryParse(StartProgramWaitTimeoutTextBox.Text.Trim(), out var wt))
                    startProg.WaitTimeoutMs = Math.Max(0, wt);
                startProg.FailureAction = StartProgramFailureActionComboBox.SelectedIndex == 1
                    ? ProgramStartFailureAction.Retry : ProgramStartFailureAction.None;
                if (int.TryParse(StartProgramRetryCountTextBox.Text.Trim(), out var rc))
                    startProg.RetryCount = Math.Max(0, rc);
                break;

            case PrintLogNodeViewModel printNode:
                if (!IsInputPinConnected(printNode, "message"))
                    printNode.Message = PrintLogMessageTextBox.Text;
                break;

            case SelectWindowNodeViewModel selectWindowNode:
                if (!IsInputPinConnected(selectWindowNode, "process_name"))
                {
                    if (selectWindowNode.InputMode == WindowInputMode.Auto)
                        selectWindowNode.ProcessName = (SelectWindowAutoComboBox.SelectedItem as string) ?? string.Empty;
                    else
                        selectWindowNode.ProcessName = SelectWindowProcessNameTextBox.Text.Trim();
                }
                break;
        }

        node.RefreshDescription();
        _graphListController.MarkDirty();
        InspectorHintTextBlock.Text = $"当前选中：{node.Title}（已自动保存）";
        SetStatus($"节点已自动保存：{node.Title}");
    }

    private void InspectorField_TextChanged(object sender, TextChangedEventArgs e) => ApplyInspectorChanges();
    private void InspectorField_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyInspectorChanges();

    private void BrowseFindImagePath_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择图片文件",
            Filter = "图片文件 (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|所有文件(*.*)|*.*",
        };

        if (dialog.ShowDialog(this) == true)
        {
            FindImagePathTextBox.Text = dialog.FileName;
            ApplyInspectorChanges();
        }
    }

    private void BrowseStartProgramPath_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择应用程序",
            Filter = "可执行文件 (*.exe;*.bat;*.cmd)|*.exe;*.bat;*.cmd|所有文件(*.*)|*.*",
        };

        if (dialog.ShowDialog(this) == true)
        {
            StartProgramPathTextBox.Text = dialog.FileName;
            ApplyInspectorChanges();
        }
    }

    private void SelectWindowInputMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingInspector) return;

        var mode = SelectWindowInputModeComboBox.SelectedIndex == 1 ? WindowInputMode.Auto : WindowInputMode.Manual;
        var node = _editorService.Nodes.OfType<SelectWindowNodeViewModel>().FirstOrDefault(n => n.IsSelected);
        if (node is null) return;

        bool locked = IsInputPinConnected(node, "process_name");
        node.InputMode = mode;
        UpdateSelectWindowModeVisibility(mode, locked);

        if (mode == WindowInputMode.Auto && !locked)
        {
            PopulateWindowListComboBox();
            SelectWindowAutoComboBox.SelectedItem = node.ProcessName;
        }

        _graphListController.MarkDirty();
    }

    private void SelectWindowAutoComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingInspector) return;
        ApplyInspectorChanges();
    }

    private void RefreshWindowList_Click(object sender, RoutedEventArgs e)
    {
        PopulateWindowListComboBox();
        var node = _editorService.Nodes.OfType<SelectWindowNodeViewModel>().FirstOrDefault(n => n.IsSelected);
        if (node is not null)
            SelectWindowAutoComboBox.SelectedItem = node.ProcessName;
    }

    private void PopulateWindowListComboBox()
    {
        var names = _windowAdapter.GetRunningWindowNames();
        SelectWindowAutoComboBox.Items.Clear();
        foreach (var name in names)
            SelectWindowAutoComboBox.Items.Add(name);
    }

    private void UpdateSelectWindowModeVisibility(WindowInputMode mode, bool locked)
    {
        bool isAuto = mode == WindowInputMode.Auto;
        SelectWindowManualPanel.Visibility = locked ? Visibility.Visible : (isAuto ? Visibility.Collapsed : Visibility.Visible);
        SelectWindowAutoPanel.Visibility = isAuto && !locked ? Visibility.Visible : Visibility.Collapsed;
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
        _graphListController.MarkDirty();

        if (_editorService.Nodes.FirstOrDefault(n => n.IsSelected) is { } selected)
        {
            LoadNodeToInspector(selected);
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

    private bool IsInputPinConnected(NodeBaseViewModel node, string pinName)
    {
        return node.InputPins.FirstOrDefault(p => p.Name == pinName)?.HasConnection ?? false;
    }

    private Point ViewportToGraph(Point viewportPoint)
    {
        return _canvasPanZoomController.ViewportToGraph(viewportPoint);
    }

    private void FitGraphToView()
    {
        _canvasPanZoomController.FitGraphToView();
    }

    private void RefreshInspectorLocks(NodeBaseViewModel? node)
    {
        _inspectorController.RefreshLocks(node, IsInputPinConnected);
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

    private void PopulateKeyboardKeyComboBox(string selectedKey)
    {
        KeyboardKeyComboBox.Items.Clear();
        string[] keys =
        {
            "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M",
            "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z",
            "D0", "D1", "D2", "D3", "D4", "D5", "D6", "D7", "D8", "D9",
            "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12",
            "Enter", "Escape", "Space", "Tab", "Backspace",
            "Shift", "Control", "Alt",
            "Left", "Up", "Right", "Down",
            "Insert", "DeleteKey", "Home", "End", "PageUp", "PageDown",
            "NumPad0", "NumPad1", "NumPad2", "NumPad3", "NumPad4",
            "NumPad5", "NumPad6", "NumPad7", "NumPad8", "NumPad9",
            "Add", "Subtract", "Multiply", "Divide",
            "LWin", "RWin",
        };

        foreach (var key in keys)
        {
            var item = new ComboBoxItem { Content = key, Tag = key };
            KeyboardKeyComboBox.Items.Add(item);
            if (key == selectedKey)
                KeyboardKeyComboBox.SelectedItem = item;
        }
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
