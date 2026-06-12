using System.Windows;
using System.Windows.Controls;
using AutomationStudioWpf.Graph;

namespace AutomationStudioWpf;

public partial class MainWindow
{
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
        if (GetOperationEditorSession() is { } session)
            CommitSessionToAsset(session, applyInspector: ReferenceEquals(session, _activeEditorSession));
    }

    private void CommitInspectorAndSnapshotAllSessions()
    {
        CommitAllSessionsToAssets(applyInspectorForActive: true);
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
}
