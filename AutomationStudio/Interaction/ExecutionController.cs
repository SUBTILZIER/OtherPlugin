using System.IO;
using System.Windows;
using AutomationStudioWpf.Graph;
using AutomationStudioWpf.GraphCore;
using AutomationStudioWpf.Logging;
using AutomationStudioWpf.Runtime;
using AutomationStudioWpf.Services;
using AutomationStudioWpf.Adapters;

namespace AutomationStudioWpf.Interaction;

internal enum ExecutionStopReason
{
    EscapeDebug,
    Toolbar,
    ApplicationExit,
}

public sealed class ExecutionController
{
    private readonly Window _owner;
    private readonly GraphEditorService _editorService;
    private readonly GraphRuntimeExecutor _runtimeExecutor;
    private readonly System.Windows.Controls.Button _runButton;
    private readonly Action<string> _setStatus;
    private readonly Func<IProgress<string>, CancellationToken, Task<bool>> _ensurePythonReady;
    private readonly GraphExecutionPreflightService _preflightService = new();

    private CancellationTokenSource? _executionCts;

    internal ExecutionController(
        Window owner,
        GraphEditorService editorService,
        GraphRuntimeExecutor runtimeExecutor,
        System.Windows.Controls.Button runButton,
        Action<string> setStatus,
        Func<IProgress<string>, CancellationToken, Task<bool>> ensurePythonReady)
    {
        _owner = owner;
        _editorService = editorService;
        _runtimeExecutor = runtimeExecutor;
        _runButton = runButton;
        _setStatus = setStatus;
        _ensurePythonReady = ensurePythonReady;
    }

    internal async Task RunAsync(GraphWorkspaceReadModel readModel, string scriptAssetId)
    {
        if (RuntimeShutdownGate.IsShutdownStarted)
        {
            _setStatus("应用正在退出，不能启动脚本。");
            return;
        }
        if (_executionCts is not null)
        {
            _setStatus("脚本正在运行，不能重复执行。");
            return;
        }

        try
        {
            _executionCts = new CancellationTokenSource();
            var ct = _executionCts.Token;
            SetRunButtonRunning();

            ContentAssetSnapshot? asset = readModel.DependencyIndex.FindAsset(scriptAssetId);
            if (asset?.Kind != ContentAssetKind.Script)
            {
                _setStatus("脚本没有主事件图，执行已取消。");
                return;
            }

            GraphExecutionPreflightResult preflight = _preflightService.Prepare(readModel, scriptAssetId);
            LogPreflightIssues(preflight.Issues);
            if (!preflight.Success)
            {
                _setStatus("执行前检查失败，执行已取消。");
                return;
            }

            GraphExecutionPlan plan = preflight.MainPlan!;
            RuntimeAssetLibrary assetLibrary = preflight.AssetLibrary!;
            var baseDirectory = ResolveBaseDirectory();

            if (preflight.Reachability.RequiresPython)
            {
                bool pythonReady = await _ensurePythonReady(new Progress<string>(_setStatus), ct);
                if (!pythonReady)
                {
                    _setStatus("Python 环境未就绪，执行已取消。");
                    return;
                }
            }

            _setStatus("执行开始...");
            var result = await Task.Run(() => _runtimeExecutor.Execute(plan, baseDirectory, assetLibrary, ct), ct);
            _setStatus(result.Message);
        }
        catch (OperationCanceledException)
        {
            Logger.Info("===== 执行已取消=====");
            _setStatus("执行已取消。");
        }
        catch (Exception ex)
        {
            ThemedDialog.Show(_owner, ex.Message, "执行失败", MessageBoxButton.OK, MessageBoxImage.Error);
            _setStatus("执行失败。");
        }
        finally
        {
            _executionCts = null;
            RestoreRunButton();
        }
    }

    internal async Task<GraphExecutionResult> RunScriptAssetOnceAsync(
        ContentAssetViewModel asset,
        GraphWorkspaceReadModel readModel,
        CancellationToken externalCancellationToken)
    {
        RuntimeShutdownGate.ThrowIfShutdownStarted();
        if (asset.Kind != ContentAssetKind.Script)
            return GraphExecutionResult.Fatal("只能执行脚本资产。");

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(externalCancellationToken);
        GraphExecutionPreflightResult preflight = _preflightService.Prepare(readModel, asset.Id);
        LogPreflightIssues(preflight.Issues);
        if (!preflight.Success)
            return GraphExecutionResult.Fatal("执行前检查失败，执行已取消。");

        GraphExecutionPlan plan = preflight.MainPlan!;
        RuntimeAssetLibrary assetLibrary = preflight.AssetLibrary!;
        var baseDirectory = Environment.CurrentDirectory;

        if (preflight.Reachability.RequiresPython)
        {
            bool pythonReady = await _ensurePythonReady(new Progress<string>(_setStatus), externalCancellationToken);
            if (!pythonReady)
                return GraphExecutionResult.Fatal("Python 环境未就绪，执行已取消。");
        }

        return await Task.Run(() => _runtimeExecutor.Execute(plan, baseDirectory, assetLibrary, linkedCts.Token), linkedCts.Token);
    }

    private void SetRunButtonRunning()
    {
        ExecutionStateChanged?.Invoke(true);
    }

    private void RestoreRunButton()
    {
        ExecutionStateChanged?.Invoke(false);
    }

    public void ReleaseAllInputs() => _runtimeExecutor.ReleaseAllInputs();

    public Action<bool>? ExecutionStateChanged;
    public bool IsManualDebugRunning => _executionCts is not null;

    internal bool Cancel(ExecutionStopReason reason)
    {
        if (_executionCts is null)
            return false;

        string reasonText = reason switch
        {
            ExecutionStopReason.EscapeDebug => "用户取消手动调试 (ESC)",
            ExecutionStopReason.Toolbar => "用户通过顶部按钮停止手动调试",
            ExecutionStopReason.ApplicationExit => "应用退出，停止手动调试",
            _ => "停止手动调试",
        };
        Logger.Info($"===== {reasonText} =====");
        _executionCts.Cancel();
        _setStatus("正在停止执行...");
        return true;
    }

    internal void CancelWithoutUi() => _executionCts?.Cancel();

    private string ResolveBaseDirectory()
    {
        return !string.IsNullOrWhiteSpace(_editorService.CurrentGraphPath)
            ? Path.GetDirectoryName(_editorService.CurrentGraphPath) ?? Environment.CurrentDirectory
            : Environment.CurrentDirectory;
    }

    private static void LogPreflightIssues(IEnumerable<GraphValidationIssue> issues)
    {
        foreach (GraphValidationIssue issue in issues)
        {
            switch (issue.Severity)
            {
                case GraphValidationSeverity.Error:
                    Logger.Error($"图谱校验：{issue.Message}");
                    break;
                case GraphValidationSeverity.Warning:
                    Logger.Warn($"图谱校验：{issue.Message}");
                    break;
                default:
                    Logger.Info($"图谱校验：{issue.Message}");
                    break;
            }
        }

    }
}
