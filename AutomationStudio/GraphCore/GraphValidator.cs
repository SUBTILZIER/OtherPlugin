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
        ValidateExecutionReachability(plan, issues);
        ValidateRequiredParameters(plan, issues);
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

    private static void ValidateExecutionReachability(GraphExecutionPlan plan, List<GraphValidationIssue> issues)
    {
        GraphRuntimeNode? start = plan.Nodes.SingleOrDefault(node => node.NodeKind == NodeKind.Start);
        if (start is null)
            return;

        var reachable = new HashSet<string>(StringComparer.Ordinal) { start.Id };
        var queue = new Queue<string>();
        queue.Enqueue(start.Id);

        var execEdges = plan.Connections
            .Where(connection => connection.SourcePinKind == PinKind.Execution && connection.TargetPinKind == PinKind.Execution)
            .GroupBy(connection => connection.SourceNodeId)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        while (queue.Count > 0)
        {
            string nodeId = queue.Dequeue();
            if (!execEdges.TryGetValue(nodeId, out var outgoing))
                continue;

            foreach (var connection in outgoing)
            {
                if (reachable.Add(connection.TargetNodeId))
                    queue.Enqueue(connection.TargetNodeId);
            }
        }

        foreach (var node in plan.Nodes)
        {
            if (reachable.Contains(node.Id))
                continue;

            if (node.NodeKind == NodeKind.Reroute && node.RoutedKind != PinKind.Execution)
                continue;

            issues.Add(Warning($"节点未接入开始执行链：{node.Title}（{node.Id}）。执行图谱时不会运行该节点。"));
        }
    }

    private static void ValidateRequiredParameters(GraphExecutionPlan plan, List<GraphValidationIssue> issues)
    {
        foreach (var node in plan.Nodes)
        {
            switch (node.NodeKind)
            {
                case NodeKind.FindImage:
                    if (string.IsNullOrWhiteSpace(node.ImagePath))
                        issues.Add(Warning($"找图节点未设置图像路径：{node.Title}。运行时会跳过并继续。"));
                    if (node.UseFindImageRegion && (node.FindImageRegionWidth <= 0 || node.FindImageRegionHeight <= 0))
                        issues.Add(Warning($"找图节点区域宽高无效：{node.Title}。运行时会跳过并继续。"));
                    break;

                case NodeKind.MouseClick:
                    if (!IsInputConnected(plan, node.Id, "position") && node.PositionX == 0 && node.PositionY == 0)
                        issues.Add(Warning($"鼠标点击节点没有有效坐标：{node.Title}。运行时会跳过并继续。"));
                    break;

                case NodeKind.MouseMove:
                    if (!IsInputConnected(plan, node.Id, "position") && node.PositionX == 0 && node.PositionY == 0)
                        issues.Add(Warning($"鼠标移动节点没有有效坐标：{node.Title}。运行时会跳过并继续。"));
                    break;

                case NodeKind.Delay:
                    if (node.DelayMs <= 0)
                        issues.Add(Warning($"延迟节点时长无效：{node.Title}。运行时会使用默认时长。"));
                    break;

                case NodeKind.Keyboard:
                    if (string.IsNullOrWhiteSpace(node.Key))
                        issues.Add(Warning($"键盘节点未设置按键：{node.Title}。运行时会跳过并继续。"));
                    break;

                case NodeKind.StartProgram:
                    if (string.IsNullOrWhiteSpace(node.ImagePath))
                        issues.Add(Warning($"启动程序节点未设置程序路径：{node.Title}。运行时会跳过并继续。"));
                    break;

                case NodeKind.SelectWindow:
                    if (!IsInputConnected(plan, node.Id, "process_name") && string.IsNullOrWhiteSpace(node.ProcessName))
                        issues.Add(Warning($"选中窗口节点未设置进程名：{node.Title}。运行时会跳过并继续。"));
                    break;

                case NodeKind.PrintLog:
                    if (!IsInputConnected(plan, node.Id, "message") && string.IsNullOrWhiteSpace(node.ImagePath))
                        issues.Add(Warning($"打印 log 节点未设置消息：{node.Title}。运行时会输出空内容。"));
                    break;
            }
        }
    }

    private static void ValidateHighRiskRuntimeInputs(GraphExecutionPlan plan, List<GraphValidationIssue> issues)
    {
        foreach (var connection in plan.Connections)
        {
            if (connection.TargetPinKind != PinKind.Vector2D)
                continue;

            var sourceNode = plan.Nodes.FirstOrDefault(node => node.Id == connection.SourceNodeId);
            if (sourceNode?.NodeKind is NodeKind.FindImage)
            {
                issues.Add(Warning(
                    $"节点 {connection.TargetNodeId} 的坐标来自 {sourceNode.Title}。如果上游未命中，下游会跳过执行，不会回退到本地默认坐标。"));
            }
        }
    }

    private static bool IsInputConnected(GraphExecutionPlan plan, string nodeId, string pinName)
    {
        return plan.Connections.Any(connection =>
            connection.TargetNodeId == nodeId &&
            connection.TargetPinName == pinName);
    }

    private static GraphValidationIssue Warning(string message) =>
        new(GraphValidationSeverity.Warning, message);

    private static GraphValidationIssue Error(string message) =>
        new(GraphValidationSeverity.Error, message);
}
