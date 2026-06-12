using System.IO;
using System.Windows;
using AutomationStudioWpf.Graph;
using AutomationStudioWpf.GraphCore;
using AutomationStudioWpf.Logging;
using AutomationStudioWpf.Runtime;
using AutomationStudioWpf.Services;

namespace AutomationStudioWpf.Interaction;

public sealed class ExecutionController
{
    private readonly Window _owner;
    private readonly GraphEditorService _editorService;
    private readonly GraphRuntimeExecutor _runtimeExecutor;
    private readonly GraphValidator _graphValidator;
    private readonly System.Windows.Controls.Button _runButton;
    private readonly Func<IEnumerable<CallableGraphItem>> _getFunctions;
    private readonly Action<string> _setStatus;

    private CancellationTokenSource? _executionCts;

    public ExecutionController(
        Window owner,
        GraphEditorService editorService,
        GraphRuntimeExecutor runtimeExecutor,
        GraphValidator graphValidator,
        System.Windows.Controls.Button runButton,
        Func<IEnumerable<CallableGraphItem>> getFunctions,
        Action<string> setStatus)
    {
        _owner = owner;
        _editorService = editorService;
        _runtimeExecutor = runtimeExecutor;
        _graphValidator = graphValidator;
        _runButton = runButton;
        _getFunctions = getFunctions;
        _setStatus = setStatus;
    }

    public async Task RunAsync()
    {
        if (_executionCts is not null)
        {
            _setStatus("已有图谱正在执行。");
            return;
        }

        try
        {
            _runButton.IsEnabled = false;
            _executionCts = new CancellationTokenSource();

            var plan = _editorService.BuildExecutionPlan();
            var assetLibrary = new RuntimeAssetLibrary(
                _getFunctions().ToDictionary(item => item.Id, item => BuildPlanFromModel(item.Graph)));
            var baseDirectory = ResolveBaseDirectory();
            if (!Validate(plan))
                return;

            if (plan.Nodes.Any(n => n.NodeKind is NodeKind.FindImage or NodeKind.WaitImage or NodeKind.WaitImageDisappear))
            {
                bool pythonReady = await PythonAutoInstaller.EnsurePythonAsync(new Progress<string>(_setStatus));
                if (!pythonReady)
                {
                    _setStatus("Python 环境未就绪，执行已取消。");
                    return;
                }
            }

            _setStatus("执行开始...");
            var ct = _executionCts.Token;
            var result = await Task.Run(() => _runtimeExecutor.Execute(plan, baseDirectory, assetLibrary, ct), ct);
            _setStatus(result.Message);
        }
        catch (OperationCanceledException)
        {
            Logger.Info("===== 执行已取消=====");
            ReleaseAllKeys();
            _setStatus("执行已取消。");
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(_owner, ex.Message, "执行失败", MessageBoxButton.OK, MessageBoxImage.Error);
            _setStatus("执行失败。");
        }
        finally
        {
            ReleaseAllKeys();
            _executionCts = null;
            _runButton.IsEnabled = true;
        }
    }

    private static GraphExecutionPlan BuildPlanFromModel(GraphFileModel graph)
    {
        var nodes = graph.Nodes
            .Select(NodeSerializer.FromFileModel)
            .Where(node => node is not null)
            .Cast<NodeBaseViewModel>()
            .ToDictionary(node => node.Id);
        var runtimeNodes = nodes.Values.Select(NodeSerializer.ToRuntimeNode).ToList();
        var runtimeConnections = new List<GraphRuntimeConnection>();
        foreach (var connection in graph.Connections)
        {
            if (!nodes.TryGetValue(connection.SourceNodeId, out var sourceNode) ||
                !nodes.TryGetValue(connection.TargetNodeId, out var targetNode))
                continue;
            var sourcePin = sourceNode.OutputPins.FirstOrDefault(pin => pin.Name == connection.SourcePinName);
            var targetPin = targetNode.InputPins.FirstOrDefault(pin => pin.Name == connection.TargetPinName);
            if (sourcePin is null || targetPin is null)
                continue;
            runtimeConnections.Add(new GraphRuntimeConnection(
                sourceNode.Id,
                sourcePin.Name,
                sourcePin.Kind,
                targetNode.Id,
                targetPin.Name,
                targetPin.Kind));
        }

        return new GraphExecutionPlan(runtimeNodes, runtimeConnections);
    }

    public void ReleaseAllKeys() => _runtimeExecutor.ReleaseAllKeys();

    public bool IsRunning => _executionCts is not null;

    public void Cancel()
    {
        if (_executionCts is null)
            return;

        Logger.Info("===== 用户取消执行 (ESC) =====");
        _executionCts.Cancel();
        ReleaseAllKeys();
        _setStatus("正在停止执行...");
    }

    private string ResolveBaseDirectory()
    {
        return !string.IsNullOrWhiteSpace(_editorService.CurrentGraphPath)
            ? Path.GetDirectoryName(_editorService.CurrentGraphPath) ?? Environment.CurrentDirectory
            : Environment.CurrentDirectory;
    }

    private bool Validate(GraphExecutionPlan plan)
    {
        var validation = _graphValidator.Validate(plan);
        foreach (var issue in validation.Issues)
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

        if (!validation.HasErrors)
            return true;

        _setStatus("图谱校验失败，执行已取消。");
        return false;
    }
}
