using AutomationStudioWpf.Graph;
using AutomationStudioWpf.GraphCore;
using AutomationStudioWpf.Runtime;

namespace AutomationStudioWpf.Services;

internal sealed record GraphRuntimePlanBuildResult(
    GraphExecutionPlan? Plan,
    IReadOnlyList<GraphValidationIssue> Issues)
{
    public bool Success => Plan is not null && Issues.All(issue => issue.Severity != GraphValidationSeverity.Error);
}

internal sealed class GraphRuntimePlanBuilder
{
    public GraphRuntimePlanBuildResult Build(GraphSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        GraphFileModel graph = snapshot.ToMutableModel();
        string graphLabel = string.IsNullOrWhiteSpace(snapshot.Name) ? snapshot.Id : snapshot.Name;
        var issues = new List<GraphValidationIssue>();
        var nodesById = new Dictionary<string, NodeBaseViewModel>(StringComparer.Ordinal);

        foreach (NodeFileModel fileNode in graph.Nodes)
        {
            if (string.IsNullOrWhiteSpace(fileNode.Id))
            {
                issues.Add(Error(graphLabel, "存在空节点 ID。"));
                continue;
            }
            if (nodesById.ContainsKey(fileNode.Id))
            {
                issues.Add(Error(graphLabel, $"节点 ID 重复：{fileNode.Id}。"));
                continue;
            }

            NodeBaseViewModel? node;
            try
            {
                node = NodeSerializer.FromFileModel(fileNode);
            }
            catch (Exception ex)
            {
                issues.Add(Error(graphLabel, $"节点无法读取：{fileNode.Id}：{ex.Message}"));
                continue;
            }

            if (node is null)
            {
                issues.Add(Error(graphLabel, $"未知或已移除的节点类型：{fileNode.NodeTypeKey}。"));
                continue;
            }
            nodesById.Add(fileNode.Id, node);
        }

        var runtimeNodes = new List<GraphRuntimeNode>(nodesById.Count);
        foreach (NodeBaseViewModel node in nodesById.Values)
        {
            try
            {
                runtimeNodes.Add(NodeSerializer.ToRuntimeNode(node));
            }
            catch (Exception ex)
            {
                issues.Add(Error(graphLabel, $"节点无法生成运行模型：{node.Title}（{node.Id}）：{ex.Message}"));
            }
        }

        var runtimeConnections = new List<GraphRuntimeConnection>(graph.Connections.Count);
        foreach (ConnectionFileModel connection in graph.Connections)
        {
            if (!nodesById.TryGetValue(connection.SourceNodeId, out NodeBaseViewModel? sourceNode))
            {
                issues.Add(Error(graphLabel, $"连线源节点不存在：{connection.SourceNodeId}。"));
                continue;
            }
            if (!nodesById.TryGetValue(connection.TargetNodeId, out NodeBaseViewModel? targetNode))
            {
                issues.Add(Error(graphLabel, $"连线目标节点不存在：{connection.TargetNodeId}。"));
                continue;
            }

            PinViewModel? sourcePin = sourceNode.OutputPins.FirstOrDefault(pin => pin.Name == connection.SourcePinName);
            PinViewModel? targetPin = targetNode.InputPins.FirstOrDefault(pin => pin.Name == connection.TargetPinName);
            if (sourcePin is null)
            {
                issues.Add(Error(graphLabel, $"连线源引脚不存在：{connection.SourceNodeId}.{connection.SourcePinName}。"));
                continue;
            }
            if (targetPin is null)
            {
                issues.Add(Error(graphLabel, $"连线目标引脚不存在：{connection.TargetNodeId}.{connection.TargetPinName}。"));
                continue;
            }
            if (sourcePin.Kind != targetPin.Kind && targetPin.Kind != PinKind.String)
            {
                issues.Add(Error(
                    graphLabel,
                    $"非法连线类型：{connection.SourceNodeId}.{connection.SourcePinName}({sourcePin.Kind}) -> " +
                    $"{connection.TargetNodeId}.{connection.TargetPinName}({targetPin.Kind})。"));
                continue;
            }

            runtimeConnections.Add(new GraphRuntimeConnection(
                sourceNode.Id,
                sourcePin.Name,
                sourcePin.Kind,
                targetNode.Id,
                targetPin.Name,
                targetPin.Kind));
        }

        GraphExecutionPlan? plan = issues.Any(issue => issue.Severity == GraphValidationSeverity.Error)
            ? null
            : new GraphExecutionPlan(runtimeNodes, runtimeConnections);
        return new GraphRuntimePlanBuildResult(plan, issues.AsReadOnly());
    }

    private static GraphValidationIssue Error(string graphLabel, string message) =>
        new(GraphValidationSeverity.Error, $"{graphLabel}: {message}");
}
