namespace AutomationStudioWpf.Adapters;

public enum NotificationSeverity
{
    Information,
    Warning,
    Error,
}

/// <summary>
/// Service-layer notification boundary. Implemented by WPF, testable without a Window.
/// </summary>
public interface IUserNotificationSink
{
    void Notify(string title, string message, NotificationSeverity severity);
}

internal sealed class NullUserNotificationSink : IUserNotificationSink
{
    public static NullUserNotificationSink Instance { get; } = new();

    private NullUserNotificationSink()
    {
    }

    public void Notify(string title, string message, NotificationSeverity severity)
    {
        // Headless/runtime tests intentionally do not display UI.
    }
}
