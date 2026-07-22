using AutomationStudioWpf.Interaction;
using AutomationStudioWpf.GraphCore;

namespace AutomationStudioWpf.Services;

internal enum ScriptExecutionStartResult
{
    Started,
    NoScriptAsset,
    CompilationNotReady,
}

/// <summary>
/// Routes a toolbar/manual execution request to the current execution controller.
/// Controller lifetime may change when the active session changes, so resolution is lazy.
/// </summary>
internal sealed class ScriptExecutionCoordinator
{
    private readonly Func<ExecutionController> _getExecutionController;
    private readonly Func<ContentAssetViewModel?> _getActiveAsset;
    private readonly Func<ContentAssetViewModel, GraphWorkspaceReadModel?> _prepareReadModel;

    public ScriptExecutionCoordinator(
        Func<ExecutionController> getExecutionController,
        Func<ContentAssetViewModel?> getActiveAsset,
        Func<ContentAssetViewModel, GraphWorkspaceReadModel?> prepareReadModel)
    {
        _getExecutionController = getExecutionController ?? throw new ArgumentNullException(nameof(getExecutionController));
        _getActiveAsset = getActiveAsset ?? throw new ArgumentNullException(nameof(getActiveAsset));
        _prepareReadModel = prepareReadModel ?? throw new ArgumentNullException(nameof(prepareReadModel));
    }

    public async Task<ScriptExecutionStartResult> RunActiveAsync()
    {
        ContentAssetViewModel? asset = _getActiveAsset();
        if (asset?.Kind != ContentAssetKind.Script)
            return ScriptExecutionStartResult.NoScriptAsset;

        GraphWorkspaceReadModel? readModel = _prepareReadModel(asset);
        if (readModel is null)
            return ScriptExecutionStartResult.CompilationNotReady;

        await _getExecutionController().RunAsync(readModel, asset.Id);
        return ScriptExecutionStartResult.Started;
    }
}
