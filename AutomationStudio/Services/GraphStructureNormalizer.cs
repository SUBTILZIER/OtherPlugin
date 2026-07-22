using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Logging;

namespace AutomationStudioWpf.Services;

internal static class GraphStructureNormalizer
{
    public static bool NormalizeContentAsset(ContentAssetViewModel asset)
    {
        if (asset.Kind == ContentAssetKind.Folder)
            return false;

        bool changed = false;
        if (asset.Kind == ContentAssetKind.Script && asset.EventGraphs.Count == 0)
        {
            asset.EventGraphs.Add(CreateMainEventGraph("事件图1"));
            changed = true;
            Logger.Warn($"脚本“{asset.Name}”没有事件图，已创建主事件图。");
        }

        if (asset.Kind == ContentAssetKind.Script)
        {
            GraphLibraryMapper.NormalizeEventGraphRoles(asset.EventGraphs);
            foreach (var item in asset.EventGraphs)
                changed |= NormalizeGraphItem(item, asset.Name);
            changed |= NormalizeCustomEventIds(asset);
        }

        foreach (var item in asset.Functions)
            changed |= NormalizeGraphItem(item, asset.Name);

        if (changed)
            asset.IsDirty = true;
        return changed;
    }

    private static bool NormalizeCustomEventIds(ContentAssetViewModel asset)
    {
        bool changed = false;
        var eventNodes = asset.EventGraphs
            .SelectMany(graph => graph.Graph.Nodes
                .Where(node => IsType(node, "custom_event"))
                .Select(node => (Graph: graph, Node: node)))
            .ToList();
        var callNodes = asset.EventGraphs
            .SelectMany(graph => graph.Graph.Nodes
                .Where(node => IsType(node, "custom_event_call"))
                .Select(node => (Graph: graph, Node: node)))
            .ToList();

        foreach (var (graphItem, eventNode) in eventNodes.Where(item => string.IsNullOrWhiteSpace(item.Node.CustomEventId)))
        {
            string oldNodeId = eventNode.Id;
            string newId = Guid.NewGuid().ToString("N");
            eventNode.CustomEventId = newId;
            graphItem.IsDirty = true;
            graphItem.IsCompileDirty = true;
            changed = true;

            if (string.IsNullOrWhiteSpace(oldNodeId))
                continue;
            foreach (var (callGraph, callNode) in callNodes.Where(item =>
                         string.Equals(item.Node.CustomEventId, oldNodeId, StringComparison.Ordinal)))
            {
                callNode.CustomEventId = newId;
                callGraph.IsDirty = true;
                callGraph.IsCompileDirty = true;
            }
        }

        if (changed)
            Logger.Warn($"脚本“{asset.Name}”的空自定义事件 ID 已生成，明确引用已同步。");

        foreach (var duplicate in eventNodes
                     .Where(item => !string.IsNullOrWhiteSpace(item.Node.CustomEventId))
                     .GroupBy(item => item.Node.CustomEventId!, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            Logger.Warn($"脚本“{asset.Name}”存在重复自定义事件 ID：{duplicate.Key}。编译将阻止，不自动重定向调用。");
        }
        return changed;
    }

    public static bool NormalizeGraphItem(GraphListItemViewModel item, string ownerName)
    {
        bool changed = item.Kind switch
        {
            GraphAssetKind.EventGraph => NormalizeEventGraph(item.Graph, item.EntryRole, ownerName, item.Name),
            GraphAssetKind.Function => NormalizeFunctionGraph(item.Graph, ownerName, item.Name),
            _ => false,
        };

        if (changed)
        {
            item.IsDirty = true;
            item.IsCompileDirty = true;
        }
        return changed;
    }

    public static GraphListItemViewModel CreateMainEventGraph(string name)
    {
        var graph = new GraphFileModel
        {
            Name = name,
            AssetKind = GraphAssetKind.EventGraph,
            EntryRole = GraphEntryRole.MainEvent,
            Nodes = [CreateStartNode("node_001")],
        };
        return new GraphListItemViewModel
        {
            Kind = GraphAssetKind.EventGraph,
            Name = name,
            EntryRole = GraphEntryRole.MainEvent,
            Graph = graph,
            IsDirty = true,
            IsCompileDirty = true,
        };
    }

    public static GraphListItemViewModel CreateFunctionGraph(string name)
    {
        var graph = new GraphFileModel
        {
            Name = name,
            AssetKind = GraphAssetKind.Function,
            EntryRole = null,
        };
        var entry = CreateFunctionBoundary(graph, "function_entry", $"{name}开始", 80, "Fun001");
        graph.Nodes.Add(entry);
        var ret = CreateFunctionBoundary(graph, "function_return", "函数返回", 420, "Fun002");
        graph.Nodes.Add(ret);
        graph.Connections.Add(new ConnectionFileModel
        {
            SourceNodeId = entry.Id,
            SourcePinName = "exec_out",
            TargetNodeId = ret.Id,
            TargetPinName = "exec_in",
        });

        return new GraphListItemViewModel
        {
            Kind = GraphAssetKind.Function,
            Name = name,
            Graph = graph,
            IsDirty = true,
            IsCompileDirty = true,
        };
    }

    private static bool NormalizeEventGraph(GraphFileModel graph, GraphEntryRole role, string ownerName, string graphName)
    {
        graph.AssetKind = GraphAssetKind.EventGraph;
        graph.EntryRole = role;
        var starts = graph.Nodes.Where(node => IsType(node, "start")).ToList();
        bool changed = false;
        if (role == GraphEntryRole.AuxiliaryEvent)
        {
            changed |= RemoveNodes(graph, starts);
        }
        else if (starts.Count == 0)
        {
            graph.Nodes.Insert(0, CreateStartNode(CreateUniqueNodeId(graph)));
            changed = true;
        }
        else if (starts.Count > 1)
        {
            changed |= RemoveNodes(graph, starts.Skip(1));
        }

        if (changed)
            Logger.Warn($"图表“{ownerName}/{graphName}”入口结构已自动修复。");
        return changed;
    }

    private static bool NormalizeFunctionGraph(GraphFileModel graph, string ownerName, string graphName)
    {
        graph.AssetKind = GraphAssetKind.Function;
        graph.EntryRole = null;
        var entries = graph.Nodes.Where(node => IsType(node, "function_entry")).ToList();
        var returns = graph.Nodes.Where(node => IsType(node, "function_return")).ToList();
        bool changed = RemoveNodes(graph, entries.Skip(1)) | RemoveNodes(graph, returns.Skip(1));

        NodeFileModel? entry = entries.FirstOrDefault();
        NodeFileModel? ret = returns.FirstOrDefault();
        if (entry is null)
        {
            entry = CreateFunctionBoundary(graph, "function_entry", "函数开始", 80, "Fun001");
            graph.Nodes.Insert(0, entry);
            changed = true;
        }
        if (ret is null)
        {
            double x = graph.Nodes.Count <= 1 ? 420 : graph.Nodes.Max(node => node.X) + 340;
            ret = CreateFunctionBoundary(graph, "function_return", "函数返回", x, "Fun002");
            graph.Nodes.Add(ret);
            changed = true;
        }

        if (graph.Nodes.Count == 2 && graph.Connections.Count == 0)
        {
            graph.Connections.Add(new ConnectionFileModel
            {
                SourceNodeId = entry.Id,
                SourcePinName = "exec_out",
                TargetNodeId = ret.Id,
                TargetPinName = "exec_in",
            });
            changed = true;
        }

        if (changed)
            Logger.Warn($"函数“{ownerName}/{graphName}”边界结构已自动修复。");
        return changed;
    }

    private static NodeFileModel CreateStartNode(string id) => new()
    {
        Id = id,
        NodeTypeKey = "start",
        Title = "开始运行",
        NodeNumber = "N001",
        X = 80,
        Y = 210,
    };

    private static NodeFileModel CreateFunctionBoundary(GraphFileModel graph, string typeKey, string title, double x, string nodeNumber) => new()
    {
        Id = CreateUniqueNodeId(graph),
        NodeTypeKey = typeKey,
        Title = title,
        NodeNumber = nodeNumber,
        X = x,
        Y = graph.Nodes.FirstOrDefault()?.Y ?? 210,
    };

    private static bool RemoveNodes(GraphFileModel graph, IEnumerable<NodeFileModel> nodes)
    {
        var ids = nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        if (ids.Count == 0)
            return false;
        graph.Nodes.RemoveAll(node => ids.Contains(node.Id));
        graph.Connections.RemoveAll(connection => ids.Contains(connection.SourceNodeId) || ids.Contains(connection.TargetNodeId));
        return true;
    }

    private static bool IsType(NodeFileModel node, string typeKey) =>
        string.Equals(node.NodeTypeKey, typeKey, StringComparison.OrdinalIgnoreCase);

    private static string CreateUniqueNodeId(GraphFileModel graph)
    {
        var ids = graph.Nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        int ordinal = 1;
        string id;
        do { id = $"node_{ordinal++:000}"; } while (ids.Contains(id));
        return id;
    }
}
