using AutomationStudioWpf.GraphCore;
using AutomationStudioWpf.Runtime;

namespace AutomationStudioWpf.Services;

internal sealed class FinalCodePreviewService
{
    private readonly GraphRuntimePlanBuilder _planBuilder = new();
    private readonly FinalCodePreviewGenerator _generator = new();

    public FinalCodePreviewResult Generate(
        GraphWorkspaceReadModel readModel,
        string assetId,
        string graphId)
    {
        ArgumentNullException.ThrowIfNull(readModel);
        ContentAssetSnapshot? asset = readModel.DependencyIndex.FindAsset(assetId);
        GraphSnapshot? graph = readModel.DependencyIndex.FindGraph(graphId);
        if (asset is null || graph is null || !asset.Graphs.Any(item => item.Id == graph.Id))
            return new FinalCodePreviewResult(string.Empty, "当前图表不在目标资产的只读快照中。");

        GraphReachabilityResult reachability = readModel.DependencyIndex.GetReachability(assetId, graphId);
        var issues = reachability.Issues
            .Select(issue => new GraphValidationIssue(
                issue.Severity == GraphDependencyIssueSeverity.Error
                    ? GraphValidationSeverity.Error
                    : GraphValidationSeverity.Warning,
                issue.Message))
            .ToList();
        var plansByGraphId = new Dictionary<string, GraphExecutionPlan>(StringComparer.Ordinal);
        foreach (string reachableGraphId in reachability.GraphIds)
        {
            GraphSnapshot? reachableGraph = readModel.DependencyIndex.FindGraph(reachableGraphId);
            if (reachableGraph is null)
            {
                issues.Add(new GraphValidationIssue(
                    GraphValidationSeverity.Error,
                    $"可达图表不存在：{reachableGraphId}。"));
                continue;
            }

            GraphRuntimePlanBuildResult build = _planBuilder.Build(reachableGraph);
            issues.AddRange(build.Issues);
            if (build.Plan is not null)
                plansByGraphId[reachableGraphId] = build.Plan;
        }

        if (!plansByGraphId.TryGetValue(graph.Id, out GraphExecutionPlan? plan) ||
            issues.Any(issue => issue.Severity == GraphValidationSeverity.Error))
        {
            string error = string.Join(Environment.NewLine, issues
                .Where(issue => issue.Severity == GraphValidationSeverity.Error)
                .Take(6)
                .Select(issue => issue.Message));
            return new FinalCodePreviewResult(
                string.Empty,
                string.IsNullOrWhiteSpace(error) ? "当前图表无法生成运行计划。" : error);
        }

        IReadOnlyList<GraphFunctionTarget> callableFunctions = reachability.GraphIds
            .Select(readModel.DependencyIndex.FindFunction)
            .Where(function => function is not null)
            .Cast<GraphFunctionTarget>()
            .ToList();
        var functionPlans = callableFunctions
            .Where(function => plansByGraphId.ContainsKey(function.Graph.Id))
            .GroupBy(function => function.Id, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => plansByGraphId[group.First().Graph.Id],
                StringComparer.Ordinal);
        var functionNames = callableFunctions
            .GroupBy(function => function.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Name, StringComparer.Ordinal);
        var customEvents = readModel.DependencyIndex.ResolveCustomEvents(assetId)
            .Where(item => reachability.GraphIds.Contains(item.Graph.Id) && plansByGraphId.ContainsKey(item.Graph.Id))
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    GraphCustomEventTarget item = group.First();
                    return new RuntimeCustomEventTarget(
                        item.Id,
                        item.Name,
                        item.Graph.Id,
                        plansByGraphId[item.Graph.Id],
                        item.EntryNodeId);
                },
                StringComparer.Ordinal);

        return _generator.Generate(plan, asset.Name, graph.Kind, functionPlans, functionNames, customEvents);
    }
}
