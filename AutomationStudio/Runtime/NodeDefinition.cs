using AutomationStudioWpf.Graph;
using AutomationStudioWpf.GraphCore;

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

    NodeTraitFlags Traits { get; }

    bool CanCreate { get; }

    bool CanDelete { get; }

    NodeRuntimeSupport RuntimeSupport { get; }

    NodePreviewSupport PreviewSupport { get; }

    bool HasSerializer { get; }
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
        IReadOnlyDictionary<string, string>? validationHints = null,
        NodeTraitFlags traits = NodeTraitFlags.None,
        bool canCreate = true,
        bool canDelete = true,
        NodeRuntimeSupport runtimeSupport = NodeRuntimeSupport.Unsupported,
        NodePreviewSupport previewSupport = NodePreviewSupport.ExplicitFallback,
        bool hasSerializer = true)
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
        Traits = traits;
        CanCreate = canCreate;
        CanDelete = canDelete;
        RuntimeSupport = runtimeSupport;
        PreviewSupport = previewSupport;
        HasSerializer = hasSerializer;
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

    public NodeTraitFlags Traits { get; init; }

    public bool CanCreate { get; init; }

    public bool CanDelete { get; init; }

    public NodeRuntimeSupport RuntimeSupport { get; init; }

    public NodePreviewSupport PreviewSupport { get; init; }

    public bool HasSerializer { get; init; }
}
