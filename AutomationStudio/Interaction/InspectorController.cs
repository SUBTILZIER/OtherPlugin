using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AutomationStudioWpf.Adapters;
using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Services;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfComboBoxItem = System.Windows.Controls.ComboBoxItem;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfButton = System.Windows.Controls.Button;
using WpfControl = System.Windows.Controls.Control;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WpfSaveFileDialog = Microsoft.Win32.SaveFileDialog;
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
    private readonly StackPanel _parameterInspectorPanel;
    private readonly TextBlock _parameterInspectorTitle;
    private readonly StackPanel _parameterRowsPanel;

    private readonly StackPanel _findImageInspectorPanel;
    private readonly WpfComboBox _findImageSourceModeComboBox;
    private readonly TextBlock _findImageSourcePathLabel;
    private readonly DockPanel _findImageSourcePathPanel;
    private readonly WpfTextBox _findImageSourcePathTextBox;
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

    private readonly StackPanel _commonInspectorPanel;
    private readonly StackPanel _commonKeyChordAddPanel;
    private readonly WpfComboBox _commonKeyChordKeyComboBox;
    private readonly StackPanel _commonModePanel;
    private readonly WpfComboBox _commonModeComboBox;
    private readonly StackPanel _commonWindowPickerPanel;
    private readonly WpfComboBox _commonWindowComboBox;
    private readonly StackPanel _commonEnumPanel;
    private readonly TextBlock _commonEnumLabel;
    private readonly WpfComboBox _commonEnumComboBox;
    private readonly WpfButton _commonBrowseFileButton;
    private readonly TextBlock _commonTextLabel;
    private readonly WpfTextBox _commonTextBox;
    private readonly TextBlock _commonText2Label;
    private readonly WpfTextBox _commonText2Box;
    private readonly TextBlock _commonText3Label;
    private readonly WpfTextBox _commonText3Box;
    private readonly TextBlock _commonNumberLabel;
    private readonly WpfTextBox _commonNumberBox;
    private readonly TextBlock _commonNumber2Label;
    private readonly WpfTextBox _commonNumber2Box;
    private readonly TextBlock _commonNumber3Label;
    private readonly WpfTextBox _commonNumber3Box;
    private readonly TextBlock _commonNumber4Label;
    private readonly WpfTextBox _commonNumber4Box;
    private readonly WpfCheckBox _commonFlagCheckBox;
    private readonly TextBlock _commonHelpTextBlock;

    private bool _isLoading;

    private enum ScreenshotSaveMode
    {
        Auto,
        Manual,
    }

    public InspectorController(
        Window owner,
        GraphEditorService editorService,
        Win32WindowAdapter windowAdapter,
        Action markDirty,
        Action<string> setStatus,
        TextBlock hintTextBlock,
        WpfTextBox nodeTitleTextBox,
        StackPanel parameterInspectorPanel,
        TextBlock parameterInspectorTitle,
        StackPanel parameterRowsPanel,
        StackPanel findImageInspectorPanel,
        WpfComboBox findImageSourceModeComboBox,
        TextBlock findImageSourcePathLabel,
        DockPanel findImageSourcePathPanel,
        WpfTextBox findImageSourcePathTextBox,
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
        WpfComboBox selectWindowAutoComboBox,
        StackPanel commonInspectorPanel,
        StackPanel commonKeyChordAddPanel,
        WpfComboBox commonKeyChordKeyComboBox,
        StackPanel commonModePanel,
        WpfComboBox commonModeComboBox,
        StackPanel commonWindowPickerPanel,
        WpfComboBox commonWindowComboBox,
        StackPanel commonEnumPanel,
        TextBlock commonEnumLabel,
        WpfComboBox commonEnumComboBox,
        WpfButton commonBrowseFileButton,
        TextBlock commonTextLabel,
        WpfTextBox commonTextBox,
        TextBlock commonText2Label,
        WpfTextBox commonText2Box,
        TextBlock commonText3Label,
        WpfTextBox commonText3Box,
        TextBlock commonNumberLabel,
        WpfTextBox commonNumberBox,
        TextBlock commonNumber2Label,
        WpfTextBox commonNumber2Box,
        TextBlock commonNumber3Label,
        WpfTextBox commonNumber3Box,
        TextBlock commonNumber4Label,
        WpfTextBox commonNumber4Box,
        WpfCheckBox commonFlagCheckBox,
        TextBlock commonHelpTextBlock)
    {
        _owner = owner;
        _editorService = editorService;
        _windowAdapter = windowAdapter;
        _markDirty = markDirty;
        _setStatus = setStatus;
        _hintTextBlock = hintTextBlock;
        _nodeTitleTextBox = nodeTitleTextBox;
        _parameterInspectorPanel = parameterInspectorPanel;
        _parameterInspectorTitle = parameterInspectorTitle;
        _parameterRowsPanel = parameterRowsPanel;

        _findImageInspectorPanel = findImageInspectorPanel;
        _findImageSourceModeComboBox = findImageSourceModeComboBox;
        _findImageSourcePathLabel = findImageSourcePathLabel;
        _findImageSourcePathPanel = findImageSourcePathPanel;
        _findImageSourcePathTextBox = findImageSourcePathTextBox;
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

        _commonInspectorPanel = commonInspectorPanel;
        _commonKeyChordAddPanel = commonKeyChordAddPanel;
        _commonKeyChordKeyComboBox = commonKeyChordKeyComboBox;
        _commonModePanel = commonModePanel;
        _commonModeComboBox = commonModeComboBox;
        _commonWindowPickerPanel = commonWindowPickerPanel;
        _commonWindowComboBox = commonWindowComboBox;
        _commonEnumPanel = commonEnumPanel;
        _commonEnumLabel = commonEnumLabel;
        _commonEnumComboBox = commonEnumComboBox;
        _commonBrowseFileButton = commonBrowseFileButton;
        _commonTextLabel = commonTextLabel;
        _commonTextBox = commonTextBox;
        _commonText2Label = commonText2Label;
        _commonText2Box = commonText2Box;
        _commonText3Label = commonText3Label;
        _commonText3Box = commonText3Box;
        _commonNumberLabel = commonNumberLabel;
        _commonNumberBox = commonNumberBox;
        _commonNumber2Label = commonNumber2Label;
        _commonNumber2Box = commonNumber2Box;
        _commonNumber3Label = commonNumber3Label;
        _commonNumber3Box = commonNumber3Box;
        _commonNumber4Label = commonNumber4Label;
        _commonNumber4Box = commonNumber4Box;
        _commonFlagCheckBox = commonFlagCheckBox;
        _commonHelpTextBlock = commonHelpTextBlock;

        _inspectorPanels =
        [
            _parameterInspectorPanel,
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
            _commonInspectorPanel,
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
                case ParameterNodeBaseViewModel parameterNode:
                    _parameterInspectorPanel.Visibility = Visibility.Visible;
                    LoadParameterNode(parameterNode);
                    if (parameterNode is MacroOutputNodeViewModel macroOutput)
                        _nodeTitleTextBox.Text = macroOutput.ExitName;
                    break;

                case FindImageNodeViewModel findImage:
                    _findImageInspectorPanel.Visibility = Visibility.Visible;
                    SelectFindImageSourceMode(findImage.SourceMode);
                    _findImageSourcePathTextBox.Text = findImage.SourceImagePath;
                    UpdateFindImageSourcePathVisibility(findImage.SourceMode);
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
                        SetComboSingleValue(_selectWindowAutoComboBox, selectWindowNode.ProcessName);
                    }
                    else
                    {
                        _selectWindowProcessNameTextBox.Text = selectWindowNode.ProcessName;
                    }
                    UpdateSelectWindowModeVisibility(selectWindowNode.InputMode, hasProcessNameInput);
                    break;

                case CommonNodeViewModel commonNode:
                    _commonInspectorPanel.Visibility = Visibility.Visible;
                    LoadCommonNode(commonNode);
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
            case MacroOutputNodeViewModel macroOutput:
                macroOutput.ExitName = _nodeTitleTextBox.Text.Trim();
                break;
            case ParameterNodeBaseViewModel:
                break;

            case FindImageNodeViewModel findImage:
                findImage.SourceMode = GetFindImageSourceMode();
                findImage.SourceImagePath = _findImageSourcePathTextBox.Text.Trim();
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

            case CommonNodeViewModel commonNode:
                ApplyCommonNodeChanges(commonNode);
                break;
        }

        node.RefreshDescription();
        _markDirty();
        _hintTextBlock.Text = $"当前选中：{node.Title}（已自动保存）";
        _setStatus($"节点已自动保存：{node.Title}");
    }

    private void ApplyCommonNodeChanges(CommonNodeViewModel commonNode)
    {
        if (_commonTextBox.Visibility == Visibility.Visible && _commonTextBox.IsEnabled)
            commonNode.Text = _commonTextBox.Text.Trim();
        if (_commonText2Box.Visibility == Visibility.Visible && _commonText2Box.IsEnabled)
            commonNode.Text2 = _commonText2Box.Text.Trim();
        if (IsEnumCommonNode(commonNode.NodeKind) && _commonEnumComboBox.IsEnabled)
            commonNode.Text2 = GetSelectedComboTag(_commonEnumComboBox, GetCommonEnumFallback(commonNode.NodeKind));
        if (_commonText3Box.Visibility == Visibility.Visible && _commonText3Box.IsEnabled)
            commonNode.Text3 = _commonText3Box.Text.Trim();

        if (_commonNumberBox.Visibility == Visibility.Visible && _commonNumberBox.IsEnabled &&
            double.TryParse(_commonNumberBox.Text.Trim(), out double number))
            commonNode.Number = number;
        if (_commonNumber2Box.Visibility == Visibility.Visible && _commonNumber2Box.IsEnabled &&
            double.TryParse(_commonNumber2Box.Text.Trim(), out double number2))
            commonNode.Number2 = number2;
        if (_commonNumber3Box.Visibility == Visibility.Visible && _commonNumber3Box.IsEnabled &&
            double.TryParse(_commonNumber3Box.Text.Trim(), out double number3))
            commonNode.Number3 = number3;
        if (_commonNumber4Box.Visibility == Visibility.Visible && _commonNumber4Box.IsEnabled &&
            double.TryParse(_commonNumber4Box.Text.Trim(), out double number4))
            commonNode.Number4 = number4;

        if (_commonFlagCheckBox.Visibility == Visibility.Visible && _commonFlagCheckBox.IsEnabled)
            commonNode.Flag = _commonFlagCheckBox.IsChecked == true;

        if (IsWindowCommonNode(commonNode.NodeKind) && _commonModeComboBox.IsEnabled)
            commonNode.Text2 = GetSelectedComboTag(_commonModeComboBox, WindowInputMode.Manual.ToString());
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

    public void BrowseFindImageSourcePath()
    {
        var dialog = new WpfOpenFileDialog
        {
            Title = "选择查找源图像",
            Filter = "图片文件 (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|所有文件(*.*)|*.*",
        };

        if (dialog.ShowDialog(_owner) == true)
        {
            _findImageSourcePathTextBox.Text = dialog.FileName;
            ApplyChanges();
        }
    }

    public void FindImageSourceModeChanged()
    {
        if (_isLoading)
            return;

        var mode = GetFindImageSourceMode();
        if (mode == ImageSearchSourceMode.RealtimeScreenshot &&
            _editorService.Nodes.FirstOrDefault(node => node.IsSelected) is FindImageNodeViewModel findImageNode)
        {
            ClearInputConnections(findImageNode, "source_image_path");
        }
        UpdateFindImageSourcePathVisibility(mode);
        ApplyChanges();
    }

    private void LoadCommonNode(CommonNodeViewModel node)
    {
        _commonTextBox.Text = node.Text;
        _commonText2Box.Text = node.Text2;
        _commonText3Box.Text = node.Text3;
        _commonNumberBox.Text = node.Number.ToString("0.##");
        _commonNumber2Box.Text = node.Number2.ToString("0.##");
        _commonNumber3Box.Text = node.Number3.ToString("0.##");
        _commonNumber4Box.Text = node.Number4.ToString("0.##");
        _commonFlagCheckBox.IsChecked = node.Flag;
        PopulateKeyComboBox(_commonKeyChordKeyComboBox, string.Empty);
        SetComboSingleValue(_commonWindowComboBox, IsWindowCommonNode(node.NodeKind) ? node.Text : string.Empty);
        SelectCommonMode(node.Text2);
        ConfigureCommonLabels(node.NodeKind);
        ConfigureCommonAuxiliaryControls(node);
    }

    private void ConfigureCommonLabels(NodeKind kind)
    {
        SetCommonVisibility(true, true, true, true, true, true, true, true);
        _commonTextLabel.Text = "文本 1";
        _commonText2Label.Text = "文本 2";
        _commonText3Label.Text = "文本 3";
        _commonNumberLabel.Text = "数值 1";
        _commonNumber2Label.Text = "数值 2";
        _commonNumber3Label.Text = "数值 3";
        _commonNumber4Label.Text = "数值 4";
        _commonFlagCheckBox.Content = "布尔值";
        _commonHelpTextBlock.Text = string.Empty;

        switch (kind)
        {
            case NodeKind.MouseDoubleClick:
                SetCommonVisibility(false, false, false, true, true, false, false, false);
                _commonNumberLabel.Text = "点击位置 X";
                _commonNumber2Label.Text = "点击位置 Y";
                _commonHelpTextBlock.Text = "也可连接 Vector2D 到 position 输入，引脚输入优先。";
                break;
            case NodeKind.KeyChord:
                SetCommonVisibility(true, false, false, true, false, false, false, false);
                _commonTextLabel.Text = "组合预览";
                _commonNumberLabel.Text = "按住时长 ms";
                _commonHelpTextBlock.Text = "从上方下拉选择按键并点击增加，也可以手动编辑，例如 Ctrl+C、Ctrl+Shift+Esc。";
                break;
            case NodeKind.WaitImage:
            case NodeKind.WaitImageDisappear:
                SetCommonVisibility(true, false, true, true, true, true, false, false);
                _commonTextLabel.Text = "查找目标";
                _commonText3Label.Text = "查找源图像路径";
                _commonNumberLabel.Text = "超时 ms（0=不超时）";
                _commonNumber2Label.Text = "检测间隔 ms";
                _commonNumber3Label.Text = "相似度阈值 %";
                _commonHelpTextBlock.Text = kind == NodeKind.WaitImage
                    ? "查找源模式用下拉框选择。输出 result、center、image_path。"
                    : "查找源模式用下拉框选择。";
                break;
            case NodeKind.Compare:
                SetCommonVisibility(true, true, true, false, false, false, false, false);
                _commonTextLabel.Text = "左值，变量用 $变量名";
                _commonText2Label.Text = "右值，变量用 $变量名";
                _commonText3Label.Text = "操作：Equal/NotEqual/Contains/>/<";
                break;
            case NodeKind.BooleanAnd:
            case NodeKind.BooleanOr:
                SetCommonVisibility(true, false, false, false, false, false, false, true);
                _commonTextLabel.Text = "右值 True/False";
                _commonFlagCheckBox.Content = "左值";
                break;
            case NodeKind.BooleanNot:
                SetCommonVisibility(false, false, false, false, false, false, false, true);
                _commonFlagCheckBox.Content = "输入值";
                break;
            case NodeKind.StringConcat:
                SetCommonVisibility(true, true, false, false, false, false, false, false);
                _commonTextLabel.Text = "左文本，变量用 $变量名";
                _commonText2Label.Text = "右文本，变量用 $变量名";
                break;
            case NodeKind.WaitWindow:
                SetCommonVisibility(true, false, false, true, true, false, false, false);
                _commonTextLabel.Text = "进程名";
                _commonNumberLabel.Text = "超时 ms（0=不超时）";
                _commonNumber2Label.Text = "检测间隔 ms";
                break;
            case NodeKind.CloseWindow:
            case NodeKind.WindowExists:
                SetCommonVisibility(true, false, false, false, false, false, false, false);
                _commonTextLabel.Text = "进程名";
                break;
            case NodeKind.GetForegroundWindow:
                SetCommonVisibility(false, false, false, false, false, false, false, false);
                _commonHelpTextBlock.Text = "输出 process_name、window_title、result，可接后续窗口/日志/比较节点。";
                break;
            case NodeKind.SaveScreenshot:
                SetCommonVisibility(true, false, false, true, true, true, true, false);
                _commonTextLabel.Text = "保存路径 .png（手动模式使用）";
                _commonNumberLabel.Text = "区域 X（0=全屏）";
                _commonNumber2Label.Text = "区域 Y";
                _commonNumber3Label.Text = "区域宽（0=全屏）";
                _commonNumber4Label.Text = "区域高";
                _commonHelpTextBlock.Text = "默认 Auto，自动保存到项目临时目录 Temp/Screenshots，并输出 image_path。Manual 模式使用手动路径。";
                break;
            case NodeKind.ShowMessage:
                SetCommonVisibility(true, true, false, false, false, false, false, false);
                _commonTextLabel.Text = "消息内容";
                _commonText2Label.Text = "窗口标题";
                break;
            case NodeKind.GetMousePosition:
                SetCommonVisibility(false, false, false, false, false, false, false, false);
                _commonHelpTextBlock.Text = "输出 position(Vector2D)，可接鼠标点击/鼠标移动的位置输入。";
                break;
        }
    }

    private void SetCommonVisibility(
        bool text,
        bool text2,
        bool text3,
        bool number,
        bool number2,
        bool number3,
        bool number4,
        bool flag)
    {
        SetVisible(_commonTextLabel, _commonTextBox, text);
        SetVisible(_commonText2Label, _commonText2Box, text2);
        SetVisible(_commonText3Label, _commonText3Box, text3);
        SetVisible(_commonNumberLabel, _commonNumberBox, number);
        SetVisible(_commonNumber2Label, _commonNumber2Box, number2);
        SetVisible(_commonNumber3Label, _commonNumber3Box, number3);
        SetVisible(_commonNumber4Label, _commonNumber4Box, number4);
        _commonFlagCheckBox.Visibility = flag ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ConfigureCommonAuxiliaryControls(CommonNodeViewModel node)
    {
        bool isWindowNode = IsWindowCommonNode(node.NodeKind);
        WindowInputMode mode = GetCommonMode();
        _commonKeyChordAddPanel.Visibility = node.NodeKind == NodeKind.KeyChord ? Visibility.Visible : Visibility.Collapsed;
        _commonModePanel.Visibility = isWindowNode ? Visibility.Visible : Visibility.Collapsed;
        _commonWindowPickerPanel.Visibility = isWindowNode && mode == WindowInputMode.Auto ? Visibility.Visible : Visibility.Collapsed;
        _commonEnumPanel.Visibility = IsEnumCommonNode(node.NodeKind) ? Visibility.Visible : Visibility.Collapsed;
        ConfigureCommonEnumComboBox(node);
        UpdateCommonEnumDependentVisibility(node);
        bool manualScreenshot = node.NodeKind == NodeKind.SaveScreenshot &&
                                GetCommonScreenshotSaveMode() == ScreenshotSaveMode.Manual;
        _commonBrowseFileButton.Visibility = node.NodeKind is NodeKind.WaitImage or NodeKind.WaitImageDisappear ||
                                             manualScreenshot ||
                                             (isWindowNode && mode == WindowInputMode.ExePath)
            ? Visibility.Visible
            : Visibility.Collapsed;
        _commonBrowseFileButton.Content = isWindowNode ? "浏览 exe" : "浏览";
    }

    private void ConfigureCommonEnumComboBox(CommonNodeViewModel node)
    {
        _commonEnumComboBox.Items.Clear();
        if (node.NodeKind is NodeKind.WaitImage or NodeKind.WaitImageDisappear)
        {
            _commonEnumLabel.Text = "查找源";
            AddCommonEnumOption("实时截屏", ImageSearchSourceMode.RealtimeScreenshot.ToString());
            AddCommonEnumOption("手动配置图像", ImageSearchSourceMode.ManualImage.ToString());
            SelectCommonEnumValue(ParseImageSearchSourceMode(node.Text2).ToString());
            return;
        }

        if (node.NodeKind == NodeKind.SaveScreenshot)
        {
            _commonEnumLabel.Text = "保存路径模式";
            AddCommonEnumOption("自动保存", ScreenshotSaveMode.Auto.ToString());
            AddCommonEnumOption("手动配置", ScreenshotSaveMode.Manual.ToString());
            SelectCommonEnumValue(ParseScreenshotSaveMode(node.Text2).ToString());
        }
    }

    private void AddCommonEnumOption(string content, string tag)
    {
        _commonEnumComboBox.Items.Add(new WpfComboBoxItem { Content = content, Tag = tag });
    }

    private void SelectCommonEnumValue(string tag)
    {
        foreach (var item in _commonEnumComboBox.Items.OfType<WpfComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            {
                _commonEnumComboBox.SelectedItem = item;
                return;
            }
        }

        if (_commonEnumComboBox.Items.Count > 0)
            _commonEnumComboBox.SelectedIndex = 0;
    }

    private void UpdateCommonEnumDependentVisibility(CommonNodeViewModel node)
    {
        if (node.NodeKind is NodeKind.WaitImage or NodeKind.WaitImageDisappear)
        {
            var mode = GetCommonImageSearchSourceMode();
            bool manual = mode == ImageSearchSourceMode.ManualImage;
            _commonText3Label.Visibility = manual ? Visibility.Visible : Visibility.Collapsed;
            _commonText3Box.Visibility = manual ? Visibility.Visible : Visibility.Collapsed;
        }

        if (node.NodeKind == NodeKind.SaveScreenshot)
        {
            bool manual = GetCommonScreenshotSaveMode() == ScreenshotSaveMode.Manual;
            _commonTextLabel.Visibility = manual ? Visibility.Visible : Visibility.Collapsed;
            _commonTextBox.Visibility = manual ? Visibility.Visible : Visibility.Collapsed;
            _commonBrowseFileButton.Visibility = manual ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private static void SetVisible(TextBlock label, WpfTextBox textBox, bool visible)
    {
        label.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        textBox.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
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
            SetComboSingleValue(_selectWindowAutoComboBox, node.ProcessName);
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

    public void AddCommonKeyChordKey()
    {
        string key = GetEditableComboValue(_commonKeyChordKeyComboBox);
        if (string.IsNullOrWhiteSpace(key))
            return;

        string current = _commonTextBox.Text.Trim();
        _commonTextBox.Text = string.IsNullOrWhiteSpace(current) ? key : $"{current}+{key}";
        ApplyChanges();
    }

    public void AddParameter()
    {
        if (_editorService.Nodes.FirstOrDefault(node => node.IsSelected) is not ParameterNodeBaseViewModel node)
            return;

        node.AddParameter("NewParam");
        LoadParameterNode(node);
        _markDirty();
    }

    private void LoadParameterNode(ParameterNodeBaseViewModel node)
    {
        _parameterInspectorTitle.Text = node switch
        {
            FunctionEntryNodeViewModel => "输入",
            FunctionReturnNodeViewModel => "输出",
            MacroEntryNodeViewModel => "输入",
            MacroOutputNodeViewModel => "输出",
            _ => "参数",
        };

        _parameterRowsPanel.Children.Clear();
        foreach (var parameter in node.Parameters.ToList())
            _parameterRowsPanel.Children.Add(CreateParameterRow(node, parameter));
    }

    private UIElement CreateParameterRow(ParameterNodeBaseViewModel node, GraphParameterDefinition parameter)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(105) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });

        var nameBox = new WpfTextBox { Text = parameter.Name, Margin = new Thickness(0, 0, 4, 0) };
        nameBox.TextChanged += (_, _) =>
        {
            if (_isLoading) return;
            parameter.Name = nameBox.Text;
            node.SyncPins();
            _editorService.UpdatePinConnectionStates();
            _markDirty();
        };
        row.Children.Add(nameBox);

        var typeBox = new WpfComboBox { Margin = new Thickness(0, 0, 4, 0) };
        foreach (var type in Enum.GetValues<GraphParameterType>())
            typeBox.Items.Add(new WpfComboBoxItem { Content = type.ToString(), Tag = type });
        typeBox.SelectedItem = typeBox.Items.OfType<WpfComboBoxItem>().FirstOrDefault(item => item.Tag is GraphParameterType t && t == parameter.Type);
        typeBox.SelectionChanged += (_, _) =>
        {
            if (_isLoading || typeBox.SelectedItem is not WpfComboBoxItem { Tag: GraphParameterType type }) return;
            ClearConnectionsForParameter(node, parameter.Id);
            parameter.Type = type;
            node.SyncPins();
            _editorService.UpdatePinConnectionStates();
            _markDirty();
            LoadParameterNode(node);
        };
        Grid.SetColumn(typeBox, 1);
        row.Children.Add(typeBox);

        AddSmallButton(row, 2, "▲", () => { node.MoveParameter(parameter, -1); LoadParameterNode(node); _markDirty(); });
        AddSmallButton(row, 3, "▼", () => { node.MoveParameter(parameter, 1); LoadParameterNode(node); _markDirty(); });
        AddSmallButton(row, 4, "×", () =>
        {
            ClearConnectionsForParameter(node, parameter.Id);
            node.RemoveParameter(parameter);
            LoadParameterNode(node);
            _markDirty();
        });

        return row;
    }

    private static void AddSmallButton(Grid row, int column, string text, Action action)
    {
        var button = new WpfButton { Content = text, Width = 22, Height = 22, Padding = new Thickness(0), Margin = new Thickness(2, 0, 0, 0) };
        button.Click += (_, _) => action();
        Grid.SetColumn(button, column);
        row.Children.Add(button);
    }

    private void ClearConnectionsForParameter(ParameterNodeBaseViewModel node, string parameterId)
    {
        foreach (var pin in node.InputPins.Concat(node.OutputPins).Where(pin => pin.Name == parameterId).ToList())
            _editorService.ClearConnectionsForPin(pin);
    }

    public void CommonModeChanged()
    {
        if (_isLoading)
            return;

        if (_editorService.Nodes.FirstOrDefault(node => node.IsSelected) is CommonNodeViewModel commonNode)
        {
            ConfigureCommonAuxiliaryControls(commonNode);
            ApplyChanges();
        }
    }

    public void CommonEnumChanged()
    {
        if (_isLoading)
            return;

        if (_editorService.Nodes.FirstOrDefault(node => node.IsSelected) is CommonNodeViewModel commonNode)
        {
            ClearConnectionsForHiddenCommonEnumPins(commonNode);
            UpdateCommonEnumDependentVisibility(commonNode);
            ApplyChanges();
            LockCommonInputs(commonNode);
        }
    }

    private void ClearConnectionsForHiddenCommonEnumPins(CommonNodeViewModel node)
    {
        if (node.NodeKind is NodeKind.WaitImage or NodeKind.WaitImageDisappear &&
            GetCommonImageSearchSourceMode() == ImageSearchSourceMode.RealtimeScreenshot)
        {
            ClearInputConnections(node, "source_image_path");
        }

        if (node.NodeKind == NodeKind.SaveScreenshot &&
            GetCommonScreenshotSaveMode() == ScreenshotSaveMode.Auto)
        {
            ClearInputConnections(node, "path");
        }
    }

    public void CommonWindowChanged()
    {
        if (_isLoading)
            return;

        if (_editorService.Nodes.FirstOrDefault(node => node.IsSelected) is not CommonNodeViewModel commonNode ||
            !IsWindowCommonNode(commonNode.NodeKind) ||
            _commonWindowComboBox.SelectedItem is not string processName)
            return;

        _commonTextBox.Text = processName;
        ApplyChanges();
    }

    public void RefreshCommonWindowList()
    {
        PopulateCommonWindowComboBox();
    }

    public void BrowseCommonFile()
    {
        if (_editorService.Nodes.FirstOrDefault(node => node.IsSelected) is not CommonNodeViewModel commonNode)
            return;

        if (IsWindowCommonNode(commonNode.NodeKind))
        {
            var dialog = new WpfOpenFileDialog
            {
                Title = "选择应用程序",
                Filter = "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*",
            };
            if (dialog.ShowDialog(_owner) == true)
            {
                _commonText3Box.Text = dialog.FileName;
                _commonTextBox.Text = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);
                SelectCommonMode(WindowInputMode.ExePath.ToString());
                ApplyChanges();
            }
            return;
        }

        if (commonNode.NodeKind is NodeKind.WaitImage or NodeKind.WaitImageDisappear)
        {
            var dialog = new WpfOpenFileDialog
            {
                Title = "选择图片文件",
                Filter = "图片文件 (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|所有文件 (*.*)|*.*",
            };
            if (dialog.ShowDialog(_owner) == true)
            {
                _commonTextBox.Text = dialog.FileName;
                ApplyChanges();
            }
            return;
        }

        if (commonNode.NodeKind == NodeKind.SaveScreenshot)
        {
            var dialog = new WpfSaveFileDialog
            {
                Title = "选择截图保存路径",
                Filter = "PNG 图片 (*.png)|*.png|所有文件 (*.*)|*.*",
                DefaultExt = ".png",
            };
            if (dialog.ShowDialog(_owner) == true)
            {
                SelectCommonEnumValue(ScreenshotSaveMode.Manual.ToString());
                _commonTextBox.Text = dialog.FileName;
                ApplyChanges();
            }
        }
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

        if (node is FindImageNodeViewModel findImageNode)
        {
            LockTextBox(_findImageSourcePathTextBox, IsInputPinConnected(findImageNode, "source_image_path"), findImageNode.SourceImagePath);
            LockTextBox(_findImagePathTextBox, IsInputPinConnected(findImageNode, "image_path"), findImageNode.ImagePath);
        }

        if (node is CommonNodeViewModel commonNode)
            LockCommonInputs(commonNode);
    }

    private void LockCommonInputs(CommonNodeViewModel node)
    {
        switch (node.NodeKind)
        {
            case NodeKind.MouseDoubleClick:
                LockTextBox(_commonNumberBox, IsInputPinConnected(node, "position"), node.Number.ToString("0.##"));
                LockTextBox(_commonNumber2Box, IsInputPinConnected(node, "position"), node.Number2.ToString("0.##"));
                break;
            case NodeKind.WaitImage:
            case NodeKind.WaitImageDisappear:
                LockTextBox(_commonTextBox, IsInputPinConnected(node, "image_path"), node.Text);
                LockTextBox(_commonText3Box, IsInputPinConnected(node, "source_image_path"), node.Text3);
                _commonBrowseFileButton.IsEnabled = !IsInputPinConnected(node, "image_path");
                break;
            case NodeKind.Compare:
                LockTextBox(_commonTextBox, IsInputPinConnected(node, "left"), node.Text);
                LockTextBox(_commonText2Box, IsInputPinConnected(node, "right"), node.Text2);
                break;
            case NodeKind.BooleanAnd:
            case NodeKind.BooleanOr:
                LockCheckBox(_commonFlagCheckBox, IsInputPinConnected(node, "left"), node.Flag, "左值");
                LockTextBox(_commonTextBox, IsInputPinConnected(node, "right"), node.Text);
                break;
            case NodeKind.BooleanNot:
                LockCheckBox(_commonFlagCheckBox, IsInputPinConnected(node, "value"), node.Flag, "输入值");
                break;
            case NodeKind.StringConcat:
                LockTextBox(_commonTextBox, IsInputPinConnected(node, "left"), node.Text);
                LockTextBox(_commonText2Box, IsInputPinConnected(node, "right"), node.Text2);
                break;
            case NodeKind.WaitWindow:
            case NodeKind.CloseWindow:
            case NodeKind.WindowExists:
                bool locked = IsInputPinConnected(node, "process_name");
                LockTextBox(_commonTextBox, locked, node.Text);
                _commonModeComboBox.IsEnabled = !locked;
                _commonWindowComboBox.IsEnabled = !locked;
                _commonBrowseFileButton.IsEnabled = !locked;
                break;
            case NodeKind.SaveScreenshot:
                bool autoSave = string.IsNullOrWhiteSpace(node.Text2) || string.Equals(node.Text2, "Auto", StringComparison.OrdinalIgnoreCase);
                bool pathLocked = autoSave || IsInputPinConnected(node, "path");
                LockTextBox(_commonTextBox, pathLocked, node.Text, autoSave ? "自动保存" : "前置输入");
                _commonBrowseFileButton.IsEnabled = !pathLocked;
                break;
            case NodeKind.ShowMessage:
                LockTextBox(_commonTextBox, IsInputPinConnected(node, "text"), node.Text);
                break;
        }
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

    private void ClearInputConnections(NodeBaseViewModel node, string pinName)
    {
        var pin = node.InputPins.FirstOrDefault(p => p.Name == pinName);
        if (pin is not null)
            _editorService.ClearConnectionsForPin(pin);
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

    private void PopulateCommonWindowComboBox()
    {
        string selected = _commonTextBox.Text.Trim();
        _commonWindowComboBox.Items.Clear();
        foreach (var name in _windowAdapter.GetRunningWindowNames())
        {
            _commonWindowComboBox.Items.Add(name);
            if (string.Equals(name, selected, StringComparison.OrdinalIgnoreCase))
                _commonWindowComboBox.SelectedItem = name;
        }
    }

    private static void SetComboSingleValue(WpfComboBox comboBox, string? value)
    {
        comboBox.Items.Clear();
        comboBox.SelectedItem = null;
        comboBox.Text = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
            return;

        comboBox.Items.Add(value);
        comboBox.SelectedItem = value;
    }

    private void SelectCommonMode(string? mode)
    {
        string target = Enum.TryParse<WindowInputMode>(mode, true, out var parsed)
            ? parsed.ToString()
            : WindowInputMode.Manual.ToString();
        foreach (var item in _commonModeComboBox.Items.OfType<WpfComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), target, StringComparison.OrdinalIgnoreCase))
            {
                _commonModeComboBox.SelectedItem = item;
                return;
            }
        }

        _commonModeComboBox.SelectedIndex = 0;
    }

    private void SelectFindImageSourceMode(ImageSearchSourceMode mode)
    {
        foreach (var item in _findImageSourceModeComboBox.Items.OfType<WpfComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), mode.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                _findImageSourceModeComboBox.SelectedItem = item;
                return;
            }
        }

        _findImageSourceModeComboBox.SelectedIndex = 1;
    }

    private ImageSearchSourceMode GetFindImageSourceMode()
    {
        string tag = GetSelectedComboTag(_findImageSourceModeComboBox, ImageSearchSourceMode.RealtimeScreenshot.ToString());
        return Enum.TryParse(tag, true, out ImageSearchSourceMode mode) ? mode : ImageSearchSourceMode.RealtimeScreenshot;
    }

    private void UpdateFindImageSourcePathVisibility(ImageSearchSourceMode mode)
    {
        var visibility = mode == ImageSearchSourceMode.ManualImage ? Visibility.Visible : Visibility.Collapsed;
        _findImageSourcePathLabel.Visibility = visibility;
        _findImageSourcePathPanel.Visibility = visibility;
    }

    private WindowInputMode GetCommonMode()
    {
        string tag = GetSelectedComboTag(_commonModeComboBox, WindowInputMode.Manual.ToString());
        return Enum.TryParse(tag, true, out WindowInputMode mode) ? mode : WindowInputMode.Manual;
    }

    private ImageSearchSourceMode GetCommonImageSearchSourceMode()
    {
        string tag = GetSelectedComboTag(_commonEnumComboBox, ImageSearchSourceMode.RealtimeScreenshot.ToString());
        return Enum.TryParse(tag, true, out ImageSearchSourceMode mode) ? mode : ImageSearchSourceMode.RealtimeScreenshot;
    }

    private ScreenshotSaveMode GetCommonScreenshotSaveMode()
    {
        string tag = GetSelectedComboTag(_commonEnumComboBox, ScreenshotSaveMode.Auto.ToString());
        return Enum.TryParse(tag, true, out ScreenshotSaveMode mode) ? mode : ScreenshotSaveMode.Auto;
    }

    private static ImageSearchSourceMode ParseImageSearchSourceMode(string? mode) =>
        Enum.TryParse(mode, true, out ImageSearchSourceMode parsed)
            ? parsed
            : ImageSearchSourceMode.RealtimeScreenshot;

    private static ScreenshotSaveMode ParseScreenshotSaveMode(string? mode) =>
        Enum.TryParse(mode, true, out ScreenshotSaveMode parsed)
            ? parsed
            : ScreenshotSaveMode.Auto;

    private static string GetCommonEnumFallback(NodeKind kind) =>
        kind switch
        {
            NodeKind.WaitImage or NodeKind.WaitImageDisappear => ImageSearchSourceMode.RealtimeScreenshot.ToString(),
            NodeKind.SaveScreenshot => ScreenshotSaveMode.Auto.ToString(),
            _ => string.Empty,
        };

    private static bool IsWindowCommonNode(NodeKind kind) =>
        kind is NodeKind.WaitWindow or NodeKind.CloseWindow or NodeKind.WindowExists;

    private static bool IsEnumCommonNode(NodeKind kind) =>
        kind is NodeKind.WaitImage or NodeKind.WaitImageDisappear or NodeKind.SaveScreenshot;

    private static string GetSelectedComboTag(WpfComboBox comboBox, string fallback)
    {
        return comboBox.SelectedItem is WpfComboBoxItem item
            ? item.Tag?.ToString() ?? fallback
            : fallback;
    }

    private static string GetEditableComboValue(WpfComboBox comboBox)
    {
        if (comboBox.SelectedItem is WpfComboBoxItem selectedItem)
            return selectedItem.Tag?.ToString() ?? selectedItem.Content?.ToString() ?? string.Empty;
        return comboBox.Text.Trim();
    }

    private void SetWhileMaxIterationsVisible(bool visible)
    {
        _whileMaxIterationsLabel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        _whileMaxIterationsTextBox.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PopulateKeyboardKeyComboBox(string selectedKey)
    {
        PopulateKeyComboBox(_keyboardKeyComboBox, selectedKey);
    }

    private static void PopulateKeyComboBox(WpfComboBox comboBox, string selectedKey)
    {
        comboBox.Items.Clear();
        foreach (var key in GetKeyboardKeys())
        {
            var item = new WpfComboBoxItem { Content = key, Tag = key };
            comboBox.Items.Add(item);
            if (key == selectedKey)
                comboBox.SelectedItem = item;
        }
    }

    private static string[] GetKeyboardKeys() =>
    [
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
    ];

    private static void LockTextBox(WpfTextBox textBox, bool locked, string restoreValue, string lockedText = "前置输入")
    {
        textBox.IsEnabled = !locked;
        if (locked)
        {
            textBox.Text = lockedText;
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

    private static void LockCheckBox(WpfCheckBox checkBox, bool locked, bool restoreValue, string restoreContent)
    {
        checkBox.IsEnabled = !locked;
        if (locked)
        {
            checkBox.Content = "前置输入";
            checkBox.Foreground = Brush(0x7A, 0x87, 0x97);
            checkBox.IsChecked = false;
        }
        else
        {
            checkBox.Content = restoreContent;
            checkBox.IsChecked = restoreValue;
            checkBox.ClearValue(WpfControl.ForegroundProperty);
        }
    }

    private static SolidColorBrush Brush(byte r, byte g, byte b) => new(System.Windows.Media.Color.FromRgb(r, g, b));
}
