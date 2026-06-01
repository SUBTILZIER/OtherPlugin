namespace AutomationStudioWpf.Logging;

public sealed record LogEntry(string Timestamp, LogLevel Level, string Message)
{
    public string DisplayText => $"[{Timestamp}] [{LevelLabel(Level)}] {Message}";

    private static string LevelLabel(LogLevel level) => level switch
    {
        LogLevel.Warn => "WARN",
        LogLevel.Error => "ERROR",
        _ => "INFO",
    };
}