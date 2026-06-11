using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using AutomationStudioWpf.Interaction;

namespace AutomationStudioWpf;

public partial class MainWindow
{
    // Keep disabled until EditorSurfaceControl owns the full graph/editor UI.
    // When disabled, the application still uses the existing EditorGrid path.
    private static readonly bool UseEditorSurfaceHostForMainWindow = false;

    private readonly EditorSurfaceHostController _editorSurfaceHostController = new();
    private readonly HashSet<EditorSessionViewModel> _editorSurfaceHostTrackedSessions = new();
    private System.Windows.Controls.ContentControl? _editorSurfaceHost;
    private bool _editorSurfaceHostSessionTrackingInstalled;

    private System.Windows.Controls.ContentControl EnsureEditorSurfaceHost()
    {
        if (_editorSurfaceHost is null)
        {
            _editorSurfaceHost = new System.Windows.Controls.ContentControl
            {
                Visibility = Visibility.Collapsed,
                Focusable = false,
            };
            System.Windows.Controls.Grid.SetRow(_editorSurfaceHost, 0);

            if (_editorGridHomeParent is not null)
            {
                var insertIndex = _editorGridHomeIndex >= 0
                    ? Math.Min(_editorGridHomeIndex + 1, _editorGridHomeParent.Children.Count)
                    : _editorGridHomeParent.Children.Count;
                _editorGridHomeParent.Children.Insert(insertIndex, _editorSurfaceHost);
            }
        }

        InstallEditorSurfaceHostSessionTracking();
        return _editorSurfaceHost;
    }

    private void InstallEditorSurfaceHostSessionTracking()
    {
        if (_editorSurfaceHostSessionTrackingInstalled)
            return;

        _editorSurfaceHostSessionTrackingInstalled = true;
        _editorSessions.CollectionChanged += EditorSurfaceHost_EditorSessionsChanged;

        foreach (var session in _editorSessions)
            TrackEditorSurfaceHostSession(session);
    }

    private void EditorSurfaceHost_EditorSessionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems)
            {
                if (item is EditorSessionViewModel session)
                    TrackEditorSurfaceHostSession(session);
            }
        }

        if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems)
            {
                if (item is EditorSessionViewModel session)
                    UntrackEditorSurfaceHostSession(session);
            }
        }
    }

    private void TrackEditorSurfaceHostSession(EditorSessionViewModel session)
    {
        if (!_editorSurfaceHostTrackedSessions.Add(session))
            return;

        session.EnsureSurfaceContext();
        session.PropertyChanged += EditorSurfaceHostSession_PropertyChanged;

        if (session.IsActive)
            PrepareSessionSurfaceForHiddenHost(session);
    }

    private void UntrackEditorSurfaceHostSession(EditorSessionViewModel session)
    {
        if (!_editorSurfaceHostTrackedSessions.Remove(session))
            return;

        session.PropertyChanged -= EditorSurfaceHostSession_PropertyChanged;
    }

    private void EditorSurfaceHostSession_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(EditorSessionViewModel.IsActive))
            return;

        if (sender is EditorSessionViewModel { IsActive: true } session)
            PrepareSessionSurfaceForHiddenHost(session);
    }

    private void PrepareSessionSurfaceForHiddenHost(EditorSessionViewModel session)
    {
        session.EnsureSurfaceContext();
        _ = EnsureEditorSurfaceHost();
    }

    private bool TryShowSessionSurfaceInMainHost(EditorSessionViewModel session)
    {
        if (!UseEditorSurfaceHostForMainWindow)
            return false;

        AttachSessionSurfaceToMainHost(session);
        EditorGrid.Visibility = Visibility.Collapsed;
        return true;
    }

    private void AttachSessionSurfaceToMainHost(EditorSessionViewModel session)
    {
        var host = EnsureEditorSurfaceHost();
        _editorSurfaceHostController.AttachToHost(session, host);
    }

    private void HideEditorSurfaceHost()
    {
        if (_editorSurfaceHost is null)
            return;

        _editorSurfaceHostController.ClearHost(_editorSurfaceHost);
    }
}
