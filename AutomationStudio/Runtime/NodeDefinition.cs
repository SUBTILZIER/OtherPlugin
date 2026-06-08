using AutomationStudioWpf.Graph;

namespace AutomationStudioWpf.Runtime;

public sealed record NodePinDefinition(string Name, string Label, PinKind Kind, PinDirection Direction);

public interface INodeDefinition
{
    NodeKind NodeKind { get; }

    string TypeKey { get; }

    string DisplayName { get; }

    string Category { get; }

    IReadOnlyList<NodePinDefinition> Pins { get; }

    IReadOnlyList<string> SearchTags { get; }

    string InspectorSchemaKey { get; }

    IReadOnlyDictionary<string, string> DefaultValues { get; }

    IReadOnlyDictionary<string, string> ValidationHints { get; }
}

public sealed record NodeDefinition : INodeDefinition
{
    public NodeDefinition(
        NodeKind nodeKind,
        string typeKey,
        string displayName,
        string category,
        IReadOnlyList<NodePinDefinition> pins,
        IReadOnlyList<string>? searchTags = null,
        string inspectorSchemaKey = "default",
        IReadOnlyDictionary<string, string>? defaultValues = null,
        IReadOnlyDictionary<string, string>? validationHints = null)
    {
        NodeKind = nodeKind;
        TypeKey = typeKey;
        DisplayName = displayName;
        Category = category;
        Pins = pins;
        SearchTags = searchTags ?? [];
        InspectorSchemaKey = inspectorSchemaKey;
        DefaultValues = defaultValues ?? new Dictionary<string, string>();
        ValidationHints = validationHints ?? new Dictionary<string, string>();
    }

    public NodeKind NodeKind { get; init; }

    public string TypeKey { get; init; }

    public string DisplayName { get; init; }

    public string Category { get; init; }

    public IReadOnlyList<NodePinDefinition> Pins { get; init; }

    public IReadOnlyList<string> SearchTags { get; init; }

    public string InspectorSchemaKey { get; init; }

    public IReadOnlyDictionary<string, string> DefaultValues { get; init; }

    public IReadOnlyDictionary<string, string> ValidationHints { get; init; }
}
