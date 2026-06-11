using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AutomationStudioWpf.Interaction;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace AutomationStudioWpf.Controls;

/// <summary>
/// Owns one session's editor UI.
/// </summary>
public partial class EditorSurfaceControl : WpfUserControl
{
    public EditorSurfaceControl()
    {
        InitializeComponent();
    }

    public EditorSessionViewModel? Session { get; private set; }

    public EditorSurfaceContext? SurfaceContext { get; private set; }

    public void Attach(EditorSessionViewModel session, EditorSurfaceContext context)
    {
        Session = session;
        SurfaceContext = context;
        DataContext = session;
    }

    private void Forward(EditorSurfaceEvent surfaceEvent, object sender, EventArgs e)
    {
        SurfaceContext?.HandleEvent(surfaceEvent, sender, e);
    }

    private void AddGraphListItem_Click(object sender, RoutedEventArgs e) => Forward(EditorSurfaceEvent.AddGraphListItemClick, sender, e);
    private void GraphListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Forward(EditorSurfaceEvent.GraphListBoxMouseDoubleClick, sender, e);
    private void GraphListBox_KeyDown(object sender, WpfKeyEventArgs e) => Forward(EditorSurfaceEvent.GraphListBoxKeyDown, sender, e);
    private void RenameGraphMenuItem_Click(object sender, RoutedEventArgs e) => Forward(EditorSurfaceEvent.RenameGraphMenuItemClick, sender, e);
    private void DeleteGraphMenuItem_Click(object sender, RoutedEventArgs e) => Forward(EditorSurfaceEvent.DeleteGraphMenuItemClick, sender, e);
    private void AddFunctionListItem_Click(object sender, RoutedEventArgs e) => Forward(EditorSurfaceEvent.AddFunctionListItemClick, sender, e);
    private void FunctionListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Forward(EditorSurfaceEvent.FunctionListBoxMouseDoubleClick, sender, e);
    private void FunctionListBox_KeyDown(object sender, WpfKeyEventArgs e) => Forward(EditorSurfaceEvent.FunctionListBoxKeyDown, sender, e);
    private void FunctionListItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e) => Forward(EditorSurfaceEvent.FunctionListItemPreviewMouseRightButtonDown, sender, e);
    private void FunctionListItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => Forward(EditorSurfaceEvent.FunctionListItemPreviewMouseLeftButtonDown, sender, e);
    private void AddMacroListItem_Click(object sender, RoutedEventArgs e) => Forward(EditorSurfaceEvent.AddMacroListItemClick, sender, e);
    private void MacroListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Forward(EditorSurfaceEvent.MacroListBoxMouseDoubleClick, sender, e);
    private void MacroListBox_KeyDown(object sender, WpfKeyEventArgs e) => Forward(EditorSurfaceEvent.MacroListBoxKeyDown, sender, e);
    private void MacroListItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e) => Forward(EditorSurfaceEvent.MacroListItemPreviewMouseRightButtonDown, sender, e);
    private void MacroListItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => Forward(EditorSurfaceEvent.MacroListItemPreviewMouseLeftButtonDown, sender, e);
    private void GraphListItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e) => Forward(EditorSurfaceEvent.GraphListItemPreviewMouseRightButtonDown, sender, e);
    private void GraphListItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => Forward(EditorSurfaceEvent.GraphListItemPreviewMouseLeftButtonDown, sender, e);
    private void GraphNameTextBox_KeyDown(object sender, WpfKeyEventArgs e) => Forward(EditorSurfaceEvent.GraphNameTextBoxKeyDown, sender, e);
    private void GraphNameTextBox_LostFocus(object sender, RoutedEventArgs e) => Forward(EditorSurfaceEvent.GraphNameTextBoxLostFocus, sender, e);
    private void LibraryPublishCheckBox_Click(object sender, RoutedEventArgs e) => Forward(EditorSurfaceEvent.LibraryPublishCheckBoxClick, sender, e);
    private void ToggleEventGraphSection_Click(object sender, RoutedEventArgs e) => Forward(EditorSurfaceEvent.ToggleEventGraphSectionClick, sender, e);
    private void ToggleFunctionSection_Click(object sender, RoutedEventArgs e) => Forward(EditorSurfaceEvent.ToggleFunctionSectionClick, sender, e);
    private void ToggleMacroSection_Click(object sender, RoutedEventArgs e) => Forward(EditorSurfaceEvent.ToggleMacroSectionClick, sender, e);
    private void NodeCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => Forward(EditorSurfaceEvent.NodeCardMouseLeftButtonDown, sender, e);
    private void NodeHeader_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => Forward(EditorSurfaceEvent.NodeHeaderPreviewMouseLeftButtonDown, sender, e);
    private void NodeHeader_PreviewMouseMove(object sender, WpfMouseEventArgs e) => Forward(EditorSurfaceEvent.NodeHeaderPreviewMouseMove, sender, e);
    private void NodeHeader_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => Forward(EditorSurfaceEvent.NodeHeaderPreviewMouseLeftButtonUp, sender, e);
    private void GraphViewport_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => Forward(EditorSurfaceEvent.GraphViewportPreviewMouseLeftButtonDown, sender, e);
    private void GraphViewport_PreviewMouseMove(object sender, WpfMouseEventArgs e) => Forward(EditorSurfaceEvent.GraphViewportPreviewMouseMove, sender, e);
    private void GraphViewport_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => Forward(EditorSurfaceEvent.GraphViewportPreviewMouseLeftButtonUp, sender, e);
    private void GraphViewport_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e) => Forward(EditorSurfaceEvent.GraphViewportPreviewMouseRightButtonDown, sender, e);
    private void GraphViewport_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e) => Forward(EditorSurfaceEvent.GraphViewportPreviewMouseRightButtonUp, sender, e);
    private void GraphViewport_PreviewMouseWheel(object sender, MouseWheelEventArgs e) => Forward(EditorSurfaceEvent.GraphViewportPreviewMouseWheel, sender, e);
    private void NodePaletteScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e) => Forward(EditorSurfaceEvent.NodePaletteScrollViewerPreviewMouseWheel, sender, e);
    private void PinButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => Forward(EditorSurfaceEvent.PinButtonPreviewMouseLeftButtonDown, sender, e);
    private void PinButton_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => Forward(EditorSurfaceEvent.PinButtonPreviewMouseLeftButtonUp, sender, e);
    private void ConnectionPath_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Forward(EditorSurfaceEvent.ConnectionPathMouseDoubleClick, sender, e);
    private void ConnectionPath_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => Forward(EditorSurfaceEvent.ConnectionPathMouseLeftButtonDown, sender, e);
    private void ConnectionPath_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => Forward(EditorSurfaceEvent.ConnectionPathPreviewMouseLeftButtonDown, sender, e);
    private void ConnectionPath_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e) => Forward(EditorSurfaceEvent.ConnectionPathPreviewMouseRightButtonDown, sender, e);
    private void DeleteConnectionPath_Click(object sender, RoutedEventArgs e) => Forward(EditorSurfaceEvent.DeleteConnectionPathClick, sender, e);
    private void AddRerouteToConnectionPath_Click(object sender, RoutedEventArgs e) => Forward(EditorSurfaceEvent.AddRerouteToConnectionPathClick, sender, e);
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
