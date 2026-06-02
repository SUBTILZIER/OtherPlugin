using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Nodes;
using AutomationStudioWpf.Services;
using Point = System.Windows.Point;
using WpfButton = System.Windows.Controls.Button;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace AutomationStudioWpf.Interaction;

public sealed class NodePaletteController
{
    private static readonly HashSet<NodeKind> HiddenKinds = [NodeKind.Start, NodeKind.Reroute];

    private readonly Border _palette;
    private readonly WpfTextBox _searchBox;
    private readonly StackPanel _content;
    private readonly NodeFactory _nodeFactory;
    private readonly GraphEditorService _editorService;
    private readonly NodeRegistry _nodeRegistry;
    private readonly Func<Point, Point> _viewportToGraph;
    private readonly Action<NodeBaseViewModel> _selectNode;

    private Point _openViewportPoint;

    public NodePaletteController(
        Border palette,
        WpfTextBox searchBox,
        StackPanel content,
        NodeFactory nodeFactory,
        GraphEditorService editorService,
        NodeRegistry nodeRegistry,
        Func<Point, Point> viewportToGraph,
        Action<NodeBaseViewModel> selectNode)
    {
        _palette = palette;
        _searchBox = searchBox;
        _content = content;
        _nodeFactory = nodeFactory;
        _editorService = editorService;
        _nodeRegistry = nodeRegistry;
        _viewportToGraph = viewportToGraph;
        _selectNode = selectNode;
    }

    public void Open(Point viewportPoint)
    {
        _openViewportPoint = viewportPoint;
        _searchBox.Text = string.Empty;
        Build(string.Empty);

        Canvas.SetLeft(_palette, viewportPoint.X);
        Canvas.SetTop(_palette, viewportPoint.Y);
        _palette.Visibility = Visibility.Visible;

        _palette.Dispatcher.BeginInvoke(new Action(() => _searchBox.Focus()), DispatcherPriority.Render);
    }

    public void Close() => _palette.Visibility = Visibility.Collapsed;

    public void Filter(string filter) => Build(filter);

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

        var definitions = _nodeRegistry.Definitions
            .Where(def => !HiddenKinds.Contains(def.NodeKind))
            .Where(def => string.IsNullOrWhiteSpace(filter) ||
                          def.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(def => def.Category)
            .ThenBy(def => def.DisplayName)
            .ToList();

        if (definitions.Count == 0)
        {
            _content.Children.Add(new TextBlock
            {
                Text = "未找到匹配节点",
                Foreground = Brush(0x7A, 0x87, 0x97),
                Margin = new Thickness(12, 8, 12, 8),
                FontSize = 12,
            });
            return;
        }

        foreach (var group in definitions.GroupBy(def => def.Category))
        {
            _content.Children.Add(new TextBlock
            {
                Text = group.Key,
                Foreground = System.Windows.Media.Brushes.White,
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                Margin = new Thickness(12, 10, 12, 4),
            });

            foreach (var definition in group)
            {
                var button = new WpfButton
                {
                    Content = definition.DisplayName,
                    Background = System.Windows.Media.Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Foreground = Brush(0xD0, 0xD7, 0xE2),
                    FontSize = 13,
                    Padding = new Thickness(12, 6, 12, 6),
                    HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Tag = definition.NodeKind,
                };
                button.Click += NodeButton_Click;
                _content.Children.Add(button);
            }
        }
    }

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

    private static SolidColorBrush Brush(byte r, byte g, byte b) => new(System.Windows.Media.Color.FromRgb(r, g, b));
}
