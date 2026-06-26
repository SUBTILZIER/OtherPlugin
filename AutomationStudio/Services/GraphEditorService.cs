using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Collections.Specialized;
using System.ComponentModel;
using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Logging;
using AutomationStudioWpf.Runtime;

namespace AutomationStudioWpf.Services;

/// <summary>
/// 图谱编辑服务 - 负责图谱的加载、保存和执行计划构建
/// </summary>
public sealed class GraphEditorService
{
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private int _batchEditDepth;
    private bool _connectionPathsDirty;
    private bool _graphChangedPending;

    public ObservableCollection<NodeBaseViewModel> Nodes { get; } = [];
    public ObservableCollection<ConnectionViewModel> Connections { get; } = [];
    public ObservableCollection<ConnectionPathViewModel> ConnectionPaths { get; } = [];

    public GraphEditorService()
    {
        Connections.CollectionChanged += ConnectionsCollectionChanged;
    }

    public string? CurrentGraphPath { get; private set; }

    public GraphAssetKind CurrentAssetKind { get; private set; } = GraphAssetKind.EventGraph;

    public event Action? GraphChanged;
    public event Action<string>? StatusChanged;

    private void ConnectionsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        MarkConnectionPathsDirty();
    }

    public void RunBatchedEdit(Action action)
    {
        _batchEditDepth++;
        try
        {
            action();
        }
        finally
        {
            _batchEditDepth--;
            if (_batchEditDepth == 0)
            {
                FlushBatchedChanges();
            }
        }
    }

    public void NewGraph() => NewMainEventGraph();

    public void NewMainEventGraph()
    {
        CurrentAssetKind = GraphAssetKind.EventGraph;
        ClearNodesAndConnections();
        CurrentGraphPath = null;

        var startNode = CreateDefaultStartNode();
        AddNodeCore(startNode, CurrentAssetKind);

        RaiseGraphChanged();
        StatusChanged?.Invoke("已新建图谱，并创建开始节点。");
    }

    public void NewAuxiliaryEventGraph()
    {
        CurrentAssetKind = GraphAssetKind.EventGraph;
        ClearNodesAndConnections();
        CurrentGraphPath = null;

        RaiseGraphChanged();
        StatusChanged?.Invoke("已新建空白事件图。");
    }

    public void NewFunctionGraph()
    {
        CurrentAssetKind = GraphAssetKind.Function;
        ClearNodesAndConnections();
        CurrentGraphPath = null;

        var entry = new FunctionEntryNodeViewModel("node_001")
        {
            Title = "函数开始",
            X = 80,
            Y = 210,
        };
        var ret = new FunctionReturnNodeViewModel("node_002")
        {
            Title = "函数返回",
            X = 420,
            Y = 210,
        };
        AddNodeCore(entry, CurrentAssetKind);
        AddNodeCore(ret, CurrentAssetKind);
        Connections.Add(new ConnectionViewModel(entry.OutputPins.First(p => p.Name == "exec_out"), ret.InputPins.First(p => p.Name == "exec_in")));

        RaiseGraphChanged();
        StatusChanged?.Invoke("已新建函数，并创建开始和返回节点。");
    }

    public void SaveGraph(string? path = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            path = CurrentGraphPath;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("未指定保存路径。");
        }

        var file = ExportGraphModel("自动化蓝图图谱");

        File.WriteAllText(path, JsonSerializer.Serialize(file, _jsonOptions));
        CurrentGraphPath = path;
        StatusChanged?.Invoke($"图谱已保存：{Path.GetFileName(path)}");
    }

    public GraphFileModel ExportGraphModel(
        string name,
        GraphAssetKind kind = GraphAssetKind.EventGraph,
        GraphEntryRole? entryRole = null)
    {
        CurrentAssetKind = kind;
        EnsureNodeNumbers(kind);
        entryRole ??= kind == GraphAssetKind.EventGraph ? GraphEntryRole.MainEvent : null;
        return new GraphFileModel
        {
            Name = name,
            AssetKind = kind,
            EntryRole = entryRole,
            Nodes = Nodes.Select(NodeSerializer.ToFileModel).ToList(),
            Connections = Connections.Select(c => new ConnectionFileModel
            {
                SourceNodeId = c.SourcePin.Owner.Id,
                SourcePinName = c.SourcePin.Name,
                TargetNodeId = c.TargetPin.Owner.Id,
                TargetPinName = c.TargetPin.Name,
            }).ToList(),
        };
    }

    public void LoadGraph(string path)
    {
        var json = File.ReadAllText(path);
        var file = JsonSerializer.Deserialize<GraphFileModel>(json);
        if (file is null)
        {
            throw new InvalidOperationException("图谱文件解析失败。");
        }

        LoadFromModel(file);
        CurrentGraphPath = path;
        StatusChanged?.Invoke($"图谱已加载：{Path.GetFileName(path)}");
    }

    public void LoadFromModel(GraphFileModel file)
    {
        CurrentAssetKind = file.AssetKind;
        ClearNodesAndConnections();

        var nodesById = new Dictionary<string, NodeBaseViewModel>();

        foreach (var nodeFile in file.Nodes)
        {
            var node = NodeSerializer.FromFileModel(nodeFile);
            if (node is null) continue;

            AddNodeCore(node, file.AssetKind);
            nodesById[node.Id] = node;
        }

        bool isAuxiliaryEvent = file.AssetKind == GraphAssetKind.EventGraph &&
                                file.EntryRole == GraphEntryRole.AuxiliaryEvent;
        var removedAuxiliaryStartIds = new HashSet<string>(StringComparer.Ordinal);
        if (isAuxiliaryEvent)
        {
            foreach (var startNode in Nodes.Where(node => node.NodeKind == NodeKind.Start).ToList())
            {
                removedAuxiliaryStartIds.Add(startNode.Id);
                Nodes.Remove(startNode);
                nodesById.Remove(startNode.Id);
            }
        }
        else if (file.AssetKind == GraphAssetKind.EventGraph && Nodes.All(node => node.NodeKind != NodeKind.Start))
        {
            string id = nodesById.ContainsKey("node_001") ? $"node_{nodesById.Count + 1:000}" : "node_001";
            while (nodesById.ContainsKey(id))
            {
                id = $"node_{nodesById.Count + 1:000}_{Guid.NewGuid():N}";
            }

            var startNode = new StartNodeViewModel(id)
            {
                Title = "开始运行",
                X = 80,
                Y = 210,
            };
            AssignNodeNumber(startNode, file.AssetKind);
            SubscribeNode(startNode);
            Nodes.Insert(0, startNode);
            nodesById[startNode.Id] = startNode;
        }

        EnsureNodeNumbers(file.AssetKind);

        foreach (var connFile in file.Connections)
        {
            if (removedAuxiliaryStartIds.Contains(connFile.SourceNodeId) ||
                removedAuxiliaryStartIds.Contains(connFile.TargetNodeId))
            {
                continue;
            }

            if (!nodesById.TryGetValue(connFile.SourceNodeId, out var sourceNode) ||
                !nodesById.TryGetValue(connFile.TargetNodeId, out var targetNode))
            {
                Logger.Warn($"旧图谱包含无效连线，已跳过：{connFile.SourceNodeId}.{connFile.SourcePinName} -> {connFile.TargetNodeId}.{connFile.TargetPinName}");
                continue;
            }

            var sourcePin = sourceNode.OutputPins.FirstOrDefault(p => p.Name == connFile.SourcePinName);
            var targetPin = targetNode.InputPins.FirstOrDefault(p => p.Name == connFile.TargetPinName);

            if (sourcePin is not null && targetPin is not null)
            {
                Connections.Add(new ConnectionViewModel(sourcePin, targetPin));
            }
        }

        RaiseGraphChanged();
    }

    public void ClearGraph()
    {
        ClearNodesAndConnections();
        RaiseGraphChanged();
    }

    public void RemoveStartNodes()
    {
        var starts = Nodes.Where(node => node.NodeKind == NodeKind.Start).ToList();
        if (starts.Count == 0)
            return;

        var startIds = starts.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        RunBatchedEdit(() =>
        {
            foreach (var connection in Connections
                         .Where(connection => startIds.Contains(connection.SourcePin.Owner.Id) ||
                                              startIds.Contains(connection.TargetPin.Owner.Id))
                         .ToList())
            {
                Connections.Remove(connection);
                connection.Dispose();
            }

            foreach (var start in starts)
                Nodes.Remove(start);
        });
    }

    public GraphExecutionPlan BuildExecutionPlan()
    {
        var runtimeNodes = Nodes.Select(n => NodeSerializer.ToRuntimeNode(n)).ToList();
        var runtimeConnections = Connections
            .Select(c => new GraphRuntimeConnection(
                c.SourcePin.Owner.Id,
                c.SourcePin.Name,
                c.SourcePin.Kind,
                c.TargetPin.Owner.Id,
                c.TargetPin.Name,
                c.TargetPin.Kind))
            .ToList();

        return new GraphExecutionPlan(runtimeNodes, runtimeConnections);
    }

    public void AddNode(NodeBaseViewModel node)
    {
        AddNodeCore(node, CurrentAssetKind);
        RaiseGraphChanged();
    }

    public void RemoveNode(NodeBaseViewModel node)
    {
        RunBatchedEdit(() =>
        {
            // 移除相关连接
            for (int i = Connections.Count - 1; i >= 0; i--)
            {
                if (Connections[i].SourcePin.Owner == node || Connections[i].TargetPin.Owner == node)
                {
                    RemoveConnectionAt(i);
                }
            }

            UnsubscribeNode(node);
            Nodes.Remove(node);
            RaiseGraphChanged();
        });
    }

    public void RemoveSelectedNodes()
    {
        RunBatchedEdit(() =>
        {
            var toDelete = Nodes.Where(n => n.IsSelected && n.CanDelete).ToList();
            foreach (var node in toDelete)
            {
                RemoveNode(node);
            }
        });
    }

    public void CreateConnection(PinViewModel sourcePin, PinViewModel targetPin)
    {
        RunBatchedEdit(() =>
        {
            // 执行引脚：输出端最多一条连线
            if (sourcePin.Kind == PinKind.Execution)
            {
                for (int i = Connections.Count - 1; i >= 0; i--)
                {
                    if (Connections[i].SourcePin == sourcePin)
                    {
                        RemoveConnectionAt(i);
                    }
                }
            }

            // 数据输入引脚：最多一条入线（执行输入引脚允许多条，支持循环）
            if (targetPin.Kind != PinKind.Execution)
            {
                for (int i = Connections.Count - 1; i >= 0; i--)
                {
                    if (Connections[i].TargetPin == targetPin)
                    {
                        RemoveConnectionAt(i);
                    }
                }
            }

            Connections.Add(new ConnectionViewModel(sourcePin, targetPin));
            RaiseGraphChanged();
        });
    }

    public void RemoveConnection(ConnectionViewModel connection)
    {
        var index = Connections.IndexOf(connection);
        if (index >= 0)
        {
            RemoveConnectionAt(index);
        }
    }

    public void RemoveConnections(IEnumerable<ConnectionViewModel> connections)
    {
        RunBatchedEdit(() =>
        {
            foreach (var connection in connections.ToList())
            {
                RemoveConnection(connection);
            }
        });
    }

    private void RemoveConnectionAt(int index)
    {
        var connection = Connections[index];
        Connections.RemoveAt(index);
        connection.Dispose();
        RaiseGraphChanged();
    }

    private void RebuildConnectionPaths()
    {
        ClearConnectionPaths();

        var incomingByReroute = Connections
            .Where(connection => connection.TargetPin.Owner.NodeKind == NodeKind.Reroute)
            .GroupBy(connection => connection.TargetPin.Owner)
            .ToDictionary(group => group.Key, group => group.ToList());
        var outgoingByReroute = Connections
            .Where(connection => connection.SourcePin.Owner.NodeKind == NodeKind.Reroute)
            .GroupBy(connection => connection.SourcePin.Owner)
            .ToDictionary(group => group.Key, group => group.ToList());

        HashSet<ConnectionViewModel> visited = [];
        foreach (var connection in Connections)
        {
            if (visited.Contains(connection))
            {
                continue;
            }

            if (connection.SourcePin.Owner.NodeKind == NodeKind.Reroute &&
                incomingByReroute.TryGetValue(connection.SourcePin.Owner, out var incoming) &&
                incoming.Count == 1)
            {
                continue;
            }

            var chain = BuildRenderableConnectionChain(connection, incomingByReroute, outgoingByReroute);
            foreach (var chainConnection in chain)
            {
                visited.Add(chainConnection);
            }

            ConnectionPaths.Add(new ConnectionPathViewModel(chain));
        }

        foreach (var connection in Connections)
        {
            if (!visited.Contains(connection))
            {
                ConnectionPaths.Add(new ConnectionPathViewModel([connection]));
            }
        }
    }

    private static List<ConnectionViewModel> BuildRenderableConnectionChain(
        ConnectionViewModel start,
        IReadOnlyDictionary<NodeBaseViewModel, List<ConnectionViewModel>> incomingByReroute,
        IReadOnlyDictionary<NodeBaseViewModel, List<ConnectionViewModel>> outgoingByReroute)
    {
        List<ConnectionViewModel> chain = [start];
        HashSet<ConnectionViewModel> seen = [start];
        ConnectionViewModel current = start;

        while (current.TargetPin.Owner.NodeKind == NodeKind.Reroute)
        {
            var reroute = current.TargetPin.Owner;
            if (!incomingByReroute.TryGetValue(reroute, out var incoming) ||
                !outgoingByReroute.TryGetValue(reroute, out var outgoing) ||
                incoming.Count != 1 ||
                outgoing.Count != 1)
            {
                return [start];
            }

            var next = outgoing[0];
            if (!seen.Add(next))
            {
                return [start];
            }

            chain.Add(next);
            current = next;
        }

        return chain;
    }

    private void ClearConnectionPaths()
    {
        foreach (var path in ConnectionPaths)
        {
            path.Dispose();
        }

        ConnectionPaths.Clear();
    }

    private void ClearNodesAndConnections()
    {
        ClearConnectionPaths();
        foreach (var connection in Connections)
        {
            connection.Dispose();
        }

        foreach (var node in Nodes)
        {
            UnsubscribeNode(node);
        }

        Connections.Clear();
        Nodes.Clear();
    }

    private void AddNodeCore(NodeBaseViewModel node, GraphAssetKind kind)
    {
        AssignNodeNumber(node, kind);
        SubscribeNode(node);
        Nodes.Add(node);
    }

    private void EnsureNodeNumbers(GraphAssetKind kind)
    {
        foreach (var node in Nodes.Where(ShouldAssignNumber))
        {
            AssignNodeNumber(node, kind);
        }
    }

    private void AssignNodeNumber(NodeBaseViewModel node, GraphAssetKind kind)
    {
        if (!ShouldAssignNumber(node))
        {
            node.NodeNumber = string.Empty;
            return;
        }

        string prefix = NodeNumberPrefix(kind);
        if (IsNodeNumberUsable(node, prefix))
            return;

        node.NodeNumber = CreateReusableNodeNumber(prefix, node);
    }

    private bool IsNodeNumberUsable(NodeBaseViewModel node, string prefix)
    {
        if (ParseNodeOrdinal(node.NodeNumber, prefix) is null)
            return false;

        return Nodes
            .Where(other => !ReferenceEquals(other, node) && ShouldAssignNumber(other))
            .All(other => !string.Equals(other.NodeNumber, node.NodeNumber, StringComparison.OrdinalIgnoreCase));
    }

    private string CreateReusableNodeNumber(string prefix, NodeBaseViewModel node)
    {
        HashSet<int> used = Nodes
            .Where(other => !ReferenceEquals(other, node) && ShouldAssignNumber(other))
            .Select(other => ParseNodeOrdinal(other.NodeNumber, prefix))
            .Where(ordinal => ordinal.HasValue)
            .Select(ordinal => ordinal!.Value)
            .ToHashSet();

        int next = 1;
        while (used.Contains(next))
        {
            next++;
        }

        return $"{prefix}{next:000}";
    }

    private static bool ShouldAssignNumber(NodeBaseViewModel node) =>
        NodeTraits.ShouldAssignNodeNumber(node.NodeKind);

    private static string NodeNumberPrefix(GraphAssetKind kind) => kind switch
    {
        GraphAssetKind.Function => "Fun",
        _ => "N",
    };

    private static int? ParseNodeOrdinal(string? nodeNumber, string prefix)
    {
        if (string.IsNullOrWhiteSpace(nodeNumber) ||
            !nodeNumber.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        string suffix = nodeNumber[prefix.Length..];
        return int.TryParse(suffix, out int ordinal) && ordinal > 0 ? ordinal : null;
    }

    private void SubscribeNode(NodeBaseViewModel node)
    {
        node.PropertyChanged -= NodePropertyChanged;
        node.PropertyChanged += NodePropertyChanged;
    }

    private void UnsubscribeNode(NodeBaseViewModel node)
    {
        node.PropertyChanged -= NodePropertyChanged;
    }

    private void NodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not NodeBaseViewModel node ||
            e.PropertyName is not (nameof(NodeBaseViewModel.Title) or nameof(NodeBaseViewModel.NodeNumber)))
        {
            return;
        }

        if (SyncToDoTargetsFor(node))
        {
            RaiseGraphChanged();
        }
    }

    private bool SyncToDoTargetsFor(NodeBaseViewModel target)
    {
        bool changed = false;
        foreach (var toDo in Nodes.OfType<ToDoNodeViewModel>().Where(node => node.TargetNodeId == target.Id))
        {
            if (ReferenceEquals(toDo, target))
                continue;

            if (!string.Equals(toDo.TargetNodeTitle, target.Title, StringComparison.Ordinal))
            {
                toDo.TargetNodeTitle = target.Title;
                changed = true;
            }

            if (!string.Equals(toDo.TargetNodeNumber, target.NodeNumber, StringComparison.OrdinalIgnoreCase))
            {
                toDo.TargetNodeNumber = target.NodeNumber;
                changed = true;
            }

            if (changed)
                toDo.RefreshDescription();
        }

        return changed;
    }

    public void ClearConnectionsForPin(PinViewModel pin)
    {
        RunBatchedEdit(() =>
        {
            for (int i = Connections.Count - 1; i >= 0; i--)
            {
                if (Connections[i].SourcePin == pin || Connections[i].TargetPin == pin)
                {
                    RemoveConnectionAt(i);
                }
            }
        });
    }

    public void UpdatePinConnectionStates()
    {
        HashSet<PinViewModel> connectedPins = [];
        foreach (var connection in Connections)
        {
            connectedPins.Add(connection.SourcePin);
            connectedPins.Add(connection.TargetPin);
        }

        foreach (var node in Nodes)
        {
            foreach (var pin in node.InputPins.Concat(node.OutputPins))
            {
                pin.HasConnection = connectedPins.Contains(pin);
            }
            node.RefreshDescription();
        }
    }

    public void RebindConnectionsToCurrentPins()
    {
        if (Connections.Count == 0)
            return;

        var nodesById = Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var rebound = new List<ConnectionViewModel>();
        foreach (var connection in Connections.ToList())
        {
            string sourceNodeId = connection.SourcePin.Owner.Id;
            string sourcePinName = connection.SourcePin.Name;
            string targetNodeId = connection.TargetPin.Owner.Id;
            string targetPinName = connection.TargetPin.Name;

            connection.Dispose();

            if (!nodesById.TryGetValue(sourceNodeId, out var sourceNode) ||
                !nodesById.TryGetValue(targetNodeId, out var targetNode))
            {
                Logger.Warn($"连线重绑定时节点不存在，已移除：{sourceNodeId}.{sourcePinName} -> {targetNodeId}.{targetPinName}");
                continue;
            }

            var sourcePin = sourceNode.OutputPins.FirstOrDefault(pin => pin.Name == sourcePinName);
            var targetPin = targetNode.InputPins.FirstOrDefault(pin => pin.Name == targetPinName);
            if (sourcePin is null || targetPin is null)
            {
                Logger.Warn($"连线重绑定时引脚不存在，已移除：{sourceNodeId}.{sourcePinName} -> {targetNodeId}.{targetPinName}");
                continue;
            }

            if (!CanConnect(sourcePin, targetPin, out string reason))
            {
                Logger.Warn($"连线重绑定时类型无效，已移除：{sourceNodeId}.{sourcePinName} -> {targetNodeId}.{targetPinName}。{reason}");
                continue;
            }

            rebound.Add(new ConnectionViewModel(sourcePin, targetPin));
        }

        RunBatchedEdit(() =>
        {
            Connections.Clear();
            foreach (var connection in rebound)
                Connections.Add(connection);

            UpdatePinConnectionStates();
            RaiseGraphChanged();
        });
    }

    public bool CanConnect(PinViewModel sourcePin, PinViewModel targetPin, out string reason)
    {
        if (sourcePin.Owner == targetPin.Owner)
        {
            reason = "暂不支持把节点连接到自身。";
            return false;
        }

        if (sourcePin.Direction != PinDirection.Output || targetPin.Direction != PinDirection.Input)
        {
            reason = "连线方向不正确，必须从输出引脚连到输入引脚。";
            return false;
        }

        // String input pins accept any type (Boolean, Vector2D, String) via ToString()
        bool targetIsString = targetPin.Kind == PinKind.String;
        if (sourcePin.Kind != targetPin.Kind && !targetIsString)
        {
            reason = $"引脚类型不匹配：{sourcePin.KindLabel} 不能连接到 {targetPin.KindLabel}。";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static StartNodeViewModel CreateDefaultStartNode()
    {
        return new StartNodeViewModel("node_001")
        {
            Title = "开始运行",
            X = 80,
            Y = 210,
        };
    }

    private void MarkConnectionPathsDirty()
    {
        _connectionPathsDirty = true;
        if (_batchEditDepth == 0)
        {
            FlushConnectionPaths();
        }
    }

    private void FlushConnectionPaths()
    {
        if (!_connectionPathsDirty)
        {
            return;
        }

        _connectionPathsDirty = false;
        RebuildConnectionPaths();
    }

    private void RaiseGraphChanged()
    {
        if (_batchEditDepth > 0)
        {
            _graphChangedPending = true;
            return;
        }

        GraphChanged?.Invoke();
    }

    private void FlushBatchedChanges()
    {
        FlushConnectionPaths();
        if (!_graphChangedPending)
        {
            return;
        }

        _graphChangedPending = false;
        GraphChanged?.Invoke();
    }
}
