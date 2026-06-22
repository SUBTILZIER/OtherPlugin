using System.Linq;
using System.Windows.Controls;
using System.Windows.Media;
using AutomationStudioWpf.Graph;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfComboBoxItem = System.Windows.Controls.ComboBoxItem;
using WpfControl = System.Windows.Controls.Control;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace AutomationStudioWpf.Interaction;

public sealed partial class InspectorController
{
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
        LockTextBox(_toDoTargetTitleTextBox, node is ToDoNodeViewModel toDoTitle && IsInputPinConnected(toDoTitle, "target_title"), node is ToDoNodeViewModel toDoTitleValue ? toDoTitleValue.TargetNodeTitle : string.Empty);
        LockTextBox(_toDoTargetNumberTextBox, node is ToDoNodeViewModel toDoNumber && IsInputPinConnected(toDoNumber, "target_number"), node is ToDoNodeViewModel toDoNumberValue ? toDoNumberValue.TargetNodeNumber : string.Empty);

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
                RebuildVariadicDefaultRows(node);
                break;
            case NodeKind.BooleanNot:
                LockCheckBox(_commonFlagCheckBox, IsInputPinConnected(node, "value"), node.Flag, "输入值");
                break;
            case NodeKind.StringConcat:
                RebuildVariadicDefaultRows(node);
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
