using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Logging;
using DrawingPoint = System.Drawing.Point;

namespace AutomationStudioWpf.Runtime;

public sealed partial class GraphRuntimeExecutor
{
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
            foreach (var parameter in entry.Parameters)
                context.Remove(entry.Id, parameter.Id);
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
}
