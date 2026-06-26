using AutomationStudioWpf.Logging;
using AutomationStudioWpf.Services;

namespace AutomationStudioWpf.Interaction;

internal sealed class ScriptRunManager : IDisposable
{
    private readonly Func<ContentAssetViewModel, CancellationToken, Task<bool>> _compileScript;
    private readonly Func<ContentAssetViewModel, IEnumerable<CallableGraphItem>> _getFunctions;
    private readonly Func<ContentAssetViewModel, IEnumerable<CallableGraphItem>, CancellationToken, Task<Runtime.GraphExecutionResult>> _runOnce;
    private readonly Action<string> _setStatus;
    private readonly Dictionary<string, ScriptRunState> _running = new(StringComparer.Ordinal);
    private bool _disposed;

    public ScriptRunManager(
        Func<ContentAssetViewModel, CancellationToken, Task<bool>> compileScript,
        Func<ContentAssetViewModel, IEnumerable<CallableGraphItem>> getFunctions,
        Func<ContentAssetViewModel, IEnumerable<CallableGraphItem>, CancellationToken, Task<Runtime.GraphExecutionResult>> runOnce,
        Action<string> setStatus)
    {
        _compileScript = compileScript;
        _getFunctions = getFunctions;
        _runOnce = runOnce;
        _setStatus = setStatus;
    }

    public async Task StartAsync(ContentAssetViewModel asset)
    {
        if (_disposed || asset.Kind != ContentAssetKind.Script)
            return;

        var settings = asset.RunSettings.Clone();
        settings.Normalize();
        if (_running.TryGetValue(asset.Id, out var existing))
        {
            if (settings.PreventDuplicateRun)
            {
                _setStatus($"脚本正在运行，已忽略重复启动：{asset.Name}");
                return;
            }

            existing.Cancellation.Cancel();
            _setStatus($"脚本已请求重启：{asset.Name}");
            if (existing.RunningTask is not null)
            {
                try
                {
                    await existing.RunningTask;
                }
                catch (OperationCanceledException)
                {
                }
            }
        }

        var cts = new CancellationTokenSource();
        var state = new ScriptRunState(asset, cts);
        _running[asset.Id] = state;
        var runTask = RunLoopAsync(asset, settings, cts.Token);
        state.RunningTask = runTask;

        try
        {
            await runTask;
        }
        catch (OperationCanceledException)
        {
            Logger.Info($"脚本已停止：{asset.Name}");
            _setStatus($"脚本已停止：{asset.Name}");
        }
        catch (Exception ex)
        {
            Logger.Error($"脚本执行失败：{asset.Name}：{ex.Message}");
            _setStatus($"脚本执行失败：{asset.Name}");
        }
        finally
        {
            if (_running.TryGetValue(asset.Id, out var current) && ReferenceEquals(current, state))
                _running.Remove(asset.Id);
            cts.Dispose();
        }
    }

    public void Stop(ContentAssetViewModel asset)
    {
        if (_running.TryGetValue(asset.Id, out var state))
        {
            state.Cancellation.Cancel();
            _setStatus($"正在停止脚本：{asset.Name}");
        }
    }

    public void StopAll()
    {
        foreach (var state in _running.Values.ToList())
            state.Cancellation.Cancel();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        StopAll();
    }

    private async Task RunLoopAsync(ContentAssetViewModel asset, ScriptRunSettings settings, CancellationToken ct)
    {
        if (!await _compileScript(asset, ct))
            return;

        var startedAt = DateTime.UtcNow;
        int iteration = 0;
        while (!ct.IsCancellationRequested)
        {
            iteration++;
            if (settings.LoopMode == ScriptLoopMode.Count && iteration > settings.LoopCount)
                break;

            if (settings.LoopMode == ScriptLoopMode.Duration && DateTime.UtcNow - startedAt >= GetDuration(settings))
                break;

            Logger.Info($"脚本循环开始：{asset.Name} 第 {iteration} 次");
            var result = await _runOnce(asset, _getFunctions(asset), ct);
            if (!result.Success)
                break;

            if (settings.LoopMode == ScriptLoopMode.Count && iteration >= settings.LoopCount)
                break;
        }

        _setStatus($"脚本执行结束：{asset.Name}");
    }

    private static TimeSpan GetDuration(ScriptRunSettings settings) =>
        new(settings.DurationHours, settings.DurationMinutes, settings.DurationSeconds);

    private sealed class ScriptRunState(ContentAssetViewModel asset, CancellationTokenSource cancellation)
    {
        public ContentAssetViewModel Asset { get; } = asset;

        public CancellationTokenSource Cancellation { get; } = cancellation;

        public Task? RunningTask { get; set; }
    }
}
