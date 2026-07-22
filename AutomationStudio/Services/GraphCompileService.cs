using AutomationStudioWpf.GraphCore;

namespace AutomationStudioWpf.Services;

public sealed class GraphCompileResult
{
    public bool Success { get; init; }

    public int UpdatedCallNodes { get; init; }

    public int RemovedConnections { get; init; }

    public IReadOnlySet<string> ChangedAssetIds { get; init; } = new HashSet<string>();

    public IReadOnlySet<string> AffectedAssetIds { get; init; } = new HashSet<string>();

    public IReadOnlySet<string> InvalidatedAssetIds { get; init; } = new HashSet<string>();

    public IReadOnlyList<string> RepairMessages { get; init; } = [];

    public IReadOnlyList<GraphValidationIssue> Issues { get; init; } = [];

    internal GraphWorkspaceReadModel? ReadModel { get; init; }
}

public sealed class GraphCompileService
{
    private readonly GraphCompilePipeline _pipeline;

    public GraphCompileService()
        : this(new CallableGraphResolver())
    {
    }

    public GraphCompileService(CallableGraphResolver callableResolver)
    {
        var customEventResolver = new CustomEventResolver();
        var referenceSyncService = new GraphCallReferenceSyncService(callableResolver, customEventResolver);
        _pipeline = new GraphCompilePipeline(
            new GraphPreparationService(),
            new GraphValidationService(),
            referenceSyncService);
    }

    public GraphCompileResult Compile(IEnumerable<ContentAssetViewModel> assets) =>
        ToPublicResult(_pipeline.CompileWorkspace(assets.ToList()));

    public GraphCompileResult CompileGraph(
        IEnumerable<ContentAssetViewModel> assets,
        ContentAssetViewModel owner,
        GraphListItemViewModel item) =>
        ToPublicResult(_pipeline.CompileGraph(assets.ToList(), owner, item));

    public GraphCompileResult CompileAsset(
        IEnumerable<ContentAssetViewModel> assets,
        ContentAssetViewModel owner) =>
        ToPublicResult(_pipeline.CompileAsset(assets.ToList(), owner));

    private static GraphCompileResult ToPublicResult(GraphCompilePipelineResult result) => new()
    {
        Success = result.Success,
        UpdatedCallNodes = result.UpdatedCallNodes,
        RemovedConnections = result.RemovedConnections,
        ChangedAssetIds = result.ChangedAssetIds,
        AffectedAssetIds = result.AffectedAssetIds,
        InvalidatedAssetIds = result.InvalidatedAssetIds,
        RepairMessages = result.RepairMessages,
        Issues = result.Issues,
        ReadModel = result.ReadModel,
    };
}
