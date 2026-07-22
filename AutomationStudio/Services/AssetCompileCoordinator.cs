using AutomationStudioWpf.Interaction;

namespace AutomationStudioWpf.Services;

/// <summary>
/// Coordinates commit plus graph compilation. It never writes the asset library.
/// </summary>
internal sealed class AssetCompileCoordinator
{
    private readonly GraphCompileService _compiler;
    private readonly WorkspaceCommitService _commitService;
    private readonly Func<IEnumerable<EditorSessionViewModel>> _getSessions;
    private readonly Func<EditorSessionViewModel?> _getActiveSession;
    private readonly Func<ContentAssetViewModel?> _getFallbackActiveAsset;
    private readonly Func<IEnumerable<ContentAssetViewModel>> _getAssets;
    private readonly Action _applyInspectorChanges;

    public AssetCompileCoordinator(
        GraphCompileService compiler,
        WorkspaceCommitService commitService,
        Func<IEnumerable<EditorSessionViewModel>> getSessions,
        Func<EditorSessionViewModel?> getActiveSession,
        Func<ContentAssetViewModel?> getFallbackActiveAsset,
        Func<IEnumerable<ContentAssetViewModel>> getAssets,
        Action applyInspectorChanges)
    {
        _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
        _commitService = commitService ?? throw new ArgumentNullException(nameof(commitService));
        _getSessions = getSessions ?? throw new ArgumentNullException(nameof(getSessions));
        _getActiveSession = getActiveSession ?? throw new ArgumentNullException(nameof(getActiveSession));
        _getFallbackActiveAsset = getFallbackActiveAsset ?? throw new ArgumentNullException(nameof(getFallbackActiveAsset));
        _getAssets = getAssets ?? throw new ArgumentNullException(nameof(getAssets));
        _applyInspectorChanges = applyInspectorChanges ?? throw new ArgumentNullException(nameof(applyInspectorChanges));
    }

    public ContentAssetViewModel? ResolveActiveAsset() =>
        _getActiveSession()?.ContentAsset ?? _getFallbackActiveAsset();

    public GraphCompileResult CompileAsset(ContentAssetViewModel asset, bool commitSessions)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (commitSessions)
            CommitAllSessions();
        return _compiler.CompileAsset(_getAssets().ToList(), asset);
    }

    public GraphCompileResult CompileAll(bool commitSessions)
    {
        if (commitSessions)
            CommitAllSessions();
        return _compiler.Compile(_getAssets().ToList());
    }

    private void CommitAllSessions()
    {
        _commitService.CommitAll(
            _getSessions(),
            _getActiveSession(),
            applyInspectorForActive: true,
            _applyInspectorChanges);
    }
}
