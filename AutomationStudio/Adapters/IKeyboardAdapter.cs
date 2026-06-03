using AutomationStudioWpf.Graph;

namespace AutomationStudioWpf.Adapters;

public interface IKeyboardAdapter
{
    void ExecuteKey(string key, PressReleaseMode mode);

    void ExecuteChord(string chord, int holdMs, CancellationToken ct);

    void ReleaseAllKeys();
}
