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
}

public sealed record NodeDefinition(
    NodeKind NodeKind,
    string TypeKey,
    string DisplayName,
    string Category,
    IReadOnlyList<NodePinDefinition> Pins) : INodeDefinition;
