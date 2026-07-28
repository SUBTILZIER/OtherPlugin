using System.Windows.Threading;
using WpfApplication = System.Windows.Application;

namespace AutomationStudioWpf.Graph;

internal interface IRenderUpdateScheduler
{
    IDisposable? Schedule(Action callback);
}

internal static class RenderUpdateScheduler
{
    public static IRenderUpdateScheduler CreateDefault()
    {
        Dispatcher? dispatcher = WpfApplication.Current?.Dispatcher;
        return dispatcher is null
            ? ImmediateRenderUpdateScheduler.Instance
            : new DispatcherRenderUpdateScheduler(dispatcher);
    }
}

internal sealed class ImmediateRenderUpdateScheduler : IRenderUpdateScheduler
{
    public static ImmediateRenderUpdateScheduler Instance { get; } = new();

    private ImmediateRenderUpdateScheduler()
    {
    }

    public IDisposable Schedule(Action callback)
    {
        callback();
        return EmptyDisposable.Instance;
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static EmptyDisposable Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}

internal sealed class DispatcherRenderUpdateScheduler(Dispatcher dispatcher) : IRenderUpdateScheduler
{
    public IDisposable? Schedule(Action callback)
    {
        if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            return null;

        try
        {
            DispatcherOperation operation = dispatcher.BeginInvoke(callback, DispatcherPriority.Render);
            return new DispatcherOperationCancellation(operation);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private sealed class DispatcherOperationCancellation(DispatcherOperation operation) : IDisposable
    {
        private DispatcherOperation? _operation = operation;

        public void Dispose()
        {
            DispatcherOperation? pending = Interlocked.Exchange(ref _operation, null);
            if (pending?.Status == DispatcherOperationStatus.Pending)
                pending.Abort();
        }
    }
}
