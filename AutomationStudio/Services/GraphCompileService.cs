using AutomationStudioWpf.Graph;
using AutomationStudioWpf.GraphCore;

namespace AutomationStudioWpf.Services;

public sealed class GraphCompileResult
{
    public bool Success { get; init; }
    public int UpdatedCallNodes { get; init; }
    public int RemovedConnections { get; init; }
    public IReadOnlySet<string> ChangedAssetIds { get; init; } = new HashSet<string>();
    public IReadOnlyList<GraphValidationIssue> Issues { get; init; } = [];
}

public sealed class GraphCompileService
{
    private readonly GraphCallReferenceSyncService _referenceSyncService = new();

    public GraphCompileResult Compile(IEnumerable<ContentAssetViewModel> assets)
    {
        var assetList = assets.ToList();
        var sync = _referenceSyncService.Sync(assetList);

        foreach (var item in assetList
                     .Where(asset => asset.Kind != ContentAssetKind.Folder)
                     .SelectMany(asset => asset.EventGraphs.Concat(asset.Functions).Concat(asset.Macros)))
        {
            item.IsCompileDirty = false;
        }

        return new GraphCompileResult
        {
            Success = true,
            UpdatedCallNodes = sync.UpdatedCallNodes,
            RemovedConnections = sync.RemovedConnections,
            ChangedAssetIds = sync.ChangedAssetIds,
            Issues = [],
        };
    }
}
