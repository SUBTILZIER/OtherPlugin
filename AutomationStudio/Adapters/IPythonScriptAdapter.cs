namespace AutomationStudioWpf.Adapters;

public sealed record PythonScriptResult(
    bool Success,
    int ExitCode,
    string Stdout,
    string Stderr,
    string Message);

public interface IPythonScriptAdapter
{
    PythonScriptResult RunJsonScript(
        string scriptPath,
        object payload,
        TimeSpan timeout,
        CancellationToken ct);
}

