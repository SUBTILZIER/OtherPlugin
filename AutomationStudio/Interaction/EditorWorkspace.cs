using System.Collections.ObjectModel;

namespace AutomationStudioWpf.Interaction;

/// <summary>
/// Owns editor-session state only. Window hosting, activation side effects, and
/// persistence remain in their dedicated coordinators.
/// </summary>
internal sealed class EditorWorkspace
{
    public ObservableCollection<EditorSessionViewModel> Sessions { get; } = [];

    public ObservableCollection<EditorSessionViewModel> MainSessions { get; } = [];

    public EditorSessionViewModel? ActiveSession { get; set; }

    public EditorSessionViewModel? LastMainSession { get; set; }
}
