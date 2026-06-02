namespace AutomationStudioWpf.Adapters;

public sealed record WindowSelectionResult(bool Success, string ProcessName, string Message);

public interface IWindowAdapter
{
    WindowSelectionResult SelectWindowByProcessName(string processName);
    List<string> GetRunningWindowNames();
}

