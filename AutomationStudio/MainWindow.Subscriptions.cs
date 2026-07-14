using System.Collections.Specialized;
using AutomationStudioWpf.Logging;
using AutomationStudioWpf.Services;

namespace AutomationStudioWpf;

public partial class MainWindow
{
    private bool _windowSubscriptionsDisposed;

    private void LoggerEntries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        _logPanelController.HandleEntriesChanged(e);

    private void DisposeWindowSubscriptions()
    {
        if (_windowSubscriptionsDisposed)
            return;

        _windowSubscriptionsDisposed = true;
        DetachAutoFitRendering();
        Logger.Entries.CollectionChanged -= LoggerEntries_CollectionChanged;
        AppThemeService.ThemeChanged -= OnAppThemeChanged;
        Closing -= Window_Closing;
        PreviewMouseDown -= Window_PreviewMouseDown;

        if (_executionController is not null)
            _executionController.ExecutionStateChanged -= OnExecutionStateChanged;
        if (_scriptRunManager is not null)
            _scriptRunManager.RunningStateChanged -= OnScriptRunningStateChanged;

        if (_attachedEditorService is not null)
        {
            _attachedEditorService.GraphChanged -= OnGraphChanged;
            _attachedEditorService.StatusChanged -= SetStatus;
            _attachedEditorService.GraphChanged -= NavigationFeatures_GraphChanged;
            _attachedEditorService = null;
        }

        foreach (var session in _editorSessions.ToList())
            session.PropertyChanged -= EditorSession_PropertyChanged;

        DetachNavigationFeatureHandlers();
        DetachEditorSurfaceHostSessionTracking();
        DetachContentAssetRenameValidation();
        DetachContentBrowserEnhancedInteractions();
        DetachUnifiedThemeInteractionFixes();
    }
}
