using AutomationStudioWpf.GraphCore;
using AutomationStudioWpf.Interaction;
using AutomationStudioWpf.Runtime;
using AutomationStudioWpf.Services;

namespace AutomationStudio.CoreTests;

[TestClass]
public sealed class ScriptRunManagerTests
{
    [TestMethod]
    public async Task PreventDuplicateRunKeepsSingleExecution()
    {
        ContentAssetViewModel asset = CreateScript(preventDuplicateRun: true);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int runCount = 0;
        using var manager = CreateManager(asset, async (_, _, ct) =>
        {
            Interlocked.Increment(ref runCount);
            started.TrySetResult();
            await release.Task.WaitAsync(ct);
            return GraphExecutionResult.Completed("ok");
        });

        Task first = manager.StartFromHotkeyAsync(asset);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await manager.StartFromHotkeyAsync(asset);

        Assert.AreEqual(1, Volatile.Read(ref runCount));
        Assert.IsTrue(manager.IsHotkeyRunActive(asset));

        release.TrySetResult();
        await first.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.IsFalse(manager.IsAnyHotkeyRunActive);
    }

    [TestMethod]
    public async Task ConcurrentRestartsKeepOnlyLatestExecution()
    {
        ContentAssetViewModel asset = CreateScript(preventDuplicateRun: false);
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int runCount = 0;
        int activeCount = 0;
        int maximumActiveCount = 0;
        using var manager = CreateManager(asset, async (_, _, ct) =>
        {
            int run = Interlocked.Increment(ref runCount);
            int active = Interlocked.Increment(ref activeCount);
            UpdateMaximum(ref maximumActiveCount, active);
            try
            {
                if (run == 1)
                {
                    firstStarted.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                }

                return GraphExecutionResult.Completed("ok");
            }
            finally
            {
                Interlocked.Decrement(ref activeCount);
            }
        });

        Task first = manager.StartFromHotkeyAsync(asset);
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Task restart1 = manager.StartFromHotkeyAsync(asset);
        Task restart2 = manager.StartFromHotkeyAsync(asset);

        await Task.WhenAll(first, restart1, restart2).WaitAsync(TimeSpan.FromSeconds(3));

        Assert.AreEqual(2, Volatile.Read(ref runCount));
        Assert.AreEqual(1, Volatile.Read(ref maximumActiveCount));
        Assert.IsFalse(manager.IsAnyHotkeyRunActive);
    }

    [TestMethod]
    public async Task StopFromHotkeyCancelsActiveExecutionOnce()
    {
        ContentAssetViewModel asset = CreateScript(preventDuplicateRun: true);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var manager = CreateManager(asset, async (_, _, ct) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return GraphExecutionResult.Completed("unreachable");
        });

        Task running = manager.StartFromHotkeyAsync(asset);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.IsTrue(manager.StopFromHotkey(asset));
        Assert.IsFalse(manager.StopFromHotkey(asset));
        await running.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.IsFalse(manager.IsAnyHotkeyRunActive);
    }

    [TestMethod]
    public async Task DisposeCancelsRunWithoutPublishingCallbacksAfterShutdown()
    {
        ContentAssetViewModel asset = CreateScript(preventDuplicateRun: true);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var statuses = new List<string>();
        int stateChangeCount = 0;
        var manager = new ScriptRunManager(
            (_, _) => Task.FromResult<GraphWorkspaceReadModel?>(GraphWorkspaceReadModel.Create([asset])),
            async (_, _, ct) =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return GraphExecutionResult.Completed("unreachable");
            },
            statuses.Add);
        manager.RunningStateChanged += () => stateChangeCount++;

        Task running = manager.StartFromHotkeyAsync(asset);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        int callbackCountBeforeShutdown = stateChangeCount;
        int statusCountBeforeShutdown = statuses.Count;

        manager.Dispose();
        await running.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.AreEqual(callbackCountBeforeShutdown, stateChangeCount);
        Assert.AreEqual(statusCountBeforeShutdown, statuses.Count);
        Assert.IsFalse(manager.IsAnyHotkeyRunActive);
    }

    [TestMethod]
    [DataRow(ScriptLoopMode.Count)]
    [DataRow(ScriptLoopMode.Duration)]
    [DataRow(ScriptLoopMode.UntilStopped)]
    public async Task FatalRunKeepsFailureStatusForEveryLoopMode(ScriptLoopMode loopMode)
    {
        ContentAssetViewModel asset = CreateScript(preventDuplicateRun: true);
        asset.RunSettings.LoopMode = loopMode;
        asset.RunSettings.LoopCount = 3;
        asset.RunSettings.DurationSeconds = 5;
        asset.RunSettings.StopHotkey = new ScriptHotkeySettings { Key = "F12" };
        var statuses = new List<string>();
        int runCount = 0;
        using var manager = new ScriptRunManager(
            (_, _) => Task.FromResult<GraphWorkspaceReadModel?>(GraphWorkspaceReadModel.Create([asset])),
            (_, _, _) =>
            {
                runCount++;
                return Task.FromResult(GraphExecutionResult.Fatal("目标节点失败"));
            },
            statuses.Add);

        await manager.StartFromHotkeyAsync(asset).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.AreEqual(1, runCount);
        StringAssert.Contains(statuses.Last(), "脚本执行失败");
        StringAssert.Contains(statuses.Last(), "目标节点失败");
        Assert.IsFalse(statuses.Any(status => status.Contains("脚本执行结束", StringComparison.Ordinal)));
    }

    private static ScriptRunManager CreateManager(
        ContentAssetViewModel asset,
        Func<ContentAssetViewModel, GraphWorkspaceReadModel, CancellationToken, Task<GraphExecutionResult>> runOnce) =>
        new(
            (_, _) => Task.FromResult<GraphWorkspaceReadModel?>(GraphWorkspaceReadModel.Create([asset])),
            runOnce,
            _ => { });

    private static ContentAssetViewModel CreateScript(bool preventDuplicateRun)
    {
        ContentAssetViewModel asset = TestGraphFactory.Script("Hotkey Script");
        asset.IsScriptEnabled = true;
        asset.RunSettings = new ScriptRunSettings
        {
            LoopMode = ScriptLoopMode.Count,
            LoopCount = 1,
            PreventDuplicateRun = preventDuplicateRun,
        };
        return asset;
    }

    private static void UpdateMaximum(ref int maximum, int candidate)
    {
        int current;
        do
        {
            current = Volatile.Read(ref maximum);
            if (candidate <= current)
                return;
        } while (Interlocked.CompareExchange(ref maximum, candidate, current) != current);
    }
}
