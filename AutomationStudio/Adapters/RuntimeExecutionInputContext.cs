using System.Threading;

namespace AutomationStudioWpf.Adapters;

internal static class RuntimeExecutionInputContext
{
    private static readonly AsyncLocal<Guid?> CurrentExecution = new();

    public static Guid CurrentExecutionId => CurrentExecution.Value ?? Guid.Empty;

    public static IDisposable Enter(Guid executionId)
    {
        Guid? previous = CurrentExecution.Value;
        CurrentExecution.Value = executionId;
        return new Scope(previous);
    }

    private sealed class Scope(Guid? previous) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                CurrentExecution.Value = previous;
        }
    }
}

internal interface IExecutionScopedInputAdapter
{
    void ReleaseExecution(Guid executionId);

    void ReleaseAllExecutions();
}
