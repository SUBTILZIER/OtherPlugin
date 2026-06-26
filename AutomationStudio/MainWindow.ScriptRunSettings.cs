using System.Windows;
using AutomationStudioWpf.Interaction;
using AutomationStudioWpf.Services;
using WpfMessageBox = System.Windows.MessageBox;

namespace AutomationStudioWpf;

public partial class MainWindow
{
    private void ShowSelectedScriptProperties()
    {
        if ((_contentBrowserContextTargetAsset ?? GetSelectedContentAsset()) is not { Kind: ContentAssetKind.Script } asset)
            return;

        _contentBrowserContextTargetAsset = null;
        asset.RunSettings.Normalize();
        var dialog = new ScriptPropertiesWindow(this, asset.Name, asset.RunSettings);
        if (dialog.ShowDialog() != true)
            return;

        var newSettings = dialog.Result;
        newSettings.Normalize();
        var conflicts = _scriptHotkeyService.Validate(ContentBrowserItems, asset, newSettings);
        if (conflicts.Count > 0)
        {
            WpfMessageBox.Show(this, string.Join(Environment.NewLine, conflicts), "热键冲突", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        asset.RunSettings = newSettings;
        asset.IsDirty = true;
        PersistAssetLibrary();
        RefreshScriptHotkeys();
        SetStatus($"已保存脚本属性：{asset.Name}");
    }

    private void RefreshScriptHotkeys()
    {
        _scriptHotkeyService.Refresh(ContentBrowserItems);
    }

    private void HandleScriptHotkey(ScriptHotkeyTrigger trigger)
    {
        if (_isClosing)
            return;

        if (trigger.Action == ScriptHotkeyAction.Stop)
        {
            Console.Beep(400, 300);
            _scriptRunManager.Stop(trigger.Asset);
            return;
        }

        Console.Beep(800, 150);
        _ = _scriptRunManager.StartAsync(trigger.Asset);
    }

    private async Task<bool> CompileScriptAssetForRunAsync(ContentAssetViewModel asset, CancellationToken ct)
    {
        CommitInspectorAndSnapshotAllSessions();
        ct.ThrowIfCancellationRequested();
        var result = _graphCompileService.CompileAsset(ContentBrowserItems, asset);
        foreach (var item in ContentBrowserItems.Where(item => result.ChangedAssetIds.Contains(item.Id)))
            item.IsDirty = true;

        if (!HandleCompileResult(result, showPrompt: false))
            return false;

        foreach (var session in _editorSessions.Where(session => ReferenceEquals(session.ContentAsset, asset)))
            SyncSessionGraphStateFromAsset(session);

        PersistAssetLibrary();
        UpdateGraphSectionVisibility();
        UpdateEditorSessionChrome();
        await Task.CompletedTask;
        return true;
    }

    private IEnumerable<CallableGraphItem> GetRuntimeCallableFunctionsForAsset(ContentAssetViewModel asset)
    {
        CommitAllSessionsToAssets(applyInspectorForActive: true);
        return _callableGraphResolver.ResolveFunctions(ContentBrowserItems, asset);
    }
}
