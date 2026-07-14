using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using AutomationStudioWpf.Adapters;
using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Logging;
using AutomationStudioWpf.Nodes;
using DrawingPoint = System.Drawing.Point;

namespace AutomationStudioWpf.Runtime;

/// <summary>
/// Graph execution scheduler. Concrete node behavior lives in node executors and adapters.
/// </summary>
public sealed partial class GraphRuntimeExecutor
{
    private const int MaxChainSteps = 10000;
    private const int MaxNestedToDoReturnJumps = 256;

    private readonly RuntimeAdapters _adapters;
    private readonly NodeRegistry _nodeRegistry;
    private readonly object _globalDeviceGate = new();

    public GraphRuntimeExecutor()
        : this(new RuntimeAdapters(), NodeRegistry.CreateDefault())
    {
    }

    public GraphRuntimeExecutor(RuntimeAdapters adapters, NodeRegistry nodeRegistry)
    {
        _adapters = adapters;
        _nodeRegistry = nodeRegistry;
    }

    public GraphExecutionResult Execute(GraphExecutionPlan plan, string baseDirectory, CancellationToken ct = default)
        => Execute(plan, baseDirectory, new RuntimeAssetLibrary(
            new Dictionary<string, GraphExecutionPlan>()), ct);

    public GraphExecutionResult Execute(GraphExecutionPlan plan, string baseDirectory, RuntimeAssetLibrary assets, CancellationToken ct = default)
    {
        using IDisposable inputScope = _adapters.BeginExecutionInputScope();
        Logger.Info("--------开始执行--------");

        GraphRuntimeNode? startNode = plan.Index.FirstNode(NodeKind.Start);
        if (startNode is null)
        {
            Logger.Error("执行失败：图中没有开始节点。");
            return new GraphExecutionResult(false, "执行失败：图中没有开始节点。", false);
        }

        using var context = CreateRuntimeContext();
        var state = new RuntimeExecutionState();
        GraphExecutionResult result = ExecuteChain(plan, startNode.Id, "exec_out", context, baseDirectory, assets, state, ct, out _);

        Logger.Info("--------执行结束--------");
        return result;
    }

    public void ReleaseAllInputs() => _adapters.ReleaseAllInputs();

    public void ReleaseAllKeys() => ReleaseAllInputs();

    private GraphExecutionResult ExecuteChain(
        GraphExecutionPlan plan,
        string startNodeId,
        string startPinName,
        RuntimeContext context,
        string baseDirectory,
        RuntimeAssetLibrary assets,
        RuntimeExecutionState state,
        CancellationToken ct,
        out GraphRuntimeNode? terminalNode,
        string? stopBeforeNodeId = null)
    {
        return ExecuteFromNode(
            plan,
            GetNextExecutionNode(plan, startNodeId, startPinName),
            context,
            baseDirectory,
            assets,
            state,
            ct,
            out terminalNode,
            stopBeforeNodeId);
    }

    private GraphExecutionResult ExecuteFromNode(
        GraphExecutionPlan plan,
        GraphRuntimeNode? firstNode,
        RuntimeContext context,
        string baseDirectory,
        RuntimeAssetLibrary assets,
        RuntimeExecutionState state,
        CancellationToken ct,
        out GraphRuntimeNode? terminalNode,
        string? stopBeforeNodeId = null)
    {
        terminalNode = null;
        GraphRuntimeNode? currentNode = firstNode;
        int stepCount = 0;

        while (currentNode is not null)
        {
            ct.ThrowIfCancellationRequested();

            if (stopBeforeNodeId is not null && string.Equals(currentNode.Id, stopBeforeNodeId, StringComparison.Ordinal))
            {
                terminalNode = currentNode;
                return new GraphExecutionResult(true, "执行完成。");
            }

            stepCount++;
            if (stepCount > MaxChainSteps)
            {
                Logger.Error($"执行链超过安全步数 {MaxChainSteps}，疑似执行环路，已停止。");
                return new GraphExecutionResult(false, "执行失败：执行链疑似存在环路。", false);
            }

            NodeExecutionResult result = ExecuteNode(plan, currentNode, context, baseDirectory, assets, state, ct);
            if (!result.ContinueExecution)
                return new GraphExecutionResult(false, result.Message, false);

            if (result.JumpTargetNodeId is not null)
            {
                GraphRuntimeNode? jumpTarget = plan.Index.GetNode(result.JumpTargetNodeId);
                if (jumpTarget is null)
                    return new GraphExecutionResult(false, $"执行失败：ToDo 目标节点不存在：{result.JumpTargetNodeId}。", false);

                if (result.ReturnAfterJump)
                {
                    GraphExecutionResult jumpResult = ExecuteReturnJump(plan, currentNode, jumpTarget, context, baseDirectory, assets, state, ct);
                    if (!jumpResult.ContinueExecution)
                        return jumpResult;

                    if (result.NextPinName is null)
                    {
                        terminalNode = currentNode;
                        break;
                    }

                    currentNode = GetNextExecutionNode(plan, currentNode.Id, result.NextPinName);
                    continue;
                }

                currentNode = jumpTarget;
                continue;
            }

            if (result.NextPinName is null)
            {
                terminalNode = currentNode;
                break;
            }

            currentNode = GetNextExecutionNode(plan, currentNode.Id, result.NextPinName);
        }

        return new GraphExecutionResult(true, "执行完成。");
    }

    private GraphExecutionResult ExecuteReturnJump(
        GraphExecutionPlan plan,
        GraphRuntimeNode sourceNode,
        GraphRuntimeNode jumpTarget,
        RuntimeContext context,
        string baseDirectory,
        RuntimeAssetLibrary assets,
        RuntimeExecutionState state,
        CancellationToken ct)
    {
        if (state.ActiveToDoReturnJumps.Count >= MaxNestedToDoReturnJumps)
        {
            string tooDeepMessage = $"执行失败：ToDo 返回跳转嵌套超过安全上限 {MaxNestedToDoReturnJumps}，已停止。";
            Logger.Error(tooDeepMessage);
            return new GraphExecutionResult(false, tooDeepMessage, false);
        }

        string jumpKey = MakeToDoReturnJumpKey(plan, sourceNode.Id, jumpTarget.Id);
        if (!state.ActiveToDoReturnJumps.Add(jumpKey))
        {
            string loopMessage = $"执行失败：检测到 ToDo 返回跳转环路：{sourceNode.Title} -> {jumpTarget.Title} {jumpTarget.NodeNumber}。";
            Logger.Error(loopMessage);
            return new GraphExecutionResult(false, loopMessage, false);
        }

        try
        {
            return ExecuteFromNode(plan, jumpTarget, context, baseDirectory, assets, state, ct, out _, sourceNode.Id);
        }
        finally
        {
            state.ActiveToDoReturnJumps.Remove(jumpKey);
        }
    }

    private NodeExecutionResult ExecuteNode(
        GraphExecutionPlan plan,
        GraphRuntimeNode node,
        RuntimeContext context,
        string baseDirectory,
        RuntimeAssetLibrary assets,
        RuntimeExecutionState state,
        CancellationToken ct)
    {
        bool shouldLog = ShouldLogExecutionNode(node);
        if (node.NodeKind == NodeKind.CustomEvent)
        {
            context.RemoveNodeOutputs(node.Id);
            ApplyParameterDefaults(node, context, node.Id);
        }
        else if (node.NodeKind != NodeKind.FunctionEntry)
        {
            context.RemoveNodeOutputs(node.Id);
        }
        var stopwatch = Stopwatch.StartNew();
        IReadOnlyDictionary<string, object> outputsBefore = shouldLog
            ? context.GetNodeOutputs(node.Id)
            : new Dictionary<string, object>();
        using var capture = Logger.BeginCapture();
        NodeExecutionResult? result = null;
        try
        {
            result = node.NodeKind switch
            {
                NodeKind.Start => NodeExecutionResult.Ok(string.Empty, "exec_out"),
                NodeKind.Reroute => NodeExecutionResult.Ok(string.Empty, node.RoutedKind == PinKind.Execution ? "out" : null),
                NodeKind.If => ExecuteIfNode(plan, node, context),
                NodeKind.ForLoop => ExecuteForLoopNode(plan, node, context, baseDirectory, assets, state, ct),
                NodeKind.WhileLoop => ExecuteWhileLoopNode(plan, node, context, baseDirectory, assets, state, ct),
                NodeKind.ToDo => ExecuteToDoNode(plan, node, context),
                NodeKind.MultiThread => ExecuteMultiThreadNode(plan, node, context, baseDirectory, assets, state, ct),
                NodeKind.FunctionEntry => NodeExecutionResult.Ok(string.Empty, "exec_out"),
                NodeKind.FunctionReturn => NodeExecutionResult.Ok(string.Empty, null),
                NodeKind.FunctionCall => ExecuteFunctionCall(plan, node, context, baseDirectory, assets, state, ct),
                NodeKind.CustomEvent => NodeExecutionResult.Ok(string.Empty, "exec_out"),
                NodeKind.CustomEventCall => ExecuteCustomEventCall(plan, node, context, baseDirectory, assets, state, ct),
                _ => ExecuteRegisteredNode(plan, node, context, baseDirectory, assets, ct),
            };
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PureNodeEvaluationException ex)
        {
            result = NodeExecutionResult.Fatal(ex.Message);
            return result;
        }
        finally
        {
            stopwatch.Stop();
            if (result is not null)
                StoreStandardExecutionOutputs(node, result, context);
            if (shouldLog && result is not null)
            {
                WriteStructuredNodeLog(node, result, context, outputsBefore, stopwatch.Elapsed, capture.Entries);
                capture.MarkSummarized();
            }
        }
    }

    private static NodeExecutionResult ExecuteToDoNode(GraphExecutionPlan plan, GraphRuntimeNode node, RuntimeContext context)
    {
        string targetTitle = context.ResolveStringInput(plan, node, "target_title", node.TargetNodeTitle).Trim();
        string targetNumber = context.ResolveStringInput(plan, node, "target_number", node.TargetNodeNumber).Trim();
        if ((string.IsNullOrWhiteSpace(targetTitle) || string.IsNullOrWhiteSpace(targetNumber)) &&
            !context.HasInputConnection(plan, node, "target_title") &&
            !context.HasInputConnection(plan, node, "target_number") &&
            !string.IsNullOrWhiteSpace(node.TargetNodeId) &&
            plan.Index.GetNode(node.TargetNodeId!) is { } target)
        {
            targetTitle = target.Title;
            targetNumber = target.NodeNumber;
        }

        if (string.IsNullOrWhiteSpace(targetTitle) || string.IsNullOrWhiteSpace(targetNumber))
            return NodeExecutionResult.Fatal($"ToDo 跳转失败：{node.Title} 缺少目标节点名或编号。");

        var matches = plan.Index
            .FindNodesByTitleAndNumber(targetTitle, targetNumber)
            .Where(candidate => NodeTraits.IsToDoTarget(candidate.NodeKind))
            .ToList();
        if (matches.Count == 0)
            return NodeExecutionResult.Fatal($"ToDo 跳转失败：找不到目标 {targetTitle} {targetNumber}。");
        if (matches.Count > 1)
            return NodeExecutionResult.Fatal($"ToDo 跳转失败：目标不唯一 {targetTitle} {targetNumber}。");
        if (matches[0].Id == node.Id)
            return NodeExecutionResult.Fatal($"ToDo 跳转失败：{node.Title} 不能跳转到自身。");

        Logger.Info(node.ReturnAfterTarget
            ? $"ToDo 跳转：{node.Title} -> {targetTitle} {targetNumber}，完成后返回。"
            : $"ToDo 跳转：{node.Title} -> {targetTitle} {targetNumber}。");
        return NodeExecutionResult.Jump($"ToDo 跳转：{targetTitle} {targetNumber}", matches[0].Id, node.ReturnAfterTarget);
    }

    private NodeExecutionResult ExecuteRegisteredNode(
        GraphExecutionPlan plan,
        GraphRuntimeNode node,
        RuntimeContext context,
        string baseDirectory,
        RuntimeAssetLibrary assets,
        CancellationToken ct)
    {
        if (!_nodeRegistry.TryGetExecutor(node.NodeKind, out INodeExecutor executor))
        {
            Logger.Warn($"已跳过未注册节点：{node.Title}");
            return NodeExecutionResult.Warn($"已跳过未注册节点：{node.Title}", null);
        }

        try
        {
            var request = new NodeExecutionRequest(plan, node, context, baseDirectory, _adapters, ct);
            if (RequiresGlobalDeviceLock(node.NodeKind))
            {
                lock (_globalDeviceGate)
                {
                    ct.ThrowIfCancellationRequested();
                    return executor.Execute(request);
                }
            }

            return executor.Execute(request);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            context.Set(node.Id, "result", false);
            Logger.Error($"{node.Title} 执行失败：{ex.Message}");
            return NodeExecutionResult.Fatal($"执行失败：{node.Title}：{ex.Message}");
        }
    }

    private static bool RequiresGlobalDeviceLock(NodeKind kind) => kind is
        NodeKind.MouseClick or
        NodeKind.MouseMove or
        NodeKind.ScrollWheel or
        NodeKind.Keyboard or
        NodeKind.KeyChord or
        NodeKind.StartProgram or
        NodeKind.SelectWindow or
        NodeKind.WaitWindow or
        NodeKind.CloseWindow or
        NodeKind.WindowExists or
        NodeKind.GetForegroundWindow;

    private static NodeExecutionResult ExecuteIfNode(GraphExecutionPlan plan, GraphRuntimeNode node, RuntimeContext context)
    {
        bool condition = node.ConditionValue;
        if (context.TryResolveBoolInput(plan, node, "condition", out bool inputCondition))
            condition = inputCondition;

        string nextPin = condition ? "exec_true" : "exec_false";
        Logger.Info($"分支：{(condition ? "True" : "False")}");
        return NodeExecutionResult.Ok($"分支：{(condition ? "True" : "False")}", nextPin);
    }

    private NodeExecutionResult ExecuteForLoopNode(
        GraphExecutionPlan plan,
        GraphRuntimeNode node,
        RuntimeContext context,
        string baseDirectory,
        RuntimeAssetLibrary assets,
        RuntimeExecutionState state,
        CancellationToken ct)
    {
        int count = node.LoopCount > 0 ? node.LoopCount : 1;
        if (node.LoopCount <= 0)
            Logger.Warn($"For 循环节点：循环次数无效 ({node.LoopCount})，将使用默认值 1。");

        Logger.Info($"For 循环开始：{count} 次");
        for (int i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();

            bool shouldEnd = node.ConditionValue;
            if (context.TryResolveBoolInput(plan, node, "end_condition", out bool endCondition))
                shouldEnd = endCondition;

            if (shouldEnd)
            {
                Logger.Info($"For 循环提前结束：结束条件为真（第 {i} 次）");
                break;
            }

            context.Set(node.Id, "index", i);
            GraphExecutionResult bodyResult = ExecuteChain(plan, node.Id, "exec_loop_body", context, baseDirectory, assets, state, ct, out _);
            if (!bodyResult.ContinueExecution)
                return NodeExecutionResult.Fatal(bodyResult.Message);
        }

        Logger.Info($"For 循环完成：{count} 次");
        return NodeExecutionResult.Ok($"循环完成：{count} 次", "exec_completed");
    }

    private NodeExecutionResult ExecuteWhileLoopNode(
        GraphExecutionPlan plan,
        GraphRuntimeNode node,
        RuntimeContext context,
        string baseDirectory,
        RuntimeAssetLibrary assets,
        RuntimeExecutionState state,
        CancellationToken ct)
    {
        WhileLoopMode loopMode = node.WhileLoopMode;
        int maxIterations = loopMode == WhileLoopMode.Infinite ? int.MaxValue : (node.MaxIterations > 0 ? node.MaxIterations : 10000);
        string modeLabel = loopMode == WhileLoopMode.Infinite ? "无限" : $"最多 {maxIterations} 次";
        Logger.Info($"While 循环开始：{modeLabel}");

        int iteration = 0;
        while (iteration < maxIterations)
        {
            ct.ThrowIfCancellationRequested();

            bool exit = node.ConditionValue;
            if (context.TryResolveBoolInput(plan, node, "condition", out bool condition))
                exit = condition;

            if (exit)
                break;

            context.Set(node.Id, "index", iteration);
            GraphExecutionResult bodyResult = ExecuteChain(plan, node.Id, "exec_loop_body", context, baseDirectory, assets, state, ct, out _);
            if (!bodyResult.ContinueExecution)
                return NodeExecutionResult.Fatal(bodyResult.Message);

            iteration++;
        }

        if (loopMode != WhileLoopMode.Infinite && iteration >= maxIterations)
        {
            Logger.Error($"While 循环超过最大迭代次数 {maxIterations}，强制终止。");
            return NodeExecutionResult.Fatal("执行失败：While 循环超过最大迭代次数。");
        }

        Logger.Info($"While 循环完成：{iteration} 次");
        return NodeExecutionResult.Ok($"While 循环完成：{iteration} 次", "exec_completed");
    }

    private static GraphRuntimeNode? GetNextExecutionNode(GraphExecutionPlan plan, string sourceNodeId, string sourcePinName)
    {
        GraphRuntimeConnection? connection = plan.Index.GetExecutionConnection(sourceNodeId, sourcePinName);

        return connection is null
            ? null
            : plan.Index.GetNode(connection.TargetNodeId);
    }

    private static string MakeToDoReturnJumpKey(GraphExecutionPlan plan, string sourceNodeId, string targetNodeId) =>
        $"{RuntimeHelpers.GetHashCode(plan)}:{sourceNodeId}->{targetNodeId}";

    private RuntimeContext CreateRuntimeContext()
    {
        return new RuntimeContext
        {
            PureNodeResolver = EvaluatePureNode,
        };
    }

    private bool EvaluatePureNode(GraphExecutionPlan plan, GraphRuntimeNode node, RuntimeContext context)
    {
        context.RemoveNodeOutputs(node.Id);
        bool evaluated = node.NodeKind switch
        {
            NodeKind.Compare => EvaluateComparePure(plan, node, context),
            NodeKind.BooleanAnd => EvaluateBooleanPure(plan, node, context, "and"),
            NodeKind.BooleanOr => EvaluateBooleanPure(plan, node, context, "or"),
            NodeKind.BooleanNot => EvaluateBooleanPure(plan, node, context, "not"),
            NodeKind.StringConcat => EvaluateStringConcatPure(plan, node, context),
            _ => false,
        };
        if (evaluated)
            StorePureEvaluationOutputs(node, context);

        return evaluated;
    }

    private static void StorePureEvaluationOutputs(GraphRuntimeNode node, RuntimeContext context)
    {
        context.Set(node.Id, "__executed", true);
        context.Set(node.Id, "__status", "PureEvaluated");
        context.Set(node.Id, "__success", true);
        context.Set(node.Id, "__message", "纯运算完成。");
        context.Set(node.Id, "__next_pin", string.Empty);
    }

    private static bool EvaluateComparePure(GraphExecutionPlan plan, GraphRuntimeNode node, RuntimeContext context)
    {
        string left = context.ResolveStringInput(plan, node, "left", node.Text);
        string right = context.ResolveStringInput(plan, node, "right", node.Text2);
        string op = string.IsNullOrWhiteSpace(node.Text3) ? "Equal" : node.Text3;
        context.Set(node.Id, "result", CompareValues(left, right, op));
        return true;
    }

    private static bool EvaluateBooleanPure(GraphExecutionPlan plan, GraphRuntimeNode node, RuntimeContext context, string mode)
    {
        bool value = node.Flag;
        if (context.TryResolveBoolInput(plan, node, "value", out bool inputValue))
            value = inputValue;

        bool result = mode switch
        {
            "and" => ResolveVariadicBoolInputs(plan, node, context, andMode: true),
            "or" => ResolveVariadicBoolInputs(plan, node, context, andMode: false),
            "not" => !value,
            _ => false,
        };
        context.Set(node.Id, "result", result);
        return true;
    }

    private static bool EvaluateStringConcatPure(GraphExecutionPlan plan, GraphRuntimeNode node, RuntimeContext context)
    {
        var values = new List<string>();
        foreach (var pinName in GetVariadicInputNames(node))
        {
            string fallback = GetVariadicStringDefault(node, pinName);
            if (context.TryResolveStringInput(plan, node, pinName, out string input, out bool hasConnection))
            {
                values.Add(input);
            }
            else if (hasConnection)
            {
                throw new PureNodeEvaluationException($"纯运算节点输入无值：{node.Title}.{pinName}");
            }
            else
            {
                values.Add(fallback);
            }
        }

        context.Set(node.Id, "value", string.Concat(values));
        return true;
    }

    private static bool ResolveVariadicBoolInputs(
        GraphExecutionPlan plan,
        GraphRuntimeNode node,
        RuntimeContext context,
        bool andMode)
    {
        bool result = andMode;
        foreach (var pinName in GetVariadicInputNames(node))
        {
            bool fallback = GetVariadicBoolDefault(node, pinName);
            bool value;
            if (context.TryResolveBoolInput(plan, node, pinName, out bool input, out bool hasConnection))
            {
                value = input;
            }
            else if (hasConnection)
            {
                throw new PureNodeEvaluationException($"纯运算节点输入无值：{node.Title}.{pinName}");
            }
            else
            {
                value = fallback;
            }
            result = andMode ? result && value : result || value;
        }

        return result;
    }

    private static IEnumerable<string> GetVariadicInputNames(GraphRuntimeNode node)
    {
        int count = Math.Max(2, node.VariadicInputCount);
        for (int i = 1; i <= count; i++)
            yield return CommonNodeViewModel.VariadicInputName(i);
    }

    private static string GetVariadicStringDefault(GraphRuntimeNode node, string pinName)
    {
        if (node.VariadicInputDefaults.TryGetValue(pinName, out string? value))
            return value;

        return pinName switch
        {
            "left" => node.Text ?? string.Empty,
            "right" => node.Text2 ?? string.Empty,
            _ => string.Empty,
        };
    }

    private static bool GetVariadicBoolDefault(GraphRuntimeNode node, string pinName)
    {
        if (node.VariadicInputDefaults.TryGetValue(pinName, out string? value))
            return bool.TryParse(value, out bool parsedDefault) && parsedDefault;

        return pinName switch
        {
            "left" => node.Flag,
            "right" => bool.TryParse(node.Text, out bool parsed) && parsed,
            _ => false,
        };
    }

    private static bool CompareValues(string left, string right, string op)
    {
        if (double.TryParse(left, out double leftNumber) && double.TryParse(right, out double rightNumber))
        {
            return op.ToLowerInvariant() switch
            {
                "greaterthan" or ">" => leftNumber > rightNumber,
                "lessthan" or "<" => leftNumber < rightNumber,
                "greaterorequal" or ">=" => leftNumber >= rightNumber,
                "lessorequal" or "<=" => leftNumber <= rightNumber,
                "notequal" or "!=" => Math.Abs(leftNumber - rightNumber) > double.Epsilon,
                _ => Math.Abs(leftNumber - rightNumber) <= double.Epsilon,
            };
        }

        return op.ToLowerInvariant() switch
        {
            "contains" => left.Contains(right, StringComparison.OrdinalIgnoreCase),
            "notequal" or "!=" => !string.Equals(left, right, StringComparison.OrdinalIgnoreCase),
            _ => string.Equals(left, right, StringComparison.OrdinalIgnoreCase),
        };
    }

    private sealed class RuntimeExecutionState
    {
        public HashSet<string> CallStack { get; } = new(StringComparer.Ordinal);

        public HashSet<string> ActiveToDoReturnJumps { get; } = new(StringComparer.Ordinal);

        public RuntimeExecutionState CreateBranchState()
        {
            var state = new RuntimeExecutionState();
            foreach (string key in CallStack)
                state.CallStack.Add(key);
            foreach (string key in ActiveToDoReturnJumps)
                state.ActiveToDoReturnJumps.Add(key);
            return state;
        }
    }
}
