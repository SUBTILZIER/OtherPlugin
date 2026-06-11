using System.Collections.Generic;
using System.Collections.Specialized;
using System.Windows;
using AutomationStudioWpf.Interaction;

namespace AutomationStudioWpf;

public partial class MainWindow
{
    private readonly EditorSurfaceHostController _editorSurfaceHostController = new();
    private readonly HashSet<EditorSessionViewModel> _editorSurfaceHostTrackedSessions = new();
    private System.Windows.Controls.ContentControl? _editorSurfaceHost;
    private bool _editorSurfaceHostSessionTrackingInstalled;

    private System.Windows.Controls.ContentControl EnsureEditorSurfaceHost()
    {
        if (_editorSurfaceHost is null)
        {
            _editorSurfaceHost = EditorSurfaceHostRoot;
            _editorSurfaceHost.Focusable = false;
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

        ConfigureEditorSurface(session);
    }

    private void UntrackEditorSurfaceHostSession(EditorSessionViewModel session)
    {
        _editorSurfaceHostTrackedSessions.Remove(session);
    }

    private void PrepareSessionSurfaceForHiddenHost(EditorSessionViewModel session)
    {
        ConfigureEditorSurface(session);
        _ = EnsureEditorSurfaceHost();
    }

    private bool TryShowSessionSurfaceInMainHost(EditorSessionViewModel session)
    {
        if (session.DockMode == EditorDockMode.Detached)
            return false;

        ConfigureEditorSurface(session);
        AttachSessionSurfaceToMainHost(session);
        return true;
    }

    private void ShowLastMainSessionSurface()
    {
        var session = _lastMainEditorSession;
        if (session is null ||
            !_editorSessions.Contains(session) ||
            session.DockMode == EditorDockMode.Detached)
        {
            session = _mainEditorSessions.LastOrDefault(item => item.DockMode != EditorDockMode.Detached);
            _lastMainEditorSession = session;
        }

        if (session is not null)
        {
            TryShowSessionSurfaceInMainHost(session);
            return;
        }

        HideMainEditorSurfaceHostOnly();
    }

    private void ShowEditorSurfaceForSession(EditorSessionViewModel? session)
    {
        if (session is null)
        {
            HideEditorSurfaceHost();
            return;
        }

        if (session.DockMode == EditorDockMode.Detached && session.DetachedWindow is { } detachedWindow)
        {
            ConfigureEditorSurface(session);
            AttachSessionSurfaceToDetachedWindow(session, detachedWindow);
            if (!detachedWindow.IsVisible)
                detachedWindow.Show();
            ShowLastMainSessionSurface();
            return;
        }

        TryShowSessionSurfaceInMainHost(session);
    }

    private void AttachSessionSurfaceToMainHost(EditorSessionViewModel session)
    {
        var host = EnsureEditorSurfaceHost();
        _editorSurfaceHostController.AttachToHost(session, host);
    }

    private void AttachSessionSurfaceToDetachedWindow(EditorSessionViewModel session, DetachedEditorWindow detachedWindow)
    {
        ConfigureEditorSurface(session);
        var context = session.EnsureSurfaceContext();
        if (detachedWindow.HasEditorContent(context.Surface))
        {
            detachedWindow.RefreshChrome();
            return;
        }

        EditorSurfaceHostController.DetachFromCurrentParent(context.Surface);
        detachedWindow.SetEditorContent(context.Surface);
        detachedWindow.RefreshChrome();
    }

    private void HideEditorSurfaceHost()
    {
        HideMainEditorSurfaceHostOnly();
    }

    private void HideMainEditorSurfaceHostOnly()
    {
        if (_editorSurfaceHost is not null)
            _editorSurfaceHostController.ClearHost(_editorSurfaceHost);
    }

}
