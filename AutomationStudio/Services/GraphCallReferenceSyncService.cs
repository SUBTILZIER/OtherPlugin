using AutomationStudioWpf.Graph;

namespace AutomationStudioWpf.Services;

public sealed class GraphCallReferenceSyncResult
{
    public int UpdatedCallNodes { get; set; }
    public int RemovedConnections { get; set; }
    public HashSet<string> ChangedAssetIds { get; } = [];
}

public sealed class GraphCallReferenceSyncService
{
    private readonly CallableGraphResolver _callableResolver;

    public GraphCallReferenceSyncService(CallableGraphResolver callableResolver)
    {
        _callableResolver = callableResolver;
    }

    public GraphCallReferenceSyncResult Sync(IEnumerable<ContentAssetViewModel> assets)
    {
        var assetList = assets.Where(asset => asset.Kind != ContentAssetKind.Folder).ToList();

        var result = new GraphCallReferenceSyncResult();
        foreach (var asset in assetList)
        {
            var functions = _callableResolver.ResolveFunctions(assetList, asset)
                .ToDictionary(item => item.Id, StringComparer.Ordinal);
            var macros = _callableResolver.ResolveMacros(assetList, asset)
                .ToDictionary(item => item.Id, StringComparer.Ordinal);

            foreach (var graph in asset.EventGraphs.Concat(asset.Functions).Concat(asset.Macros).Select(item => item.Graph))
            {
                int updated = SyncGraph(graph, functions, macros, out int removed);
                result.UpdatedCallNodes += updated;
                result.RemovedConnections += removed;
                if (updated > 0 || removed > 0)
                    result.ChangedAssetIds.Add(asset.Id);
            }
        }

        return result;
    }

    private static int SyncGraph(
        GraphFileModel graph,
        IReadOnlyDictionary<string, CallableGraphItem> functions,
        IReadOnlyDictionary<string, CallableGraphItem> macros,
        out int removedConnections)
    {
        int updated = 0;
        var invalidPins = new HashSet<(string NodeId, string PinName)>();
        var customEvents = graph.Nodes
            .Where(node => node.NodeTypeKey == "custom_event")
            .Select(node => new
            {
                Id = string.IsNullOrWhiteSpace(node.CustomEventId) ? node.Id : node.CustomEventId!,
                Node = node,
            })
            .GroupBy(item => item.Id)
            .ToDictionary(group => group.Key, group => group.First().Node);

        foreach (var node in graph.Nodes)
        {
            if (node.NodeTypeKey == "function_call" && !string.IsNullOrWhiteSpace(node.FunctionId) &&
                functions.TryGetValue(node.FunctionId, out var function))
            {
                var inputs = GetParameterFiles(function.Graph, "function_entry");
                var outputs = GetParameterFiles(function.Graph, "function_return");
                updated += ReplaceParameters(node, inputs, outputs, invalidPins);
                updated += ReplaceTitle(node, function.Name);
            }
            else if (node.NodeTypeKey == "macro_call" && !string.IsNullOrWhiteSpace(node.MacroId) &&
                     macros.TryGetValue(node.MacroId, out var macro))
            {
                var inputs = GetParameterFiles(macro.Graph, "macro_entry");
                var outputs = GetParameterFiles(macro.Graph, "macro_output");
                var exits = macro.Graph.Nodes
                    .Where(n => n.NodeTypeKey == "macro_output")
                    .Select(n => new MacroExitFileModel
                    {
                        Id = string.IsNullOrWhiteSpace(n.Id) ? Guid.NewGuid().ToString("N") : n.Id,
                        Name = string.IsNullOrWhiteSpace(n.ExitName) ? "完成" : n.ExitName!,
                    })
                    .ToList();

                updated += ReplaceParameters(node, inputs, outputs, invalidPins);
                if (!SameMacroExits(node.MacroExits, exits))
                {
                    foreach (var oldExit in node.MacroExits)
                        invalidPins.Add((node.Id, $"exec_{oldExit.Id}"));
                    node.MacroExits = exits;
                    updated++;
                }
                updated += ReplaceTitle(node, macro.Name);
            }
            else if (node.NodeTypeKey == "custom_event_call" && !string.IsNullOrWhiteSpace(node.CustomEventId) &&
                     customEvents.TryGetValue(node.CustomEventId, out var customEvent))
            {
                var inputs = customEvent.Parameters.Select(CloneParameter).ToList();
                updated += ReplaceParameters(node, inputs, [], invalidPins);
                updated += ReplaceTitle(node, string.IsNullOrWhiteSpace(customEvent.Title) ? "自定义事件" : customEvent.Title);
            }
        }

        int before = graph.Connections.Count;
        graph.Connections = graph.Connections
            .Where(conn => !invalidPins.Contains((conn.SourceNodeId, conn.SourcePinName)) &&
                           !invalidPins.Contains((conn.TargetNodeId, conn.TargetPinName)))
            .ToList();
        removedConnections = before - graph.Connections.Count;
        return updated;
    }

    private static int ReplaceTitle(NodeFileModel node, string title)
    {
        if (string.Equals(node.Title, title, StringComparison.Ordinal))
            return 0;

        node.Title = title;
        return 1;
    }

    private static int ReplaceParameters(
        NodeFileModel node,
        List<GraphParameterFileModel> inputs,
        List<GraphParameterFileModel> outputs,
        HashSet<(string NodeId, string PinName)> invalidPins)
    {
        int changed = 0;
        changed += ReplaceParameterSide(node, node.InputParameters, inputs, invalidPins, preserveDefaultValue: true);
        changed += ReplaceParameterSide(node, node.OutputParameters, outputs, invalidPins, preserveDefaultValue: false);

        return changed;
    }

    private static int ReplaceParameterSide(
        NodeFileModel node,
        List<GraphParameterFileModel> current,
        List<GraphParameterFileModel> signature,
        HashSet<(string NodeId, string PinName)> invalidPins,
        bool preserveDefaultValue)
    {
        var merged = MergeParameters(current, signature, preserveDefaultValue);
        MarkInvalidPins(node, current, signature, invalidPins);
        if (SameParameters(current, merged))
            return 0;

        current.Clear();
        current.AddRange(merged);
        return 1;
    }

    private static List<GraphParameterFileModel> MergeParameters(
        IReadOnlyList<GraphParameterFileModel> current,
        IReadOnlyList<GraphParameterFileModel> signature,
        bool preserveDefaultValue)
    {
        var currentById = current.ToDictionary(parameter => parameter.Id, StringComparer.Ordinal);
        var merged = new List<GraphParameterFileModel>();
        foreach (var parameter in signature)
        {
            var next = CloneParameter(parameter);
            if (preserveDefaultValue &&
                currentById.TryGetValue(parameter.Id, out var old) &&
                old.Type == parameter.Type)
            {
                next.DefaultValue = old.DefaultValue;
            }

            merged.Add(next);
        }

        return merged;
    }

    private static void MarkInvalidPins(
        NodeFileModel node,
        IReadOnlyList<GraphParameterFileModel> current,
        IReadOnlyList<GraphParameterFileModel> signature,
        HashSet<(string NodeId, string PinName)> invalidPins)
    {
        var nextById = signature.ToDictionary(parameter => parameter.Id, StringComparer.Ordinal);
        foreach (var old in current)
        {
            if (!nextById.TryGetValue(old.Id, out var next) || ToPinKind(old.Type) != ToPinKind(next.Type))
                invalidPins.Add((node.Id, old.Id));
        }
    }

    private static List<GraphParameterFileModel> GetParameterFiles(GraphFileModel graph, string nodeTypeKey) =>
        graph.Nodes
            .Where(node => node.NodeTypeKey == nodeTypeKey)
            .SelectMany(node => node.Parameters)
            .Select(CloneParameter)
            .ToList();

    private static GraphParameterFileModel CloneParameter(GraphParameterFileModel parameter) => new()
    {
        Id = parameter.Id,
        Name = parameter.Name,
        Type = parameter.Type,
        DefaultValue = parameter.DefaultValue,
    };

    private static bool SameParameters(IReadOnlyList<GraphParameterFileModel> left, IReadOnlyList<GraphParameterFileModel> right) =>
        left.Count == right.Count &&
        left.Zip(right).All(pair => pair.First.Id == pair.Second.Id &&
                                    pair.First.Name == pair.Second.Name &&
                                    pair.First.Type == pair.Second.Type &&
                                    pair.First.DefaultValue == pair.Second.DefaultValue);

    private static PinKind ToPinKind(GraphParameterType type) => type switch
    {
        GraphParameterType.Boolean => PinKind.Boolean,
        GraphParameterType.Vector2D => PinKind.Vector2D,
        _ => PinKind.String,
    };

    private static bool SameMacroExits(IReadOnlyList<MacroExitFileModel> left, IReadOnlyList<MacroExitFileModel> right) =>
        left.Count == right.Count &&
        left.Zip(right).All(pair => pair.First.Id == pair.Second.Id && pair.First.Name == pair.Second.Name);
}
