using AutomationStudioWpf.GraphCore;

namespace AutomationStudioWpf.Services;

/// <summary>
/// Creates one immutable workspace read model for a single compile, preview, or run operation.
/// The model is intentionally not cached across UI edits.
/// </summary>
internal sealed class WorkspaceReadModelService
{
    private readonly Func<IEnumerable<ContentAssetViewModel>> _getAssets;

    public WorkspaceReadModelService(Func<IEnumerable<ContentAssetViewModel>> getAssets)
    {
        _getAssets = getAssets ?? throw new ArgumentNullException(nameof(getAssets));
    }

    public GraphWorkspaceReadModel Create() => GraphWorkspaceReadModel.Create(_getAssets());
}
