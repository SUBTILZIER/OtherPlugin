using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AutomationStudioWpf.Interaction;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace AutomationStudioWpf.Controls;

/// <summary>
/// Owns one session's editor UI.
/// </summary>
public partial class EditorSurfaceControl : WpfUserControl
{
    private readonly Dictionary<WpfTextBox, string> _inspectorEditStartValues = new();

    public static readonly DependencyProperty IsExecutionFrozenProperty =
        DependencyProperty.Register(
            nameof(IsExecutionFrozen),
            typeof(bool),
            typeof(EditorSurfaceControl),
            new PropertyMetadata(false));

    public static readonly DependencyProperty ExecutionFreezeMessageProperty =
        DependencyProperty.Register(
            nameof(ExecutionFreezeMessage),
            typeof(string),
            typeof(EditorSurfaceControl),
            new PropertyMetadata("按 Esc 或顶部停止按钮结束调试。"));

    public EditorSurfaceControl()
    {
        InitializeComponent();
    }

    public bool IsExecutionFrozen
    {
        get => (bool)GetValue(IsExecutionFrozenProperty);
        set => SetValue(IsExecutionFrozenProperty, value);
    }

    public string ExecutionFreezeMessage
    {
        get => (string)GetValue(ExecutionFreezeMessageProperty);
        set => SetValue(ExecutionFreezeMessageProperty, value);
    }

    public EditorSessionViewModel? Session { get; private set; }
    internal void ApplyLayout(double sidebar, double inspector)
    {
        GraphSidebarColumn.Width = new GridLength(sidebar);
        InspectorColumn.Width = new GridLength(inspector);
    }

    internal (double Sidebar, double Inspector) ReadLayout() =>
        (GraphSidebarColumn.ActualWidth, InspectorColumn.ActualWidth);
    internal void ToggleSidebar() { bool hide = GraphSidebarColumn.Width.Value > 1; GraphSidebarColumn.MinWidth = hide ? 0 : 180; GraphSidebarColumn.Width = hide ? new GridLength(0) : new GridLength(248); }
    internal void ToggleInspector() { bool hide = InspectorColumn.Width.Value > 1; InspectorColumn.MinWidth = hide ? 0 : 420; InspectorColumn.Width = hide ? new GridLength(0) : new GridLength(472); }
    internal void FocusInspector() => NodeTitleTextBox.Focus();

    public EditorSurfaceContext? SurfaceContext { get; private set; }

    public InspectorViewModel StructuredInspector =>
        SurfaceContext?.InspectorController.StructuredInspector ?? new InspectorViewModel();

    public void Attach(EditorSessionViewModel session, EditorSurfaceContext context)
    {
        Session = session;
        SurfaceContext = context;
        DataContext = session;
    }

    internal void Detach()
    {
        EditorSurfaceHostController.DetachFromCurrentParent(this);
        IsExecutionFrozen = false;
        Session = null;
        SurfaceContext = null;
        DataContext = null;
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
    private void GraphListItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e) => Forward(EditorSurfaceEvent.GraphListItemPreviewMouseRightButtonDown, sender, e);
    private void GraphListItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => Forward(EditorSurfaceEvent.GraphListItemPreviewMouseLeftButtonDown, sender, e);
    private void GraphNameTextBox_KeyDown(object sender, WpfKeyEventArgs e) => Forward(EditorSurfaceEvent.GraphNameTextBoxKeyDown, sender, e);
    private void GraphNameTextBox_LostFocus(object sender, RoutedEventArgs e) => Forward(EditorSurfaceEvent.GraphNameTextBoxLostFocus, sender, e);
    private void LibraryPublishCheckBox_Click(object sender, RoutedEventArgs e) => Forward(EditorSurfaceEvent.LibraryPublishCheckBoxClick, sender, e);
    private void ToggleEventGraphSection_Click(object sender, RoutedEventArgs e) => Forward(EditorSurfaceEvent.ToggleEventGraphSectionClick, sender, e);
    private void ToggleFunctionSection_Click(object sender, RoutedEventArgs e) => Forward(EditorSurfaceEvent.ToggleFunctionSectionClick, sender, e);
    private void NodeCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => Forward(EditorSurfaceEvent.NodeCardMouseLeftButtonDown, sender, e);
    private void NodeHeader_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => Forward(EditorSurfaceEvent.NodeHeaderPreviewMouseLeftButtonDown, sender, e);
    private void NodeHeader_PreviewMouseMove(object sender, WpfMouseEventArgs e) => Forward(EditorSurfaceEvent.NodeHeaderPreviewMouseMove, sender, e);
    private void NodeHeader_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => Forward(EditorSurfaceEvent.NodeHeaderPreviewMouseLeftButtonUp, sender, e);
    private void CommonVariadicAddButton_Click(object sender, RoutedEventArgs e) => Forward(EditorSurfaceEvent.CommonVariadicAddButtonClick, sender, e);
    private void CommonVariadicRemoveButton_Click(object sender, RoutedEventArgs e) => Forward(EditorSurfaceEvent.CommonVariadicRemoveButtonClick, sender, e);
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
    private void GridSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e) =>
        SurfaceContext?.NotifyLayoutChanged();
    private void NodePaletteSearchBox_PreviewKeyDown(object sender, WpfKeyEventArgs e) => SurfaceContext?.HandleNodePaletteKeyDown(e);

    private void InspectorEditor_PreviewGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (e.NewFocus is WpfTextBox textBox &&
            textBox.TemplatedParent is not NumericUpDown)
        {
            _inspectorEditStartValues[textBox] = textBox.Text;
        }
    }

    private void InspectorEditor_PreviewLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (e.OldFocus is WpfTextBox textBox)
            _inspectorEditStartValues.Remove(textBox);
    }

    private void InspectorEditor_PreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.OriginalSource is not WpfTextBox textBox ||
            textBox.TemplatedParent is NumericUpDown ||
            textBox.Tag is not null)
            return;

        if (e.Key == Key.Enter && !textBox.AcceptsReturn)
        {
            SurfaceContext?.ApplyInspectorChanges();
            Keyboard.ClearFocus();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && _inspectorEditStartValues.TryGetValue(textBox, out var originalValue))
        {
            textBox.Text = originalValue;
            SurfaceContext?.ApplyInspectorChanges();
            e.Handled = true;
        }
    }
}
