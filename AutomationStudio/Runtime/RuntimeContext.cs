using System.Drawing;
using AutomationStudioWpf.Graph;

namespace AutomationStudioWpf.Runtime;

/// <summary>
/// Runtime value bag for node outputs and connected input resolution.
/// Keeps key formatting centralized instead of scattering "{node}:{pin}" strings.
/// </summary>
public sealed class RuntimeContext
{
    private readonly Dictionary<string, object> _values = [];

    public void Set(string nodeId, string pinName, object value) => _values[MakeKey(nodeId, pinName)] = value;

    public bool TryGet<T>(string nodeId, string pinName, out T value)
    {
        if (_values.TryGetValue(MakeKey(nodeId, pinName), out object? raw) && raw is T typed)
        {
            value = typed;
            return true;
        }

        value = default!;
        return false;
    }

    public bool HasInputConnection(GraphExecutionPlan plan, GraphRuntimeNode node, string pinName)
    {
        return plan.Connections.Any(c => c.TargetNodeId == node.Id && c.TargetPinName == pinName);
    }

    public bool TryResolvePointInput(GraphExecutionPlan plan, GraphRuntimeNode node, string pinName, out Point point, out bool hasConnection)
    {
        GraphRuntimeConnection? connection = FindInputConnection(plan, node, pinName, PinKind.Vector2D);
        hasConnection = connection is not null;
        if (connection is not null && TryGet(connection.SourceNodeId, connection.SourcePinName, out point))
            return true;

        point = default;
        return false;
    }

    public bool TryResolveBoolInput(GraphExecutionPlan plan, GraphRuntimeNode node, string pinName, out bool value)
    {
        GraphRuntimeConnection? connection = FindInputConnection(plan, node, pinName, PinKind.Boolean);
        if (connection is not null && TryGet(connection.SourceNodeId, connection.SourcePinName, out value))
            return true;

        value = default;
        return false;
    }

    public string ResolveStringInput(GraphExecutionPlan plan, GraphRuntimeNode node, string pinName, string? fallback)
    {
        if (TryResolveStringInput(plan, node, pinName, out string value, out bool hasConnection))
            return value;

        if (hasConnection)
            return string.Empty;

        return fallback ?? string.Empty;
    }

    public bool TryResolveStringInput(GraphExecutionPlan plan, GraphRuntimeNode node, string pinName, out string value, out bool hasConnection)
    {
        GraphRuntimeConnection? connection = plan.Connections.FirstOrDefault(c =>
            c.TargetNodeId == node.Id &&
            c.TargetPinName == pinName);

        hasConnection = connection is not null;
        if (connection is not null && _values.TryGetValue(MakeKey(connection.SourceNodeId, connection.SourcePinName), out object? raw))
        {
            value = FormatValue(raw);
            return true;
        }

        value = string.Empty;
        return false;
    }

    public static string FormatValue(object? value) => value switch
    {
        null => "null",
        bool b => b ? "True" : "False",
        Point p => $"({p.X}, {p.Y})",
        _ => value.ToString() ?? string.Empty,
    };

    private static GraphRuntimeConnection? FindInputConnection(
        GraphExecutionPlan plan,
        GraphRuntimeNode node,
        string pinName,
        PinKind sourceKind)
    {
        return plan.Connections.FirstOrDefault(c =>
            c.TargetNodeId == node.Id &&
            c.TargetPinName == pinName &&
            c.SourcePinKind == sourceKind);
    }

    private static string MakeKey(string nodeId, string pinName) => $"{nodeId}:{pinName}";
}
