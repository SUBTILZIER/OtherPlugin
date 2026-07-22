using AutomationStudioWpf.GraphCore;

namespace AutomationStudioWpf.Services;

internal sealed record GraphCompilePipelineResult(
    bool Success,
    int UpdatedCallNodes,
    int RemovedConnections,
    IReadOnlySet<string> ChangedAssetIds,
    IReadOnlySet<string> AffectedAssetIds,
    IReadOnlySet<string> InvalidatedAssetIds,
    IReadOnlyList<string> RepairMessages,
    IReadOnlyList<GraphValidationIssue> Issues,
    GraphWorkspaceReadModel ReadModel);

internal sealed class GraphCompilePipeline
{
    private readonly GraphPreparationService _preparationService;
    private readonly GraphValidationService _validationService;
    private readonly GraphCallReferenceSyncService _referenceSyncService;
    private readonly GraphDependencyIndexBuilder _dependencyIndexBuilder = new();

    public GraphCompilePipeline(
        GraphPreparationService preparationService,
        GraphValidationService validationService,
        GraphCallReferenceSyncService referenceSyncService)
    {
        _preparationService = preparationService;
        _validationService = validationService;
        _referenceSyncService = referenceSyncService;
    }

    public GraphCompilePipelineResult CompileWorkspace(IReadOnlyList<ContentAssetViewModel> assets)
    {
        var affectedAssetIds = GetNonFolderAssets(assets)
            .Select(asset => asset.Id)
            .ToHashSet(StringComparer.Ordinal);
        GraphPreparationResult preparation = _preparationService.PrepareWorkspace(assets);
        (GraphWorkspaceSnapshot snapshot, GraphDependencyIndex index) = CreateReadModel(assets);
        GraphCallReferenceSyncResult sync = snapshot.HasErrors
            ? new GraphCallReferenceSyncResult()
            : _referenceSyncService.Sync(GetNonFolderAssets(assets), index);
        ApplySyncChanges(assets, sync);
        RebuildReadModelIfChanged(assets, sync, ref snapshot, ref index);

        IReadOnlyList<GraphValidationIssue> issues = _validationService.ValidateWorkspace(snapshot, index);
        bool success = IsSuccessful(issues);
        if (success)
        {
            foreach (ContentAssetViewModel asset in GetNonFolderAssets(assets))
            {
                foreach (GraphListItemViewModel graph in GraphPreparationService.GetCompilableGraphs(asset))
                    graph.IsCompileDirty = false;
            }
        }

        return BuildResult(preparation, sync, issues, success, affectedAssetIds, snapshot, index);
    }

    public GraphCompilePipelineResult CompileAsset(
        IReadOnlyList<ContentAssetViewModel> assets,
        ContentAssetViewModel owner)
    {
        GraphPreparationResult preparation = _preparationService.PrepareAsset(owner);
        (GraphWorkspaceSnapshot snapshot, GraphDependencyIndex index) = CreateReadModel(assets);
        HashSet<string> affectedAssetIds = GetAffectedAssetIds(owner, index);
        IReadOnlyList<ContentAssetViewModel> affectedAssets = GetNonFolderAssets(assets)
            .Where(asset => affectedAssetIds.Contains(asset.Id))
            .ToList();
        GraphCallReferenceSyncResult sync = HasSnapshotErrorsForAssets(snapshot, affectedAssetIds)
            ? new GraphCallReferenceSyncResult()
            : _referenceSyncService.Sync(affectedAssets, index);
        ApplySyncChanges(assets, sync);
        RebuildReadModelIfChanged(assets, sync, ref snapshot, ref index);

        affectedAssetIds.UnionWith(GetAffectedAssetIds(owner, index));
        IReadOnlyList<GraphValidationIssue> issues = _validationService.ValidateAssets(snapshot, affectedAssetIds, index);
        bool success = IsSuccessful(issues);
        if (success)
            ClearCompileDirty(assets, affectedAssetIds);
        else
            MarkCompileDirty(assets, affectedAssetIds);

        return BuildResult(preparation, sync, issues, success, affectedAssetIds, snapshot, index);
    }

    public GraphCompilePipelineResult CompileGraph(
        IReadOnlyList<ContentAssetViewModel> assets,
        ContentAssetViewModel owner,
        GraphListItemViewModel graph)
    {
        GraphPreparationResult preparation = _preparationService.PrepareGraph(owner, graph);
        (GraphWorkspaceSnapshot snapshot, GraphDependencyIndex index) = CreateReadModel(assets);
        GraphCallReferenceSyncResult sync = snapshot.HasErrors
            ? new GraphCallReferenceSyncResult()
            : _referenceSyncService.SyncGraph(GetNonFolderAssets(assets), owner, graph.Graph, index);
        ApplySyncChanges(assets, sync);
        RebuildReadModelIfChanged(assets, sync, ref snapshot, ref index);

        IReadOnlyList<GraphValidationIssue> issues = _validationService.ValidateGraph(snapshot, owner.Id, graph.Id, index);
        bool success = IsSuccessful(issues);
        if (success)
            graph.IsCompileDirty = false;

        return BuildResult(
            preparation,
            sync,
            issues,
            success,
            new HashSet<string>(StringComparer.Ordinal) { owner.Id },
            snapshot,
            index);
    }

    private (GraphWorkspaceSnapshot Snapshot, GraphDependencyIndex Index) CreateReadModel(
        IReadOnlyList<ContentAssetViewModel> assets)
    {
        GraphWorkspaceSnapshot snapshot = GraphWorkspaceSnapshotFactory.Create(assets);
        return (snapshot, _dependencyIndexBuilder.Build(snapshot));
    }

    private void RebuildReadModelIfChanged(
        IReadOnlyList<ContentAssetViewModel> assets,
        GraphCallReferenceSyncResult sync,
        ref GraphWorkspaceSnapshot snapshot,
        ref GraphDependencyIndex index)
    {
        if (sync.UpdatedCallNodes == 0 && sync.RemovedConnections == 0)
            return;
        (snapshot, index) = CreateReadModel(assets);
    }

    private static void ApplySyncChanges(
        IReadOnlyList<ContentAssetViewModel> assets,
        GraphCallReferenceSyncResult sync)
    {
        if (sync.ChangedAssetIds.Count == 0)
            return;

        foreach (ContentAssetViewModel asset in assets.Where(asset => sync.ChangedAssetIds.Contains(asset.Id)))
        {
            asset.IsDirty = true;
            foreach (GraphListItemViewModel graph in GraphPreparationService.GetCompilableGraphs(asset))
            {
                if (sync.ChangedGraphIds.Count > 0 && !sync.ChangedGraphIds.Contains(graph.Id))
                    continue;
                graph.IsDirty = true;
                graph.IsCompileDirty = true;
            }
        }
    }

    private static IReadOnlyList<ContentAssetViewModel> GetNonFolderAssets(
        IEnumerable<ContentAssetViewModel> assets) =>
        assets.Where(asset => asset.Kind != ContentAssetKind.Folder).ToList();

    private static bool IsSuccessful(IEnumerable<GraphValidationIssue> issues) =>
        issues.All(issue => issue.Severity != GraphValidationSeverity.Error);

    private static HashSet<string> GetAffectedAssetIds(
        ContentAssetViewModel owner,
        GraphDependencyIndex index)
    {
        var affectedAssetIds = new HashSet<string>(StringComparer.Ordinal) { owner.Id };
        affectedAssetIds.UnionWith(index.GetAffectedCallerAssetIds(
            GraphPreparationService.GetCompilableGraphs(owner).Select(graph => graph.Id)));
        return affectedAssetIds;
    }

    private static bool HasSnapshotErrorsForAssets(
        GraphWorkspaceSnapshot snapshot,
        IReadOnlySet<string> assetIds) =>
        snapshot.Issues.Any(issue =>
            issue.Severity == GraphDependencyIssueSeverity.Error &&
            IsSnapshotIssueInScope(issue.Scope, assetIds));

    private static bool IsSnapshotIssueInScope(string scope, IReadOnlySet<string> assetIds)
    {
        if (scope == "workspace")
            return true;

        return assetIds.Any(assetId =>
            string.Equals(scope, $"asset:{assetId}", StringComparison.Ordinal) ||
            scope.StartsWith($"graph:{assetId}/", StringComparison.Ordinal));
    }

    private static void ClearCompileDirty(
        IEnumerable<ContentAssetViewModel> assets,
        IReadOnlySet<string> affectedAssetIds)
    {
        foreach (ContentAssetViewModel asset in assets.Where(asset => affectedAssetIds.Contains(asset.Id)))
        {
            foreach (GraphListItemViewModel graph in GraphPreparationService.GetCompilableGraphs(asset))
                graph.IsCompileDirty = false;
        }
    }

    private static void MarkCompileDirty(
        IEnumerable<ContentAssetViewModel> assets,
        IReadOnlySet<string> affectedAssetIds)
    {
        foreach (ContentAssetViewModel asset in assets.Where(asset => affectedAssetIds.Contains(asset.Id)))
        {
            foreach (GraphListItemViewModel graph in GraphPreparationService.GetCompilableGraphs(asset))
                graph.IsCompileDirty = true;
        }
    }

    private static GraphCompilePipelineResult BuildResult(
        GraphPreparationResult preparation,
        GraphCallReferenceSyncResult sync,
        IReadOnlyList<GraphValidationIssue> issues,
        bool success,
        IReadOnlySet<string> affectedAssetIds,
        GraphWorkspaceSnapshot snapshot,
        GraphDependencyIndex index)
    {
        var changedAssetIds = new HashSet<string>(preparation.ChangedAssetIds, StringComparer.Ordinal);
        changedAssetIds.UnionWith(sync.ChangedAssetIds);
        var messages = preparation.RepairMessages.ToList();
        if (sync.UpdatedCallNodes > 0 || sync.RemovedConnections > 0)
        {
            messages.Add(
                $"已同步 {sync.UpdatedCallNodes} 个调用节点，并移除 {sync.RemovedConnections} 条失效连线。");
        }

        return new GraphCompilePipelineResult(
            success,
            sync.UpdatedCallNodes,
            sync.RemovedConnections,
            changedAssetIds,
            new HashSet<string>(affectedAssetIds, StringComparer.Ordinal),
            success
                ? new HashSet<string>(StringComparer.Ordinal)
                : new HashSet<string>(affectedAssetIds, StringComparer.Ordinal),
            messages.AsReadOnly(),
            issues,
            GraphWorkspaceReadModel.From(snapshot, index));
    }
}
