using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AutomationStudioWpf.Interaction;

namespace AutomationStudioWpf.Controls;

public partial class EditorSurfaceControl
{
    private void InspectorField_TextChanged(object sender, TextChangedEventArgs e) => Forward(EditorSurfaceEvent.InspectorFieldTextChanged, sender, e);
    private void InspectorField_SelectionChanged(object sender, SelectionChangedEventArgs e) => Forward(EditorSurfaceEvent.InspectorFieldSelectionChanged, sender, e);
    private void InspectorField_CheckedChanged(object sender, RoutedEventArgs e) => Forward(EditorSurfaceEvent.InspectorFieldCheckedChanged, sender, e);
    private void ToDoSearchBox_TextChanged(object sender, TextChangedEventArgs e) => Forward(EditorSurfaceEvent.ToDoSearchBoxTextChanged, sender, e);
    private void ToDoTargetListBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => Forward(EditorSurfaceEvent.ToDoTargetListBoxSelectionChanged, sender, e);
    private void BrowseFindImagePath_Click(object sender, RoutedEventArgs e) => Forward(EditorSurfaceEvent.BrowseFindImagePathClick, sender, e);
    private void BrowseStartProgramPath_Click(object sender, RoutedEventArgs e) => Forward(EditorSurfaceEvent.BrowseStartProgramPathClick, sender, e);
    private void RefreshWindowList_Click(object sender, RoutedEventArgs e) => Forward(EditorSurfaceEvent.RefreshWindowListClick, sender, e);
    private void BrowseFindImageSourcePath_Click(object sender, RoutedEventArgs e) => Forward(EditorSurfaceEvent.BrowseFindImageSourcePathClick, sender, e);
    private void CommonKeyChordAddButton_Click(object sender, RoutedEventArgs e) => Forward(EditorSurfaceEvent.CommonKeyChordAddButtonClick, sender, e);
    private void AddParameterButton_Click(object sender, RoutedEventArgs e) => Forward(EditorSurfaceEvent.AddParameterButtonClick, sender, e);
    private void CommonModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => Forward(EditorSurfaceEvent.CommonModeComboBoxSelectionChanged, sender, e);
    private void CommonWindowComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => Forward(EditorSurfaceEvent.CommonWindowComboBoxSelectionChanged, sender, e);
    private void CommonWindowRefreshButton_Click(object sender, RoutedEventArgs e) => Forward(EditorSurfaceEvent.CommonWindowRefreshButtonClick, sender, e);
    private void CommonEnumComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => Forward(EditorSurfaceEvent.CommonEnumComboBoxSelectionChanged, sender, e);
    private void CommonBrowseFileButton_Click(object sender, RoutedEventArgs e) => Forward(EditorSurfaceEvent.CommonBrowseFileButtonClick, sender, e);
    private void SelectWindowInputMode_SelectionChanged(object sender, SelectionChangedEventArgs e) => Forward(EditorSurfaceEvent.SelectWindowInputModeSelectionChanged, sender, e);
    private void SelectWindowAutoComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => Forward(EditorSurfaceEvent.SelectWindowAutoComboBoxSelectionChanged, sender, e);
    private void FindImageSourceModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => Forward(EditorSurfaceEvent.FindImageSourceModeComboBoxSelectionChanged, sender, e);
    private void PinAnchor_Loaded(object sender, RoutedEventArgs e) => Forward(EditorSurfaceEvent.PinAnchorLoaded, sender, e);
    private void PinAnchor_LayoutUpdated(object sender, EventArgs e) => Forward(EditorSurfaceEvent.PinAnchorLayoutUpdated, sender, e);
    private void NodePaletteSearchBox_TextChanged(object sender, TextChangedEventArgs e) => Forward(EditorSurfaceEvent.NodePaletteSearchBoxTextChanged, sender, e);
}
