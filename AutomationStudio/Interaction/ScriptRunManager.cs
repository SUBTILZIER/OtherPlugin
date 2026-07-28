using AutomationStudioWpf.Logging;
using AutomationStudioWpf.Services;
using AutomationStudioWpf.Adapters;
using AutomationStudioWpf.GraphCore;

namespace AutomationStudioWpf.Interaction;

internal enum ScriptRunStopReason
{
    GlobalHotkey,
    Toolbar,
    ApplicationExit,
}

internal sealed class ScriptRunManager : IDisposable
{
    private readonly Func<ContentAssetViewModel, CancellationToken, Task<GraphWorkspaceReadModel?>> _compileScript;
    private readonly Func<ContentAssetViewModel, GraphWorkspaceReadModel, CancellationToken, Task<Runtime.GraphExecutionResult>> _runOnce;
    private readonly Action<string> _setStatus;
    private readonly object _stateGate = new();
    private readonly object _callbackGate = new();
    private readonly Dictionary<string, ScriptRunState> _running = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _startGenerations = new(StringComparer.Ordinal);
    private bool _disposed;
    public event Action? RunningStateChanged;
    public bool IsAnyHotkeyRunActive
    {
        get
        {
            lock (_stateGate)
                return _running.Count > 0;
        }
    }

    public bool IsHotkeyRunActive(ContentAssetViewModel asset)
    {
        lock (_stateGate)
            return _running.ContainsKey(asset.Id);
    }

    public ScriptRunManager(
        Func<ContentAssetViewModel, CancellationToken, Task<GraphWorkspaceReadModel?>> compileScript,
        Func<ContentAssetViewModel, GraphWorkspaceReadModel, CancellationToken, Task<Runtime.GraphExecutionResult>> runOnce,
        Action<string> setStatus)
    {
        _compileScript = compileScript;
        _runOnce = runOnce;
        _setStatus = setStatus;
    }

    public async Task StartFromHotkeyAsync(ContentAssetViewModel asset)
    {
        if (RuntimeShutdownGate.IsShutdownStarted || asset.Kind != ContentAssetKind.Script || !asset.IsScriptEnabled)
            return;

        var settings = asset.RunSettings.Clone();
        settings.Normalize();
        if (settings.LoopMode == ScriptLoopMode.UntilStopped && !settings.StopHotkey.IsConfigured)
        {
            string message = $"脚本未启动：{asset.Name} 的循环到终止键模式必须配置终止热键。";
            Logger.Warn(message);
            PublishStatus(message);
            return;
        }

        ScriptRunState? existing;
        long generation;
        bool duplicateBlocked;
        lock (_stateGate)
        {
            if (_disposed || RuntimeShutdownGate.IsShutdownStarted)
                return;

            _running.TryGetValue(asset.Id, out existing);
            duplicateBlocked = existing is not null && settings.PreventDuplicateRun;
            generation = duplicateBlocked ? 0 : NextGenerationLocked(asset.Id);
        }

        if (duplicateBlocked)
        {
            PublishStatus($"脚本正在运行，已忽略重复启动：{asset.Name}");
            return;
        }

        if (existing is not null)
        {
            existing.TryCancel();
            PublishStatus($"脚本已请求重启：{asset.Name}");
            try
            {
                await existing.RunningTask;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Logger.Warn($"等待旧脚本结束时发现异常，继续重启：{asset.Name}：{ex.Message}");
            }
        }

        ScriptRunState state;
        lock (_stateGate)
        {
            if (_disposed || RuntimeShutdownGate.IsShutdownStarted ||
                !_startGenerations.TryGetValue(asset.Id, out long currentGeneration) ||
                currentGeneration != generation)
            {
                return;
            }

            var cts = new CancellationTokenSource();
            state = new ScriptRunState(asset, cts, generation);
            _running[asset.Id] = state;
            state.RunningTask = RunManagedStateAsync(state, settings);
        }

        NotifyRunningStateChanged();
        state.Start();
        await state.RunningTask;
    }

    public bool StopFromHotkey(ContentAssetViewModel asset)
    {
        ScriptRunState? state;
        lock (_stateGate)
            _running.TryGetValue(asset.Id, out state);

        if (state is null || !state.TryCancel())
            return false;

        Logger.Info($"终止热键请求停止脚本：{asset.Name}");
        PublishStatus($"正在停止脚本：{asset.Name}");
        return true;
    }

    public void StopAll(ScriptRunStopReason reason)
    {
        List<ScriptRunState> states;
        lock (_stateGate)
            states = _running.Values.ToList();
        if (states.Count == 0)
            return;

        string reasonText = reason switch
        {
            ScriptRunStopReason.Toolbar => "顶部停止按钮",
            ScriptRunStopReason.ApplicationExit => "应用退出",
            _ => "终止热键",
        };
        Logger.Info($"{reasonText}请求停止全部热键脚本。");
        foreach (var state in states)
            state.TryCancel();
        PublishStatus("正在停止热键脚本...");
    }

    internal void BeginShutdown()
    {
        List<ScriptRunState> states;
        lock (_stateGate)
        {
            if (_disposed)
                return;

            _disposed = true;
            foreach (string assetId in _startGenerations.Keys.ToList())
                _startGenerations[assetId]++;
            states = _running.Values.ToList();
        }

        foreach (ScriptRunState state in states)
            state.TryCancel();

        // Wait for an already-running callback to leave. New callbacks observe _disposed and no-op.
        lock (_callbackGate)
        {
        }
    }

    internal void CancelAllWithoutUi() => BeginShutdown();

    public void Dispose() => BeginShutdown();

    private async Task RunManagedStateAsync(ScriptRunState state, ScriptRunSettings settings)
    {
        await state.WaitForStartAsync();
        try
        {
            await RunLoopAsync(state.Asset, settings, state.Cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            Logger.Info($"脚本已停止：{state.Asset.Name}");
            PublishStatus($"脚本已停止：{state.Asset.Name}");
        }
        catch (Exception ex)
        {
            Logger.Error($"脚本执行失败：{state.Asset.Name}：{ex.Message}");
            PublishStatus($"脚本执行失败：{state.Asset.Name}");
        }
        finally
        {
            bool removed;
            lock (_stateGate)
            {
                removed = _running.TryGetValue(state.Asset.Id, out ScriptRunState? current) &&
                          ReferenceEquals(current, state);
                if (removed)
                    _running.Remove(state.Asset.Id);
            }

            state.Dispose();
            if (removed)
                NotifyRunningStateChanged();
        }
    }

    private void NotifyRunningStateChanged()
    {
        lock (_callbackGate)
        {
            lock (_stateGate)
            {
                if (_disposed)
                    return;
            }

            try
            {
                RunningStateChanged?.Invoke();
            }
            catch (Exception ex)
            {
                Logger.Error($"刷新脚本运行状态失败：{ex.Message}");
            }
        }
    }

    private void PublishStatus(string message)
    {
        lock (_callbackGate)
        {
            lock (_stateGate)
            {
                if (_disposed)
                    return;
            }

            _setStatus(message);
        }
    }

    private long NextGenerationLocked(string assetId)
    {
        long next = _startGenerations.TryGetValue(assetId, out long current) ? current + 1 : 1;
        _startGenerations[assetId] = next;
        return next;
    }

    private async Task RunLoopAsync(ContentAssetViewModel asset, ScriptRunSettings settings, CancellationToken ct)
    {
        if (settings.LoopMode == ScriptLoopMode.Duration && GetDuration(settings) <= TimeSpan.Zero)
        {
            Logger.Warn($"脚本循环时长无效：{asset.Name}。循环一段时间模式必须大于 0 秒。");
            PublishStatus($"脚本循环时长无效：{asset.Name}");
            return;
        }

        GraphWorkspaceReadModel? readModel = await _compileScript(asset, ct);
        if (readModel is null)
            return;

        TimeSpan duration = GetDuration(settings);
        using var durationCancellation = settings.LoopMode == ScriptLoopMode.Duration
            ? CancellationTokenSource.CreateLinkedTokenSource(ct)
            : null;
        if (durationCancellation is not null)
            durationCancellation.CancelAfter(duration);
        CancellationToken runToken = durationCancellation?.Token ?? ct;

        var startedAt = DateTime.UtcNow;
        int iteration = 0;
        Runtime.GraphExecutionResult? fatalResult = null;
        try
        {
            while (!runToken.IsCancellationRequested)
            {
                iteration++;
                if (settings.LoopMode == ScriptLoopMode.Count && iteration > settings.LoopCount)
                    break;

                if (settings.LoopMode == ScriptLoopMode.Duration && DateTime.UtcNow - startedAt >= duration)
                    break;

                Logger.Info($"脚本循环开始：{asset.Name} 第 {iteration} 次");
                var result = await _runOnce(asset, readModel, runToken);
                if (!result.Success)
                {
                    fatalResult = result;
                    break;
                }

                if (settings.LoopMode == ScriptLoopMode.Count && iteration >= settings.LoopCount)
                    break;
            }
        }
        catch (OperationCanceledException) when (durationCancellation?.IsCancellationRequested == true && !ct.IsCancellationRequested)
        {
            Logger.Info($"脚本运行时长已到：{asset.Name}");
            PublishStatus($"脚本运行时长已到：{asset.Name}");
            return;
        }

        if (ct.IsCancellationRequested)
            ct.ThrowIfCancellationRequested();

        if (durationCancellation?.IsCancellationRequested == true)
        {
            Logger.Info($"脚本运行时长已到：{asset.Name}");
            PublishStatus($"脚本运行时长已到：{asset.Name}");
            return;
        }

        if (fatalResult is not null)
        {
            string detail = string.IsNullOrWhiteSpace(fatalResult.Message) ? "未知执行错误" : fatalResult.Message;
            Logger.Error($"脚本执行失败：{asset.Name}：{detail}");
            PublishStatus($"脚本执行失败：{asset.Name}：{detail}");
            return;
        }

        PublishStatus($"脚本执行结束：{asset.Name}");
    }

    private static TimeSpan GetDuration(ScriptRunSettings settings) =>
        new(settings.DurationHours, settings.DurationMinutes, settings.DurationSeconds);

    private sealed class ScriptRunState(
        ContentAssetViewModel asset,
        CancellationTokenSource cancellation,
        long generation) : IDisposable
    {
        private readonly object _cancellationGate = new();
        private readonly TaskCompletionSource _startSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _disposed;

        public ContentAssetViewModel Asset { get; } = asset;

        public CancellationTokenSource Cancellation { get; } = cancellation;

        public long Generation { get; } = generation;

        public Task RunningTask { get; set; } = Task.CompletedTask;

        public void Start() => _startSignal.TrySetResult();

        public Task WaitForStartAsync() => _startSignal.Task;

        public bool TryCancel()
        {
            lock (_cancellationGate)
            {
                if (_disposed || Cancellation.IsCancellationRequested)
                    return false;

                Cancellation.Cancel();
                return true;
            }
        }

        public void Dispose()
        {
            lock (_cancellationGate)
            {
                if (_disposed)
                    return;

                _disposed = true;
                _startSignal.TrySetCanceled();
                Cancellation.Dispose();
            }
        }
    }
}
