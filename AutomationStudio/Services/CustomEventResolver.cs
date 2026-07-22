using AutomationStudioWpf.Graph;
using AutomationStudioWpf.GraphCore;

namespace AutomationStudioWpf.Services;

public sealed class CustomEventResolver
{
    public IReadOnlyList<CallableCustomEventItem> Resolve(ContentAssetViewModel? owner)
    {
        if (owner is null)
            return [];

        var workspace = GraphWorkspaceSnapshotFactory.Create([owner]);
        var index = new GraphDependencyIndexBuilder().Build(workspace);
        return Resolve(index, owner.Id);
    }

    internal IReadOnlyList<CallableCustomEventItem> Resolve(
        GraphDependencyIndex index,
        string? ownerAssetId) =>
        index.ResolveCustomEvents(ownerAssetId)
            .Select(target => new CallableCustomEventItem(
                target.Id,
                target.Name,
                target.GroupName,
                target.Parameters.Select(GraphModelCopyMapper.Copy).ToList(),
                target.Graph.Id,
                target.Graph.ToMutableModel()))
            .ToList();
}
