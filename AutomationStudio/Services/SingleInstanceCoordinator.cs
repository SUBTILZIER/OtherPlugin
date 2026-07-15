using System.Diagnostics;

namespace AutomationStudioWpf.Services;

internal enum SingleInstanceCommand
{
    Activate,
    ShutdownForUpdate,
}

internal sealed class SingleInstanceCoordinator : IDisposable
{
    private const string MutexName = @"Local\SUBTILZIER.AutomationStudioWpf.SingleInstance";
    private const string ActivationEventName = @"Local\SUBTILZIER.AutomationStudioWpf.Activate";
    private const string ShutdownForUpdateEventName = @"Local\SUBTILZIER.AutomationStudioWpf.ShutdownForUpdate";
    private static readonly TimeSpan ActivationSignalTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ActivationRetryDelay = TimeSpan.FromMilliseconds(50);

    private Mutex? _mutex;
    private EventWaitHandle? _activationEvent;
    private EventWaitHandle? _shutdownForUpdateEvent;
    private RegisteredWaitHandle? _activationRegistration;
    private RegisteredWaitHandle? _shutdownForUpdateRegistration;
    private bool _ownsMutex;
    private bool _disposed;

    public event Action? ActivationRequested;
    public event Action? ShutdownForUpdateRequested;

    public bool TryBecomePrimary()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_mutex is not null)
            throw new InvalidOperationException("Single-instance coordination has already started.");

        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            _mutex.Dispose();
            _mutex = null;
            return false;
        }

        _ownsMutex = true;
        _activationEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            ActivationEventName);
        _activationRegistration = ThreadPool.RegisterWaitForSingleObject(
            _activationEvent,
            (_, timedOut) =>
            {
                if (!timedOut && !_disposed)
                    ActivationRequested?.Invoke();
            },
            state: null,
            Timeout.Infinite,
            executeOnlyOnce: false);
        _shutdownForUpdateEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            ShutdownForUpdateEventName);
        _shutdownForUpdateRegistration = ThreadPool.RegisterWaitForSingleObject(
            _shutdownForUpdateEvent,
            (_, timedOut) =>
            {
                if (!timedOut && !_disposed)
                    ShutdownForUpdateRequested?.Invoke();
            },
            state: null,
            Timeout.Infinite,
            executeOnlyOnce: false);
        return true;
    }

    public bool SignalPrimary(SingleInstanceCommand command = SingleInstanceCommand.Activate)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        string eventName = command == SingleInstanceCommand.ShutdownForUpdate
            ? ShutdownForUpdateEventName
            : ActivationEventName;
        var stopwatch = Stopwatch.StartNew();
        do
        {
            try
            {
                using var commandEvent = EventWaitHandle.OpenExisting(eventName);
                return commandEvent.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                Thread.Sleep(ActivationRetryDelay);
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }
        while (stopwatch.Elapsed < ActivationSignalTimeout);

        return false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        ActivationRequested = null;
        ShutdownForUpdateRequested = null;
        _activationRegistration?.Unregister(null);
        _activationRegistration = null;
        _shutdownForUpdateRegistration?.Unregister(null);
        _shutdownForUpdateRegistration = null;
        _activationEvent?.Dispose();
        _activationEvent = null;
        _shutdownForUpdateEvent?.Dispose();
        _shutdownForUpdateEvent = null;

        if (_ownsMutex && _mutex is not null)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }
        }

        _ownsMutex = false;
        _mutex?.Dispose();
        _mutex = null;
    }
}
