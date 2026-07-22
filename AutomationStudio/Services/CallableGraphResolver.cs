using AutomationStudioWpf.GraphCore;

namespace AutomationStudioWpf.Services;

public sealed class CallableGraphResolver
{
    public IReadOnlyList<CallableGraphItem> ResolveFunctions(
        IEnumerable<ContentAssetViewModel> assets,
        ContentAssetViewModel? activeAsset)
    {
        var workspace = GraphWorkspaceSnapshotFactory.Create(assets);
        var index = new GraphDependencyIndexBuilder().Build(workspace);
        return ResolveFunctions(index, activeAsset?.Id);
    }

    internal IReadOnlyList<CallableGraphItem> ResolveFunctions(
        GraphDependencyIndex index,
        string? activeAssetId) =>
        index.ResolveFunctions(activeAssetId)
            .Select(target => new CallableGraphItem(
                target.Id,
                target.Name,
                target.GroupName,
                target.Graph.ToMutableModel()))
            .ToList();
}
