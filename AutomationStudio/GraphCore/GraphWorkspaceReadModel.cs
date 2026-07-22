using AutomationStudioWpf.Services;

namespace AutomationStudioWpf.GraphCore;

internal sealed class GraphWorkspaceReadModel
{
    private readonly Dictionary<string, GraphReachabilityResult> _mainEventReachabilityByAssetId = new(StringComparer.Ordinal);

    private GraphWorkspaceReadModel(
        GraphWorkspaceSnapshot snapshot,
        GraphDependencyIndex dependencyIndex)
    {
        Snapshot = snapshot;
        DependencyIndex = dependencyIndex;
    }

    public GraphWorkspaceSnapshot Snapshot { get; }

    public GraphDependencyIndex DependencyIndex { get; }

    public GraphReachabilityResult GetMainEventReachability(string assetId)
    {
        if (!_mainEventReachabilityByAssetId.TryGetValue(assetId, out GraphReachabilityResult? result))
        {
            result = DependencyIndex.GetMainEventReachability(assetId);
            _mainEventReachabilityByAssetId[assetId] = result;
        }
        return result;
    }

    public static GraphWorkspaceReadModel Create(IEnumerable<ContentAssetViewModel> assets)
    {
        GraphWorkspaceSnapshot snapshot = GraphWorkspaceSnapshotFactory.Create(assets);
        return new GraphWorkspaceReadModel(snapshot, new GraphDependencyIndexBuilder().Build(snapshot));
    }

    internal static GraphWorkspaceReadModel From(
        GraphWorkspaceSnapshot snapshot,
        GraphDependencyIndex dependencyIndex) =>
        new(snapshot, dependencyIndex);
}
