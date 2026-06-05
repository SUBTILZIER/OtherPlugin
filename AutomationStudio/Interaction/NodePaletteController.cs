using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Nodes;
using AutomationStudioWpf.Services;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using WpfButton = System.Windows.Controls.Button;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace AutomationStudioWpf.Interaction;

public sealed class NodePaletteController
{
    private static readonly HashSet<NodeKind> HiddenKinds =
    [
        NodeKind.Start,
        NodeKind.Reroute,
        NodeKind.FunctionEntry,
        NodeKind.FunctionReturn,
        NodeKind.MacroEntry,
        NodeKind.MacroOutput,
        NodeKind.FunctionCall,
        NodeKind.MacroCall,
        NodeKind.CustomEventCall,
    ];

    private readonly Border _palette;
    private readonly WpfTextBox _searchBox;
    private readonly StackPanel _content;
    private readonly NodeFactory _nodeFactory;
    private readonly GraphEditorService _editorService;
    private readonly NodeRegistry _nodeRegistry;
    private readonly Func<IEnumerable<CallableGraphItem>> _getFunctions;
    private readonly Func<IEnumerable<CallableGraphItem>> _getMacros;
    private readonly Func<IEnumerable<CallableCustomEventItem>> _getCustomEvents;
    private readonly Func<GraphAssetKind?> _getActiveGraphKind;
    private readonly Action _snapshotActiveAsset;
    private readonly Func<Point, Point> _viewportToGraph;
    private readonly Func<Size> _getViewportSize;
    private readonly Action<NodeBaseViewModel> _selectNode;

    private Point _openViewportPoint;

    public NodePaletteController(
        Border palette,
        WpfTextBox searchBox,
        StackPanel content,
        NodeFactory nodeFactory,
        GraphEditorService editorService,
        NodeRegistry nodeRegistry,
        Func<IEnumerable<CallableGraphItem>> getFunctions,
        Func<IEnumerable<CallableGraphItem>> getMacros,
        Func<IEnumerable<CallableCustomEventItem>> getCustomEvents,
        Func<GraphAssetKind?> getActiveGraphKind,
        Action snapshotActiveAsset,
        Func<Point, Point> viewportToGraph,
        Func<Size> getViewportSize,
        Action<NodeBaseViewModel> selectNode)
    {
        _palette = palette;
        _searchBox = searchBox;
        _content = content;
        _nodeFactory = nodeFactory;
        _editorService = editorService;
        _nodeRegistry = nodeRegistry;
        _getFunctions = getFunctions;
        _getMacros = getMacros;
        _getCustomEvents = getCustomEvents;
        _getActiveGraphKind = getActiveGraphKind;
        _snapshotActiveAsset = snapshotActiveAsset;
        _viewportToGraph = viewportToGraph;
        _getViewportSize = getViewportSize;
        _selectNode = selectNode;
    }

    public void Open(Point viewportPoint)
    {
        _snapshotActiveAsset();
        _openViewportPoint = viewportPoint;
        _searchBox.Text = string.Empty;
        Build(string.Empty);

        Canvas.SetLeft(_palette, viewportPoint.X);
        Canvas.SetTop(_palette, viewportPoint.Y);
        _palette.Visibility = Visibility.Visible;
        PositionPalette();

        _palette.Dispatcher.BeginInvoke(new Action(() =>
        {
            PositionPalette();
            _searchBox.Focus();
        }), DispatcherPriority.Render);
    }

    public void Close() => _palette.Visibility = Visibility.Collapsed;

    public void Filter(string filter)
    {
        Build(filter);
        if (IsOpen)
            PositionPalette();
    }

    public bool IsOpen => _palette.Visibility == Visibility.Visible;

    public bool IsPointInside(Point palettePoint)
    {
        return palettePoint.X >= 0 &&
               palettePoint.X <= _palette.ActualWidth &&
               palettePoint.Y >= 0 &&
               palettePoint.Y <= _palette.ActualHeight;
    }

    private void Build(string filter)
    {
        _content.Children.Clear();

        bool hasAny = false;
        bool isEventGraph = _getActiveGraphKind() == GraphAssetKind.EventGraph;
        var definitions = _nodeRegistry.Definitions
            .Where(def => !HiddenKinds.Contains(def.NodeKind))
            .Where(def => def.NodeKind != NodeKind.CustomEvent || isEventGraph)
            .Where(def => string.IsNullOrWhiteSpace(filter) ||
                          def.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(def => def.Category)
            .ThenBy(def => def.DisplayName)
            .ToList();

        foreach (var group in definitions.GroupBy(def => def.Category))
        {
            hasAny = true;
            AddGroupHeader(group.Key);
            foreach (var definition in group)
            {
                var button = CreateMenuButton(definition.DisplayName, definition.NodeKind);
                button.Click += NodeButton_Click;
                _content.Children.Add(button);
            }
        }

        hasAny |= AddAssetGroups(_getFunctions(), filter, isMacro: false);
        hasAny |= AddAssetGroups(_getMacros(), filter, isMacro: true);
        if (isEventGraph)
            hasAny |= AddCustomEventGroups(_getCustomEvents(), filter);

        if (!hasAny)
        {
            _content.Children.Add(new TextBlock
            {
                Text = "未找到匹配节点",
                Foreground = Brush(0x7A, 0x87, 0x97),
                Margin = new Thickness(12, 8, 12, 8),
                FontSize = 12,
            });
        }
    }

    private void PositionPalette()
    {
        const double margin = 8;
        const double fallbackWidth = 260;
        const double fallbackHeight = 500;

        var viewportSize = _getViewportSize();
        double viewportWidth = viewportSize.Width > 0 ? viewportSize.Width : fallbackWidth + margin * 2;
        double viewportHeight = viewportSize.Height > 0 ? viewportSize.Height : fallbackHeight + margin * 2;
        double maxHeight = Math.Max(140, Math.Min(fallbackHeight, viewportHeight - margin * 2));

        _palette.MaxHeight = maxHeight;
        _palette.Measure(new Size(fallbackWidth, maxHeight));

        double desiredWidth = _palette.DesiredSize.Width > 0 ? _palette.DesiredSize.Width : fallbackWidth;
        double desiredHeight = _palette.DesiredSize.Height > 0 ? _palette.DesiredSize.Height : maxHeight;
        desiredHeight = Math.Min(desiredHeight, maxHeight);

        double left = Clamp(_openViewportPoint.X, margin, Math.Max(margin, viewportWidth - desiredWidth - margin));
        double top = _openViewportPoint.Y;
        if (top + desiredHeight > viewportHeight - margin)
            top = viewportHeight - desiredHeight - margin;
        top = Clamp(top, margin, Math.Max(margin, viewportHeight - desiredHeight - margin));

        Canvas.SetLeft(_palette, left);
        Canvas.SetTop(_palette, top);
    }

    private bool AddAssetGroups(IEnumerable<CallableGraphItem> assets, string filter, bool isMacro)
    {
        var matched = assets
            .Where(asset => string.IsNullOrWhiteSpace(filter) ||
                            asset.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                            asset.GroupName.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(asset => asset.GroupName)
            .ThenBy(asset => asset.Name)
            .ToList();
        if (matched.Count == 0)
            return false;

        foreach (var group in matched.GroupBy(asset => asset.GroupName))
        {
            AddGroupHeader(group.Key);
            foreach (var asset in group)
            {
                var button = CreateMenuButton(asset.Name, new PaletteAsset(asset, isMacro));
                button.Click += AssetButton_Click;
                _content.Children.Add(button);
            }
        }

        return true;
    }

    private bool AddCustomEventGroups(IEnumerable<CallableCustomEventItem> events, string filter)
    {
        var matched = events
            .Where(item => string.IsNullOrWhiteSpace(filter) ||
                           item.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                           item.GroupName.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .GroupBy(item => item.Id)
            .Select(group => group.First())
            .OrderBy(item => item.GroupName)
            .ThenBy(item => item.Name)
            .ToList();
        if (matched.Count == 0)
            return false;

        foreach (var group in matched.GroupBy(item => item.GroupName))
        {
            AddGroupHeader(group.Key);
            foreach (var item in group)
            {
                var button = CreateMenuButton(item.Name, new PaletteCustomEvent(item));
                button.Click += CustomEventButton_Click;
                _content.Children.Add(button);
            }
        }

        return true;
    }

    private void AddGroupHeader(string text)
    {
        _content.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = System.Windows.Media.Brushes.White,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            Margin = new Thickness(12, 10, 12, 4),
        });
    }

    private static WpfButton CreateMenuButton(string text, object tag) => new()
    {
        Content = text,
        Background = System.Windows.Media.Brushes.Transparent,
        BorderThickness = new Thickness(0),
        Foreground = Brush(0xD0, 0xD7, 0xE2),
        FontSize = 13,
        Padding = new Thickness(12, 6, 12, 6),
        HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left,
        Cursor = System.Windows.Input.Cursors.Hand,
        Tag = tag,
    };

    private void NodeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton { Tag: NodeKind kind })
            return;

        var graphPoint = _viewportToGraph(_openViewportPoint);
        var node = _nodeFactory.CreateNode(kind, graphPoint.X, graphPoint.Y);
        _editorService.AddNode(node);
        _selectNode(node);
        Close();
    }

    private void CustomEventButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton { Tag: PaletteCustomEvent customEvent })
            return;

        _snapshotActiveAsset();
        var graphPoint = _viewportToGraph(_openViewportPoint);
        var node = _nodeFactory.CreateCustomEventCallNode(
            customEvent.Item.Id,
            customEvent.Item.Name,
            customEvent.Item.Parameters.Select(ToParameter),
            graphPoint.X,
            graphPoint.Y);
        _editorService.AddNode(node);
        _selectNode(node);
        Close();
    }

    private void AssetButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton { Tag: PaletteAsset asset })
            return;

        _snapshotActiveAsset();
        var graphPoint = _viewportToGraph(_openViewportPoint);
        NodeBaseViewModel node;
        if (asset.IsMacro)
        {
            node = _nodeFactory.CreateMacroCallNode(
                asset.Item.Id,
                asset.Item.Name,
                GetEntryParameters(asset.Item.Graph, NodeKind.MacroEntry),
                GetOutputParameters(asset.Item.Graph, NodeKind.MacroOutput),
                GetMacroExits(asset.Item.Graph),
                graphPoint.X,
                graphPoint.Y);
        }
        else
        {
            node = _nodeFactory.CreateFunctionCallNode(
                asset.Item.Id,
                asset.Item.Name,
                GetEntryParameters(asset.Item.Graph, NodeKind.FunctionEntry),
                GetOutputParameters(asset.Item.Graph, NodeKind.FunctionReturn),
                graphPoint.X,
                graphPoint.Y);
        }

        _editorService.AddNode(node);
        _selectNode(node);
        Close();
    }

    private static IEnumerable<GraphParameterDefinition> GetEntryParameters(GraphFileModel graph, NodeKind kind) =>
        graph.Nodes
            .FirstOrDefault(node => NodeKindFromTypeKey(node.NodeTypeKey) == kind)?
            .Parameters
            .Select(ToParameter)
        ?? [];

    private static IEnumerable<GraphParameterDefinition> GetOutputParameters(GraphFileModel graph, NodeKind kind)
    {
        var nodes = graph.Nodes.Where(node => NodeKindFromTypeKey(node.NodeTypeKey) == kind);
        return nodes.SelectMany(node => node.Parameters.Select(ToParameter));
    }

    private static IEnumerable<(string Id, string Name)> GetMacroExits(GraphFileModel graph) =>
        graph.Nodes
            .Where(node => node.NodeTypeKey == "macro_output")
            .Select(node => (node.Id, string.IsNullOrWhiteSpace(node.ExitName) ? "完成" : node.ExitName!));

    private static GraphParameterDefinition ToParameter(GraphParameterFileModel file) => new()
    {
        Id = file.Id,
        Name = file.Name,
        Type = file.Type,
    };

    private static NodeKind? NodeKindFromTypeKey(string typeKey) => typeKey switch
    {
        "function_entry" => NodeKind.FunctionEntry,
        "function_return" => NodeKind.FunctionReturn,
        "macro_entry" => NodeKind.MacroEntry,
        "macro_output" => NodeKind.MacroOutput,
        _ => null,
    };

    private static SolidColorBrush Brush(byte r, byte g, byte b) => new(System.Windows.Media.Color.FromRgb(r, g, b));

    private static double Clamp(double value, double min, double max) => Math.Min(Math.Max(value, min), max);

    private sealed record PaletteAsset(CallableGraphItem Item, bool IsMacro);

    private sealed record PaletteCustomEvent(CallableCustomEventItem Item);
}
