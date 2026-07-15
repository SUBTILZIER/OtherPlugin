using AutomationStudioWpf.Logging;
using AutomationStudioWpf.Services;
using AutomationStudioWpf.Adapters;

namespace AutomationStudioWpf.Interaction;

internal enum ScriptRunStopReason
{
    GlobalHotkey,
    Toolbar,
    ApplicationExit,
}

internal sealed class ScriptRunManager : IDisposable
{
    private readonly Func<ContentAssetViewModel, CancellationToken, Task<bool>> _compileScript;
    private readonly Func<ContentAssetViewModel, IEnumerable<CallableGraphItem>> _getFunctions;
    private readonly Func<ContentAssetViewModel, IEnumerable<CallableGraphItem>, CancellationToken, Task<Runtime.GraphExecutionResult>> _runOnce;
    private readonly Action<string> _setStatus;
    private readonly Dictionary<string, ScriptRunState> _running = new(StringComparer.Ordinal);
    private bool _disposed;
    public event Action? RunningStateChanged;
    public bool IsAnyHotkeyRunActive => _running.Count > 0;

    public bool IsHotkeyRunActive(ContentAssetViewModel asset) => _running.ContainsKey(asset.Id);

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

    public async Task StartFromHotkeyAsync(ContentAssetViewModel asset)
    {
        if (_disposed || RuntimeShutdownGate.IsShutdownStarted || asset.Kind != ContentAssetKind.Script || !asset.IsScriptEnabled)
            return;

        var settings = asset.RunSettings.Clone();
        settings.Normalize();
        if (settings.LoopMode == ScriptLoopMode.UntilStopped && !settings.StopHotkey.IsConfigured)
        {
            string message = $"脚本未启动：{asset.Name} 的循环到终止键模式必须配置终止热键。";
            Logger.Warn(message);
            _setStatus(message);
            return;
        }

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
                catch (Exception ex)
                {
                    Logger.Warn($"等待旧脚本结束时发现异常，继续重启：{asset.Name}：{ex.Message}");
                }
            }
        }

        var cts = new CancellationTokenSource();
        var state = new ScriptRunState(asset, cts);
        _running[asset.Id] = state;
        RunningStateChanged?.Invoke();
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
            {
                _running.Remove(asset.Id);
                RunningStateChanged?.Invoke();
            }
            cts.Dispose();
        }
    }

    public bool StopFromHotkey(ContentAssetViewModel asset)
    {
        if (!_running.TryGetValue(asset.Id, out var state) || state.Cancellation.IsCancellationRequested)
            return false;

        Logger.Info($"终止热键请求停止脚本：{asset.Name}");
        state.Cancellation.Cancel();
        _setStatus($"正在停止脚本：{asset.Name}");
        return true;
    }

    public void StopAll(ScriptRunStopReason reason)
    {
        if (_running.Count == 0)
            return;

        string reasonText = reason switch
        {
            ScriptRunStopReason.Toolbar => "顶部停止按钮",
            ScriptRunStopReason.ApplicationExit => "应用退出",
            _ => "终止热键",
        };
        Logger.Info($"{reasonText}请求停止全部热键脚本。");
        foreach (var state in _running.Values.ToList())
            state.Cancellation.Cancel();
        _setStatus("正在停止热键脚本...");
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        StopAll(ScriptRunStopReason.ApplicationExit);
    }

    private async Task RunLoopAsync(ContentAssetViewModel asset, ScriptRunSettings settings, CancellationToken ct)
    {
        if (settings.LoopMode == ScriptLoopMode.Duration && GetDuration(settings) <= TimeSpan.Zero)
        {
            Logger.Warn($"脚本循环时长无效：{asset.Name}。循环一段时间模式必须大于 0 秒。");
            _setStatus($"脚本循环时长无效：{asset.Name}");
            return;
        }

        if (!await _compileScript(asset, ct))
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
                var result = await _runOnce(asset, _getFunctions(asset), runToken);
                if (!result.Success)
                    break;

                if (settings.LoopMode == ScriptLoopMode.Count && iteration >= settings.LoopCount)
                    break;
            }
        }
        catch (OperationCanceledException) when (durationCancellation?.IsCancellationRequested == true && !ct.IsCancellationRequested)
        {
            Logger.Info($"脚本运行时长已到：{asset.Name}");
            _setStatus($"脚本运行时长已到：{asset.Name}");
            return;
        }

        if (ct.IsCancellationRequested)
            ct.ThrowIfCancellationRequested();

        if (durationCancellation?.IsCancellationRequested == true)
        {
            Logger.Info($"脚本运行时长已到：{asset.Name}");
            _setStatus($"脚本运行时长已到：{asset.Name}");
            return;
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
