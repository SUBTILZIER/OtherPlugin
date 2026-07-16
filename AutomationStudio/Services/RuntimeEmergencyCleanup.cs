using AutomationStudioWpf.Adapters;

namespace AutomationStudioWpf.Services;

internal static class RuntimeEmergencyCleanup
{
    private static Action? _cleanup;
    private static int _started;

    internal static void Register(Action cleanup)
    {
        ArgumentNullException.ThrowIfNull(cleanup);
        Interlocked.Exchange(ref _cleanup, cleanup);
    }

    internal static void Run()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
            return;

        RuntimeShutdownGate.BeginShutdown();
        try
        {
            _cleanup?.Invoke();
        }
        catch
        {
        }

        try
        {
            PythonEnvironmentService.Shared.TerminateAllProcessesImmediately();
        }
        catch
        {
        }
    }
}
