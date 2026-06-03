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

public sealed class GraphValidator
{
    public GraphValidationResult Validate(GraphExecutionPlan plan)
    {
        var issues = new List<GraphValidationIssue>();
        ValidateStartNodes(plan, issues);
        ValidateDuplicateNodeIds(plan, issues);
        ValidateConnectionEndpoints(plan, issues);
        ValidateConnectionTypes(plan, issues);
        ValidateConnectionMultiplicity(plan, issues);
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
            issues.Add(Error($"节点 ID 重复：{group.Key}。"));
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

            // String input pins accept Boolean/Vector2D/String via RuntimeContext.FormatValue.
            if (connection.TargetPinKind == PinKind.String)
                continue;

            issues.Add(Error(
                $"非法连线类型：{connection.SourceNodeId}.{connection.SourcePinName}({connection.SourcePinKind}) -> " +
                $"{connection.TargetNodeId}.{connection.TargetPinName}({connection.TargetPinKind})。"));
        }
    }

    private static void ValidateConnectionMultiplicity(GraphExecutionPlan plan, List<GraphValidationIssue> issues)
    {
        var duplicateExecutionOutputs = plan.Connections
            .Where(connection => connection.SourcePinKind == PinKind.Execution)
            .GroupBy(connection => (connection.SourceNodeId, connection.SourcePinName))
            .Where(group => group.Count() > 1);

        foreach (var group in duplicateExecutionOutputs)
            issues.Add(Error($"执行输出引脚存在多条连线：{group.Key.SourceNodeId}.{group.Key.SourcePinName}。"));

        var duplicateDataInputs = plan.Connections
            .Where(connection => connection.TargetPinKind != PinKind.Execution)
            .GroupBy(connection => (connection.TargetNodeId, connection.TargetPinName))
            .Where(group => group.Count() > 1);

        foreach (var group in duplicateDataInputs)
            issues.Add(Error($"数据输入引脚存在多条入线：{group.Key.TargetNodeId}.{group.Key.TargetPinName}。"));
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
                    if (!IsInputConnected(plan, node.Id, "image_path") && string.IsNullOrWhiteSpace(node.ImagePath))
                        issues.Add(Warning($"找图节点未设置图片路径：{node.Title}。运行时会跳过并继续。"));
                    if (node.UseFindImageRegion && (node.FindImageRegionWidth <= 0 || node.FindImageRegionHeight <= 0))
                        issues.Add(Warning($"找图节点区域宽高无效：{node.Title}。运行时会跳过并继续。"));
                    break;
                case NodeKind.MouseClick:
                    WarnIfMissingPoint(plan, issues, node, "position", node.PositionX, node.PositionY, "鼠标点击");
                    break;
                case NodeKind.MouseMove:
                    WarnIfMissingPoint(plan, issues, node, "position", node.PositionX, node.PositionY, "鼠标移动");
                    break;
                case NodeKind.MouseDoubleClick:
                    WarnIfMissingPoint(plan, issues, node, "position", node.Number, node.Number2, "鼠标双击");
                    break;
                case NodeKind.Delay when node.DelayMs <= 0:
                    issues.Add(Warning($"延迟节点时长无效：{node.Title}。运行时会使用默认时长。"));
                    break;
                case NodeKind.Keyboard when string.IsNullOrWhiteSpace(node.Key):
                    issues.Add(Warning($"键盘节点未设置按键：{node.Title}。运行时会跳过并继续。"));
                    break;
                case NodeKind.KeyChord when string.IsNullOrWhiteSpace(node.Text):
                    issues.Add(Warning($"组合键节点未设置按键组合：{node.Title}。运行时会跳过并继续。"));
                    break;
                case NodeKind.StartProgram when string.IsNullOrWhiteSpace(node.ProgramPath):
                    issues.Add(Warning($"启动程序节点未设置程序路径：{node.Title}。运行时会跳过并继续。"));
                    break;
                case NodeKind.SelectWindow:
                    WarnIfMissingString(plan, issues, node, "process_name", node.ProcessName, "选中窗口");
                    break;
                case NodeKind.PrintLog:
                    WarnIfMissingString(plan, issues, node, "message", node.PrintLogMessage, "打印 log");
                    break;
                case NodeKind.WaitImage:
                case NodeKind.WaitImageDisappear:
                    WarnIfMissingString(plan, issues, node, "image_path", node.Text, "图像");
                    break;
                case NodeKind.WaitWindow:
                case NodeKind.CloseWindow:
                case NodeKind.WindowExists:
                    WarnIfMissingString(plan, issues, node, "process_name", node.Text, "窗口");
                    break;
                case NodeKind.SaveScreenshot:
                    if (string.Equals(node.Text2, "Manual", StringComparison.OrdinalIgnoreCase))
                        WarnIfMissingString(plan, issues, node, "path", node.Text, "截图保存");
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
            if (sourceNode?.NodeKind is NodeKind.FindImage or NodeKind.WaitImage)
            {
                issues.Add(Warning(
                    $"节点 {connection.TargetNodeId} 的坐标来自 {sourceNode.Title}。如果上游未命中，下游会跳过执行，不会回退本地默认坐标。"));
            }
        }
    }

    private static void WarnIfMissingPoint(
        GraphExecutionPlan plan,
        List<GraphValidationIssue> issues,
        GraphRuntimeNode node,
        string pinName,
        double x,
        double y,
        string nodeName)
    {
        if (!IsInputConnected(plan, node.Id, pinName) && x == 0 && y == 0)
            issues.Add(Warning($"{nodeName}节点没有有效坐标：{node.Title}。运行时会跳过并继续。"));
    }

    private static void WarnIfMissingString(
        GraphExecutionPlan plan,
        List<GraphValidationIssue> issues,
        GraphRuntimeNode node,
        string pinName,
        string? value,
        string nodeName)
    {
        if (!IsInputConnected(plan, node.Id, pinName) && string.IsNullOrWhiteSpace(value))
            issues.Add(Warning($"{nodeName}节点未设置必要文本：{node.Title}。运行时会跳过并继续。"));
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
