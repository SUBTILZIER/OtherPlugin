using System.Runtime.CompilerServices;
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
            new Dictionary<string, GraphExecutionPlan>(),
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

        var context = new RuntimeContext();
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
        return node.NodeKind switch
        {
            NodeKind.Start => NodeExecutionResult.Ok(string.Empty, "exec_out"),
            NodeKind.Reroute => NodeExecutionResult.Ok(string.Empty, node.RoutedKind == PinKind.Execution ? "out" : null),
            NodeKind.If => ExecuteIfNode(plan, node, context),
            NodeKind.ForLoop => ExecuteForLoopNode(plan, node, context, baseDirectory, assets, state, ct),
            NodeKind.WhileLoop => ExecuteWhileLoopNode(plan, node, context, baseDirectory, assets, state, ct),
            NodeKind.ToDo => ExecuteToDoNode(plan, node, context),
            NodeKind.FunctionEntry => NodeExecutionResult.Ok(string.Empty, "exec_out"),
            NodeKind.FunctionReturn => NodeExecutionResult.Ok(string.Empty, null),
            NodeKind.MacroEntry => NodeExecutionResult.Ok(string.Empty, "exec_out"),
            NodeKind.MacroOutput => NodeExecutionResult.Ok(string.Empty, null),
            NodeKind.FunctionCall => ExecuteFunctionCall(plan, node, context, baseDirectory, assets, state, ct),
            NodeKind.MacroCall => ExecuteMacroCall(plan, node, context, baseDirectory, assets, state, ct),
            NodeKind.CustomEvent => NodeExecutionResult.Ok(string.Empty, "exec_out"),
            NodeKind.CustomEventCall => ExecuteCustomEventCall(plan, node, context, baseDirectory, assets, state, ct),
            _ => ExecuteRegisteredNode(plan, node, context, baseDirectory, assets, ct),
        };
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
            .Where(candidate => candidate.NodeKind != NodeKind.Reroute)
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
            return executor.Execute(new NodeExecutionRequest(plan, node, context, baseDirectory, _adapters, ct));
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

            var childContext = new RuntimeContext();
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

    private NodeExecutionResult ExecuteMacroCall(
        GraphExecutionPlan callerPlan,
        GraphRuntimeNode callNode,
        RuntimeContext callerContext,
        string baseDirectory,
        RuntimeAssetLibrary assets,
        RuntimeExecutionState state,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(callNode.MacroId) || !assets.Macros.TryGetValue(callNode.MacroId, out var macroPlan))
            return NodeExecutionResult.Fatal($"宏不存在：{callNode.Title}");
        if (!state.CallStack.Add($"macro:{callNode.MacroId}"))
            return NodeExecutionResult.Fatal($"检测到宏递归调用：{callNode.Title}");

        try
        {
            var entry = macroPlan.Index.FirstNode(NodeKind.MacroEntry);
            if (entry is null)
                return NodeExecutionResult.Fatal($"宏结构无效：{callNode.Title}");

            var childContext = new RuntimeContext();
            CopyCallInputsToEntry(callerPlan, callNode, callerContext, entry, childContext);
            var result = ExecuteChain(macroPlan, entry.Id, "exec_out", childContext, baseDirectory, assets, state, ct, out var terminal);
            if (!result.ContinueExecution)
                return NodeExecutionResult.Fatal(result.Message);
            if (terminal?.NodeKind != NodeKind.MacroOutput)
                return NodeExecutionResult.Warn($"宏没有到达输出节点：{callNode.Title}", null);

            CopyReturnInputsToCallOutputs(macroPlan, terminal, childContext, callNode, callerContext);
            string nextPin = $"exec_{terminal.Id}";
            Logger.Info($"宏调用完成：{callNode.Title} -> {terminal.ExitName}");
            return NodeExecutionResult.Ok($"宏调用完成：{callNode.Title}", nextPin);
        }
        finally
        {
            state.CallStack.Remove($"macro:{callNode.MacroId}");
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
            connectedPins.Add(input.TargetPinName);
            if (callerContext.TryGetRaw(input.SourceNodeId, input.SourcePinName, out object value))
                childContext.Set(entryNode.Id, input.TargetPinName, value);
        }

        ApplyParameterDefaults(callNode, childContext, entryNode.Id, connectedPins);
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
            if (childContext.TryGetRaw(input.SourceNodeId, input.SourcePinName, out object value))
                callerContext.Set(callNode.Id, input.TargetPinName, value);
        }

        ApplyParameterDefaults(returnNode, callerContext, callNode.Id, connectedPins);
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

    private sealed class RuntimeExecutionState
    {
        public HashSet<string> CallStack { get; } = new(StringComparer.Ordinal);

        public HashSet<string> ActiveToDoReturnJumps { get; } = new(StringComparer.Ordinal);
    }
}
