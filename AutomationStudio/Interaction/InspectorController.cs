using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AutomationStudioWpf.Adapters;
using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Services;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfComboBoxItem = System.Windows.Controls.ComboBoxItem;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfControl = System.Windows.Controls.Control;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace AutomationStudioWpf.Interaction;

/// <summary>
/// Owns node inspector loading, autosave, browse dialogs, and field lock state.
/// MainWindow only forwards XAML events into this controller.
/// </summary>
public sealed class InspectorController
{
    private readonly Window _owner;
    private readonly GraphEditorService _editorService;
    private readonly Win32WindowAdapter _windowAdapter;
    private readonly Action _markDirty;
    private readonly Action<string> _setStatus;

    private readonly TextBlock _hintTextBlock;
    private readonly WpfTextBox _nodeTitleTextBox;
    private readonly StackPanel[] _inspectorPanels;

    private readonly StackPanel _findImageInspectorPanel;
    private readonly WpfTextBox _findImagePathTextBox;
    private readonly WpfTextBox _findImageThresholdTextBox;
    private readonly WpfCheckBox _findImageUseRegionCheckBox;
    private readonly WpfTextBox _findImageRegionXTextBox;
    private readonly WpfTextBox _findImageRegionYTextBox;
    private readonly WpfTextBox _findImageRegionWidthTextBox;
    private readonly WpfTextBox _findImageRegionHeightTextBox;

    private readonly StackPanel _mouseLeftInspectorPanel;
    private readonly WpfTextBox _mousePositionXTextBox;
    private readonly WpfTextBox _mousePositionYTextBox;
    private readonly WpfComboBox _mouseClickOperationModeComboBox;
    private readonly WpfComboBox _mouseButtonComboBox;

    private readonly StackPanel _keyboardInspectorPanel;
    private readonly WpfComboBox _keyboardKeyComboBox;
    private readonly WpfComboBox _keyboardOperationModeComboBox;

    private readonly StackPanel _scrollWheelInspectorPanel;
    private readonly WpfComboBox _scrollWheelActionComboBox;
    private readonly WpfTextBox _scrollWheelSpeedTextBox;
    private readonly WpfTextBox _scrollWheelIntervalTextBox;
    private readonly WpfTextBox _scrollWheelDurationTextBox;

    private readonly StackPanel _ifInspectorPanel;
    private readonly WpfComboBox _ifConditionComboBox;

    private readonly StackPanel _forLoopInspectorPanel;
    private readonly WpfTextBox _forLoopCountTextBox;
    private readonly WpfComboBox _forLoopEndConditionComboBox;

    private readonly StackPanel _whileLoopInspectorPanel;
    private readonly WpfComboBox _whileLoopConditionComboBox;
    private readonly WpfComboBox _whileLoopModeComboBox;
    private readonly TextBlock _whileMaxIterationsLabel;
    private readonly WpfTextBox _whileMaxIterationsTextBox;

    private readonly StackPanel _delayInspectorPanel;
    private readonly WpfTextBox _delayMsTextBox;

    private readonly StackPanel _mouseMoveInspectorPanel;
    private readonly WpfTextBox _mouseMovePositionXTextBox;
    private readonly WpfTextBox _mouseMovePositionYTextBox;

    private readonly StackPanel _startProgramInspectorPanel;
    private readonly WpfTextBox _startProgramPathTextBox;
    private readonly WpfTextBox _startProgramWaitTimeoutTextBox;
    private readonly WpfComboBox _startProgramFailureActionComboBox;
    private readonly WpfTextBox _startProgramRetryCountTextBox;

    private readonly StackPanel _printLogInspectorPanel;
    private readonly WpfTextBox _printLogMessageTextBox;

    private readonly StackPanel _selectWindowInspectorPanel;
    private readonly WpfComboBox _selectWindowInputModeComboBox;
    private readonly StackPanel _selectWindowManualPanel;
    private readonly WpfTextBox _selectWindowProcessNameTextBox;
    private readonly StackPanel _selectWindowAutoPanel;
    private readonly WpfComboBox _selectWindowAutoComboBox;

    private bool _isLoading;

    public InspectorController(
        Window owner,
        GraphEditorService editorService,
        Win32WindowAdapter windowAdapter,
        Action markDirty,
        Action<string> setStatus,
        TextBlock hintTextBlock,
        WpfTextBox nodeTitleTextBox,
        StackPanel findImageInspectorPanel,
        WpfTextBox findImagePathTextBox,
        WpfTextBox findImageThresholdTextBox,
        WpfCheckBox findImageUseRegionCheckBox,
        WpfTextBox findImageRegionXTextBox,
        WpfTextBox findImageRegionYTextBox,
        WpfTextBox findImageRegionWidthTextBox,
        WpfTextBox findImageRegionHeightTextBox,
        StackPanel mouseLeftInspectorPanel,
        WpfTextBox mousePositionXTextBox,
        WpfTextBox mousePositionYTextBox,
        WpfComboBox mouseClickOperationModeComboBox,
        WpfComboBox mouseButtonComboBox,
        StackPanel keyboardInspectorPanel,
        WpfComboBox keyboardKeyComboBox,
        WpfComboBox keyboardOperationModeComboBox,
        StackPanel scrollWheelInspectorPanel,
        WpfComboBox scrollWheelActionComboBox,
        WpfTextBox scrollWheelSpeedTextBox,
        WpfTextBox scrollWheelIntervalTextBox,
        WpfTextBox scrollWheelDurationTextBox,
        StackPanel ifInspectorPanel,
        WpfComboBox ifConditionComboBox,
        StackPanel forLoopInspectorPanel,
        WpfTextBox forLoopCountTextBox,
        WpfComboBox forLoopEndConditionComboBox,
        StackPanel whileLoopInspectorPanel,
        WpfComboBox whileLoopConditionComboBox,
        WpfComboBox whileLoopModeComboBox,
        TextBlock whileMaxIterationsLabel,
        WpfTextBox whileMaxIterationsTextBox,
        StackPanel delayInspectorPanel,
        WpfTextBox delayMsTextBox,
        StackPanel mouseMoveInspectorPanel,
        WpfTextBox mouseMovePositionXTextBox,
        WpfTextBox mouseMovePositionYTextBox,
        StackPanel startProgramInspectorPanel,
        WpfTextBox startProgramPathTextBox,
        WpfTextBox startProgramWaitTimeoutTextBox,
        WpfComboBox startProgramFailureActionComboBox,
        WpfTextBox startProgramRetryCountTextBox,
        StackPanel printLogInspectorPanel,
        WpfTextBox printLogMessageTextBox,
        StackPanel selectWindowInspectorPanel,
        WpfComboBox selectWindowInputModeComboBox,
        StackPanel selectWindowManualPanel,
        WpfTextBox selectWindowProcessNameTextBox,
        StackPanel selectWindowAutoPanel,
        WpfComboBox selectWindowAutoComboBox)
    {
        _owner = owner;
        _editorService = editorService;
        _windowAdapter = windowAdapter;
        _markDirty = markDirty;
        _setStatus = setStatus;
        _hintTextBlock = hintTextBlock;
        _nodeTitleTextBox = nodeTitleTextBox;

        _findImageInspectorPanel = findImageInspectorPanel;
        _findImagePathTextBox = findImagePathTextBox;
        _findImageThresholdTextBox = findImageThresholdTextBox;
        _findImageUseRegionCheckBox = findImageUseRegionCheckBox;
        _findImageRegionXTextBox = findImageRegionXTextBox;
        _findImageRegionYTextBox = findImageRegionYTextBox;
        _findImageRegionWidthTextBox = findImageRegionWidthTextBox;
        _findImageRegionHeightTextBox = findImageRegionHeightTextBox;

        _mouseLeftInspectorPanel = mouseLeftInspectorPanel;
        _mousePositionXTextBox = mousePositionXTextBox;
        _mousePositionYTextBox = mousePositionYTextBox;
        _mouseClickOperationModeComboBox = mouseClickOperationModeComboBox;
        _mouseButtonComboBox = mouseButtonComboBox;

        _keyboardInspectorPanel = keyboardInspectorPanel;
        _keyboardKeyComboBox = keyboardKeyComboBox;
        _keyboardOperationModeComboBox = keyboardOperationModeComboBox;

        _scrollWheelInspectorPanel = scrollWheelInspectorPanel;
        _scrollWheelActionComboBox = scrollWheelActionComboBox;
        _scrollWheelSpeedTextBox = scrollWheelSpeedTextBox;
        _scrollWheelIntervalTextBox = scrollWheelIntervalTextBox;
        _scrollWheelDurationTextBox = scrollWheelDurationTextBox;

        _ifInspectorPanel = ifInspectorPanel;
        _ifConditionComboBox = ifConditionComboBox;

        _forLoopInspectorPanel = forLoopInspectorPanel;
        _forLoopCountTextBox = forLoopCountTextBox;
        _forLoopEndConditionComboBox = forLoopEndConditionComboBox;

        _whileLoopInspectorPanel = whileLoopInspectorPanel;
        _whileLoopConditionComboBox = whileLoopConditionComboBox;
        _whileLoopModeComboBox = whileLoopModeComboBox;
        _whileMaxIterationsLabel = whileMaxIterationsLabel;
        _whileMaxIterationsTextBox = whileMaxIterationsTextBox;

        _delayInspectorPanel = delayInspectorPanel;
        _delayMsTextBox = delayMsTextBox;

        _mouseMoveInspectorPanel = mouseMoveInspectorPanel;
        _mouseMovePositionXTextBox = mouseMovePositionXTextBox;
        _mouseMovePositionYTextBox = mouseMovePositionYTextBox;

        _startProgramInspectorPanel = startProgramInspectorPanel;
        _startProgramPathTextBox = startProgramPathTextBox;
        _startProgramWaitTimeoutTextBox = startProgramWaitTimeoutTextBox;
        _startProgramFailureActionComboBox = startProgramFailureActionComboBox;
        _startProgramRetryCountTextBox = startProgramRetryCountTextBox;

        _printLogInspectorPanel = printLogInspectorPanel;
        _printLogMessageTextBox = printLogMessageTextBox;

        _selectWindowInspectorPanel = selectWindowInspectorPanel;
        _selectWindowInputModeComboBox = selectWindowInputModeComboBox;
        _selectWindowManualPanel = selectWindowManualPanel;
        _selectWindowProcessNameTextBox = selectWindowProcessNameTextBox;
        _selectWindowAutoPanel = selectWindowAutoPanel;
        _selectWindowAutoComboBox = selectWindowAutoComboBox;

        _inspectorPanels =
        [
            _findImageInspectorPanel,
            _mouseLeftInspectorPanel,
            _keyboardInspectorPanel,
            _scrollWheelInspectorPanel,
            _ifInspectorPanel,
            _forLoopInspectorPanel,
            _whileLoopInspectorPanel,
            _delayInspectorPanel,
            _mouseMoveInspectorPanel,
            _startProgramInspectorPanel,
            _printLogInspectorPanel,
            _selectWindowInspectorPanel,
        ];
    }

    public void LoadNode(NodeBaseViewModel? node)
    {
        _isLoading = true;
        try
        {
            if (node is null)
            {
                _nodeTitleTextBox.Text = string.Empty;
                HideAllPanels();
                _hintTextBlock.Text = "请选择一个节点进行编辑。";
                RefreshLocks(null);
                return;
            }

            _nodeTitleTextBox.Text = node.Title;
            _hintTextBlock.Text = $"当前选中：{node.Title}";
            HideAllPanels();

            switch (node)
            {
                case FindImageNodeViewModel findImage:
                    _findImageInspectorPanel.Visibility = Visibility.Visible;
                    _findImagePathTextBox.Text = findImage.ImagePath;
                    _findImageThresholdTextBox.Text = findImage.SimilarityThresholdPercent.ToString();
                    _findImageUseRegionCheckBox.IsChecked = findImage.UseRegion;
                    _findImageRegionXTextBox.Text = findImage.RegionX.ToString("0.##");
                    _findImageRegionYTextBox.Text = findImage.RegionY.ToString("0.##");
                    _findImageRegionWidthTextBox.Text = findImage.RegionWidth.ToString("0.##");
                    _findImageRegionHeightTextBox.Text = findImage.RegionHeight.ToString("0.##");
                    break;

                case MouseClickNodeViewModel mouseNode:
                    _mouseLeftInspectorPanel.Visibility = Visibility.Visible;
                    _mousePositionXTextBox.Text = mouseNode.PositionX.ToString("0.##");
                    _mousePositionYTextBox.Text = mouseNode.PositionY.ToString("0.##");
                    _mouseClickOperationModeComboBox.SelectedIndex = (int)mouseNode.OperationMode;
                    _mouseButtonComboBox.SelectedIndex = (int)mouseNode.MouseButton;
                    break;

                case KeyboardNodeViewModel keyboardNode:
                    _keyboardInspectorPanel.Visibility = Visibility.Visible;
                    PopulateKeyboardKeyComboBox(keyboardNode.Key);
                    _keyboardOperationModeComboBox.SelectedIndex = keyboardNode.OperationMode switch
                    {
                        PressReleaseMode.Press => 0,
                        PressReleaseMode.Release => 1,
                        PressReleaseMode.Click => 2,
                        _ => 0,
                    };
                    break;

                case IfNodeViewModel ifNode:
                    _ifInspectorPanel.Visibility = Visibility.Visible;
                    _ifConditionComboBox.SelectedIndex = ifNode.ConditionValue ? 1 : 0;
                    break;

                case WhileLoopNodeViewModel whileNode:
                    _whileLoopInspectorPanel.Visibility = Visibility.Visible;
                    _whileLoopConditionComboBox.SelectedIndex = whileNode.ConditionValue ? 1 : 0;
                    _whileLoopModeComboBox.SelectedIndex = whileNode.LoopMode == WhileLoopMode.Infinite ? 1 : 0;
                    _whileMaxIterationsTextBox.Text = whileNode.MaxIterations.ToString();
                    SetWhileMaxIterationsVisible(whileNode.LoopMode != WhileLoopMode.Infinite);
                    break;

                case ForLoopNodeViewModel forLoopNode:
                    _forLoopInspectorPanel.Visibility = Visibility.Visible;
                    _forLoopCountTextBox.Text = forLoopNode.LoopCount.ToString();
                    _forLoopEndConditionComboBox.SelectedIndex = forLoopNode.EndConditionValue ? 1 : 0;
                    break;

                case ScrollWheelNodeViewModel scrollNode:
                    _scrollWheelInspectorPanel.Visibility = Visibility.Visible;
                    _scrollWheelActionComboBox.SelectedIndex = (int)scrollNode.ScrollAction;
                    _scrollWheelSpeedTextBox.Text = scrollNode.ScrollSpeed.ToString();
                    _scrollWheelIntervalTextBox.Text = scrollNode.ScrollInterval.ToString();
                    _scrollWheelDurationTextBox.Text = scrollNode.ScrollDuration.ToString();
                    break;

                case DelayNodeViewModel delayNode:
                    _delayInspectorPanel.Visibility = Visibility.Visible;
                    _delayMsTextBox.Text = delayNode.DelayMs.ToString();
                    break;

                case MouseMoveNodeViewModel moveNode:
                    _mouseMoveInspectorPanel.Visibility = Visibility.Visible;
                    _mouseMovePositionXTextBox.Text = moveNode.PositionX.ToString("0.##");
                    _mouseMovePositionYTextBox.Text = moveNode.PositionY.ToString("0.##");
                    break;

                case StartProgramNodeViewModel startProg:
                    _startProgramInspectorPanel.Visibility = Visibility.Visible;
                    _startProgramPathTextBox.Text = startProg.ProgramPath;
                    _startProgramWaitTimeoutTextBox.Text = startProg.WaitTimeoutMs.ToString();
                    _startProgramFailureActionComboBox.SelectedIndex = startProg.FailureAction == ProgramStartFailureAction.Retry ? 1 : 0;
                    _startProgramRetryCountTextBox.Text = startProg.RetryCount.ToString();
                    break;

                case PrintLogNodeViewModel printNode:
                    _printLogInspectorPanel.Visibility = Visibility.Visible;
                    _printLogMessageTextBox.Text = IsInputPinConnected(printNode, "message") ? "前置输入" : printNode.Message;
                    break;

                case SelectWindowNodeViewModel selectWindowNode:
                    _selectWindowInspectorPanel.Visibility = Visibility.Visible;
                    _selectWindowInputModeComboBox.SelectedIndex = selectWindowNode.InputMode == WindowInputMode.Auto ? 1 : 0;
                    bool hasProcessNameInput = IsInputPinConnected(selectWindowNode, "process_name");
                    if (hasProcessNameInput)
                    {
                        _selectWindowProcessNameTextBox.Text = "前置输入";
                        _selectWindowAutoComboBox.SelectedItem = null;
                    }
                    else if (selectWindowNode.InputMode == WindowInputMode.Auto)
                    {
                        PopulateWindowListComboBox();
                        _selectWindowAutoComboBox.SelectedItem = selectWindowNode.ProcessName;
                    }
                    else
                    {
                        _selectWindowProcessNameTextBox.Text = selectWindowNode.ProcessName;
                    }
                    UpdateSelectWindowModeVisibility(selectWindowNode.InputMode, hasProcessNameInput);
                    break;
            }

            RefreshLocks(node);
        }
        finally
        {
            _isLoading = false;
        }
    }

    public void ApplyChanges()
    {
        if (_editorService.Nodes.FirstOrDefault(n => n.IsSelected) is not { } node || _isLoading)
            return;

        node.Title = _nodeTitleTextBox.Text.Trim();

        switch (node)
        {
            case FindImageNodeViewModel findImage:
                findImage.ImagePath = _findImagePathTextBox.Text.Trim();
                if (int.TryParse(_findImageThresholdTextBox.Text.Trim(), out var threshold))
                    findImage.SimilarityThresholdPercent = threshold;
                findImage.UseRegion = _findImageUseRegionCheckBox.IsChecked == true;
                if (double.TryParse(_findImageRegionXTextBox.Text.Trim(), out var regionX))
                    findImage.RegionX = regionX;
                if (double.TryParse(_findImageRegionYTextBox.Text.Trim(), out var regionY))
                    findImage.RegionY = regionY;
                if (double.TryParse(_findImageRegionWidthTextBox.Text.Trim(), out var regionWidth))
                    findImage.RegionWidth = regionWidth;
                if (double.TryParse(_findImageRegionHeightTextBox.Text.Trim(), out var regionHeight))
                    findImage.RegionHeight = regionHeight;
                break;

            case MouseClickNodeViewModel mouseNode:
                mouseNode.OperationMode = (PressReleaseMode)_mouseClickOperationModeComboBox.SelectedIndex;
                mouseNode.MouseButton = (Graph.MouseButton)_mouseButtonComboBox.SelectedIndex;
                if (double.TryParse(_mousePositionXTextBox.Text.Trim(), out var x))
                    mouseNode.PositionX = x;
                if (double.TryParse(_mousePositionYTextBox.Text.Trim(), out var y))
                    mouseNode.PositionY = y;
                break;

            case KeyboardNodeViewModel keyboardNode:
                keyboardNode.OperationMode = _keyboardOperationModeComboBox.SelectedIndex switch
                {
                    1 => PressReleaseMode.Release,
                    2 => PressReleaseMode.Click,
                    _ => PressReleaseMode.Press,
                };
                if (_keyboardKeyComboBox.SelectedItem is WpfComboBoxItem keyItem && keyItem.Tag is string keyStr)
                    keyboardNode.Key = keyStr;
                break;

            case IfNodeViewModel ifNode:
                ifNode.ConditionValue = _ifConditionComboBox.SelectedIndex == 1;
                break;

            case WhileLoopNodeViewModel whileNode:
                whileNode.ConditionValue = _whileLoopConditionComboBox.SelectedIndex == 1;
                whileNode.LoopMode = _whileLoopModeComboBox.SelectedIndex == 1 ? WhileLoopMode.Infinite : WhileLoopMode.Finite;
                if (int.TryParse(_whileMaxIterationsTextBox.Text.Trim(), out var wm))
                    whileNode.MaxIterations = Math.Max(1, wm);
                SetWhileMaxIterationsVisible(whileNode.LoopMode != WhileLoopMode.Infinite);
                break;

            case ForLoopNodeViewModel forLoopNode:
                if (int.TryParse(_forLoopCountTextBox.Text.Trim(), out var count))
                    forLoopNode.LoopCount = Math.Max(1, count);
                forLoopNode.EndConditionValue = _forLoopEndConditionComboBox.SelectedIndex == 1;
                break;

            case ScrollWheelNodeViewModel scrollNode:
                scrollNode.ScrollAction = (ScrollWheelAction)_scrollWheelActionComboBox.SelectedIndex;
                if (int.TryParse(_scrollWheelSpeedTextBox.Text.Trim(), out var speed))
                    scrollNode.ScrollSpeed = Math.Max(0, speed);
                if (int.TryParse(_scrollWheelIntervalTextBox.Text.Trim(), out var interval))
                    scrollNode.ScrollInterval = Math.Max(1, interval);
                if (int.TryParse(_scrollWheelDurationTextBox.Text.Trim(), out var duration))
                    scrollNode.ScrollDuration = Math.Max(0, duration);
                break;

            case DelayNodeViewModel delayNode:
                if (int.TryParse(_delayMsTextBox.Text.Trim(), out var delayMs))
                    delayNode.DelayMs = delayMs;
                break;

            case MouseMoveNodeViewModel moveNode:
                if (double.TryParse(_mouseMovePositionXTextBox.Text.Trim(), out var moveX))
                    moveNode.PositionX = moveX;
                if (double.TryParse(_mouseMovePositionYTextBox.Text.Trim(), out var moveY))
                    moveNode.PositionY = moveY;
                break;

            case StartProgramNodeViewModel startProg:
                startProg.ProgramPath = _startProgramPathTextBox.Text.Trim();
                if (int.TryParse(_startProgramWaitTimeoutTextBox.Text.Trim(), out var wt))
                    startProg.WaitTimeoutMs = Math.Max(0, wt);
                startProg.FailureAction = _startProgramFailureActionComboBox.SelectedIndex == 1
                    ? ProgramStartFailureAction.Retry : ProgramStartFailureAction.None;
                if (int.TryParse(_startProgramRetryCountTextBox.Text.Trim(), out var rc))
                    startProg.RetryCount = Math.Max(0, rc);
                break;

            case PrintLogNodeViewModel printNode:
                if (!IsInputPinConnected(printNode, "message"))
                    printNode.Message = _printLogMessageTextBox.Text;
                break;

            case SelectWindowNodeViewModel selectWindowNode:
                if (!IsInputPinConnected(selectWindowNode, "process_name"))
                {
                    if (selectWindowNode.InputMode == WindowInputMode.Auto)
                        selectWindowNode.ProcessName = (_selectWindowAutoComboBox.SelectedItem as string) ?? string.Empty;
                    else
                        selectWindowNode.ProcessName = _selectWindowProcessNameTextBox.Text.Trim();
                }
                break;
        }

        node.RefreshDescription();
        _markDirty();
        _hintTextBlock.Text = $"当前选中：{node.Title}（已自动保存）";
        _setStatus($"节点已自动保存：{node.Title}");
    }

    public void BrowseFindImagePath()
    {
        var dialog = new WpfOpenFileDialog
        {
            Title = "选择图片文件",
            Filter = "图片文件 (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|所有文件(*.*)|*.*",
        };

        if (dialog.ShowDialog(_owner) == true)
        {
            _findImagePathTextBox.Text = dialog.FileName;
            ApplyChanges();
        }
    }

    public void BrowseStartProgramPath()
    {
        var dialog = new WpfOpenFileDialog
        {
            Title = "选择应用程序",
            Filter = "可执行文件 (*.exe;*.bat;*.cmd)|*.exe;*.bat;*.cmd|所有文件(*.*)|*.*",
        };

        if (dialog.ShowDialog(_owner) == true)
        {
            _startProgramPathTextBox.Text = dialog.FileName;
            ApplyChanges();
        }
    }

    public void SelectWindowInputModeChanged()
    {
        if (_isLoading)
            return;

        var mode = _selectWindowInputModeComboBox.SelectedIndex == 1 ? WindowInputMode.Auto : WindowInputMode.Manual;
        var node = _editorService.Nodes.OfType<SelectWindowNodeViewModel>().FirstOrDefault(n => n.IsSelected);
        if (node is null)
            return;

        bool locked = IsInputPinConnected(node, "process_name");
        node.InputMode = mode;
        UpdateSelectWindowModeVisibility(mode, locked);

        if (mode == WindowInputMode.Auto && !locked)
        {
            PopulateWindowListComboBox();
            _selectWindowAutoComboBox.SelectedItem = node.ProcessName;
        }

        _markDirty();
    }

    public void SelectWindowAutoChanged()
    {
        if (!_isLoading)
            ApplyChanges();
    }

    public void RefreshWindowList()
    {
        PopulateWindowListComboBox();
        var node = _editorService.Nodes.OfType<SelectWindowNodeViewModel>().FirstOrDefault(n => n.IsSelected);
        if (node is not null)
            _selectWindowAutoComboBox.SelectedItem = node.ProcessName;
    }

    public void RefreshLocks(NodeBaseViewModel? node)
    {
        if (node is MouseClickNodeViewModel clickNode)
        {
            bool locked = IsInputPinConnected(clickNode, "position");
            LockTextBox(_mousePositionXTextBox, locked, clickNode.PositionX.ToString("0.##"));
            LockTextBox(_mousePositionYTextBox, locked, clickNode.PositionY.ToString("0.##"));
        }
        else
        {
            LockTextBox(_mousePositionXTextBox, false, string.Empty);
            LockTextBox(_mousePositionYTextBox, false, string.Empty);
        }

        if (node is MouseMoveNodeViewModel moveNode)
        {
            bool locked = IsInputPinConnected(moveNode, "position");
            LockTextBox(_mouseMovePositionXTextBox, locked, moveNode.PositionX.ToString("0.##"));
            LockTextBox(_mouseMovePositionYTextBox, locked, moveNode.PositionY.ToString("0.##"));
        }
        else
        {
            LockTextBox(_mouseMovePositionXTextBox, false, string.Empty);
            LockTextBox(_mouseMovePositionYTextBox, false, string.Empty);
        }

        LockCondition(_ifConditionComboBox, node is IfNodeViewModel ifNode && IsInputPinConnected(ifNode, "condition"), node is IfNodeViewModel ifValue && ifValue.ConditionValue);
        LockCondition(_whileLoopConditionComboBox, node is WhileLoopNodeViewModel whileNode && IsInputPinConnected(whileNode, "condition"), node is WhileLoopNodeViewModel whileValue && whileValue.ConditionValue);
        LockCondition(_forLoopEndConditionComboBox, node is ForLoopNodeViewModel forNode && IsInputPinConnected(forNode, "end_condition"), node is ForLoopNodeViewModel forValue && forValue.EndConditionValue);

        LockTextBox(_printLogMessageTextBox, node is PrintLogNodeViewModel printNode && IsInputPinConnected(printNode, "message"), node is PrintLogNodeViewModel printValue ? printValue.Message : string.Empty);
        LockTextBox(_selectWindowProcessNameTextBox, node is SelectWindowNodeViewModel selectNode && IsInputPinConnected(selectNode, "process_name"), node is SelectWindowNodeViewModel selectValue ? selectValue.ProcessName : string.Empty);
    }

    private void HideAllPanels()
    {
        foreach (var panel in _inspectorPanels)
            panel.Visibility = Visibility.Collapsed;
    }

    private bool IsInputPinConnected(NodeBaseViewModel node, string pinName)
    {
        return node.InputPins.FirstOrDefault(pin => pin.Name == pinName)?.HasConnection ?? false;
    }

    private void PopulateWindowListComboBox()
    {
        var names = _windowAdapter.GetRunningWindowNames();
        _selectWindowAutoComboBox.Items.Clear();
        foreach (var name in names)
            _selectWindowAutoComboBox.Items.Add(name);
    }

    private void UpdateSelectWindowModeVisibility(WindowInputMode mode, bool locked)
    {
        bool isAuto = mode == WindowInputMode.Auto;
        _selectWindowManualPanel.Visibility = locked ? Visibility.Visible : (isAuto ? Visibility.Collapsed : Visibility.Visible);
        _selectWindowAutoPanel.Visibility = isAuto && !locked ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetWhileMaxIterationsVisible(bool visible)
    {
        _whileMaxIterationsLabel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        _whileMaxIterationsTextBox.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PopulateKeyboardKeyComboBox(string selectedKey)
    {
        _keyboardKeyComboBox.Items.Clear();
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
            var item = new WpfComboBoxItem { Content = key, Tag = key };
            _keyboardKeyComboBox.Items.Add(item);
            if (key == selectedKey)
                _keyboardKeyComboBox.SelectedItem = item;
        }
    }

    private static void LockTextBox(WpfTextBox textBox, bool locked, string restoreValue)
    {
        textBox.IsEnabled = !locked;
        if (locked)
        {
            textBox.Text = "前置输入";
            textBox.Foreground = Brush(0x7A, 0x87, 0x97);
            textBox.Background = Brush(0x25, 0x29, 0x30);
            textBox.BorderBrush = Brush(0x3A, 0x40, 0x4A);
        }
        else
        {
            textBox.Text = restoreValue;
            textBox.ClearValue(WpfControl.ForegroundProperty);
            textBox.ClearValue(WpfControl.BackgroundProperty);
            textBox.ClearValue(WpfControl.BorderBrushProperty);
        }
    }

    private static void LockCondition(WpfComboBox comboBox, bool locked, bool restoreValue)
    {
        comboBox.IsEnabled = !locked;
        if (locked)
        {
            comboBox.Foreground = System.Windows.Media.Brushes.Gray;
            comboBox.Background = Brush(0x25, 0x29, 0x30);
            comboBox.BorderBrush = Brush(0x3A, 0x40, 0x4A);
            comboBox.Items.Clear();
            comboBox.Items.Add(new WpfComboBoxItem { Content = "前置输入" });
            comboBox.SelectedIndex = 0;
        }
        else
        {
            comboBox.ClearValue(WpfControl.ForegroundProperty);
            comboBox.ClearValue(WpfControl.BackgroundProperty);
            comboBox.ClearValue(WpfControl.BorderBrushProperty);
            if (comboBox.Items.Count != 2)
            {
                comboBox.Items.Clear();
                comboBox.Items.Add(new WpfComboBoxItem { Content = "False" });
                comboBox.Items.Add(new WpfComboBoxItem { Content = "True" });
            }

            comboBox.SelectedIndex = restoreValue ? 1 : 0;
        }
    }

    private static SolidColorBrush Brush(byte r, byte g, byte b) => new(System.Windows.Media.Color.FromRgb(r, g, b));
}
