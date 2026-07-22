namespace AutomationStudioWpf.Adapters;

/// <summary>
/// Runtime-facing UI boundary. Runtime/node code must not depend on WPF.
/// </summary>
public interface IRuntimeUiAdapter
{
    void ShowMessage(string message, string title);
}

internal sealed class NullRuntimeUiAdapter : IRuntimeUiAdapter
{
    public static NullRuntimeUiAdapter Instance { get; } = new();

    private NullRuntimeUiAdapter()
    {
    }

    public void ShowMessage(string message, string title)
    {
        // Headless/runtime tests intentionally do not display UI.
    }
}
