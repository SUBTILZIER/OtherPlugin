using System.Windows;
using System.Linq;
using AutomationStudioWpf.Controls;
using AutomationStudioWpf.Interaction;
using AutomationStudioWpf.Services;

namespace AutomationStudioWpf;

public partial class MainWindow
{
    private ScriptPropertiesSummaryControl CreateScriptPropertiesSummary(ContentAssetViewModel asset, bool overview = false) =>
        new(
            asset,
            ApplyScriptRunSettings,
            _hotkeyCaptureCoordinator,
            enabled => SetScriptAssetEnabled(asset, enabled),
            overview ? () => OpenOrActivateAsset(asset) : null,
            overview ? () => CompileActiveAsset(showPrompt: false) : null,
            overview ? () => _ = RunActiveScriptFromToolbarAsync() : null);

    private void ShowContentAssetPreviewIfIdle(ContentAssetViewModel? asset)
    {
        if (_activeEditorSession is not null || _editorSessions.Count > 0)
            return;

        if (asset is { Kind: ContentAssetKind.Script })
        {
            ShowScriptPropertiesInEmptyEditorPanel(asset);
            return;
        }

        RestoreEmptyEditorDefaultPanelIfIdle();
    }

    private void ShowScriptPropertiesInEmptyEditorPanel(ContentAssetViewModel asset)
    {
        EnsureEmptyEditorPanelDefaultChild();
        EmptyEditorPanel.Child = CreateScriptPropertiesSummary(asset, overview: true);
        EmptyEditorPanel.Visibility = Visibility.Visible;
    }

    private void RestoreEmptyEditorDefaultPanelIfIdle()
    {
        if (_activeEditorSession is not null || _editorSessions.Count > 0)
            return;

        RestoreMainEditorDefaultPanel();
        EmptyEditorPanel.Visibility = Visibility.Visible;
    }

    private void HideScriptPropertiesInInspector()
    {
        if (TryGetActiveEditorSurface() is not { } surface)
            return;

        HideScriptPropertiesInInspector(surface);
    }

    private bool TryShowActiveScriptPropertiesInInspector()
    {
        if (_activeContentAsset is not { Kind: ContentAssetKind.Script } asset ||
            TryGetActiveEditorSurface() is not { } surface ||
            _editorService.Nodes.Any(node => node.IsSelected))
        {
            return false;
        }

        return TryShowScriptPropertiesInInspector(_activeEditorSession, surface);
    }

    internal void HideScriptPropertiesInInspector(EditorSurfaceControl surface)
    {
        surface.ScriptPropertiesSummaryHost.Content = null;
        surface.ScriptPropertiesSummaryHost.Visibility = Visibility.Collapsed;
        surface.NodeBasicsSection.Visibility = Visibility.Visible;
        surface.NodeBasicsPanel.Visibility = Visibility.Visible;
    }

    internal bool TryShowScriptPropertiesInInspector(EditorSessionViewModel? session, EditorSurfaceControl surface)
    {
        if (session?.ContentAsset is not { Kind: ContentAssetKind.Script } asset ||
            session.EditorService.Nodes.Any(node => node.IsSelected))
        {
            return false;
        }

        surface.InspectorHintTextBlock.Text = $"当前脚本：{asset.Name}";
        surface.NodeBasicsSection.Visibility = Visibility.Collapsed;
        surface.NodeBasicsPanel.Visibility = Visibility.Collapsed;
        surface.ScriptPropertiesSummaryHost.Content = CreateScriptPropertiesSummary(asset);
        surface.ScriptPropertiesSummaryHost.Visibility = Visibility.Visible;
        return true;
    }

    private void RefreshVisibleScriptPropertiesSummaries()
    {
        if (EmptyEditorPanel.Child is ScriptPropertiesSummaryControl emptySummary)
            emptySummary.Refresh();

        foreach (var session in _editorSessions)
        {
            if (session.Surface?.ScriptPropertiesSummaryHost.Content is ScriptPropertiesSummaryControl inspectorSummary)
                inspectorSummary.Refresh();
        }
    }
}
