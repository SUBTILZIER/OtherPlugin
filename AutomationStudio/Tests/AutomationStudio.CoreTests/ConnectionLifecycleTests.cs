using System.ComponentModel;
using AutomationStudioWpf.Graph;

namespace AutomationStudio.CoreTests;

[TestClass]
public sealed class ConnectionLifecycleTests
{
    [TestMethod]
    public void DisposedConnectionCancelsQueuedGeometryRefresh()
    {
        var scheduler = new DeferredRenderUpdateScheduler();
        var start = new StartNodeViewModel("start");
        var delay = new DelayNodeViewModel("delay");
        var connection = new ConnectionViewModel(
            start.OutputPins.Single(pin => pin.Name == "exec_out"),
            delay.InputPins.Single(pin => pin.Name == "exec_in"),
            scheduler);
        int pathNotifications = 0;
        connection.PropertyChanged += (_, e) => pathNotifications += IsPathChange(e) ? 1 : 0;

        start.X += 100;
        Assert.AreEqual(1, scheduler.PendingCount);

        connection.Dispose();
        scheduler.RunAll();

        Assert.AreEqual(0, scheduler.ExecutedCount);
        Assert.AreEqual(0, pathNotifications);
    }

    [TestMethod]
    public void DisposedConnectionPathCancelsQueuedGeometryRefresh()
    {
        var scheduler = new DeferredRenderUpdateScheduler();
        var start = new StartNodeViewModel("start");
        var delay = new DelayNodeViewModel("delay");
        var connection = new ConnectionViewModel(
            start.OutputPins.Single(pin => pin.Name == "exec_out"),
            delay.InputPins.Single(pin => pin.Name == "exec_in"),
            ImmediateRenderUpdateScheduler.Instance);
        var path = new ConnectionPathViewModel([connection], scheduler);
        int pathNotifications = 0;
        path.PropertyChanged += (_, e) => pathNotifications += IsPathChange(e) ? 1 : 0;

        delay.Y += 100;
        Assert.AreEqual(1, scheduler.PendingCount);

        path.Dispose();
        scheduler.RunAll();

        Assert.AreEqual(0, scheduler.ExecutedCount);
        Assert.AreEqual(0, pathNotifications);
        connection.Dispose();
    }

    private static bool IsPathChange(PropertyChangedEventArgs e) =>
        e.PropertyName == nameof(ConnectionViewModel.PathGeometry);

    private sealed class DeferredRenderUpdateScheduler : IRenderUpdateScheduler
    {
        private readonly List<DeferredRequest> _requests = [];

        public int PendingCount => _requests.Count(request => !request.IsCanceled && !request.IsExecuted);

        public int ExecutedCount { get; private set; }

        public IDisposable Schedule(Action callback)
        {
            var request = new DeferredRequest(callback);
            _requests.Add(request);
            return request;
        }

        public void RunAll()
        {
            foreach (DeferredRequest request in _requests)
            {
                if (!request.TryExecute())
                    continue;

                ExecutedCount++;
            }
        }

        private sealed class DeferredRequest(Action callback) : IDisposable
        {
            public bool IsCanceled { get; private set; }

            public bool IsExecuted { get; private set; }

            public bool TryExecute()
            {
                if (IsCanceled || IsExecuted)
                    return false;

                IsExecuted = true;
                callback();
                return true;
            }

            public void Dispose() => IsCanceled = true;
        }
    }
}
