using AutomationStudioWpf.Graph;
using AutomationStudioWpf.GraphCore;
using AutomationStudioWpf.Runtime;

namespace AutomationStudioWpf.Services;

internal sealed record GraphExecutionPreflightResult(
    GraphExecutionPlan? MainPlan,
    RuntimeAssetLibrary? AssetLibrary,
    GraphReachabilityResult Reachability,
    IReadOnlyList<GraphValidationIssue> Issues)
{
    public bool Success =>
        MainPlan is not null &&
        AssetLibrary is not null &&
        Issues.All(issue => issue.Severity != GraphValidationSeverity.Error);
}

internal sealed class GraphExecutionPreflightService
{
    private readonly GraphRuntimePlanBuilder _planBuilder = new();
    private readonly GraphValidator _validator = new();

    public GraphExecutionPreflightResult Prepare(
        GraphWorkspaceReadModel readModel,
        string scriptAssetId)
    {
        ArgumentNullException.ThrowIfNull(readModel);
        var issues = new List<GraphValidationIssue>();
        ContentAssetSnapshot? asset = readModel.DependencyIndex.FindAsset(scriptAssetId);
        if (asset?.Kind != ContentAssetKind.Script)
        {
            issues.Add(Error("只能执行脚本资产。"));
            return Failure(issues);
        }

        GraphSnapshot? mainGraph = readModel.DependencyIndex.GetMainEvent(scriptAssetId);
        if (mainGraph is null)
        {
            issues.Add(Error("脚本没有主事件图。"));
            return Failure(issues);
        }

        GraphReachabilityResult reachability = readModel.GetMainEventReachability(scriptAssetId);
        AddDependencyIssues(issues, reachability.Issues);
        AddRelevantWorkspaceIssues(
            issues,
            readModel.Snapshot.Issues.Concat(readModel.DependencyIndex.Issues),
            scriptAssetId,
            reachability.GraphIds);

        var plansByGraphId = new Dictionary<string, GraphExecutionPlan>(StringComparer.Ordinal);
        foreach (string graphId in reachability.GraphIds)
        {
            GraphSnapshot? graph = readModel.DependencyIndex.FindGraph(graphId);
            if (graph is null)
            {
                issues.Add(Error($"可达图表不存在：{graphId}。"));
                continue;
            }

            GraphRuntimePlanBuildResult build = _planBuilder.Build(graph);
            issues.AddRange(build.Issues);
            if (build.Plan is null)
                continue;

            GraphPlanEntryKind entryKind = graph.Id == mainGraph.Id
                ? GraphPlanEntryKind.MainEvent
                : graph.Kind == GraphAssetKind.Function
                    ? GraphPlanEntryKind.Function
                    : GraphPlanEntryKind.CustomEvent;
            GraphValidationResult validation = _validator.Validate(build.Plan, entryKind);
            issues.AddRange(validation.Issues.Select(issue => issue with
            {
                Message = $"{graph.Name}: {issue.Message}",
            }));
            plansByGraphId[graph.Id] = build.Plan;
        }

        plansByGraphId.TryGetValue(mainGraph.Id, out GraphExecutionPlan? mainPlan);
        RuntimeAssetLibrary? library = BuildRuntimeAssetLibrary(
            readModel.DependencyIndex,
            scriptAssetId,
            reachability.GraphIds,
            plansByGraphId,
            issues);

        return new GraphExecutionPreflightResult(
            mainPlan,
            library,
            reachability,
            DistinctIssues(issues));
    }

    private static RuntimeAssetLibrary? BuildRuntimeAssetLibrary(
        GraphDependencyIndex index,
        string scriptAssetId,
        IReadOnlySet<string> reachableGraphIds,
        IReadOnlyDictionary<string, GraphExecutionPlan> plansByGraphId,
        ICollection<GraphValidationIssue> issues)
    {
        var functions = new Dictionary<string, GraphExecutionPlan>(StringComparer.Ordinal);
        foreach (GraphFunctionTarget target in reachableGraphIds
                     .Select(index.FindFunction)
                     .Where(target => target is not null)
                     .Cast<GraphFunctionTarget>())
        {
            if (!plansByGraphId.TryGetValue(target.Graph.Id, out GraphExecutionPlan? plan))
            {
                issues.Add(Error($"函数运行计划不可用：{target.Name}。"));
                continue;
            }
            if (!functions.TryAdd(target.Id, plan))
                issues.Add(Error($"函数 ID 重复：{target.Id}。"));
        }

        var customEvents = new Dictionary<string, RuntimeCustomEventTarget>(StringComparer.Ordinal);
        foreach (GraphCustomEventTarget target in index.ResolveCustomEvents(scriptAssetId)
                     .Where(target => reachableGraphIds.Contains(target.Graph.Id)))
        {
            if (!plansByGraphId.TryGetValue(target.Graph.Id, out GraphExecutionPlan? plan))
            {
                issues.Add(Error($"自定义事件运行计划不可用：{target.Name}。"));
                continue;
            }
            GraphRuntimeNode? entry = plan.Index.GetNode(target.EntryNodeId);
            if (entry?.NodeKind != NodeKind.CustomEvent ||
                !string.Equals(entry.CustomEventId, target.Id, StringComparison.Ordinal))
            {
                issues.Add(Error($"自定义事件入口无效：{target.Name}。"));
                continue;
            }
            if (!customEvents.TryAdd(
                    target.Id,
                    new RuntimeCustomEventTarget(target.Id, target.Name, target.Graph.Id, plan, target.EntryNodeId)))
            {
                issues.Add(Error($"自定义事件 ID 重复：{target.Id}。"));
            }
        }

        return issues.Any(issue => issue.Severity == GraphValidationSeverity.Error)
            ? null
            : new RuntimeAssetLibrary(functions, customEvents);
    }

    private static void AddRelevantWorkspaceIssues(
        ICollection<GraphValidationIssue> destination,
        IEnumerable<GraphDependencyIssue> source,
        string assetId,
        IReadOnlySet<string> reachableGraphIds)
    {
        foreach (GraphDependencyIssue issue in source)
        {
            if (!IsRelevantScope(issue.Scope, assetId, reachableGraphIds))
                continue;
            destination.Add(new GraphValidationIssue(
                issue.Severity == GraphDependencyIssueSeverity.Error
                    ? GraphValidationSeverity.Error
                    : GraphValidationSeverity.Warning,
                $"{issue.Scope}: {issue.Message}"));
        }
    }

    private static bool IsRelevantScope(string scope, string assetId, IReadOnlySet<string> graphIds)
    {
        if (string.Equals(scope, "workspace", StringComparison.Ordinal) ||
            scope.StartsWith($"asset:{assetId}", StringComparison.Ordinal))
        {
            return true;
        }

        return graphIds.Any(graphId => scope.Contains($"/{graphId}", StringComparison.Ordinal));
    }

    private static void AddDependencyIssues(
        ICollection<GraphValidationIssue> destination,
        IEnumerable<GraphDependencyIssue> source)
    {
        foreach (GraphDependencyIssue issue in source)
        {
            destination.Add(new GraphValidationIssue(
                issue.Severity == GraphDependencyIssueSeverity.Error
                    ? GraphValidationSeverity.Error
                    : GraphValidationSeverity.Warning,
                $"{issue.Scope}: {issue.Message}"));
        }
    }

    private static IReadOnlyList<GraphValidationIssue> DistinctIssues(IEnumerable<GraphValidationIssue> issues) =>
        issues
            .DistinctBy(issue => (issue.Severity, issue.Message))
            .ToList()
            .AsReadOnly();

    private static GraphExecutionPreflightResult Failure(IReadOnlyList<GraphValidationIssue> issues) =>
        new(
            null,
            null,
            new GraphReachabilityResult(new HashSet<string>(), false, []),
            issues);

    private static GraphValidationIssue Error(string message) =>
        new(GraphValidationSeverity.Error, message);
}
