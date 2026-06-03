using AutomationStudioWpf.Adapters;
using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Logging;
using AutomationStudioWpf.Nodes;

namespace AutomationStudioWpf.Runtime;

/// <summary>
/// Graph execution scheduler. Concrete node behavior lives in node executors and adapters.
/// </summary>
public sealed class GraphRuntimeExecutor
{
    private const int MaxChainSteps = 10000;

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

        GraphRuntimeNode? startNode = plan.Nodes.FirstOrDefault(node => node.NodeKind == NodeKind.Start);
        if (startNode is null)
        {
            Logger.Error("执行失败：图中没有开始节点。");
            return new GraphExecutionResult(false, "执行失败：图中没有开始节点。", false);
        }

        var context = new RuntimeContext();
        GraphExecutionResult result = ExecuteChain(plan, startNode.Id, "exec_out", context, baseDirectory, assets, [], ct, out _);

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
        HashSet<string> callStack,
        CancellationToken ct,
        out GraphRuntimeNode? terminalNode)
    {
        terminalNode = null;
        GraphRuntimeNode? currentNode = GetNextExecutionNode(plan, startNodeId, startPinName);
        int stepCount = 0;

        while (currentNode is not null)
        {
            ct.ThrowIfCancellationRequested();
            stepCount++;
            if (stepCount > MaxChainSteps)
            {
                Logger.Error($"执行链超过安全步数 {MaxChainSteps}，疑似执行环路，已停止。");
                return new GraphExecutionResult(false, "执行失败：执行链疑似存在环路。", false);
            }

            NodeExecutionResult result = ExecuteNode(plan, currentNode, context, baseDirectory, assets, callStack, ct);
            if (!result.ContinueExecution)
                return new GraphExecutionResult(false, result.Message, false);

            if (result.NextPinName is null)
            {
                terminalNode = currentNode;
                break;
            }

            currentNode = GetNextExecutionNode(plan, currentNode.Id, result.NextPinName);
        }

        return new GraphExecutionResult(true, "执行完成。");
    }

    private NodeExecutionResult ExecuteNode(
        GraphExecutionPlan plan,
        GraphRuntimeNode node,
        RuntimeContext context,
        string baseDirectory,
        RuntimeAssetLibrary assets,
        HashSet<string> callStack,
        CancellationToken ct)
    {
        return node.NodeKind switch
        {
            NodeKind.Start => NodeExecutionResult.Ok(string.Empty, "exec_out"),
            NodeKind.Reroute => NodeExecutionResult.Ok(string.Empty, node.RoutedKind == PinKind.Execution ? "out" : null),
            NodeKind.If => ExecuteIfNode(plan, node, context),
            NodeKind.ForLoop => ExecuteForLoopNode(plan, node, context, baseDirectory, assets, callStack, ct),
            NodeKind.WhileLoop => ExecuteWhileLoopNode(plan, node, context, baseDirectory, assets, callStack, ct),
            NodeKind.FunctionEntry => NodeExecutionResult.Ok(string.Empty, "exec_out"),
            NodeKind.FunctionReturn => NodeExecutionResult.Ok(string.Empty, null),
            NodeKind.MacroEntry => NodeExecutionResult.Ok(string.Empty, "exec_out"),
            NodeKind.MacroOutput => NodeExecutionResult.Ok(string.Empty, null),
            NodeKind.FunctionCall => ExecuteFunctionCall(plan, node, context, baseDirectory, assets, callStack, ct),
            NodeKind.MacroCall => ExecuteMacroCall(plan, node, context, baseDirectory, assets, callStack, ct),
            _ => ExecuteRegisteredNode(plan, node, context, baseDirectory, assets, callStack, ct),
        };
    }

    private NodeExecutionResult ExecuteRegisteredNode(
        GraphExecutionPlan plan,
        GraphRuntimeNode node,
        RuntimeContext context,
        string baseDirectory,
        RuntimeAssetLibrary assets,
        HashSet<string> callStack,
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
        HashSet<string> callStack,
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
            GraphExecutionResult bodyResult = ExecuteChain(plan, node.Id, "exec_loop_body", context, baseDirectory, assets, callStack, ct, out _);
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
        HashSet<string> callStack,
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
            GraphExecutionResult bodyResult = ExecuteChain(plan, node.Id, "exec_loop_body", context, baseDirectory, assets, callStack, ct, out _);
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
        HashSet<string> callStack,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(callNode.FunctionId) || !assets.Functions.TryGetValue(callNode.FunctionId, out var functionPlan))
            return NodeExecutionResult.Fatal($"函数不存在：{callNode.Title}");
        if (!callStack.Add($"function:{callNode.FunctionId}"))
            return NodeExecutionResult.Fatal($"检测到函数递归调用：{callNode.Title}");

        try
        {
            var entry = functionPlan.Nodes.FirstOrDefault(n => n.NodeKind == NodeKind.FunctionEntry);
            var ret = functionPlan.Nodes.FirstOrDefault(n => n.NodeKind == NodeKind.FunctionReturn);
            if (entry is null || ret is null)
                return NodeExecutionResult.Fatal($"函数结构无效：{callNode.Title}");

            var childContext = new RuntimeContext();
            CopyCallInputsToEntry(callerPlan, callNode, callerContext, entry, childContext);
            var result = ExecuteChain(functionPlan, entry.Id, "exec_out", childContext, baseDirectory, assets, callStack, ct, out _);
            if (!result.ContinueExecution)
                return NodeExecutionResult.Fatal(result.Message);

            CopyReturnInputsToCallOutputs(functionPlan, ret, childContext, callNode, callerContext);
            Logger.Info($"函数调用完成：{callNode.Title}");
            return NodeExecutionResult.Ok($"函数调用完成：{callNode.Title}", "exec_out");
        }
        finally
        {
            callStack.Remove($"function:{callNode.FunctionId}");
        }
    }

    private NodeExecutionResult ExecuteMacroCall(
        GraphExecutionPlan callerPlan,
        GraphRuntimeNode callNode,
        RuntimeContext callerContext,
        string baseDirectory,
        RuntimeAssetLibrary assets,
        HashSet<string> callStack,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(callNode.MacroId) || !assets.Macros.TryGetValue(callNode.MacroId, out var macroPlan))
            return NodeExecutionResult.Fatal($"宏不存在：{callNode.Title}");
        if (!callStack.Add($"macro:{callNode.MacroId}"))
            return NodeExecutionResult.Fatal($"检测到宏递归调用：{callNode.Title}");

        try
        {
            var entry = macroPlan.Nodes.FirstOrDefault(n => n.NodeKind == NodeKind.MacroEntry);
            if (entry is null)
                return NodeExecutionResult.Fatal($"宏结构无效：{callNode.Title}");

            var childContext = new RuntimeContext();
            CopyCallInputsToEntry(callerPlan, callNode, callerContext, entry, childContext);
            var result = ExecuteChain(macroPlan, entry.Id, "exec_out", childContext, baseDirectory, assets, callStack, ct, out var terminal);
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
            callStack.Remove($"macro:{callNode.MacroId}");
        }
    }

    private static void CopyCallInputsToEntry(
        GraphExecutionPlan callerPlan,
        GraphRuntimeNode callNode,
        RuntimeContext callerContext,
        GraphRuntimeNode entryNode,
        RuntimeContext childContext)
    {
        foreach (var input in callerPlan.Connections.Where(c => c.TargetNodeId == callNode.Id && c.TargetPinKind != PinKind.Execution))
        {
            if (callerContext.TryGetRaw(input.SourceNodeId, input.SourcePinName, out object value))
                childContext.Set(entryNode.Id, input.TargetPinName, value);
        }
    }

    private static void CopyReturnInputsToCallOutputs(
        GraphExecutionPlan assetPlan,
        GraphRuntimeNode returnNode,
        RuntimeContext childContext,
        GraphRuntimeNode callNode,
        RuntimeContext callerContext)
    {
        foreach (var input in assetPlan.Connections.Where(c => c.TargetNodeId == returnNode.Id && c.TargetPinKind != PinKind.Execution))
        {
            if (childContext.TryGetRaw(input.SourceNodeId, input.SourcePinName, out object value))
                callerContext.Set(callNode.Id, input.TargetPinName, value);
        }
    }

    private static GraphRuntimeNode? GetNextExecutionNode(GraphExecutionPlan plan, string sourceNodeId, string sourcePinName)
    {
        GraphRuntimeConnection? connection = plan.Connections.FirstOrDefault(connection =>
            connection.SourceNodeId == sourceNodeId &&
            connection.SourcePinName == sourcePinName &&
            connection.TargetPinKind == PinKind.Execution);

        return connection is null
            ? null
            : plan.Nodes.FirstOrDefault(node => node.Id == connection.TargetNodeId);
    }
}
