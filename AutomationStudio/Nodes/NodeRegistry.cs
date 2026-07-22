using AutomationStudioWpf.Graph;
using AutomationStudioWpf.GraphCore;
using AutomationStudioWpf.Interaction;
using AutomationStudioWpf.Nodes.Common;
using AutomationStudioWpf.Nodes.Core;
using AutomationStudioWpf.Nodes.Debug;
using AutomationStudioWpf.Nodes.Input.Keyboard;
using AutomationStudioWpf.Nodes.Input.Mouse;
using AutomationStudioWpf.Nodes.Plugins.ImageRecognition;
using AutomationStudioWpf.Nodes.System.Window;
using AutomationStudioWpf.Runtime;

namespace AutomationStudioWpf.Nodes;

public sealed class NodeRegistry
{
    private readonly Dictionary<NodeKind, INodeExecutor> _executors;
    private readonly Dictionary<NodeKind, INodeDefinition> _definitions;

    private NodeRegistry(IEnumerable<INodeExecutor> executors, IEnumerable<INodeDefinition> definitions)
    {
        _executors = executors.ToDictionary(e => e.NodeKind);
        _definitions = definitions.ToDictionary(d => d.NodeKind);
    }

    public IReadOnlyCollection<INodeDefinition> Definitions => _definitions.Values;

    public static NodeRegistry CreateDefault()
    {
        return new NodeRegistry(
        [
            new DelayNodeExecutor(),
            new MouseClickNodeExecutor(),
            new MouseMoveNodeExecutor(),
            new ScrollWheelNodeExecutor(),
            new KeyboardNodeExecutor(),
            new KeyChordNodeExecutor(),
            new StartProgramNodeExecutor(),
            new SelectWindowNodeExecutor(),
            new PrintLogNodeExecutor(),
            new FindImageNodeExecutor(),
            new CommonNodeExecutor(NodeKind.GetMousePosition),
            new CommonNodeExecutor(NodeKind.WaitImage),
            new CommonNodeExecutor(NodeKind.WaitImageDisappear),
            new CommonNodeExecutor(NodeKind.WaitWindow),
            new CommonNodeExecutor(NodeKind.CloseWindow),
            new CommonNodeExecutor(NodeKind.WindowExists),
            new CommonNodeExecutor(NodeKind.GetForegroundWindow),
            new CommonNodeExecutor(NodeKind.SaveScreenshot),
            new CommonNodeExecutor(NodeKind.ShowMessage),
        ],
        CreateDefaultDefinitions());
    }

    public bool TryGetExecutor(NodeKind kind, out INodeExecutor executor) => _executors.TryGetValue(kind, out executor!);

    public bool TryGetDefinition(NodeKind kind, out INodeDefinition definition) => _definitions.TryGetValue(kind, out definition!);

    private static IReadOnlyList<INodeDefinition> CreateDefaultDefinitions()
    {
        return NodeDescriptorCatalog.All
            .Select(descriptor =>
            {
                NodePresentationDescriptor presentation = NodePresentationCatalog.Get(descriptor.Kind);
                IReadOnlyList<NodePinDefinition> pins = descriptor.Pins
                    .Select(pin => new NodePinDefinition(pin.Name, pin.Label, pin.Kind, pin.Direction))
                    .ToList();
                return (INodeDefinition)new NodeDefinition(
                    descriptor.Kind,
                    descriptor.TypeKey,
                    presentation.DisplayName,
                    presentation.Category,
                    pins,
                    BuildSearchTags(descriptor.Kind, descriptor.TypeKey, presentation.Category, pins),
                    InspectorSchemaKey(presentation.InspectorKind),
                    traits: descriptor.Traits,
                    canCreate: descriptor.CanCreate,
                    canDelete: descriptor.CanDelete,
                    runtimeSupport: descriptor.RuntimeSupport,
                    previewSupport: descriptor.PreviewSupport,
                    hasSerializer: descriptor.HasSerializer);
            })
            .ToList();
    }

    private static IReadOnlyList<string> BuildSearchTags(NodeKind kind, string typeKey, string category, IReadOnlyList<NodePinDefinition> pins)
    {
        return
        [
            kind.ToString(),
            typeKey,
            .. typeKey.Split('_', StringSplitOptions.RemoveEmptyEntries),
            category,
            .. category.Split('/', StringSplitOptions.RemoveEmptyEntries),
            .. pins.Select(pin => pin.Name),
            .. pins.Select(pin => pin.Label),
        ];
    }

    private static string InspectorSchemaKey(NodeInspectorKind kind) => kind switch
    {
        NodeInspectorKind.FindImage => "find_image",
        NodeInspectorKind.MouseClick => "mouse_click",
        NodeInspectorKind.MouseMove => "mouse_move",
        NodeInspectorKind.Keyboard => "keyboard",
        NodeInspectorKind.ScrollWheel => "scroll_wheel",
        NodeInspectorKind.Delay => "delay",
        NodeInspectorKind.If => "if",
        NodeInspectorKind.ForLoop => "for_loop",
        NodeInspectorKind.WhileLoop => "while_loop",
        NodeInspectorKind.ToDo => "todo",
        NodeInspectorKind.StartProgram => "start_program",
        NodeInspectorKind.SelectWindow => "select_window",
        NodeInspectorKind.ParameterizedEntry => "parameterized_entry",
        NodeInspectorKind.Callable => "callable",
        _ => "common",
    };
}
