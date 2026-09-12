using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AutomationStudioWpf.Graph;
using WpfButton = System.Windows.Controls.Button;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfComboBoxItem = System.Windows.Controls.ComboBoxItem;
using WpfBorder = System.Windows.Controls.Border;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace AutomationStudioWpf.Interaction;

public sealed partial class InspectorController
{
    public void AddParameter()
    {
        if (_editorService.Nodes.FirstOrDefault(node => node.IsSelected) is not ParameterNodeBaseViewModel node)
            return;

        node.AddParameter("NewParam");
        _editorService.RebindConnectionsToCurrentPins();
        LoadParameterNode(node);
        _markDirty();
    }

    private void LoadParameterNode(ParameterNodeBaseViewModel node)
    {
        _addParameterButton.Visibility = Visibility.Visible;
        _parameterInspectorTitle.Text = node switch
        {
            FunctionEntryNodeViewModel => "输入",
            FunctionReturnNodeViewModel => "输出",
            CustomEventNodeViewModel => "输入",
            _ => "参数",
        };

        _parameterRowsPanel.Children.Clear();
        foreach (var parameter in node.Parameters.ToList())
            _parameterRowsPanel.Children.Add(CreateParameterRow(node, parameter));
    }

    private void LoadCallNodeInputs(NodeBaseViewModel node, IEnumerable<GraphParameterDefinition> parameters)
    {
        _addParameterButton.Visibility = Visibility.Collapsed;
        _parameterInspectorTitle.Text = "调用输入";
        _parameterRowsPanel.Children.Clear();
        foreach (var parameter in parameters.ToList())
            _parameterRowsPanel.Children.Add(CreateCallInputRow(node, parameter));
    }

    private UIElement CreateParameterRow(ParameterNodeBaseViewModel node, GraphParameterDefinition parameter)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        AddParameterColumns(row, includeActions: true);

        var nameEdit = new ParameterNameEditContext(node, parameter);
        var nameBox = new WpfTextBox
        {
            Text = parameter.Name,
            Margin = new Thickness(0, 0, 4, 0),
            Tag = nameEdit,
        };
        nameBox.TextChanged += (_, _) =>
        {
            if (_isLoading) return;
            node.PreviewParameterName(parameter, nameBox.Text);
        };
        nameBox.LostFocus += (_, _) => CommitParameterName(nameEdit, nameBox);
        nameBox.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                CommitParameterName(nameEdit, nameBox);
                Keyboard.ClearFocus();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                node.PreviewParameterName(parameter, nameEdit.CommittedName);
                nameBox.Text = nameEdit.CommittedName;
                nameBox.SelectAll();
                e.Handled = true;
            }
        };
        row.Children.Add(nameBox);

        var typeBox = new WpfComboBox { Margin = new Thickness(0, 0, 4, 0) };
        foreach (var type in Enum.GetValues<GraphParameterType>())
            typeBox.Items.Add(new WpfComboBoxItem { Content = type.ToString(), Tag = type });
        typeBox.SelectedItem = typeBox.Items.OfType<WpfComboBoxItem>().FirstOrDefault(item => item.Tag is GraphParameterType t && t == parameter.Type);
        typeBox.SelectionChanged += (_, _) =>
        {
            if (_isLoading || typeBox.SelectedItem is not WpfComboBoxItem { Tag: GraphParameterType type }) return;
            var oldType = parameter.Type;
            bool shouldResetDefault = string.IsNullOrWhiteSpace(parameter.DefaultValue) ||
                                      parameter.DefaultValue == GraphParameterDefinition.DefaultValueForType(oldType);
            ClearConnectionsForParameter(node, parameter.Id);
            parameter.Type = type;
            if (shouldResetDefault)
                parameter.DefaultValue = GraphParameterDefinition.DefaultValueForType(type);
            node.SyncPins();
            _editorService.RebindConnectionsToCurrentPins();
            _markDirty();
            LoadParameterNode(node);
        };
        Grid.SetColumn(typeBox, 1);
        row.Children.Add(typeBox);

        bool valueLocked = node is FunctionReturnNodeViewModel &&
                           IsInputPinConnected(node, parameter.Id);
        var defaultEditor = CreateParameterValueEditor(parameter, valueLocked, () => LoadParameterNode(node));
        Grid.SetColumn(defaultEditor, 2);
        row.Children.Add(defaultEditor);

        AddSmallButton(row, 3, "▲", () =>
        {
            node.MoveParameter(parameter, -1);
            _editorService.RebindConnectionsToCurrentPins();
            LoadParameterNode(node);
            _markDirty();
        });
        AddSmallButton(row, 4, "▼", () =>
        {
            node.MoveParameter(parameter, 1);
            _editorService.RebindConnectionsToCurrentPins();
            LoadParameterNode(node);
            _markDirty();
        });
        AddSmallButton(row, 5, "×", () =>
        {
            ClearConnectionsForParameter(node, parameter.Id);
            node.RemoveParameter(parameter);
            _editorService.RebindConnectionsToCurrentPins();
            LoadParameterNode(node);
            _markDirty();
        });

        return row;
    }

    private void CommitParameterName(ParameterNameEditContext edit, WpfTextBox nameBox)
    {
        string normalized = nameBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(normalized) && edit.Node.RenameParameter(edit.Parameter, normalized))
            _markDirty();

        edit.CommittedName = edit.Parameter.Name;
        edit.Node.PreviewParameterName(edit.Parameter, edit.CommittedName);

        if (!string.Equals(nameBox.Text, edit.CommittedName, StringComparison.Ordinal))
            nameBox.Text = edit.CommittedName;
    }

    private void CommitPendingParameterNames(ParameterNodeBaseViewModel node)
    {
        foreach (Grid row in _parameterRowsPanel.Children.OfType<Grid>())
        {
            WpfTextBox? nameBox = row.Children
                .OfType<WpfTextBox>()
                .FirstOrDefault(textBox => textBox.Tag is ParameterNameEditContext);
            if (nameBox?.Tag is ParameterNameEditContext edit && ReferenceEquals(edit.Node, node))
                CommitParameterName(edit, nameBox);
        }
    }

    private sealed class ParameterNameEditContext(
        ParameterNodeBaseViewModel node,
        GraphParameterDefinition parameter)
    {
        public ParameterNodeBaseViewModel Node { get; } = node;
        public GraphParameterDefinition Parameter { get; } = parameter;
        public string CommittedName { get; set; } = parameter.Name;
    }

    private UIElement CreateCallInputRow(NodeBaseViewModel node, GraphParameterDefinition parameter)
    {
        var row = new Grid { MinHeight = 30, Margin = new Thickness(0, 0, 0, 6) };
        AddParameterColumns(row, includeActions: false);

        var nameText = new TextBlock
        {
            Text = parameter.Name,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var nameField = CreateReadOnlyParameterField(nameText, "EditorTextBrush");
        Grid.SetColumn(nameField, 0);
        row.Children.Add(nameField);

        var typeText = new TextBlock
        {
            Text = parameter.Type.ToString(),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var typeField = CreateReadOnlyParameterField(typeText, "EditorMutedTextBrush");
        Grid.SetColumn(typeField, 1);
        row.Children.Add(typeField);

        var editor = CreateParameterValueEditor(parameter, IsInputPinConnected(node, parameter.Id), null);
        Grid.SetColumn(editor, 2);
        row.Children.Add(editor);
        return row;
    }

    private static void AddParameterColumns(Grid row, bool includeActions)
    {
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(94) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        if (!includeActions)
            return;

        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
    }

    private static WpfBorder CreateReadOnlyParameterField(UIElement content, string foregroundKey)
    {
        var field = new WpfBorder
        {
            Background = null,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 4, 0),
            Child = content,
        };
        ThemeResourceHelper.SetResource(field, WpfBorder.BackgroundProperty, "EditorFieldCardBrush");
        ThemeResourceHelper.SetResource(field, WpfBorder.BorderBrushProperty, "EditorPanelBorderBrush");
        if (content is TextBlock text)
            ThemeResourceHelper.SetResource(text, TextBlock.ForegroundProperty, foregroundKey);
        return field;
    }

    private UIElement CreateParameterValueEditor(
        GraphParameterDefinition parameter,
        bool locked,
        Action? reload)
    {
        if (locked)
        {
            var lockedBox = new WpfTextBox { Margin = new Thickness(0, 0, 4, 0), ToolTip = "前置输入" };
            LockTextBox(lockedBox, locked: true, restoreValue: parameter.DefaultValue);
            return lockedBox;
        }

        return parameter.Type switch
        {
            GraphParameterType.Boolean => CreateBooleanValueEditor(parameter),
            GraphParameterType.Vector2D => CreateVectorValueEditor(parameter, 2),
            GraphParameterType.Vector3D => CreateVectorValueEditor(parameter, 3),
            GraphParameterType.Vector4D => CreateVectorValueEditor(parameter, 4),
            GraphParameterType.Float => CreateTextValueEditor(parameter, reload, multiline: false, numeric: true),
            _ => CreateTextValueEditor(parameter, reload, multiline: true, numeric: false),
        };
    }

    private UIElement CreateBooleanValueEditor(GraphParameterDefinition parameter)
    {
        var comboBox = new WpfComboBox { Margin = new Thickness(0, 0, 4, 0) };
        comboBox.Items.Add(new WpfComboBoxItem { Content = "False", Tag = "False" });
        comboBox.Items.Add(new WpfComboBoxItem { Content = "True", Tag = "True" });
        comboBox.SelectedIndex = bool.TryParse(parameter.DefaultValue, out bool value) && value ? 1 : 0;
        comboBox.SelectionChanged += (_, _) =>
        {
            if (_isLoading) return;
            parameter.DefaultValue = comboBox.SelectedIndex == 1 ? "True" : "False";
            _markDirty();
        };
        return comboBox;
    }

    private UIElement CreateTextValueEditor(
        GraphParameterDefinition parameter,
        Action? reload,
        bool multiline,
        bool numeric)
    {
        var textBox = new WpfTextBox
        {
            Text = parameter.DefaultValue,
            Margin = new Thickness(0, 0, 4, 0),
            ToolTip = numeric ? "数值默认值" : "默认值",
        };
        ConfigureFreeTextBox(textBox, multiline);
        textBox.TextChanged += (_, _) =>
        {
            if (_isLoading) return;
            parameter.DefaultValue = textBox.Text;
            _markDirty();
        };
        textBox.LostFocus += (_, _) =>
        {
            if (!numeric || string.IsNullOrWhiteSpace(parameter.DefaultValue))
                return;

            var normalized = parameter.DefaultValue.Trim();
            if (normalized.EndsWith("f", StringComparison.OrdinalIgnoreCase))
                normalized = normalized[..^1];
            if (!double.TryParse(normalized, out _))
                parameter.DefaultValue = GraphParameterDefinition.DefaultValueForType(parameter.Type);
            reload?.Invoke();
        };
        return textBox;
    }

    private UIElement CreateVectorValueEditor(GraphParameterDefinition parameter, int componentCount)
    {
        var values = SplitVectorDefault(parameter.DefaultValue, componentCount);
        var panel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 0, 4, 0) };
        for (int i = 0; i < componentCount; i++)
        {
            int index = i;
            var box = new WpfTextBox
            {
                Text = values[index],
                Width = componentCount > 3 ? 28 : 36,
                Margin = new Thickness(index == 0 ? 0 : 3, 0, 0, 0),
                ToolTip = index switch
                {
                    0 => "X",
                    1 => "Y",
                    2 => "Z",
                    _ => "W",
                },
            };
            box.TextChanged += (_, _) =>
            {
                if (_isLoading) return;
                values[index] = box.Text.Trim();
                parameter.DefaultValue = string.Join(",", values);
                _markDirty();
            };
            panel.Children.Add(box);
        }

        return panel;
    }

    private static string[] SplitVectorDefault(string value, int componentCount)
    {
        var defaults = GraphParameterDefinition.DefaultValueForType(componentCount switch
        {
            2 => GraphParameterType.Vector2D,
            3 => GraphParameterType.Vector3D,
            _ => GraphParameterType.Vector4D,
        }).Split(',');
        var parts = value
            .Trim()
            .Trim('(', ')')
            .Split(',', StringSplitOptions.TrimEntries);
        var result = new string[componentCount];
        for (int i = 0; i < componentCount; i++)
            result[i] = i < parts.Length && !string.IsNullOrWhiteSpace(parts[i]) ? parts[i] : defaults[i];
        return result;
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
}
