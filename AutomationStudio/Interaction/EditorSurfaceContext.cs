using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AutomationStudioWpf.Adapters;
using AutomationStudioWpf.Controls;
using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Services;
using Point = System.Windows.Point;

namespace AutomationStudioWpf.Interaction;

/// <summary>
/// Runtime context for a session-owned editor surface.
/// </summary>
public sealed class EditorSurfaceContext
{
    private enum SurfaceEventActivation
    {
        Ignore,
        Passive,
        Promote,
    }

    private AutomationStudioWpf.MainWindow? _host;
    private EditorSurfaceHostServices? _services;
    private GraphListController? _activeAssetController;

    public EditorSurfaceContext(EditorSessionViewModel session, EditorSurfaceControl surface)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
        Surface = surface ?? throw new ArgumentNullException(nameof(surface));
    }

    public EditorSessionViewModel Session { get; }

    public EditorSurfaceControl Surface { get; }

    public GraphCommandService CommandService { get; private set; } = null!;

    public GraphListController GraphListController { get; private set; } = null!;

    public GraphListController FunctionListController { get; private set; } = null!;

    public CanvasPanZoomController CanvasPanZoomController { get; private set; } = null!;

    public NodeDragSelectionController NodeDragSelectionController { get; private set; } = null!;

    public InspectorController InspectorController { get; private set; } = null!;

    public PinConnectionController PinConnectionController { get; private set; } = null!;

    public NodePaletteController NodePaletteController { get; private set; } = null!;

    public GraphImportDropController GraphImportDropController { get; private set; } = null!;

    public GraphListController? ActiveAssetController
    {
        get => _activeAssetController;
        set => _activeAssetController = value;
    }

    public bool IsConfigured => _services is not null;

    internal void Configure(AutomationStudioWpf.MainWindow host, EditorSurfaceHostServices services)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        Surface.Attach(Session, this);
        if (CommandService is null)
            RebuildControllers();
    }

    public void RebuildControllers()
    {
        var services = _services ?? throw new InvalidOperationException("Editor surface context is not configured.");

        CommandService = Session.CommandService ??= new GraphCommandService(
            Session.EditorService,
            () => GetActiveGraphKind() ?? Session.ActiveGraphKind ?? GraphAssetKind.EventGraph,
            SyncNodeFactorySequence,
            services.SetStatus);

        GraphListController = new GraphListController(
            services.Owner,
            Session.EditorService,
            services.LibraryService,
            Session.NodeFactory,
            Surface.GraphListBox,
            Session.GraphListItems,
            SyncNodeFactorySequence,
            services.PersistAll,
            GraphAssetKind.EventGraph,
            "事件图",
            "事件图",
            services.SetStatus);

        FunctionListController = new GraphListController(
            services.Owner,
            Session.EditorService,
            services.LibraryService,
            Session.NodeFactory,
            Surface.FunctionListBox,
            Session.FunctionListItems,
            SyncNodeFactorySequence,
            services.PersistAll,
            GraphAssetKind.Function,
            "函数",
            "新函数_",
            services.SetStatus);

        CanvasPanZoomController = new CanvasPanZoomController(
            Surface.GraphViewport,
            Surface.GraphZoomTransform,
            Surface.GraphPanTransform,
            Session.EditorService,
            services.SetStatus);

        NodeDragSelectionController = new NodeDragSelectionController(
            Session.EditorService,
            CommandService,
            services.ClipboardService,
            Session.NodeFactory,
            Surface.GraphViewport,
            Surface.SelectionRectangle,
            ViewportToGraph,
            LoadNodeToInspector,
            FitGraphToView,
            services.EnsureCanvasLargeEnough,
            services.MarkLayoutDirty,
            () => PinConnectionController?.ClearSelectedConnectionPath(),
            services.SetStatus);

        InspectorController = new InspectorController(
            services.Owner,
            Session.EditorService,
            new Win32WindowAdapter(),
            services.MarkDirty,
            services.SetStatus,
            Surface.InspectorHintTextBlock,
            Surface.NodeTitleTextBox,
            Surface.NodeNumberTextBlock,
            Surface.ParameterInspectorPanel,
            Surface.AddParameterButton,
            Surface.ParameterInspectorTitle,
            Surface.ParameterRowsPanel,
            Surface.FindImageInspectorPanel,
            Surface.FindImageSourceModeComboBox,
            Surface.FindImageSourcePathLabel,
            Surface.FindImageSourcePathPanel,
            Surface.FindImageSourcePathTextBox,
            Surface.FindImagePathTextBox,
            Surface.FindImageThresholdTextBox,
            Surface.FindImageUseRegionCheckBox,
            Surface.FindImageRegionXTextBox,
            Surface.FindImageRegionYTextBox,
            Surface.FindImageRegionWidthTextBox,
            Surface.FindImageRegionHeightTextBox,
            Surface.MouseLeftInspectorPanel,
            Surface.MousePositionXTextBox,
            Surface.MousePositionYTextBox,
            Surface.MouseClickOperationModeComboBox,
            Surface.MouseButtonComboBox,
            Surface.KeyboardInspectorPanel,
            Surface.KeyboardKeyComboBox,
            Surface.KeyboardOperationModeComboBox,
            Surface.ScrollWheelInspectorPanel,
            Surface.ScrollWheelActionComboBox,
            Surface.ScrollWheelSpeedTextBox,
            Surface.ScrollWheelIntervalTextBox,
            Surface.ScrollWheelDurationTextBox,
            Surface.IfInspectorPanel,
            Surface.IfConditionComboBox,
            Surface.ForLoopInspectorPanel,
            Surface.ForLoopCountTextBox,
            Surface.ForLoopEndConditionComboBox,
            Surface.WhileLoopInspectorPanel,
            Surface.WhileLoopConditionComboBox,
            Surface.WhileLoopModeComboBox,
            Surface.WhileMaxIterationsLabel,
            Surface.WhileMaxIterationsTextBox,
            Surface.DelayInspectorPanel,
            Surface.DelayMsTextBox,
            Surface.MouseMoveInspectorPanel,
            Surface.MouseMovePositionXTextBox,
            Surface.MouseMovePositionYTextBox,
            Surface.StartProgramInspectorPanel,
            Surface.StartProgramPathTextBox,
            Surface.StartProgramWaitTimeoutTextBox,
            Surface.StartProgramFailureActionComboBox,
            Surface.StartProgramRetryCountTextBox,
            Surface.PrintLogInspectorPanel,
            Surface.PrintLogMessageTextBox,
            Surface.SelectWindowInspectorPanel,
            Surface.SelectWindowInputModeComboBox,
            Surface.SelectWindowManualPanel,
            Surface.SelectWindowProcessNameTextBox,
            Surface.SelectWindowAutoPanel,
            Surface.SelectWindowAutoComboBox,
            Surface.ToDoInspectorPanel,
            Surface.ToDoSearchBox,
            Surface.ToDoTargetListBox,
            Surface.ToDoTargetTitleTextBox,
            Surface.ToDoTargetNumberTextBox,
            Surface.ToDoReturnAfterTargetCheckBox,
            Surface.CommonInspectorPanel,
            Surface.CommonKeyChordAddPanel,
            Surface.CommonKeyChordKeyComboBox,
            Surface.CommonModePanel,
            Surface.CommonModeComboBox,
            Surface.CommonWindowPickerPanel,
            Surface.CommonWindowComboBox,
            Surface.CommonEnumPanel,
            Surface.CommonEnumLabel,
            Surface.CommonEnumComboBox,
            Surface.CommonBrowseFileButton,
            Surface.CommonTextLabel,
            Surface.CommonTextBox,
            Surface.CommonText2Label,
            Surface.CommonText2Box,
            Surface.CommonText3Label,
            Surface.CommonText3Box,
            Surface.CommonNumberLabel,
            Surface.CommonNumberBox,
            Surface.CommonNumber2Label,
            Surface.CommonNumber2Box,
            Surface.CommonNumber3Label,
            Surface.CommonNumber3Box,
            Surface.CommonNumber4Label,
            Surface.CommonNumber4Box,
            Surface.CommonFlagCheckBox,
            Surface.CommonHelpTextBlock);

        PinConnectionController = new PinConnectionController(
            Session.EditorService,
            CommandService,
            Session.NodeFactory,
            Surface.GraphViewport,
            Surface.PreviewConnectionPath,
            ViewportToGraph,
            position => TryGetPinAtPosition(position, out var pin) ? pin : null,
            OpenNodePaletteForConnection,
            SelectNode,
            ClearNodeSelectionForConnection,
            NodeDragSelectionController.SetCanvasFocusActive,
            services.SetStatus);

        NodePaletteController = new NodePaletteController(
            Surface.NodePalette,
            Surface.NodePaletteSearchBox,
            Surface.NodePaletteContent,
            Session.NodeFactory,
            Session.EditorService,
            CommandService,
            services.NodeRegistry,
            services.GetCallableFunctions,
            services.GetCallableCustomEvents,
            GetActiveGraphKind,
            services.SnapshotActiveAsset,
            ViewportToGraph,
            () => new System.Windows.Size(Surface.GraphViewport.ActualWidth, Surface.GraphViewport.ActualHeight),
            SelectNode,
            node => PinConnectionController.TryAutoConnectNewNode(node));

        GraphImportDropController = new GraphImportDropController(services.Owner, GraphListController);
    }

    internal void HandleEvent(EditorSurfaceEvent surfaceEvent, object sender, EventArgs e)
    {
        if (_host is null)
            return;

        var activation = GetEventActivation(surfaceEvent, sender, e);
        if (activation == SurfaceEventActivation.Ignore)
            return;

        _host.HandleEditorSurfaceEvent(Session, surfaceEvent, sender, e, activation == SurfaceEventActivation.Promote);
    }

    private static SurfaceEventActivation GetEventActivation(EditorSurfaceEvent surfaceEvent, object sender, EventArgs e)
    {
        return surfaceEvent switch
        {
            EditorSurfaceEvent.PinAnchorLoaded or
            EditorSurfaceEvent.PinAnchorLayoutUpdated => SurfaceEventActivation.Passive,

            EditorSurfaceEvent.GraphViewportPreviewMouseMove or
            EditorSurfaceEvent.NodeHeaderPreviewMouseMove => e is System.Windows.Input.MouseEventArgs mouseEvent && HasPressedMouseButton(mouseEvent)
                ? SurfaceEventActivation.Promote
                : SurfaceEventActivation.Ignore,

            EditorSurfaceEvent.InspectorFieldTextChanged or
            EditorSurfaceEvent.InspectorFieldSelectionChanged or
            EditorSurfaceEvent.InspectorFieldCheckedChanged or
            EditorSurfaceEvent.ToDoSearchBoxTextChanged or
            EditorSurfaceEvent.ToDoTargetListBoxSelectionChanged or
            EditorSurfaceEvent.CommonModeComboBoxSelectionChanged or
            EditorSurfaceEvent.CommonWindowComboBoxSelectionChanged or
            EditorSurfaceEvent.CommonEnumComboBoxSelectionChanged or
            EditorSurfaceEvent.SelectWindowInputModeSelectionChanged or
            EditorSurfaceEvent.SelectWindowAutoComboBoxSelectionChanged or
            EditorSurfaceEvent.FindImageSourceModeComboBoxSelectionChanged or
            EditorSurfaceEvent.NodePaletteSearchBoxTextChanged => IsFocusWithin(sender)
                ? SurfaceEventActivation.Promote
                : SurfaceEventActivation.Ignore,

            _ => SurfaceEventActivation.Promote,
        };
    }

    private static bool HasPressedMouseButton(System.Windows.Input.MouseEventArgs e) =>
        e.LeftButton == MouseButtonState.Pressed ||
        e.RightButton == MouseButtonState.Pressed ||
        e.MiddleButton == MouseButtonState.Pressed ||
        e.XButton1 == MouseButtonState.Pressed ||
        e.XButton2 == MouseButtonState.Pressed;

    private static bool IsFocusWithin(object sender) =>
        sender is UIElement element && element.IsKeyboardFocusWithin;

    public GraphAssetKind? GetActiveGraphKind() => _activeAssetController?.AssetKind;

    public void ClearControllerActiveState()
    {
        GraphListController.ClearActive();
        FunctionListController.ClearActive();
        _activeAssetController = null;
    }

    public void LoadInspector(NodeBaseViewModel? node) => InspectorController.LoadNode(node);

    public void ApplyInspectorChanges() => InspectorController.ApplyChanges();

    public void SelectNode(NodeBaseViewModel? node) => NodeDragSelectionController.SelectNode(node);

    public Point ViewportToGraph(Point viewportPoint) => CanvasPanZoomController.ViewportToGraph(viewportPoint);

    public void FitGraphToView() => CanvasPanZoomController.FitGraphToView();

    public void OpenNodePalette(Point viewportPos)
    {
        PinConnectionController.CancelPendingPaletteConnection();
        NodePaletteController.Open(viewportPos);
    }

    public void OpenNodePaletteForConnection(Point viewportPos) => NodePaletteController.Open(viewportPos);

    public void CloseNodePalette()
    {
        PinConnectionController.CancelPendingPaletteConnection();
        NodePaletteController.Close();
    }

    public void SyncNodeFactorySequence()
    {
        int maxSeq = Session.EditorService.Nodes
            .Select(n => n.Id)
            .Select(id => id.StartsWith("node_") && int.TryParse(id[5..], out var num) ? num : 0)
            .DefaultIfEmpty(0)
            .Max();
        Session.NodeFactory.ResetCounter(maxSeq);
    }

    private void LoadNodeToInspector(NodeBaseViewModel? node) => InspectorController.LoadNode(node);

    private bool TryGetPinAtPosition(Point position, out PinViewModel? pin)
    {
        var hit = Surface.GraphSurface.InputHitTest(position) as DependencyObject;
        if (TryGetPinFromSource(hit, out pin))
            return true;

        return TryGetNearestPinAtPosition(position, out pin);
    }

    private bool TryGetNearestPinAtPosition(Point position, out PinViewModel? pin)
    {
        const double hitRadius = 24.0;
        double bestDistanceSquared = hitRadius * hitRadius;
        PinViewModel? bestPin = null;

        foreach (var candidate in Session.EditorService.Nodes.SelectMany(node => node.InputPins.Concat(node.OutputPins)))
        {
            var anchor = candidate.Owner.GetPinAnchor(candidate);
            double x = candidate.Owner.X + anchor.X;
            double y = candidate.Owner.Y + anchor.Y;
            double dx = position.X - x;
            double dy = position.Y - y;
            double distanceSquared = dx * dx + dy * dy;
            if (distanceSquared > bestDistanceSquared)
                continue;

            bestDistanceSquared = distanceSquared;
            bestPin = candidate;
        }

        pin = bestPin;
        return pin is not null;
    }

    private static bool TryGetPinFromSource(DependencyObject? source, out PinViewModel? pin)
    {
        var current = source;
        while (current is not null)
        {
            if (current is FrameworkElement element && element.DataContext is PinViewModel dataPin)
            {
                pin = dataPin;
                return true;
            }
            current = VisualTreeHelper.GetParent(current);
        }
        pin = null;
        return false;
    }

    private void ClearNodeSelectionForConnection()
    {
        foreach (var node in Session.EditorService.Nodes)
            node.IsSelected = false;

        LoadNodeToInspector(null);
    }
}
