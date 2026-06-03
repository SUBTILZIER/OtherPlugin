using System.Windows.Controls;
using System.Windows.Media;
using AutomationStudioWpf.Graph;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfComboBoxItem = System.Windows.Controls.ComboBoxItem;
using WpfControl = System.Windows.Controls.Control;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace AutomationStudioWpf.Interaction;

public sealed class InspectorController
{
    private readonly WpfTextBox _mousePositionXTextBox;
    private readonly WpfTextBox _mousePositionYTextBox;
    private readonly WpfTextBox _mouseMovePositionXTextBox;
    private readonly WpfTextBox _mouseMovePositionYTextBox;
    private readonly WpfComboBox _ifConditionComboBox;
    private readonly WpfComboBox _whileLoopConditionComboBox;
    private readonly WpfComboBox _forLoopEndConditionComboBox;
    private readonly WpfTextBox _printLogMessageTextBox;
    private readonly WpfTextBox _selectWindowProcessNameTextBox;

    public InspectorController(
        WpfTextBox mousePositionXTextBox,
        WpfTextBox mousePositionYTextBox,
        WpfTextBox mouseMovePositionXTextBox,
        WpfTextBox mouseMovePositionYTextBox,
        WpfComboBox ifConditionComboBox,
        WpfComboBox whileLoopConditionComboBox,
        WpfComboBox forLoopEndConditionComboBox,
        WpfTextBox printLogMessageTextBox,
        WpfTextBox selectWindowProcessNameTextBox)
    {
        _mousePositionXTextBox = mousePositionXTextBox;
        _mousePositionYTextBox = mousePositionYTextBox;
        _mouseMovePositionXTextBox = mouseMovePositionXTextBox;
        _mouseMovePositionYTextBox = mouseMovePositionYTextBox;
        _ifConditionComboBox = ifConditionComboBox;
        _whileLoopConditionComboBox = whileLoopConditionComboBox;
        _forLoopEndConditionComboBox = forLoopEndConditionComboBox;
        _printLogMessageTextBox = printLogMessageTextBox;
        _selectWindowProcessNameTextBox = selectWindowProcessNameTextBox;
    }

    public void RefreshLocks(NodeBaseViewModel? node, Func<NodeBaseViewModel, string, bool> isInputConnected)
    {
        if (node is MouseClickNodeViewModel clickNode)
        {
            bool locked = isInputConnected(clickNode, "position");
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
            bool locked = isInputConnected(moveNode, "position");
            LockTextBox(_mouseMovePositionXTextBox, locked, moveNode.PositionX.ToString("0.##"));
            LockTextBox(_mouseMovePositionYTextBox, locked, moveNode.PositionY.ToString("0.##"));
        }
        else
        {
            LockTextBox(_mouseMovePositionXTextBox, false, string.Empty);
            LockTextBox(_mouseMovePositionYTextBox, false, string.Empty);
        }

        LockCondition(_ifConditionComboBox, node is IfNodeViewModel ifNode && isInputConnected(ifNode, "condition"), node is IfNodeViewModel ifValue && ifValue.ConditionValue);
        LockCondition(_whileLoopConditionComboBox, node is WhileLoopNodeViewModel whileNode && isInputConnected(whileNode, "condition"), node is WhileLoopNodeViewModel whileValue && whileValue.ConditionValue);
        LockCondition(_forLoopEndConditionComboBox, node is ForLoopNodeViewModel forNode && isInputConnected(forNode, "end_condition"), node is ForLoopNodeViewModel forValue && forValue.EndConditionValue);

        LockTextBox(_printLogMessageTextBox, node is PrintLogNodeViewModel printNode && isInputConnected(printNode, "message"), node is PrintLogNodeViewModel printValue ? printValue.Message : string.Empty);
        LockTextBox(_selectWindowProcessNameTextBox, node is SelectWindowNodeViewModel selectNode && isInputConnected(selectNode, "process_name"), node is SelectWindowNodeViewModel selectValue ? selectValue.ProcessName : string.Empty);
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
            comboBox.Items.Clear();
            comboBox.Items.Add(new WpfComboBoxItem { Content = "前置输入" });
            comboBox.SelectedIndex = 0;
        }
        else
        {
            comboBox.ClearValue(WpfControl.ForegroundProperty);
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
