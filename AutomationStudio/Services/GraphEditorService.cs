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
    private readonly IRenderUpdateScheduler _renderUpdateScheduler;
    private int _batchEditDepth;
    private bool _connectionPathsDirty;
    private bool _graphChangedPending;

    public ObservableCollection<NodeBaseViewModel> Nodes { get; } = [];
    public ObservableCollection<ConnectionViewModel> Connections { get; } = [];
    public ObservableCollection<ConnectionPathViewModel> ConnectionPaths { get; } = [];

    public GraphEditorService()
        : this(RenderUpdateScheduler.CreateDefault())
    {
    }

    internal GraphEditorService(IRenderUpdateScheduler renderUpdateScheduler)
    {
        _renderUpdateScheduler = renderUpdateScheduler ?? throw new ArgumentNullException(nameof(renderUpdateScheduler));
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
        Connections.Add(CreateConnectionViewModel(entry.OutputPins.First(p => p.Name == "exec_out"), ret.InputPins.First(p => p.Name == "exec_in")));

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

        AtomicJsonFileStore.Write(path, file, _jsonOptions);
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
        var readResult = AtomicJsonFileStore.Read<GraphFileModel>(path, _jsonOptions);
        if (readResult.RecoveredFromBackup && readResult.PrimaryFileRepaired)
            StatusChanged?.Invoke($"图谱已从备份恢复，并修复主文件：{Path.GetFileName(path)}");
        else if (readResult.RepairError is not null)
            StatusChanged?.Invoke($"图谱已从备份读取，但原路径修复失败；请使用另存为：{readResult.RepairError.Message}");

        LoadFromModel(readResult.Value);
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
            if (string.IsNullOrWhiteSpace(nodeFile.Id) || nodesById.ContainsKey(nodeFile.Id))
            {
                Logger.Error($"图表包含空或重复节点 ID，已跳过后续重复项：{nodeFile.Id}");
                continue;
            }
            var node = NodeSerializer.FromFileModel(nodeFile);
            if (node is null) continue;

            AddNodeCore(node, file.AssetKind);
            nodesById.Add(node.Id, node);
        }

        var ignoredNodeIds = new HashSet<string>(StringComparer.Ordinal);
        bool addedFunctionBoundary = file.AssetKind == GraphAssetKind.Function &&
                                     EnsureFunctionBoundaryNodes(file.Name, nodesById, ignoredNodeIds);
        bool isAuxiliaryEvent = file.AssetKind == GraphAssetKind.EventGraph &&
                                file.EntryRole == GraphEntryRole.AuxiliaryEvent;
        if (isAuxiliaryEvent)
        {
            foreach (var startNode in Nodes.Where(node => node.NodeKind == NodeKind.Start).ToList())
            {
                ignoredNodeIds.Add(startNode.Id);
                UnsubscribeNode(startNode);
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
        else if (file.AssetKind == GraphAssetKind.EventGraph)
        {
            foreach (var duplicate in Nodes.Where(node => node.NodeKind == NodeKind.Start).Skip(1).ToList())
            {
                ignoredNodeIds.Add(duplicate.Id);
                nodesById.Remove(duplicate.Id);
                UnsubscribeNode(duplicate);
                Nodes.Remove(duplicate);
            }
        }

        EnsureNodeNumbers(file.AssetKind);

        foreach (var connFile in file.Connections)
        {
            if (ignoredNodeIds.Contains(connFile.SourceNodeId) ||
                ignoredNodeIds.Contains(connFile.TargetNodeId))
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
                Connections.Add(CreateConnectionViewModel(sourcePin, targetPin));
            }
        }

        if (addedFunctionBoundary &&
            file.AssetKind == GraphAssetKind.Function &&
            Nodes.Count == 2 &&
            Connections.Count == 0)
        {
            var entry = Nodes.OfType<FunctionEntryNodeViewModel>().Single();
            var ret = Nodes.OfType<FunctionReturnNodeViewModel>().Single();
            Connections.Add(CreateConnectionViewModel(
                entry.OutputPins.First(pin => pin.Name == "exec_out"),
                ret.InputPins.First(pin => pin.Name == "exec_in")));
        }

        RaiseGraphChanged();
    }

    private bool EnsureFunctionBoundaryNodes(
        string graphName,
        Dictionary<string, NodeBaseViewModel> nodesById,
        ISet<string> ignoredNodeIds)
    {
        var entries = Nodes.OfType<FunctionEntryNodeViewModel>().ToList();
        var returns = Nodes.OfType<FunctionReturnNodeViewModel>().ToList();
        var repairs = new List<string>();

        RemoveDuplicateFunctionBoundaryNodes(entries.Skip(1), nodesById, ignoredNodeIds, repairs, "函数开始");
        RemoveDuplicateFunctionBoundaryNodes(returns.Skip(1), nodesById, ignoredNodeIds, repairs, "函数返回");

        bool addedBoundary = false;
        var entry = entries.FirstOrDefault();
        var ret = returns.FirstOrDefault();
        double boundaryY = entry?.Y ?? ret?.Y ?? Nodes.FirstOrDefault()?.Y ?? 210;

        if (entry is null)
        {
            entry = new FunctionEntryNodeViewModel(CreateUniqueNodeId(nodesById))
            {
                Title = "函数开始",
                X = Nodes.Count == 0 ? 80 : Nodes.Min(node => node.X) - 340,
                Y = boundaryY,
            };
            AssignNodeNumber(entry, GraphAssetKind.Function);
            SubscribeNode(entry);
            Nodes.Insert(0, entry);
            nodesById.Add(entry.Id, entry);
            repairs.Add("补回函数开始");
            addedBoundary = true;
        }

        if (ret is null)
        {
            ret = new FunctionReturnNodeViewModel(CreateUniqueNodeId(nodesById))
            {
                Title = "函数返回",
                X = Nodes.Count == 1 ? 420 : Nodes.Max(node => node.X) + 340,
                Y = boundaryY,
            };
            AddNodeCore(ret, GraphAssetKind.Function);
            nodesById.Add(ret.Id, ret);
            repairs.Add("补回函数返回");
            addedBoundary = true;
        }

        if (repairs.Count > 0)
        {
            Logger.Warn($"函数图“{graphName}”结构已修复：{string.Join("；", repairs)}。函数开始和函数返回为必需节点，不可删除。");
        }

        return addedBoundary;
    }

    private void RemoveDuplicateFunctionBoundaryNodes<TNode>(
        IEnumerable<TNode> duplicates,
        IDictionary<string, NodeBaseViewModel> nodesById,
        ISet<string> ignoredNodeIds,
        ICollection<string> repairs,
        string nodeName)
        where TNode : NodeBaseViewModel
    {
        int removedCount = 0;
        foreach (var duplicate in duplicates.ToList())
        {
            ignoredNodeIds.Add(duplicate.Id);
            nodesById.Remove(duplicate.Id);
            UnsubscribeNode(duplicate);
            Nodes.Remove(duplicate);
            removedCount++;
        }

        if (removedCount > 0)
            repairs.Add($"移除重复{nodeName} {removedCount} 个");
    }

    private static string CreateUniqueNodeId(IReadOnlyDictionary<string, NodeBaseViewModel> nodesById)
    {
        int ordinal = 1;
        string id;
        do
        {
            id = $"node_{ordinal++:000}";
        }
        while (nodesById.ContainsKey(id));

        return id;
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
        if (node.NodeKind is NodeKind.FunctionEntry or NodeKind.FunctionReturn &&
            Nodes.Any(existing => existing.NodeKind == node.NodeKind))
        {
            StatusChanged?.Invoke($"{node.Title} 是函数图唯一的结构节点，不能重复添加。");
            return;
        }

        AddNodeCore(node, CurrentAssetKind);
        RaiseGraphChanged();
    }

    public void RemoveNode(NodeBaseViewModel node)
    {
        if (!node.CanDelete)
        {
            StatusChanged?.Invoke($"{node.Title} 是图谱必需节点，不能删除。");
            return;
        }

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
            if (toDelete.Count == 0 && Nodes.Any(node => node.IsSelected && !node.CanDelete))
                StatusChanged?.Invoke("选中的节点是图谱必需节点，不能删除。");

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

            Connections.Add(CreateConnectionViewModel(sourcePin, targetPin));
            RaiseGraphChanged();
        });
    }

    private ConnectionViewModel CreateConnectionViewModel(PinViewModel sourcePin, PinViewModel targetPin) =>
        new(sourcePin, targetPin, _renderUpdateScheduler);

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

            ConnectionPaths.Add(new ConnectionPathViewModel(chain, _renderUpdateScheduler));
        }

        foreach (var connection in Connections)
        {
            if (!visited.Contains(connection))
            {
                ConnectionPaths.Add(new ConnectionPathViewModel([connection], _renderUpdateScheduler));
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

        var nodesById = Nodes
            .GroupBy(node => node.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
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

            rebound.Add(CreateConnectionViewModel(sourcePin, targetPin));
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
