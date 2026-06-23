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
public sealed class GraphRuntimeExecutor
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

    public void ReleaseAllKeys() => _adapters.Keyboard.ReleaseAllKeys();

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
                WriteStructuredNodeLog(node, result, context, outputsBefore, stopwatch.Elapsed, capture.Entries);
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

    private NodeExecutionResult ExecuteMultiThreadNode(
        GraphExecutionPlan plan,
        GraphRuntimeNode node,
        RuntimeContext context,
        string baseDirectory,
        RuntimeAssetLibrary assets,
        RuntimeExecutionState state,
        CancellationToken ct)
    {
        int threadCount = Math.Max(MultiThreadNodeViewModel.MinimumThreadOutputCount, node.ThreadOutputCount);
        var connectedBranches = Enumerable.Range(1, threadCount)
            .Select(ordinal => new
            {
                Ordinal = ordinal,
                PinName = MultiThreadNodeViewModel.ThreadOutputPinName(ordinal),
                Label = MultiThreadNodeViewModel.ThreadOutputPinLabel(ordinal),
            })
            .Where(branch => plan.Index.GetExecutionConnection(node.Id, branch.PinName) is not null)
            .ToList();

        if (connectedBranches.Count == 0)
        {
            Logger.Info($"多线程无连接分支：{NodeLogLabel(node)}，直接进入全部完成。");
            return NodeExecutionResult.Ok("多线程无连接分支。", MultiThreadNodeViewModel.CompletedPinName);
        }

        Logger.Info($"多线程开始：{NodeLogLabel(node)}，连接分支 {connectedBranches.Count}/{threadCount}。");
        var tasks = connectedBranches
            .Select(branch => Task.Run(
                () => ExecuteMultiThreadBranch(plan, node, branch.PinName, branch.Label, context, baseDirectory, assets, state.CreateBranchState(), ct),
                ct))
            .ToArray();

        try
        {
            Task.WaitAll(tasks, ct);
        }
        catch (AggregateException ex)
        {
            Exception inner = ex.Flatten().InnerExceptions.FirstOrDefault(exception => exception is not OperationCanceledException)
                ?? ex.Flatten().InnerExceptions.FirstOrDefault()
                ?? ex;
            if (inner is OperationCanceledException)
                throw inner;

            string message = $"多线程执行失败：{NodeLogLabel(node)}：{inner.Message}";
            Logger.Error(message);
            return NodeExecutionResult.Fatal(message);
        }

        foreach (var task in tasks)
        {
            GraphExecutionResult result = task.Result;
            if (!result.ContinueExecution)
            {
                string message = $"多线程分支失败：{NodeLogLabel(node)}：{result.Message}";
                Logger.Error(message);
                return NodeExecutionResult.Fatal(message);
            }
        }

        Logger.Info($"多线程全部完成：{NodeLogLabel(node)}。");
        return NodeExecutionResult.Ok("多线程全部完成。", MultiThreadNodeViewModel.CompletedPinName);
    }

    private GraphExecutionResult ExecuteMultiThreadBranch(
        GraphExecutionPlan plan,
        GraphRuntimeNode node,
        string pinName,
        string label,
        RuntimeContext context,
        string baseDirectory,
        RuntimeAssetLibrary assets,
        RuntimeExecutionState branchState,
        CancellationToken ct)
    {
        Logger.Info($"多线程 {NodeLogLabel(node)} / {label} 开始。");
        try
        {
            GraphExecutionResult result = ExecuteChain(plan, node.Id, pinName, context, baseDirectory, assets, branchState, ct, out _);
            if (result.ContinueExecution)
                Logger.Info($"多线程 {NodeLogLabel(node)} / {label} 完成。");
            else
                Logger.Error($"多线程 {NodeLogLabel(node)} / {label} 失败：{result.Message}");

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            string message = $"多线程 {NodeLogLabel(node)} / {label} 异常：{ex.Message}";
            Logger.Error(message);
            return new GraphExecutionResult(false, message, false);
        }
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
        NodeKind.MouseDoubleClick or
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

    private NodeExecutionResult ExecuteFunctionCall(
        GraphExecutionPlan callerPlan,
        GraphRuntimeNode callNode,
        RuntimeContext callerContext,
        string baseDirectory,
        RuntimeAssetLibrary assets,
        RuntimeExecutionState state,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(callNode.FunctionId) || !assets.Functions.TryGetValue(callNode.FunctionId, out var functionPlan))
            return NodeExecutionResult.Fatal($"函数不存在：{callNode.Title}");
        if (!state.CallStack.Add($"function:{callNode.FunctionId}"))
            return NodeExecutionResult.Fatal($"检测到函数递归调用：{callNode.Title}");

        try
        {
            var entry = functionPlan.Index.FirstNode(NodeKind.FunctionEntry);
            var ret = functionPlan.Index.FirstNode(NodeKind.FunctionReturn);
            if (entry is null || ret is null)
                return NodeExecutionResult.Fatal($"函数结构无效：{callNode.Title}");

            using var childContext = CreateRuntimeContext();
            CopyCallInputsToEntry(callerPlan, callNode, callerContext, entry, childContext);
            var result = ExecuteChain(functionPlan, entry.Id, "exec_out", childContext, baseDirectory, assets, state, ct, out _);
            if (!result.ContinueExecution)
                return NodeExecutionResult.Fatal(result.Message);

            CopyReturnInputsToCallOutputs(functionPlan, ret, childContext, callNode, callerContext);
            Logger.Info($"函数调用完成：{callNode.Title}");
            return NodeExecutionResult.Ok($"函数调用完成：{callNode.Title}", "exec_out");
        }
        finally
        {
            state.CallStack.Remove($"function:{callNode.FunctionId}");
        }
    }

    private NodeExecutionResult ExecuteCustomEventCall(
        GraphExecutionPlan plan,
        GraphRuntimeNode callNode,
        RuntimeContext context,
        string baseDirectory,
        RuntimeAssetLibrary assets,
        RuntimeExecutionState state,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(callNode.CustomEventId))
            return NodeExecutionResult.Fatal($"自定义事件不存在：{callNode.Title}");

        var entry = plan.Index.GetCustomEvent(callNode.CustomEventId);
        if (entry is null)
            return NodeExecutionResult.Fatal($"自定义事件不存在：{callNode.Title}");

        string stackKey = $"custom_event:{callNode.CustomEventId}";
        if (!state.CallStack.Add(stackKey))
            return NodeExecutionResult.Fatal($"检测到自定义事件递归调用：{callNode.Title}");

        try
        {
            CopyCallInputsToEntry(plan, callNode, context, entry, context);
            var result = ExecuteChain(plan, entry.Id, "exec_out", context, baseDirectory, assets, state, ct, out _);
            if (!result.ContinueExecution)
                return NodeExecutionResult.Fatal(result.Message);

            Logger.Info($"自定义事件调用完成：{callNode.Title}");
            return NodeExecutionResult.Ok($"自定义事件调用完成：{callNode.Title}", "exec_out");
        }
        finally
        {
            state.CallStack.Remove(stackKey);
        }
    }

    private static void CopyCallInputsToEntry(
        GraphExecutionPlan callerPlan,
        GraphRuntimeNode callNode,
        RuntimeContext callerContext,
        GraphRuntimeNode entryNode,
        RuntimeContext childContext)
    {
        var connectedPins = new HashSet<string>(StringComparer.Ordinal);
        foreach (var input in callerPlan.Index.GetNonExecutionInputs(callNode.Id))
        {
            string entryPinName = MapParameterPin(input.TargetPinName, callNode.Parameters, entryNode.Parameters, connectedPins);
            connectedPins.Add(entryPinName);
            if (callerContext.TryResolveConnectionRaw(callerPlan, input, out object value))
                childContext.Set(entryNode.Id, entryPinName, value);
        }

        ApplyCallParameterDefaults(callNode, entryNode, childContext, connectedPins);
        ApplyParameterDefaults(entryNode, childContext, entryNode.Id, connectedPins);
    }

    private static void CopyReturnInputsToCallOutputs(
        GraphExecutionPlan assetPlan,
        GraphRuntimeNode returnNode,
        RuntimeContext childContext,
        GraphRuntimeNode callNode,
        RuntimeContext callerContext)
    {
        var connectedPins = new HashSet<string>(StringComparer.Ordinal);
        foreach (var input in assetPlan.Index.GetNonExecutionInputs(returnNode.Id))
        {
            connectedPins.Add(input.TargetPinName);
            string callOutputPinName = MapParameterPin(input.TargetPinName, returnNode.Parameters, callNode.OutputParameters, null);
            if (childContext.TryResolveConnectionRaw(assetPlan, input, out object value))
                callerContext.Set(callNode.Id, callOutputPinName, value);
        }

        ApplyReturnDefaults(returnNode, callNode, callerContext, connectedPins);
    }

    private static string MapParameterPin(
        string sourcePinName,
        IReadOnlyList<GraphRuntimeParameter> sourceParameters,
        IReadOnlyList<GraphRuntimeParameter> targetParameters,
        ISet<string>? alreadyMappedTargetPins)
    {
        if (targetParameters.Any(parameter => string.Equals(parameter.Id, sourcePinName, StringComparison.Ordinal)) &&
            alreadyMappedTargetPins?.Contains(sourcePinName) != true)
            return sourcePinName;

        GraphRuntimeParameter? source = sourceParameters.FirstOrDefault(parameter => string.Equals(parameter.Id, sourcePinName, StringComparison.Ordinal));
        if (source is not null)
        {
            GraphRuntimeParameter? byName = targetParameters.FirstOrDefault(parameter =>
                string.Equals(parameter.Name, source.Name, StringComparison.OrdinalIgnoreCase) &&
                alreadyMappedTargetPins?.Contains(parameter.Id) != true);
            if (byName is not null)
                return byName.Id;
        }

        int sourceIndex = source is null ? -1 : GetParameterIndex(sourceParameters, source);
        if (sourceIndex >= 0 && sourceIndex < targetParameters.Count &&
            alreadyMappedTargetPins?.Contains(targetParameters[sourceIndex].Id) != true)
            return targetParameters[sourceIndex].Id;

        return sourcePinName;
    }

    private static int GetParameterIndex(IReadOnlyList<GraphRuntimeParameter> parameters, GraphRuntimeParameter target)
    {
        for (int i = 0; i < parameters.Count; i++)
        {
            if (ReferenceEquals(parameters[i], target) ||
                string.Equals(parameters[i].Id, target.Id, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

    private static void ApplyParameterDefaults(
        GraphRuntimeNode parameterNode,
        RuntimeContext context,
        string targetNodeId,
        ISet<string>? skippedPins = null)
    {
        foreach (var parameter in parameterNode.Parameters)
        {
            if (skippedPins?.Contains(parameter.Id) == true)
                continue;
            if (context.TryGetRaw(targetNodeId, parameter.Id, out _))
                continue;

            context.Set(targetNodeId, parameter.Id, ConvertParameterDefault(parameter));
        }
    }

    private static void ApplyCallParameterDefaults(
        GraphRuntimeNode callNode,
        GraphRuntimeNode entryNode,
        RuntimeContext childContext,
        ISet<string> connectedEntryPins)
    {
        var mappedPins = new HashSet<string>(connectedEntryPins, StringComparer.Ordinal);
        foreach (var callParameter in callNode.Parameters)
        {
            string entryPinName = MapParameterPin(callParameter.Id, callNode.Parameters, entryNode.Parameters, mappedPins);
            if (connectedEntryPins.Contains(entryPinName) || childContext.TryGetRaw(entryNode.Id, entryPinName, out _))
                continue;

            childContext.Set(entryNode.Id, entryPinName, ConvertParameterDefault(callParameter));
            mappedPins.Add(entryPinName);
        }
    }

    private static void ApplyReturnDefaults(
        GraphRuntimeNode returnNode,
        GraphRuntimeNode callNode,
        RuntimeContext callerContext,
        ISet<string> connectedReturnPins)
    {
        foreach (var returnParameter in returnNode.Parameters)
        {
            if (connectedReturnPins.Contains(returnParameter.Id))
                continue;

            string callOutputPinName = MapParameterPin(returnParameter.Id, returnNode.Parameters, callNode.OutputParameters, null);
            if (callerContext.TryGetRaw(callNode.Id, callOutputPinName, out _))
                continue;

            var callDefault = callNode.OutputParameters.FirstOrDefault(parameter =>
                string.Equals(parameter.Id, callOutputPinName, StringComparison.Ordinal));
            callerContext.Set(callNode.Id, callOutputPinName, ConvertParameterDefault(callDefault ?? returnParameter));
        }
    }

    private static object ConvertParameterDefault(GraphRuntimeParameter parameter)
    {
        string value = parameter.DefaultValue ?? string.Empty;
        return parameter.Type switch
        {
            GraphParameterType.Boolean => bool.TryParse(value, out bool boolean) && boolean,
            GraphParameterType.Vector2D => TryParsePoint(value, out var point) ? point : new DrawingPoint(0, 0),
            _ => value,
        };
    }

    private static bool TryParsePoint(string value, out DrawingPoint point)
    {
        var parts = value
            .Trim()
            .Trim('(', ')')
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 &&
            double.TryParse(parts[0], out double x) &&
            double.TryParse(parts[1], out double y))
        {
            point = new DrawingPoint((int)Math.Round(x), (int)Math.Round(y));
            return true;
        }

        point = default;
        return false;
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

    private static bool ShouldLogExecutionNode(GraphRuntimeNode node) =>
        node.NodeKind is not NodeKind.Start and
        not NodeKind.Reroute and
        not NodeKind.FunctionEntry and
        not NodeKind.CustomEvent;

    private void StoreStandardExecutionOutputs(GraphRuntimeNode node, NodeExecutionResult result, RuntimeContext context)
    {
        context.Set(node.Id, "__executed", true);
        context.Set(node.Id, "__status", result.Status.ToString());
        context.Set(node.Id, "__success", result.Status == NodeExecutionStatus.Success);
        context.Set(node.Id, "__message", result.Message);
        context.Set(node.Id, "__next_pin", result.NextPinName ?? string.Empty);

        if (HasOutputPin(node.NodeKind, "result") && !context.TryGetRaw(node.Id, "result", out _))
            context.Set(node.Id, "result", result.Status == NodeExecutionStatus.Success);
    }

    private bool HasOutputPin(NodeKind nodeKind, string pinName)
    {
        return _nodeRegistry.TryGetDefinition(nodeKind, out INodeDefinition definition) &&
               definition.Pins.Any(pin =>
                   pin.Direction == PinDirection.Output &&
                   pin.Kind != PinKind.Execution &&
                   string.Equals(pin.Name, pinName, StringComparison.Ordinal));
    }

    private static void WriteStructuredNodeLog(
        GraphRuntimeNode node,
        NodeExecutionResult result,
        RuntimeContext context,
        IReadOnlyDictionary<string, object> outputsBefore,
        TimeSpan elapsed,
        IReadOnlyList<LogEntry> capturedEntries)
    {
        LogLevel level = ResolveNodeLogLevel(result, capturedEntries);
        string status = result.Status switch
        {
            NodeExecutionStatus.FatalStop => "失败",
            NodeExecutionStatus.WarnButContinue => "警告",
            _ => capturedEntries.Any(entry => entry.Level == LogLevel.Warn) ? "警告" : "成功",
        };

        string returnText = FormatReturnResult(result, context.GetNodeOutputs(node.Id), outputsBefore);
        var builder = new StringBuilder();
        builder.AppendLine("执行节点");
        builder.AppendLine($"名称：{NodeLogLabel(node)}");
        builder.AppendLine($"耗时：{FormatElapsed(elapsed)}");
        builder.AppendLine($"结果：{status}");
        builder.AppendLine($"返回结果：{returnText}");

        var details = BuildCapturedDetails(capturedEntries, result).ToList();
        if (details.Count > 0)
        {
            builder.AppendLine("详情：");
            foreach (string detail in details)
                builder.AppendLine($"- {detail}");
        }

        builder.AppendLine();
        Logger.WriteDirect(level, builder.ToString().TrimEnd('\r', '\n') + Environment.NewLine);
    }

    private static LogLevel ResolveNodeLogLevel(NodeExecutionResult result, IReadOnlyList<LogEntry> capturedEntries)
    {
        if (result.Status == NodeExecutionStatus.FatalStop || capturedEntries.Any(entry => entry.Level == LogLevel.Error))
            return LogLevel.Error;
        if (result.Status == NodeExecutionStatus.WarnButContinue || capturedEntries.Any(entry => entry.Level == LogLevel.Warn))
            return LogLevel.Warn;

        return LogLevel.Info;
    }

    private static string FormatReturnResult(
        NodeExecutionResult result,
        IReadOnlyDictionary<string, object> outputsAfter,
        IReadOnlyDictionary<string, object> outputsBefore)
    {
        var changedOutputs = outputsAfter
            .Where(pair => !IsInternalRuntimeOutput(pair.Key))
            .Where(pair => !outputsBefore.TryGetValue(pair.Key, out object? before) || !Equals(before, pair.Value))
            .Select(pair => $"{pair.Key}={RuntimeContext.FormatValue(pair.Value)}")
            .ToList();
        if (changedOutputs.Count > 0)
            return string.Join("; ", changedOutputs);

        return string.IsNullOrWhiteSpace(result.Message) ? "-" : result.Message;
    }

    private static bool IsInternalRuntimeOutput(string pinName) =>
        pinName.StartsWith("__", StringComparison.Ordinal);

    private static IEnumerable<string> BuildCapturedDetails(IReadOnlyList<LogEntry> capturedEntries, NodeExecutionResult result)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (LogEntry entry in capturedEntries)
        {
            if (result.Status == NodeExecutionStatus.Success && entry.Level == LogLevel.Info)
                continue;

            string message = (entry.Message ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(message))
                continue;
            if (string.Equals(message, result.Message, StringComparison.Ordinal))
                continue;
            if (seen.Add(message))
                yield return message;
        }
    }

    private static string FormatElapsed(TimeSpan elapsed) =>
        elapsed.TotalSeconds >= 1
            ? $"{elapsed.TotalSeconds:0.00}s"
            : $"{elapsed.TotalMilliseconds:0}ms";

    private static string NodeLogLabel(GraphRuntimeNode node)
    {
        return string.IsNullOrWhiteSpace(node.NodeNumber)
            ? node.Title
            : $"{node.Title} {node.NodeNumber}";
    }

    private RuntimeContext CreateRuntimeContext()
    {
        return new RuntimeContext
        {
            PureNodeResolver = EvaluatePureNode,
        };
    }

    private bool EvaluatePureNode(GraphExecutionPlan plan, GraphRuntimeNode node, RuntimeContext context)
    {
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
