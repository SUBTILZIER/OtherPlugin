using AutomationStudioWpf.Graph;

namespace AutomationStudioWpf.Adapters;

public sealed record ProcessStartResult(bool Success, string ProcessName, string Message);

public interface IProcessAdapter
{
    ProcessStartResult StartProgram(
        string programPath,
        int waitTimeoutMs,
        ProgramStartFailureAction failureAction,
        int retryCount,
        CancellationToken ct);
}

