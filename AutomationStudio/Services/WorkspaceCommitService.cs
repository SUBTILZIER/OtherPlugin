using AutomationStudioWpf.Interaction;

namespace AutomationStudioWpf.Services;

/// <summary>
/// Converts session-owned editor state into the corresponding content asset.
/// It deliberately does not persist to disk or change the active session.
/// </summary>
internal sealed class WorkspaceCommitService
{
    private readonly Func<EditorSessionViewModel, GraphListController?> _resolveController;

    public WorkspaceCommitService(Func<EditorSessionViewModel, GraphListController?> resolveController)
    {
        _resolveController = resolveController ?? throw new ArgumentNullException(nameof(resolveController));
    }

    public void CommitSession(
        EditorSessionViewModel session,
        bool applyInspector,
        Action applyInspectorChanges)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(applyInspectorChanges);

        if (applyInspector)
            applyInspectorChanges();

        _resolveController(session)?.SnapshotActive();
        session.RememberActive(_resolveController(session));
        session.SaveToAsset();
    }

    public void CommitAll(
        IEnumerable<EditorSessionViewModel> sessions,
        EditorSessionViewModel? activeSession,
        bool applyInspectorForActive,
        Action applyInspectorChanges)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(applyInspectorChanges);

        foreach (var session in sessions.ToList())
        {
            CommitSession(
                session,
                applyInspectorForActive && ReferenceEquals(session, activeSession),
                applyInspectorChanges);
        }
    }
}
