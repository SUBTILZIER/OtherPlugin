using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Runtime;

namespace AutomationStudioWpf.GraphCore;

public enum GraphValidationSeverity
{
    Info,
    Warning,
    Error,
}

public sealed record GraphValidationIssue(GraphValidationSeverity Severity, string Message);

public sealed class GraphValidationResult
{
    public GraphValidationResult(IReadOnlyList<GraphValidationIssue> issues)
    {
        Issues = issues;
    }

    public IReadOnlyList<GraphValidationIssue> Issues { get; }

    public bool HasErrors => Issues.Any(issue => issue.Severity == GraphValidationSeverity.Error);
}

/// <summary>
/// Validates graph structure before runtime starts. It does not execute nodes.
/// </summary>
public sealed class GraphValidator
{
    public GraphValidationResult Validate(GraphExecutionPlan plan)
    {
        var issues = new List<GraphValidationIssue>();
        ValidateStartNodes(plan, issues);
        ValidateDuplicateNodeIds(plan, issues);
        ValidateConnectionEndpoints(plan, issues);
        ValidateConnectionTypes(plan, issues);
        ValidateHighRiskRuntimeInputs(plan, issues);
        return new GraphValidationResult(issues);
    }

    private static void ValidateStartNodes(GraphExecutionPlan plan, List<GraphValidationIssue> issues)
    {
        int startCount = plan.Nodes.Count(node => node.NodeKind == NodeKind.Start);
        if (startCount == 0)
            issues.Add(Error("图谱没有开始节点，无法执行。"));
        else if (startCount > 1)
            issues.Add(Error($"图谱存在多个开始节点（{startCount} 个），请保留一个。"));
    }

    private static void ValidateDuplicateNodeIds(GraphExecutionPlan plan, List<GraphValidationIssue> issues)
    {
        foreach (var group in plan.Nodes.GroupBy(node => node.Id).Where(group => group.Count() > 1))
        {
            issues.Add(Error($"节点 ID 重复：{group.Key}。"));
        }
    }

    private static void ValidateConnectionEndpoints(GraphExecutionPlan plan, List<GraphValidationIssue> issues)
    {
        var nodeIds = plan.Nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var connection in plan.Connections)
        {
            if (!nodeIds.Contains(connection.SourceNodeId))
                issues.Add(Error($"连线源节点不存在：{connection.SourceNodeId}。"));

            if (!nodeIds.Contains(connection.TargetNodeId))
                issues.Add(Error($"连线目标节点不存在：{connection.TargetNodeId}。"));
        }
    }

    private static void ValidateConnectionTypes(GraphExecutionPlan plan, List<GraphValidationIssue> issues)
    {
        foreach (var connection in plan.Connections)
        {
            if (connection.SourcePinKind == connection.TargetPinKind)
                continue;

            if (connection.TargetPinKind == PinKind.String)
                continue;

            issues.Add(Error(
                $"非法连线类型：{connection.SourceNodeId}.{connection.SourcePinName}({connection.SourcePinKind}) -> " +
                $"{connection.TargetNodeId}.{connection.TargetPinName}({connection.TargetPinKind})。"));
        }
    }

    private static void ValidateHighRiskRuntimeInputs(GraphExecutionPlan plan, List<GraphValidationIssue> issues)
    {
        foreach (var connection in plan.Connections)
        {
            if (connection.TargetPinKind != PinKind.Vector2D)
                continue;

            var sourceNode = plan.Nodes.FirstOrDefault(node => node.Id == connection.SourceNodeId);
            if (sourceNode?.NodeKind is NodeKind.FindImage or NodeKind.FindText)
            {
                issues.Add(Warning(
                    $"节点 {connection.TargetNodeId} 的坐标来自 {sourceNode.Title}。如果上游未命中，下游会跳过执行，不会回退到本地默认坐标。"));
            }
        }
    }

    private static GraphValidationIssue Warning(string message) =>
        new(GraphValidationSeverity.Warning, message);

    private static GraphValidationIssue Error(string message) =>
        new(GraphValidationSeverity.Error, message);
}
