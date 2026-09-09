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
    private double _minimapMinX, _minimapMinY, _minimapScale = 1;
    private bool _minimapEnabled = true;
    private double _mapMaxX, _mapMaxY;
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
    internal void RefreshMinimap(IEnumerable<Graph.NodeBaseViewModel> nodes)
    {
        MinimapCanvas.Children.Clear(); var list = nodes.ToList(); if (list.Count == 0) { MinimapPanel.Visibility = Visibility.Collapsed; return; }
        MinimapPanel.Visibility = _minimapEnabled ? Visibility.Visible : Visibility.Collapsed; double minX=list.Min(n=>n.X), minY=list.Min(n=>n.Y), maxX=list.Max(n=>n.X+n.Width), maxY=list.Max(n=>n.Y+n.Height); double sx=160/Math.Max(1,maxX-minX), sy=100/Math.Max(1,maxY-minY), s=Math.Min(sx,sy); _minimapMinX=minX; _minimapMinY=minY; _mapMaxX=maxX; _mapMaxY=maxY; _minimapScale=s;
        foreach(var n in list.Take(2000)){ var b=new Border{Width=Math.Max(2,n.Width*s),Height=Math.Max(2,n.Height*s),Background=System.Windows.Media.Brushes.SteelBlue,CornerRadius=new CornerRadius(1)}; Canvas.SetLeft(b,(n.X-minX)*s); Canvas.SetTop(b,(n.Y-minY)*s); MinimapCanvas.Children.Add(b); }
        UpdateMinimapViewport();
    }

    private void UpdateMinimapViewport()
    {
        if (SurfaceContext?.CanvasPanZoomController is null || MinimapCanvas is null || MinimapCanvas.Children.Count == 0) return;
        if (!double.IsFinite(_minimapScale) || _minimapScale <= 0) return;
        try
        {
        if (MinimapCanvas.Children[^1] is Border old && old.Tag as string == "viewport") MinimapCanvas.Children.RemoveAt(MinimapCanvas.Children.Count - 1);
        var state = SurfaceContext.CanvasPanZoomController.GetViewportState();
        var viewport = new Border { Tag = "viewport", Width = Math.Max(8, state.Size.X * _minimapScale), Height = Math.Max(8, state.Size.Y * _minimapScale), BorderBrush = System.Windows.Media.Brushes.White, BorderThickness = new Thickness(1), Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(35, 255, 255, 255)), IsHitTestVisible = false };
        Canvas.SetLeft(viewport, (state.TopLeft.X - _minimapMinX) * _minimapScale); Canvas.SetTop(viewport, (state.TopLeft.Y - _minimapMinY) * _minimapScale); MinimapCanvas.Children.Add(viewport);
        }
        catch (InvalidOperationException) { }
    }

    internal void SetMinimapEnabled(bool enabled)
    {
        _minimapEnabled = enabled;
        MinimapPanel.Visibility = enabled && MinimapCanvas.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ToggleMinimap_Click(object sender, RoutedEventArgs e)
    {
        bool enabled = MinimapPanel.Visibility != Visibility.Visible;
        SetMinimapEnabled(enabled);
        MinimapToggled?.Invoke(enabled);
    }

    internal event Action<bool>? MinimapToggled;
    internal void FocusInspector() => NodeTitleTextBox.Focus();
    private void Minimap_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var p = e.GetPosition(MinimapCanvas); SurfaceContext?.NavigateToGraphPoint(new System.Windows.Point(_minimapMinX + p.X / _minimapScale, _minimapMinY + p.Y / _minimapScale)); e.Handled = true;
    }
    private void UpdateZoomLabel()
    {
        if (ZoomResetButton is null || SurfaceContext?.CanvasPanZoomController is not { } controller)
            return;
        ZoomResetButton.Content = $"{controller.ZoomLevel:P0}";
    }
    private void ZoomOut_Click(object sender, RoutedEventArgs e) { SurfaceContext?.ZoomBy(0.88); UpdateZoomLabel(); }
    private void ZoomIn_Click(object sender, RoutedEventArgs e) { SurfaceContext?.ZoomBy(1.12); UpdateZoomLabel(); }
    private void ZoomReset_Click(object sender, RoutedEventArgs e) { SurfaceContext?.ResetView(); UpdateZoomLabel(); }
    private void FitGraph_Click(object sender, RoutedEventArgs e) => SurfaceContext?.FitGraphToView();

    public EditorSurfaceContext? SurfaceContext { get; private set; }

    public InspectorViewModel StructuredInspector =>
        SurfaceContext?.InspectorController.StructuredInspector ?? new InspectorViewModel();

    public void Attach(EditorSessionViewModel session, EditorSurfaceContext context)
    {
        if (SurfaceContext?.CanvasPanZoomController is { } controller) controller.ViewChanged -= OnViewChanged;
        Session = session;
        SurfaceContext = context;
        if (context.CanvasPanZoomController is not null) context.CanvasPanZoomController.ViewChanged += OnViewChanged;
        OnViewChanged();
        DataContext = session;
    }

    private void OnViewChanged()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(OnViewChanged); return; }
        UpdateZoomLabel();
        UpdateMinimapViewport();
    }

    internal void Detach()
    {
        if (SurfaceContext?.CanvasPanZoomController is { } controller) controller.ViewChanged -= OnViewChanged;
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
    private void LayoutSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e) =>
        SurfaceContext?.NotifyLayoutChanged();
    private void NodePaletteSearchBox_PreviewKeyDown(object sender, WpfKeyEventArgs e) => SurfaceContext?.HandleNodePaletteKeyDown(e);
}
