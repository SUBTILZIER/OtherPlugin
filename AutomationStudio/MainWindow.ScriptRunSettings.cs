using System.Windows;
using AutomationStudioWpf.Interaction;
using AutomationStudioWpf.Services;

namespace AutomationStudioWpf;

public partial class MainWindow
{
    private void ShowSelectedScriptProperties()
    {
        if ((_contentBrowserContextTargetAsset ?? GetSelectedContentAsset()) is not { Kind: ContentAssetKind.Script } asset)
            return;

        _contentBrowserContextTargetAsset = null;
        ShowScriptProperties(asset);
    }

    private bool ShowScriptProperties(ContentAssetViewModel asset)
    {
        if (asset.Kind != ContentAssetKind.Script)
            return false;

        asset.RunSettings.Normalize();
        var dialog = new ScriptPropertiesWindow(this, asset.Name, asset.RunSettings, _hotkeyCaptureCoordinator);
        if (dialog.ShowDialog() != true)
            return false;

        return ApplyScriptRunSettings(asset, dialog.Result);
    }

    private bool ApplyScriptRunSettings(ContentAssetViewModel asset, ScriptRunSettings newSettings)
    {
        if (_scriptRunManager.IsHotkeyRunActive(asset))
        {
            ThemedDialog.Show(
                this,
                "脚本运行期间不能修改热键或循环设置。请先使用终止热键或顶部停止按钮结束脚本。",
                "脚本正在运行",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        newSettings.Normalize();
        var conflicts = _scriptHotkeyService.Validate(ContentBrowserItems, asset, newSettings);
        if (conflicts.Count > 0)
        {
            ThemedDialog.Show(this, string.Join(Environment.NewLine, conflicts), "热键冲突", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        asset.RunSettings = newSettings;
        asset.IsDirty = true;
        PersistAssetLibrary();
        RefreshScriptHotkeys();
        RefreshScriptPropertiesSummaries(asset);
        SetStatus($"已保存脚本属性：{asset.Name}");
        return true;
    }

    private void RefreshScriptPropertiesSummaries(ContentAssetViewModel asset)
    {
        if (EmptyEditorPanel.Child is ScriptPropertiesSummaryControl emptySummary &&
            ReferenceEquals(emptySummary.Asset, asset))
        {
            emptySummary.Refresh();
        }

        foreach (var session in _editorSessions)
        {
            if (!ReferenceEquals(session.ContentAsset, asset) ||
                session.Surface?.ScriptPropertiesSummaryHost.Content is not ScriptPropertiesSummaryControl inspectorSummary ||
                !ReferenceEquals(inspectorSummary.Asset, asset))
            {
                continue;
            }

            inspectorSummary.Refresh();
        }
    }

    private void RefreshScriptHotkeys()
    {
        var refreshResult = _scriptHotkeyService.Refresh(ContentBrowserItems);

        foreach (string conflict in refreshResult.Conflicts)
            Logging.Logger.Warn(conflict);

        var hookFailures = new List<string>();
        if (refreshResult.Hooks.Keyboard.Failed)
            hookFailures.Add(refreshResult.Hooks.Keyboard.FormatFailure("键盘全局热键监听"));
        if (refreshResult.Hooks.Mouse.Failed)
            hookFailures.Add(refreshResult.Hooks.Mouse.FormatFailure("鼠标全局热键监听"));

        var newFailures = hookFailures
            .Where(message => _reportedHookFailures.Add(message))
            .ToArray();
        foreach (string failure in newFailures)
            Logging.Logger.Error(failure);

        if (newFailures.Length > 0)
        {
            string message = string.Join(Environment.NewLine, newFailures);
            SetStatus("部分全局热键监听安装失败。");
            ThemedDialog.Show(this, message, "全局热键不可用", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        else if (refreshResult.Conflicts.Count > 0)
        {
            SetStatus("存在热键冲突；冲突项未注册。请修改脚本属性。");
        }
    }

    private void HandleScriptHotkey(ScriptHotkeyTrigger trigger)
    {
        if (_isClosing)
            return;

        if (trigger.Action == ScriptHotkeyAction.Stop)
        {
            PlayHotkeyTone(400, 300);
            _scriptRunManager.StopFromHotkey(trigger.Asset);
            return;
        }

        PlayHotkeyTone(800, 150);
        _ = _scriptRunManager.StartFromHotkeyAsync(trigger.Asset);
    }

    private static void PlayHotkeyTone(int frequency, int durationMs)
    {
        _ = Task.Run(() =>
        {
            try
            {
                Console.Beep(frequency, durationMs);
            }
            catch (Exception)
            {
            }
        });
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
