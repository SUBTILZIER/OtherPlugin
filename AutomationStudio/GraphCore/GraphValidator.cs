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
        ValidateNodeNumbers(plan, issues);
        ValidateConnectionEndpoints(plan, issues);
        ValidateConnectionTypes(plan, issues);
        ValidateConnectionMultiplicity(plan, issues);
        ValidateMultiThreadNodes(plan, issues);
        ValidateExecutionReachability(plan, issues);
        ValidateToDoTargets(plan, issues);
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

    private static void ValidateNodeNumbers(GraphExecutionPlan plan, List<GraphValidationIssue> issues)
    {
        foreach (var group in plan.Nodes
                     .Where(node => NodeTraits.ShouldAssignNodeNumber(node.NodeKind) && !string.IsNullOrWhiteSpace(node.NodeNumber))
                     .GroupBy(node => node.NodeNumber, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            issues.Add(Error($"节点编号重复：{group.Key}。"));
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

    private static void ValidateMultiThreadNodes(GraphExecutionPlan plan, List<GraphValidationIssue> issues)
    {
        foreach (var node in plan.Nodes.Where(node => node.NodeKind == NodeKind.MultiThread))
        {
            if (node.ThreadOutputCount < MultiThreadNodeViewModel.MinimumThreadOutputCount)
                issues.Add(Error($"多线程节点输出数量无效：{node.Title}。至少需要 {MultiThreadNodeViewModel.MinimumThreadOutputCount} 个线程输出。"));
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
        foreach (var customEvent in plan.Nodes.Where(node => node.NodeKind == NodeKind.CustomEvent))
        {
            if (reachable.Add(customEvent.Id))
                queue.Enqueue(customEvent.Id);
        }

        var execEdges = plan.Connections
            .Where(connection => connection.SourcePinKind == PinKind.Execution && connection.TargetPinKind == PinKind.Execution)
            .GroupBy(connection => connection.SourceNodeId)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        var nodesById = plan.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);

        while (queue.Count > 0)
        {
            string nodeId = queue.Dequeue();
            if (!execEdges.TryGetValue(nodeId, out var outgoing))
                outgoing = [];

            foreach (var connection in outgoing)
            {
                if (reachable.Add(connection.TargetNodeId))
                    queue.Enqueue(connection.TargetNodeId);
            }

            if (nodesById.TryGetValue(nodeId, out var node) && node.NodeKind == NodeKind.ToDo)
            {
                foreach (var target in FindStaticToDoTargets(plan, node))
                {
                    if (reachable.Add(target.Id))
                        queue.Enqueue(target.Id);
                }
            }
        }

        // Isolated nodes are valid scratch/backup nodes on the canvas; do not log them as warnings.
    }

    private static void ValidateToDoTargets(GraphExecutionPlan plan, List<GraphValidationIssue> issues)
    {
        foreach (var node in plan.Nodes.Where(node => node.NodeKind == NodeKind.ToDo))
        {
            bool titleConnected = IsInputConnected(plan, node.Id, "target_title");
            bool numberConnected = IsInputConnected(plan, node.Id, "target_number");
            if (titleConnected || numberConnected)
                continue;

            var targetKey = ResolveStaticToDoTarget(plan, node);
            if (targetKey is null)
            {
                issues.Add(Error($"ToDo 节点缺少目标节点名或编号：{node.Title}。"));
                continue;
            }

            var matches = FindStaticToDoTargets(plan, node).ToList();
            if (matches.Count == 0)
                issues.Add(Error($"ToDo 目标不存在：{node.Title} -> {targetKey.Value.Title} {targetKey.Value.NodeNumber}。"));
            else if (matches.Count > 1)
                issues.Add(Error($"ToDo 目标不唯一：{node.Title} -> {targetKey.Value.Title} {targetKey.Value.NodeNumber}。"));
            else if (matches[0].Id == node.Id)
                issues.Add(Error($"ToDo 不能跳转到自身：{node.Title}。"));
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

    private static IReadOnlyList<GraphRuntimeNode> FindStaticToDoTargets(GraphExecutionPlan plan, GraphRuntimeNode node)
    {
        if (IsInputConnected(plan, node.Id, "target_title") ||
            IsInputConnected(plan, node.Id, "target_number"))
        {
            return [];
        }

        var targetKey = ResolveStaticToDoTarget(plan, node);
        if (targetKey is null)
            return [];

        return plan.Index
            .FindNodesByTitleAndNumber(targetKey.Value.Title, targetKey.Value.NodeNumber)
            .Where(candidate => NodeTraits.IsToDoTarget(candidate.NodeKind))
            .ToList();
    }

    private static (string Title, string NodeNumber)? ResolveStaticToDoTarget(GraphExecutionPlan plan, GraphRuntimeNode node)
    {
        if (!string.IsNullOrWhiteSpace(node.TargetNodeTitle) &&
            !string.IsNullOrWhiteSpace(node.TargetNodeNumber))
        {
            return (node.TargetNodeTitle, node.TargetNodeNumber);
        }

        if (!string.IsNullOrWhiteSpace(node.TargetNodeId) &&
            plan.Index.GetNode(node.TargetNodeId!) is { } target &&
            NodeTraits.IsToDoTarget(target.NodeKind) &&
            target.Id != node.Id &&
            !string.IsNullOrWhiteSpace(target.Title) &&
            !string.IsNullOrWhiteSpace(target.NodeNumber))
        {
            return (target.Title, target.NodeNumber);
        }

        return null;
    }

    private static GraphValidationIssue Warning(string message) =>
        new(GraphValidationSeverity.Warning, message);

    private static GraphValidationIssue Error(string message) =>
        new(GraphValidationSeverity.Error, message);
}
