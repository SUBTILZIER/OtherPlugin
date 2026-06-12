using AutomationStudioWpf.Graph;

namespace AutomationStudioWpf.Interaction;

public sealed partial class InspectorController
{
    private sealed record ToDoTargetOption(string NodeId, string Title, string Number);

    private void LoadToDoNode(ToDoNodeViewModel node)
    {
        _toDoTargetTitleTextBox.Text = node.TargetNodeTitle;
        _toDoTargetNumberTextBox.Text = node.TargetNodeNumber;
        _toDoReturnAfterTargetCheckBox.IsChecked = node.ReturnAfterTarget;
        _toDoSearchBox.Text = string.Empty;
        RefreshToDoTargetOptions();
    }

    private void ApplyToDoNodeChanges(ToDoNodeViewModel node)
    {
        node.TargetNodeTitle = _toDoTargetTitleTextBox.Text.Trim();
        node.TargetNodeNumber = _toDoTargetNumberTextBox.Text.Trim();
        node.ReturnAfterTarget = _toDoReturnAfterTargetCheckBox.IsChecked == true;

        var target = FindToDoTarget(node, node.TargetNodeTitle, node.TargetNodeNumber);
        node.TargetNodeId = target?.Id;
    }

    public void ToDoSearchChanged()
    {
        if (_isLoading)
            return;

        RefreshToDoTargetOptions();
    }
}
