using System.Drawing;
using System.Threading;
using AutomationStudioWpf.Graph;

namespace AutomationStudioWpf.Runtime;

/// <summary>
/// Runtime value bag for node outputs and connected input resolution.
/// Keeps key formatting centralized instead of scattering "{node}:{pin}" strings.
/// </summary>
public sealed class RuntimeContext : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, object> _values = [];
    private readonly ThreadLocal<HashSet<string>> _activePureNodes = new(() => new HashSet<string>(StringComparer.Ordinal));
    private readonly ThreadLocal<HashSet<string>> _activeResolvedConnections = new(() => new HashSet<string>(StringComparer.Ordinal));
    private bool _disposed;

    internal Func<GraphExecutionPlan, GraphRuntimeNode, RuntimeContext, bool>? PureNodeResolver { get; set; }

    public void Set(string nodeId, string pinName, object value)
    {
        lock (_gate)
        {
            _values[MakeKey(nodeId, pinName)] = value;
        }
    }

    public bool TryGetRaw(string nodeId, string pinName, out object value)
    {
        lock (_gate)
        {
            return _values.TryGetValue(MakeKey(nodeId, pinName), out value!);
        }
    }

    public bool TryGet<T>(string nodeId, string pinName, out T value)
    {
        lock (_gate)
        {
            if (_values.TryGetValue(MakeKey(nodeId, pinName), out object? raw) && raw is T typed)
            {
                value = typed;
                return true;
            }
        }

        value = default!;
        return false;
    }

    public IReadOnlyDictionary<string, object> GetNodeOutputs(string nodeId)
    {
        string prefix = $"{nodeId}:";
        lock (_gate)
        {
            return _values
                .Where(pair => pair.Key.StartsWith(prefix, StringComparison.Ordinal))
                .ToDictionary(
                    pair => pair.Key[prefix.Length..],
                    pair => pair.Value,
                    StringComparer.Ordinal);
        }
    }

    public bool HasInputConnection(GraphExecutionPlan plan, GraphRuntimeNode node, string pinName)
    {
        return plan.Index.HasInputConnection(node.Id, pinName);
    }

    public bool TryResolveInputRaw(
        GraphExecutionPlan plan,
        GraphRuntimeNode node,
        string pinName,
        out object value,
        out bool hasConnection)
    {
        GraphRuntimeConnection? connection = plan.Index.GetInputConnection(node.Id, pinName);
        hasConnection = connection is not null;
        if (connection is not null && TryResolveConnectionRaw(plan, connection, out value))
            return true;

        value = default!;
        return false;
    }

    public bool TryResolveInputRaw(
        GraphExecutionPlan plan,
        GraphRuntimeNode node,
        string pinName,
        PinKind sourceKind,
        out object value,
        out bool hasConnection)
    {
        GraphRuntimeConnection? connection = FindInputConnection(plan, node, pinName, sourceKind);
        hasConnection = connection is not null;
        if (connection is not null && TryResolveConnectionRaw(plan, connection, out value))
            return true;

        value = default!;
        return false;
    }

    public bool TryResolveConnectionRaw(GraphExecutionPlan plan, GraphRuntimeConnection connection, out object value)
    {
        if (TryResolveRerouteConnectionRaw(plan, connection, out value))
            return true;

        EnsurePureSourceEvaluated(plan, connection);
        return TryGetRaw(connection.SourceNodeId, connection.SourcePinName, out value);
    }

    private bool TryResolveRerouteConnectionRaw(GraphExecutionPlan plan, GraphRuntimeConnection connection, out object value)
    {
        value = default!;
        if (plan.Index.GetNode(connection.SourceNodeId) is not { NodeKind: NodeKind.Reroute } rerouteNode ||
            rerouteNode.RoutedKind == PinKind.Execution)
            return false;

        string guardKey = $"{connection.SourceNodeId}.{connection.SourcePinName}->{connection.TargetNodeId}.{connection.TargetPinName}";
        HashSet<string> activeConnections = _activeResolvedConnections.Value!;
        if (!activeConnections.Add(guardKey))
            throw new PureNodeEvaluationException($"数据转接点存在环路：{rerouteNode.Title}。");

        try
        {
            GraphRuntimeConnection? incoming = plan.Index.GetInputConnection(rerouteNode.Id, "in");
            if (incoming is null)
                incoming = plan.Index.GetNonExecutionInputs(rerouteNode.Id).FirstOrDefault();

            return incoming is not null && TryResolveConnectionRaw(plan, incoming, out value);
        }
        finally
        {
            activeConnections.Remove(guardKey);
        }
    }

    public bool TryResolvePointInput(GraphExecutionPlan plan, GraphRuntimeNode node, string pinName, out Point point, out bool hasConnection)
    {
        if (TryResolveInputRaw(plan, node, pinName, PinKind.Vector2D, out object raw, out hasConnection) && raw is Point typedPoint)
        {
            point = typedPoint;
            return true;
        }

        point = default;
        return false;
    }

    public bool TryResolveBoolInput(GraphExecutionPlan plan, GraphRuntimeNode node, string pinName, out bool value)
    {
        if (TryResolveInputRaw(plan, node, pinName, PinKind.Boolean, out object raw, out _) && raw is bool typedValue)
        {
            value = typedValue;
            return true;
        }

        value = default;
        return false;
    }

    public bool TryResolveBoolInput(GraphExecutionPlan plan, GraphRuntimeNode node, string pinName, out bool value, out bool hasConnection)
    {
        if (TryResolveInputRaw(plan, node, pinName, PinKind.Boolean, out object raw, out hasConnection) && raw is bool typedValue)
        {
            value = typedValue;
            return true;
        }

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
        if (TryResolveInputRaw(plan, node, pinName, out object raw, out hasConnection))
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
        return plan.Index.GetInputConnection(node.Id, pinName, sourceKind);
    }

    private void EnsurePureSourceEvaluated(GraphExecutionPlan plan, GraphRuntimeConnection? connection)
    {
        if (connection is null || TryGetRaw(connection.SourceNodeId, connection.SourcePinName, out _))
            return;
        if (PureNodeResolver is null || plan.Index.GetNode(connection.SourceNodeId) is not { } sourceNode)
            return;
        if (!NodeTraits.IsPure(sourceNode.NodeKind))
            return;
        HashSet<string> activePureNodes = _activePureNodes.Value!;
        if (!activePureNodes.Add(sourceNode.Id))
            throw new PureNodeEvaluationException($"纯运算节点存在数据环路：{sourceNode.Title}。");

        try
        {
            if (!PureNodeResolver(plan, sourceNode, this))
                throw new PureNodeEvaluationException($"纯运算节点求值失败：{sourceNode.Title}。");
        }
        finally
        {
            activePureNodes.Remove(sourceNode.Id);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _activePureNodes.Dispose();
        _activeResolvedConnections.Dispose();
    }

    private static string MakeKey(string nodeId, string pinName) => $"{nodeId}:{pinName}";
}
