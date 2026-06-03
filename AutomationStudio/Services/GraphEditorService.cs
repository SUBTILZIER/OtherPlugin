using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
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

    public ObservableCollection<NodeBaseViewModel> Nodes { get; } = [];
    public ObservableCollection<ConnectionViewModel> Connections { get; } = [];

    public string? CurrentGraphPath { get; private set; }

    public event Action? GraphChanged;
    public event Action<string>? StatusChanged;

    public void NewGraph()
    {
        Nodes.Clear();
        Connections.Clear();
        CurrentGraphPath = null;

        var startNode = CreateDefaultStartNode();
        Nodes.Add(startNode);

        GraphChanged?.Invoke();
        StatusChanged?.Invoke("已新建图谱，并创建开始节点。");
    }

    public void NewFunctionGraph()
    {
        Nodes.Clear();
        Connections.Clear();
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
        Nodes.Add(entry);
        Nodes.Add(ret);
        Connections.Add(new ConnectionViewModel(entry.OutputPins.First(p => p.Name == "exec_out"), ret.InputPins.First(p => p.Name == "exec_in")));

        GraphChanged?.Invoke();
        StatusChanged?.Invoke("已新建函数，并创建开始和返回节点。");
    }

    public void NewMacroGraph()
    {
        Nodes.Clear();
        Connections.Clear();
        CurrentGraphPath = null;

        var entry = new MacroEntryNodeViewModel("node_001")
        {
            Title = "宏开始",
            X = 80,
            Y = 210,
        };
        var output = new MacroOutputNodeViewModel("node_002")
        {
            Title = "宏输出",
            X = 420,
            Y = 210,
        };
        Nodes.Add(entry);
        Nodes.Add(output);
        Connections.Add(new ConnectionViewModel(entry.OutputPins.First(p => p.Name == "exec_out"), output.InputPins.First(p => p.Name == "exec_in")));

        GraphChanged?.Invoke();
        StatusChanged?.Invoke("已新建宏，并创建开始和输出节点。");
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

    public GraphFileModel ExportGraphModel(string name, GraphAssetKind kind = GraphAssetKind.EventGraph)
    {
        return new GraphFileModel
        {
            Name = name,
            AssetKind = kind,
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
        foreach (var connection in Connections)
        {
            connection.Dispose();
        }

        Nodes.Clear();
        Connections.Clear();

        var nodesById = new Dictionary<string, NodeBaseViewModel>();

        foreach (var nodeFile in file.Nodes)
        {
            var node = NodeSerializer.FromFileModel(nodeFile);
            if (node is null) continue;

            Nodes.Add(node);
            nodesById[node.Id] = node;
        }

        if (file.AssetKind == GraphAssetKind.EventGraph && Nodes.All(node => node.NodeKind != NodeKind.Start))
        {
            string id = nodesById.ContainsKey("node_001") ? $"node_{nodesById.Count + 1:000}" : "node_001";
            while (nodesById.ContainsKey(id))
            {
                id = $"node_{nodesById.Count + 1:000}_{Guid.NewGuid():N}";
            }

            var startNode = new StartNodeViewModel(id)
            {
                Title = "事件开始运行",
                X = 80,
                Y = 210,
            };
            Nodes.Insert(0, startNode);
            nodesById[startNode.Id] = startNode;
        }

        foreach (var connFile in file.Connections)
        {
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

        GraphChanged?.Invoke();
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
        Nodes.Add(node);
        GraphChanged?.Invoke();
    }

    public void RemoveNode(NodeBaseViewModel node)
    {
        // 移除相关连接
        for (int i = Connections.Count - 1; i >= 0; i--)
        {
            if (Connections[i].SourcePin.Owner == node || Connections[i].TargetPin.Owner == node)
            {
                RemoveConnectionAt(i);
            }
        }

        Nodes.Remove(node);
        GraphChanged?.Invoke();
    }

    public void RemoveSelectedNodes()
    {
        var toDelete = Nodes.Where(n => n.IsSelected && n.CanDelete).ToList();
        foreach (var node in toDelete)
        {
            RemoveNode(node);
        }
    }

    public void CreateConnection(PinViewModel sourcePin, PinViewModel targetPin)
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
        GraphChanged?.Invoke();
    }

    public void RemoveConnection(ConnectionViewModel connection)
    {
        var index = Connections.IndexOf(connection);
        if (index >= 0)
        {
            RemoveConnectionAt(index);
        }
    }

    private void RemoveConnectionAt(int index)
    {
        var connection = Connections[index];
        Connections.RemoveAt(index);
        connection.Dispose();
        GraphChanged?.Invoke();
    }

    public void ClearConnectionsForPin(PinViewModel pin)
    {
        for (int i = Connections.Count - 1; i >= 0; i--)
        {
            if (Connections[i].SourcePin == pin || Connections[i].TargetPin == pin)
            {
                RemoveConnectionAt(i);
            }
        }
    }

    public void UpdatePinConnectionStates()
    {
        foreach (var node in Nodes)
        {
            foreach (var pin in node.InputPins.Concat(node.OutputPins))
            {
                pin.HasConnection = Connections.Any(c => c.SourcePin == pin || c.TargetPin == pin);
            }
            node.RefreshDescription();
        }
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
            Title = "事件开始运行",
            X = 80,
            Y = 210,
        };
    }
}
