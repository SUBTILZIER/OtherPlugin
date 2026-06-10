using AutomationStudioWpf.Controls;

namespace AutomationStudioWpf.Interaction;

/// <summary>
/// Runtime context for a single editor surface.
///
/// The current application still uses MainWindow-owned graph editor services and
/// controllers. This class is a scaffold for the next migration step, where each
/// EditorSessionViewModel will own one EditorSurfaceControl and one matching
/// EditorSurfaceContext.
/// </summary>
public sealed class EditorSurfaceContext
{
    public EditorSurfaceContext(EditorSessionViewModel session, EditorSurfaceControl surface)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
        Surface = surface ?? throw new ArgumentNullException(nameof(surface));
    }

    public EditorSessionViewModel Session { get; }

    public EditorSurfaceControl Surface { get; }
}
