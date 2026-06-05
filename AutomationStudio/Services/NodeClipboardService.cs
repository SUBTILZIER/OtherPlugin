using System.Collections.ObjectModel;
using System.Text.Json;
using AutomationStudioWpf.Graph;
using Point = System.Windows.Point;

namespace AutomationStudioWpf.Services;

/// <summary>
/// 节点剪贴板服务 - 处理节点的复制、粘贴和序列号生成
/// </summary>
public sealed class NodeClipboardService
{
    private List<NodeFileModel> _clipboardNodes = [];
    private List<ConnectionFileModel> _clipboardConnections = [];

    /// <summary>
    /// 复制选中的节点到剪贴板
    /// </summary>
    public void CopySelectedNodes(ObservableCollection<NodeBaseViewModel> nodes, ObservableCollection<ConnectionViewModel> connections)
    {
        var selectedNodes = nodes.Where(n => n.IsSelected && n.CanDelete).ToList();
        if (selectedNodes.Count == 0)
        {
            return;
        }

        _clipboardNodes = selectedNodes.Select(NodeSerializer.ToFileModel).ToList();
        var selectedIds = selectedNodes.Select(n => n.Id).ToHashSet();

        _clipboardConnections = connections
            .Where(c => selectedIds.Contains(c.SourcePin.Owner.Id) && selectedIds.Contains(c.TargetPin.Owner.Id))
            .Select(c => new ConnectionFileModel
            {
                SourceNodeId = c.SourcePin.Owner.Id,
                SourcePinName = c.SourcePin.Name,
                TargetNodeId = c.TargetPin.Owner.Id,
                TargetPinName = c.TargetPin.Name,
            })
            .ToList();
    }

    /// <summary>
    /// 检查剪贴板是否有内容
    /// </summary>
    public bool HasClipboardContent => _clipboardNodes.Count > 0;

    /// <summary>
    /// 获取剪贴板中的节点数量
    /// </summary>
    public int ClipboardNodeCount => _clipboardNodes.Count;

    /// <summary>
    /// 获取剪贴板中的连接数量
    /// </summary>
    public int ClipboardConnectionCount => _clipboardConnections.Count;

    /// <summary>
    /// 在指定位置粘贴节点
    /// </summary>
    public IReadOnlyList<NodeBaseViewModel> PasteNodesAt(
        Point position,
        Func<string> createNodeId,
        out IReadOnlyList<(string SourceId, string SourcePin, string TargetId, string TargetPin)> connections)
    {
        var pastedNodes = new List<NodeBaseViewModel>();
        var nodeIdMap = new Dictionary<string, string>();

        if (_clipboardNodes.Count == 0)
        {
            connections = [];
            return pastedNodes;
        }

        // 计算中心点偏移
        double centerX = _clipboardNodes.Average(n => n.X);
        double centerY = _clipboardNodes.Average(n => n.Y);

        foreach (var source in _clipboardNodes)
            nodeIdMap[source.Id] = createNodeId();

        var customEventIdMap = _clipboardNodes
            .Where(node => node.NodeTypeKey == "custom_event")
            .GroupBy(node => string.IsNullOrWhiteSpace(node.CustomEventId) ? node.Id : node.CustomEventId!)
            .ToDictionary(
                group => group.Key,
                group => nodeIdMap[group.First().Id]);

        // 创建新节点
        foreach (var source in _clipboardNodes)
        {
            string newId = nodeIdMap[source.Id];
            double offsetX = source.X - centerX;
            double offsetY = source.Y - centerY;

            // 创建新的文件模型副本，使用新的ID
            var newModel = JsonSerializer.Deserialize<NodeFileModel>(JsonSerializer.Serialize(source))!;
            newModel.Id = newId;
            if (newModel.NodeTypeKey == "custom_event")
                newModel.CustomEventId = newId;
            else if (newModel.NodeTypeKey == "custom_event_call" &&
                     !string.IsNullOrWhiteSpace(newModel.CustomEventId) &&
                     customEventIdMap.TryGetValue(newModel.CustomEventId, out var remappedEventId))
                newModel.CustomEventId = remappedEventId;

            var node = NodeSerializer.FromFileModel(newModel);
            if (node is null) continue;

            node.X = position.X + offsetX;
            node.Y = position.Y + offsetY;
            node.IsSelected = true;

            pastedNodes.Add(node);
        }

        // 构建连接映射
        var connectionList = new List<(string, string, string, string)>();
        foreach (var conn in _clipboardConnections)
        {
            if (nodeIdMap.TryGetValue(conn.SourceNodeId, out var newSourceId) &&
                nodeIdMap.TryGetValue(conn.TargetNodeId, out var newTargetId))
            {
                connectionList.Add((newSourceId, conn.SourcePinName, newTargetId, conn.TargetPinName));
            }
        }

        connections = connectionList;
        return pastedNodes;
    }

    /// <summary>
    /// 清空剪贴板
    /// </summary>
    public void Clear()
    {
        _clipboardNodes.Clear();
        _clipboardConnections.Clear();
    }
}
