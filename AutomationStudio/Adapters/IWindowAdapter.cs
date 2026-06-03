namespace AutomationStudioWpf.Adapters;

public sealed record WindowSelectionResult(bool Success, string ProcessName, string Message);

public sealed record WindowInfoResult(bool Success, string ProcessName, string WindowTitle, string Message);

public interface IWindowAdapter
{
    WindowSelectionResult SelectWindowByProcessName(string processName);
    WindowSelectionResult WaitWindowByProcessName(string processName, int timeoutMs, int intervalMs, CancellationToken ct);
    WindowSelectionResult CloseWindowByProcessName(string processName);
    WindowSelectionResult WindowExists(string processName);
    WindowInfoResult GetForegroundWindowInfo();
    List<string> GetRunningWindowNames();
}
