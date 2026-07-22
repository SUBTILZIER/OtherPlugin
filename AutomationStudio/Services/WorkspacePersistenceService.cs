namespace AutomationStudioWpf.Services;

/// <summary>
/// Owns the single asset-library persistence entry point.
/// UI concerns such as prompts and status text stay in MainWindow.
/// </summary>
internal sealed class WorkspacePersistenceService
{
    private readonly GraphLibraryService _repository;
    private readonly Func<IEnumerable<ContentAssetViewModel>> _getAssets;
    private readonly Func<string?> _getSelectedAssetId;
    private readonly Action _refreshHotkeys;

    public WorkspacePersistenceService(
        GraphLibraryService repository,
        Func<IEnumerable<ContentAssetViewModel>> getAssets,
        Func<string?> getSelectedAssetId,
        Action refreshHotkeys)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _getAssets = getAssets ?? throw new ArgumentNullException(nameof(getAssets));
        _getSelectedAssetId = getSelectedAssetId ?? throw new ArgumentNullException(nameof(getSelectedAssetId));
        _refreshHotkeys = refreshHotkeys ?? throw new ArgumentNullException(nameof(refreshHotkeys));
    }

    public bool TryPersist(out Exception? error)
    {
        try
        {
            _repository.SaveContentLibrary(_getAssets(), _getSelectedAssetId());
            _refreshHotkeys();
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex;
            return false;
        }
    }
}
