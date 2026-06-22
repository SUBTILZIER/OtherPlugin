using System.Windows;
using System.Windows.Controls;
using AutomationStudioWpf.Adapters;
using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Services;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfComboBoxItem = System.Windows.Controls.ComboBoxItem;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfButton = System.Windows.Controls.Button;
using WpfListBox = System.Windows.Controls.ListBox;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace AutomationStudioWpf.Interaction;

/// <summary>
/// Owns node inspector loading, autosave, browse dialogs, and field lock state.
/// MainWindow only forwards XAML events into this controller.
/// </summary>
public sealed partial class InspectorController
{
    private readonly Window _owner;
    private readonly GraphEditorService _editorService;
    private readonly Win32WindowAdapter _windowAdapter;
    private readonly Action _markDirty;
    private readonly Action<string> _setStatus;

    private readonly TextBlock _hintTextBlock;
    private readonly WpfTextBox _nodeTitleTextBox;
    private readonly TextBlock _nodeNumberTextBlock;
    private readonly StackPanel[] _inspectorPanels;
    private readonly StackPanel _parameterInspectorPanel;
    private readonly WpfButton _addParameterButton;
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

    private readonly StackPanel _toDoInspectorPanel;
    private readonly WpfTextBox _toDoSearchBox;
    private readonly WpfListBox _toDoTargetListBox;
    private readonly WpfTextBox _toDoTargetTitleTextBox;
    private readonly WpfTextBox _toDoTargetNumberTextBox;
    private readonly WpfCheckBox _toDoReturnAfterTargetCheckBox;

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
    private StackPanel? _commonVariadicDefaultsPanel;

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
        TextBlock nodeNumberTextBlock,
        StackPanel parameterInspectorPanel,
        WpfButton addParameterButton,
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
        StackPanel toDoInspectorPanel,
        WpfTextBox toDoSearchBox,
        WpfListBox toDoTargetListBox,
        WpfTextBox toDoTargetTitleTextBox,
        WpfTextBox toDoTargetNumberTextBox,
        WpfCheckBox toDoReturnAfterTargetCheckBox,
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
        _nodeNumberTextBlock = nodeNumberTextBlock;
        _parameterInspectorPanel = parameterInspectorPanel;
        _addParameterButton = addParameterButton;
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

        _toDoInspectorPanel = toDoInspectorPanel;
        _toDoSearchBox = toDoSearchBox;
        _toDoTargetListBox = toDoTargetListBox;
        _toDoTargetTitleTextBox = toDoTargetTitleTextBox;
        _toDoTargetNumberTextBox = toDoTargetNumberTextBox;
        _toDoReturnAfterTargetCheckBox = toDoReturnAfterTargetCheckBox;

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
            _toDoInspectorPanel,
            _commonInspectorPanel,
        ];

        ConfigureStaticFreeTextEditors();
    }

    public void LoadNode(NodeBaseViewModel? node)
    {
        _isLoading = true;
        try
        {
            if (node is null)
            {
                _nodeTitleTextBox.Text = string.Empty;
                _nodeNumberTextBlock.Text = string.Empty;
                HideAllPanels();
                _hintTextBlock.Text = "请选择一个节点进行编辑。";
                RefreshLocks(null);
                return;
            }

            _nodeTitleTextBox.Text = node.Title;
            _nodeNumberTextBlock.Text = NodeNumberLabel(node);
            _hintTextBlock.Text = $"当前选中：{node.Title}";
            HideAllPanels();

            switch (node)
            {
                case ParameterNodeBaseViewModel parameterNode:
                    _parameterInspectorPanel.Visibility = Visibility.Visible;
                    LoadParameterNode(parameterNode);
                    break;

                case FunctionCallNodeViewModel functionCall:
                    _parameterInspectorPanel.Visibility = Visibility.Visible;
                    LoadCallNodeInputs(functionCall, functionCall.InputParameters);
                    break;

                case CustomEventCallNodeViewModel customEventCall:
                    _parameterInspectorPanel.Visibility = Visibility.Visible;
                    LoadCallNodeInputs(customEventCall, customEventCall.InputParameters);
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

                case ToDoNodeViewModel toDoNode:
                    _toDoInspectorPanel.Visibility = Visibility.Visible;
                    LoadToDoNode(toDoNode);
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

            case ToDoNodeViewModel toDoNode:
                ApplyToDoNodeChanges(toDoNode);
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

    public bool ToDoTargetSelected()
    {
        if (_isLoading ||
            _toDoTargetListBox.SelectedItem is not ToDoTargetOption option ||
            _editorService.Nodes.FirstOrDefault(node => node.IsSelected) is not ToDoNodeViewModel toDoNode)
        {
            return false;
        }

        bool wasLoading = _isLoading;
        _isLoading = true;
        try
        {
            _toDoTargetTitleTextBox.Text = option.Title;
            _toDoTargetNumberTextBox.Text = option.Number;
        }
        finally
        {
            _isLoading = wasLoading;
        }

        toDoNode.TargetNodeTitle = option.Title;
        toDoNode.TargetNodeNumber = option.Number;
        toDoNode.TargetNodeId = option.NodeId;
        toDoNode.RefreshDescription();
        RefreshLocks(toDoNode);
        _markDirty();
        _setStatus($"ToDo 目标已选择：{option.Title} {option.Number}");
        return true;
    }

    private void RefreshToDoTargetOptions()
    {
        string filter = _toDoSearchBox.Text.Trim();
        var selectedToDo = _editorService.Nodes.FirstOrDefault(node => node.IsSelected) as ToDoNodeViewModel;
        var options = _editorService.Nodes
            .Where(node => NodeTraits.IsToDoTarget(node.NodeKind))
            .Where(node => !ReferenceEquals(node, selectedToDo))
            .Where(node => string.IsNullOrWhiteSpace(filter) ||
                           node.Title.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                           node.NodeNumber.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(node => NodeSortOrdinal(node.NodeNumber))
            .ThenBy(node => node.Title)
            .Select(node => new ToDoTargetOption(node.Id, node.Title, node.NodeNumber))
            .ToList();

        _toDoTargetListBox.ItemsSource = options;
        if (selectedToDo is not null && !string.IsNullOrWhiteSpace(selectedToDo.TargetNodeId))
        {
            _toDoTargetListBox.SelectedItem = options.FirstOrDefault(option => option.NodeId == selectedToDo.TargetNodeId);
        }
    }

    private NodeBaseViewModel? FindToDoTarget(ToDoNodeViewModel source, string title, string number)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(number))
            return null;

        var matches = _editorService.Nodes
            .Where(node => !ReferenceEquals(node, source))
            .Where(node => NodeTraits.IsToDoTarget(node.NodeKind))
            .Where(node => string.Equals(node.Title, title, StringComparison.Ordinal) &&
                           string.Equals(node.NodeNumber, number, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    private static int NodeSortOrdinal(string nodeNumber)
    {
        int start = 0;
        while (start < nodeNumber.Length && !char.IsDigit(nodeNumber[start]))
        {
            start++;
        }

        return start < nodeNumber.Length && int.TryParse(nodeNumber[start..], out int ordinal) ? ordinal : int.MaxValue;
    }

    private static string NodeNumberLabel(NodeBaseViewModel node)
    {
        if (NodeTraits.ShouldAssignNodeNumber(node.NodeKind))
            return string.IsNullOrWhiteSpace(node.NodeNumber) ? "未分配" : node.NodeNumber;

        return node.NodeKind == NodeKind.Reroute
            ? "无编号（路由点）"
            : "无编号（纯运算节点）";
    }

    private void HideAllPanels()
    {
        foreach (var panel in _inspectorPanels)
            panel.Visibility = Visibility.Collapsed;
    }
}
