using System.Text;
using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Runtime;

namespace AutomationStudioWpf.Services;

internal sealed class FinalCodePreviewGenerator
{
    private const int MaxDepth = 64;
    private const int MaxLines = 4000;

    public FinalCodePreviewResult Generate(
        GraphExecutionPlan plan,
        ContentAssetViewModel asset,
        GraphAssetKind graphKind,
        IReadOnlyDictionary<string, GraphExecutionPlan>? functionPlans = null,
        IReadOnlyDictionary<string, string>? functionNames = null)
    {
        var builder = new StringBuilder();
        var state = new GenerationState(functionPlans ?? new Dictionary<string, GraphExecutionPlan>(StringComparer.Ordinal), functionNames ?? new Dictionary<string, string>(StringComparer.Ordinal));

        try
        {
            AppendLine(builder, $"# asset: {asset.Name}");
            AppendLine(builder, $"# graph kind: {graphKind}");
            AppendLine(builder, string.Empty);

            var entries = plan.Nodes.Where(node => node.NodeKind is NodeKind.Start or NodeKind.FunctionEntry or NodeKind.CustomEvent).ToList();
            if (entries.Count == 0)
            {
                AppendLine(builder, "# no entry node");
                return new FinalCodePreviewResult(builder.ToString().TrimEnd(), null);
            }

            foreach (var entry in entries)
            {
                if (state.LineCount >= MaxLines)
                    break;

                if (entry.NodeKind == NodeKind.FunctionEntry)
                {
                    EmitChain(plan, "current", entry, "exec_out", builder, state, 0, new HashSet<string>(StringComparer.Ordinal));
                }
                else
                {
                    EmitLine(builder, state, 0, DescribeEntry(entry));
                    EmitNextChain(plan, "current", entry, "exec_out", builder, state, 0, new HashSet<string>(StringComparer.Ordinal));
                }

                EmitLine(builder, state, 0, string.Empty);
            }

            return new FinalCodePreviewResult(builder.ToString().TrimEnd(), null);
        }
        catch (Exception ex)
        {
            return new FinalCodePreviewResult(builder.Length == 0 ? "# final code preview failed" : builder.ToString().TrimEnd(), ex.Message);
        }
    }

    private void EmitNextChain(
        GraphExecutionPlan plan,
        string scopeKey,
        GraphRuntimeNode sourceNode,
        string sourcePinName,
        StringBuilder builder,
        GenerationState state,
        int depth,
        HashSet<string> stack)
    {
        if (plan.Index.GetExecutionConnection(sourceNode.Id, sourcePinName) is null)
            return;

        EmitBlankLine(builder, state);
        EmitChain(plan, scopeKey, sourceNode, sourcePinName, builder, state, depth, stack);
    }

    private void EmitChain(
        GraphExecutionPlan plan,
        string scopeKey,
        GraphRuntimeNode sourceNode,
        string sourcePinName,
        StringBuilder builder,
        GenerationState state,
        int depth,
        HashSet<string> stack)
    {
        if (state.LineCount >= MaxLines)
            return;
        if (depth > MaxDepth)
        {
            EmitLine(builder, state, depth, "# depth limit reached");
            return;
        }

        GraphRuntimeConnection? connection = plan.Index.GetExecutionConnection(sourceNode.Id, sourcePinName);
        if (connection is null)
            return;

        GraphRuntimeNode? node = plan.Index.GetNode(connection.TargetNodeId);
        if (node is null)
        {
            EmitLine(builder, state, depth, $"# broken exec link: {FormatNode(sourceNode)}.{sourcePinName} -> {connection.TargetNodeId}.{connection.TargetPinName}");
            return;
        }

        string stackKey = $"{scopeKey}:{node.Id}";
        if (!stack.Add(stackKey))
        {
            EmitLine(builder, state, depth, $"# loop detected: {FormatNode(node)}");
            return;
        }

        try
        {
            EmitNode(plan, scopeKey, node, builder, state, depth, stack);
        }
        finally
        {
            stack.Remove(stackKey);
        }
    }

    private void EmitNode(
        GraphExecutionPlan plan,
        string scopeKey,
        GraphRuntimeNode node,
        StringBuilder builder,
        GenerationState state,
        int depth,
        HashSet<string> stack)
    {
        switch (node.NodeKind)
        {
            case NodeKind.Reroute:
                EmitChain(plan, scopeKey, node, "out", builder, state, depth, stack);
                return;
            case NodeKind.If:
                EmitIf(plan, scopeKey, node, builder, state, depth, stack);
                return;
            case NodeKind.ForLoop:
                EmitFor(plan, scopeKey, node, builder, state, depth, stack);
                return;
            case NodeKind.WhileLoop:
                EmitWhile(plan, scopeKey, node, builder, state, depth, stack);
                return;
            case NodeKind.ToDo:
                EmitToDo(plan, node, builder, state, depth);
                return;
            case NodeKind.MultiThread:
                EmitMultiThread(plan, scopeKey, node, builder, state, depth, stack);
                return;
            case NodeKind.FunctionCall:
                EmitFunctionCall(plan, node, builder, state, depth, stack);
                EmitNextChain(plan, scopeKey, node, "exec_out", builder, state, depth, stack);
                return;
            case NodeKind.CustomEventCall:
                EmitCustomEventCall(plan, scopeKey, node, builder, state, depth, stack);
                EmitNextChain(plan, scopeKey, node, "exec_out", builder, state, depth, stack);
                return;
            case NodeKind.Start:
            case NodeKind.CustomEvent:
                EmitLine(builder, state, depth, $"# {FormatNode(node)}");
                EmitNextChain(plan, scopeKey, node, "exec_out", builder, state, depth, stack);
                return;
            case NodeKind.FunctionEntry:
                EmitChain(plan, scopeKey, node, "exec_out", builder, state, depth, stack);
                return;
            case NodeKind.FunctionReturn:
                string returnValues = FormatParameterAssignments(plan, node, state);
                EmitLine(builder, state, depth, string.IsNullOrWhiteSpace(returnValues) ? "return;" : $"return {returnValues};");
                return;
            default:
                EmitLine(builder, state, depth, DescribeNode(node, plan, state));
                EmitNextChain(plan, scopeKey, node, "exec_out", builder, state, depth, stack);
                return;
        }
    }

    private void EmitIf(GraphExecutionPlan plan, string scopeKey, GraphRuntimeNode node, StringBuilder builder, GenerationState state, int depth, HashSet<string> stack)
    {
        EmitLine(builder, state, depth, $"if ({ResolveValueExpression(plan, node, "condition", node.ConditionValue ? "true" : "false", state)}) {{");
        EmitChain(plan, scopeKey, node, "exec_true", builder, state, depth + 1, stack);
        EmitLine(builder, state, depth, "} else {");
        EmitChain(plan, scopeKey, node, "exec_false", builder, state, depth + 1, stack);
        EmitLine(builder, state, depth, "}");
    }

    private void EmitFor(GraphExecutionPlan plan, string scopeKey, GraphRuntimeNode node, StringBuilder builder, GenerationState state, int depth, HashSet<string> stack)
    {
        string endCondition = ResolveValueExpression(plan, node, "end_condition", node.ConditionValue ? "true" : "false", state);
        EmitLine(builder, state, depth, $"for (...; end_condition = {endCondition}; ...) {{");
        EmitChain(plan, scopeKey, node, "exec_loop_body", builder, state, depth + 1, stack);
        EmitLine(builder, state, depth, "}");
        EmitNextChain(plan, scopeKey, node, "exec_completed", builder, state, depth, stack);
    }

    private void EmitWhile(GraphExecutionPlan plan, string scopeKey, GraphRuntimeNode node, StringBuilder builder, GenerationState state, int depth, HashSet<string> stack)
    {
        string condition = ResolveValueExpression(plan, node, "condition", node.ConditionValue ? "true" : "false", state);
        EmitLine(builder, state, depth, $"while (!({condition})) {{");
        EmitChain(plan, scopeKey, node, "exec_loop_body", builder, state, depth + 1, stack);
        EmitLine(builder, state, depth, "}");
        EmitNextChain(plan, scopeKey, node, "exec_completed", builder, state, depth, stack);
    }

    private void EmitToDo(GraphExecutionPlan plan, GraphRuntimeNode node, StringBuilder builder, GenerationState state, int depth)
    {
        EmitLine(builder, state, depth, $"goto {ResolveToDoTarget(plan, node)};");
        if (node.ReturnAfterTarget)
            EmitLine(builder, state, depth, "# return after target");
    }

    private void EmitMultiThread(GraphExecutionPlan plan, string scopeKey, GraphRuntimeNode node, StringBuilder builder, GenerationState state, int depth, HashSet<string> stack)
    {
        int threadCount = Math.Max(MultiThreadNodeViewModel.MinimumThreadOutputCount, node.ThreadOutputCount);
        EmitLine(builder, state, depth, $"parallel {{ // {FormatNode(node)}");
        for (int i = 1; i <= threadCount; i++)
        {
            string pinName = MultiThreadNodeViewModel.ThreadOutputPinName(i);
            if (plan.Index.GetExecutionConnection(node.Id, pinName) is null)
                continue;

            EmitLine(builder, state, depth + 1, $"thread {i} {{");
            EmitChain(plan, scopeKey, node, pinName, builder, state, depth + 2, stack);
            EmitLine(builder, state, depth + 1, "}");
        }

        EmitLine(builder, state, depth, "} wait_all");
        EmitNextChain(plan, scopeKey, node, MultiThreadNodeViewModel.CompletedPinName, builder, state, depth, stack);
    }

    private void EmitFunctionCall(
        GraphExecutionPlan callerPlan,
        GraphRuntimeNode node,
        StringBuilder builder,
        GenerationState state,
        int depth,
        HashSet<string> stack)
    {
        string functionId = node.FunctionId ?? string.Empty;
        string functionName = state.FunctionNames.TryGetValue(functionId, out string? name) ? name : FormatCall(node);
        string arguments = FormatParameterAssignments(callerPlan, node, state);
        if (string.IsNullOrWhiteSpace(functionId) || !state.FunctionPlans.TryGetValue(functionId, out var functionPlan))
        {
            EmitLine(builder, state, depth, $"call {functionName}({arguments}); # function not found");
            return;
        }

        string callStackKey = $"function:{functionId}";
        if (!state.CallStack.Add(callStackKey))
        {
            EmitLine(builder, state, depth, $"call {functionName}({arguments}); # recursive call omitted");
            return;
        }

        try
        {
            var entry = functionPlan.Index.FirstNode(NodeKind.FunctionEntry);
            if (entry is null)
            {
                EmitLine(builder, state, depth, $"call {functionName}({arguments}); # invalid function graph");
                return;
            }

            EmitLine(builder, state, depth, $"function_call {functionName}({arguments}) {{");
            state.PushParameterBindings(BuildParameterBindings(callerPlan, node, entry, state));
            try
            {
                EmitChain(functionPlan, callStackKey, entry, "exec_out", builder, state, depth + 1, stack);
            }
            finally
            {
                state.PopParameterBindings();
            }

            EmitLine(builder, state, depth, "}");
        }
        finally
        {
            state.CallStack.Remove(callStackKey);
        }
    }

    private void EmitCustomEventCall(
        GraphExecutionPlan plan,
        string scopeKey,
        GraphRuntimeNode node,
        StringBuilder builder,
        GenerationState state,
        int depth,
        HashSet<string> stack)
    {
        string eventId = node.CustomEventId ?? string.Empty;
        string arguments = FormatParameterAssignments(plan, node, state);
        var entry = string.IsNullOrWhiteSpace(eventId) ? null : plan.Index.GetCustomEvent(eventId);
        if (entry is null)
        {
            EmitLine(builder, state, depth, $"event_call {FormatCall(node)}({arguments}); # custom event not found");
            return;
        }

        string callStackKey = $"{scopeKey}:custom_event:{eventId}";
        if (!state.CallStack.Add(callStackKey))
        {
            EmitLine(builder, state, depth, $"event_call {FormatCall(node)}({arguments}); # recursive event omitted");
            return;
        }

        try
        {
            EmitLine(builder, state, depth, $"event_call {FormatNode(entry)}({arguments}) {{");
            state.PushParameterBindings(BuildParameterBindings(plan, node, entry, state));
            try
            {
                EmitChain(plan, scopeKey, entry, "exec_out", builder, state, depth + 1, stack);
            }
            finally
            {
                state.PopParameterBindings();
            }

            EmitLine(builder, state, depth, "}");
        }
        finally
        {
            state.CallStack.Remove(callStackKey);
        }
    }

    private string ResolveValueExpression(GraphExecutionPlan plan, GraphRuntimeNode node, string pinName, string fallback, GenerationState state, HashSet<string>? pureStack = null)
    {
        GraphRuntimeConnection? connection = plan.Index.GetInputConnection(node.Id, pinName);
        if (connection is null)
            return fallback;

        pureStack ??= new HashSet<string>(StringComparer.Ordinal);
        return ResolveSourceExpression(plan, connection.SourceNodeId, connection.SourcePinName, connection.SourcePinKind, state, 0, pureStack);
    }

    private string ResolveStringInputExpression(GraphExecutionPlan plan, GraphRuntimeNode node, string pinName, string fallback, GenerationState state, HashSet<string>? pureStack = null)
    {
        GraphRuntimeConnection? connection = plan.Index.GetInputConnection(node.Id, pinName);
        if (connection is null)
            return Quote(fallback);

        pureStack ??= new HashSet<string>(StringComparer.Ordinal);
        return ResolveSourceExpression(plan, connection.SourceNodeId, connection.SourcePinName, connection.SourcePinKind, state, 0, pureStack);
    }

    private string ResolveVectorInputExpression(GraphExecutionPlan plan, GraphRuntimeNode node, string pinName, double fallbackX, double fallbackY, GenerationState state, HashSet<string>? pureStack = null)
    {
        GraphRuntimeConnection? connection = plan.Index.GetInputConnection(node.Id, pinName, PinKind.Vector2D);
        if (connection is null)
            return FormatPoint(fallbackX, fallbackY);

        pureStack ??= new HashSet<string>(StringComparer.Ordinal);
        return ResolveSourceExpression(plan, connection.SourceNodeId, connection.SourcePinName, connection.SourcePinKind, state, 0, pureStack);
    }

    private string ResolveSourceExpression(
        GraphExecutionPlan plan,
        string sourceNodeId,
        string sourcePinName,
        PinKind sourcePinKind,
        GenerationState state,
        int depth,
        HashSet<string> pureStack)
    {
        if (depth > MaxDepth)
            return "# depth limit";

        GraphRuntimeNode? sourceNode = plan.Index.GetNode(sourceNodeId);
        if (sourceNode is null)
            return $"{sourceNodeId}.{sourcePinName}";

        if (sourceNode.NodeKind is NodeKind.FunctionEntry or NodeKind.CustomEvent &&
            state.TryGetParameterBinding(sourcePinName, out string? parameterExpression))
        {
            return parameterExpression;
        }

        if (NodeTraits.IsPure(sourceNode.NodeKind))
        {
            if (!pureStack.Add(sourceNode.Id))
                return "# pure cycle";

            try
            {
                return ResolvePureNodeExpression(plan, sourceNode, state, depth + 1, pureStack);
            }
            finally
            {
                pureStack.Remove(sourceNode.Id);
            }
        }

        if (TryResolveKnownOutputExpression(plan, sourceNode, sourcePinName, sourcePinKind, state, pureStack, out string? knownExpression))
        {
            return knownExpression!;
        }

        return sourcePinKind == PinKind.Execution
            ? FormatNode(sourceNode)
            : $"{FormatNode(sourceNode)}.{GetOutputLabel(sourceNode, sourcePinName)}";
    }

    private bool TryResolveKnownOutputExpression(
        GraphExecutionPlan plan,
        GraphRuntimeNode sourceNode,
        string sourcePinName,
        PinKind sourcePinKind,
        GenerationState state,
        HashSet<string> pureStack,
        out string? expression)
    {
        expression = null;
        string outputStackKey = $"output:{sourceNode.Id}:{sourcePinName}";
        if (!pureStack.Add(outputStackKey))
        {
            expression = $"{FormatNode(sourceNode)}.{sourcePinName}";
            return true;
        }

        try
        {
        if (sourceNode.NodeKind == NodeKind.FunctionCall && sourcePinKind != PinKind.Execution)
        {
            expression = ResolveFunctionCallOutputExpression(plan, sourceNode, sourcePinName, sourcePinKind, state, pureStack);
            return true;
        }

        if (sourceNode.NodeKind == NodeKind.GetMousePosition)
        {
            if (sourcePinName == "position" && sourcePinKind == PinKind.Vector2D)
            {
                expression = "get_mouse_position().position";
                return true;
            }

            if (sourcePinName == "result" && sourcePinKind == PinKind.Boolean)
            {
                expression = "get_mouse_position().result";
                return true;
            }
        }

        if (sourceNode.NodeKind == NodeKind.MouseMove)
        {
            if (sourcePinName == "position" && sourcePinKind == PinKind.Vector2D)
            {
                expression = $"mouse_move({ResolveVectorInputExpression(plan, sourceNode, "position", sourceNode.PositionX, sourceNode.PositionY, state, pureStack)}).position";
                return true;
            }

            if (sourcePinName == "result" && sourcePinKind == PinKind.Boolean)
            {
                expression = $"mouse_move({ResolveVectorInputExpression(plan, sourceNode, "position", sourceNode.PositionX, sourceNode.PositionY, state, pureStack)}).result";
                return true;
            }
        }

        if (sourcePinKind == PinKind.String && sourcePinName == "process_name")
        {
            if (sourceNode.NodeKind is NodeKind.SelectWindow)
            {
                expression = ResolveStringInputExpression(plan, sourceNode, "process_name", sourceNode.ProcessName ?? string.Empty, state, pureStack);
                return true;
            }

            if (sourceNode.NodeKind is NodeKind.WaitWindow or NodeKind.CloseWindow or NodeKind.WindowExists)
            {
                expression = ResolveStringInputExpression(plan, sourceNode, "process_name", sourceNode.Text ?? string.Empty, state, pureStack);
                return true;
            }
        }

        if (sourcePinKind == PinKind.String && sourcePinName == "image_path")
        {
            if (sourceNode.NodeKind is NodeKind.WaitImage)
            {
                expression = ResolveStringInputExpression(plan, sourceNode, "image_path", sourceNode.ImagePath ?? string.Empty, state, pureStack);
                return true;
            }

            if (sourceNode.NodeKind is NodeKind.SaveScreenshot)
            {
                expression = ResolveStringInputExpression(plan, sourceNode, "path", sourceNode.Text ?? string.Empty, state, pureStack);
                return true;
            }
        }

        return false;
        }
        finally
        {
            pureStack.Remove(outputStackKey);
        }
    }

    private string ResolvePureNodeExpression(GraphExecutionPlan plan, GraphRuntimeNode node, GenerationState state, int depth, HashSet<string> pureStack)
    {
        if (depth > MaxDepth)
            return "# depth limit";

        return node.NodeKind switch
        {
            NodeKind.Compare => $"{ResolveStringInputExpression(plan, node, "left", node.Text ?? string.Empty, state, pureStack)} {NormalizeCompareOp(node.Text3)} {ResolveStringInputExpression(plan, node, "right", node.Text2 ?? string.Empty, state, pureStack)}",
            NodeKind.BooleanAnd => string.Join(" AND ", GetVariadicPinNames(node).Select(pinName => ResolveValueExpression(plan, node, pinName, GetVariadicBoolFallback(node, pinName), state, pureStack))),
            NodeKind.BooleanOr => string.Join(" OR ", GetVariadicPinNames(node).Select(pinName => ResolveValueExpression(plan, node, pinName, GetVariadicBoolFallback(node, pinName), state, pureStack))),
            NodeKind.BooleanNot => $"NOT {ResolveValueExpression(plan, node, "value", node.Flag ? "true" : "false", state, pureStack)}",
            NodeKind.StringConcat => string.Join(" + ", GetVariadicPinNames(node).Select(pinName => ResolveStringInputExpression(plan, node, pinName, GetVariadicFallback(node, pinName), state, pureStack))),
            _ => node.Title,
        };
    }

    private static IEnumerable<string> GetVariadicPinNames(GraphRuntimeNode node)
    {
        int count = Math.Max(2, node.VariadicInputCount);
        for (int i = 1; i <= count; i++)
            yield return CommonNodeViewModel.VariadicInputName(i);
    }

    private static string GetVariadicFallback(GraphRuntimeNode node, string pinName)
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

    private static string GetVariadicBoolFallback(GraphRuntimeNode node, string pinName)
    {
        string value = GetVariadicFallback(node, pinName);
        return bool.TryParse(value, out bool parsed) && parsed ? "true" : "false";
    }

    private static string ResolveToDoTarget(GraphExecutionPlan plan, GraphRuntimeNode node)
    {
        if (!string.IsNullOrWhiteSpace(node.TargetNodeTitle) && !string.IsNullOrWhiteSpace(node.TargetNodeNumber))
            return $"{node.TargetNodeTitle} {node.TargetNodeNumber}";

        if (!string.IsNullOrWhiteSpace(node.TargetNodeId) && plan.Index.GetNode(node.TargetNodeId) is { } target)
            return $"{target.Title} {target.NodeNumber}";

        return "<invalid_todo_target>";
    }

    private IReadOnlyDictionary<string, string> BuildParameterBindings(
        GraphExecutionPlan callerPlan,
        GraphRuntimeNode callNode,
        GraphRuntimeNode entryNode,
        GenerationState state)
    {
        var bindings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entryParameter in entryNode.Parameters)
        {
            GraphRuntimeParameter callParameter = callNode.Parameters.FirstOrDefault(parameter => parameter.Id == entryParameter.Id)
                ?? entryParameter;
            bindings[entryParameter.Id] = ResolveTypedInputExpression(
                callerPlan,
                callNode,
                entryParameter.Id,
                entryParameter.Type,
                string.IsNullOrWhiteSpace(callParameter.DefaultValue) ? entryParameter.DefaultValue : callParameter.DefaultValue,
                state,
                new HashSet<string>(StringComparer.Ordinal));
        }

        return bindings;
    }

    private string ResolveFunctionCallOutputExpression(
        GraphExecutionPlan callerPlan,
        GraphRuntimeNode callNode,
        string outputPinName,
        PinKind outputPinKind,
        GenerationState state,
        HashSet<string> pureStack)
    {
        string functionId = callNode.FunctionId ?? string.Empty;
        if (string.IsNullOrWhiteSpace(functionId) || !state.FunctionPlans.TryGetValue(functionId, out var functionPlan))
            return $"{FormatCall(callNode)}.{GetOutputLabel(callNode, outputPinName)}";

        string stackKey = $"function_output:{functionId}:{outputPinName}";
        if (!state.CallStack.Add(stackKey))
            return $"{FormatCall(callNode)}.{GetOutputLabel(callNode, outputPinName)}";

        try
        {
            var entry = functionPlan.Index.FirstNode(NodeKind.FunctionEntry);
            var returnNode = functionPlan.Index.FirstNode(NodeKind.FunctionReturn);
            if (entry is null || returnNode is null)
                return $"{FormatCall(callNode)}.{GetOutputLabel(callNode, outputPinName)}";

            GraphRuntimeParameter returnParameter = returnNode.Parameters.FirstOrDefault(parameter => parameter.Id == outputPinName)
                ?? new GraphRuntimeParameter(outputPinName, outputPinName, ToParameterType(outputPinKind), string.Empty);

            state.PushParameterBindings(BuildParameterBindings(callerPlan, callNode, entry, state));
            try
            {
                return ResolveTypedInputExpression(
                    functionPlan,
                    returnNode,
                    outputPinName,
                    returnParameter.Type,
                    returnParameter.DefaultValue,
                    state,
                    pureStack);
            }
            finally
            {
                state.PopParameterBindings();
            }
        }
        finally
        {
            state.CallStack.Remove(stackKey);
        }
    }

    private string FormatParameterAssignments(GraphExecutionPlan plan, GraphRuntimeNode node, GenerationState state)
    {
        var values = new List<string>();
        foreach (var parameter in node.Parameters)
        {
            string value = ResolveParameterInputExpression(plan, node, parameter, state, new HashSet<string>(StringComparer.Ordinal));
            values.Add($"{ParameterLabel(parameter)}={value}");
        }

        return string.Join(", ", values);
    }

    private string ResolveParameterInputExpression(
        GraphExecutionPlan plan,
        GraphRuntimeNode node,
        GraphRuntimeParameter parameter,
        GenerationState state,
        HashSet<string> pureStack) =>
        ResolveTypedInputExpression(plan, node, parameter.Id, parameter.Type, parameter.DefaultValue, state, pureStack);

    private string ResolveTypedInputExpression(
        GraphExecutionPlan plan,
        GraphRuntimeNode node,
        string pinName,
        GraphParameterType type,
        string fallback,
        GenerationState state,
        HashSet<string> pureStack)
    {
        PinKind pinKind = ToPinKind(type);
        GraphRuntimeConnection? connection = type == GraphParameterType.String
            ? plan.Index.GetInputConnection(node.Id, pinName)
            : plan.Index.GetInputConnection(node.Id, pinName, pinKind);
        if (connection is not null)
            return ResolveSourceExpression(plan, connection.SourceNodeId, connection.SourcePinName, connection.SourcePinKind, state, 0, pureStack);

        return type switch
        {
            GraphParameterType.Boolean => bool.TryParse(fallback, out bool value) && value ? "true" : "false",
            GraphParameterType.Vector2D => FormatVectorDefault(fallback),
            _ => Quote(fallback),
        };
    }

    private static string DescribeEntry(GraphRuntimeNode node) => node.NodeKind switch
    {
        NodeKind.Start => $"start {FormatNode(node)}",
        NodeKind.FunctionEntry => $"function {FormatNode(node)}",
        NodeKind.CustomEvent => $"event {FormatNode(node)}",
        _ => FormatNode(node),
    };

    private string DescribeNode(GraphRuntimeNode node, GraphExecutionPlan plan, GenerationState state)
    {
        return node.NodeKind switch
        {
            NodeKind.PrintLog => $"print({ResolveStringInputExpression(plan, node, "message", node.PrintLogMessage ?? string.Empty, state)})",
            NodeKind.Delay => $"delay({node.DelayMs})",
            NodeKind.MouseClick => $"mouse_click({ResolveVectorInputExpression(plan, node, "position", node.PositionX, node.PositionY, state)})",
            NodeKind.MouseMove => $"mouse_move({ResolveVectorInputExpression(plan, node, "position", node.PositionX, node.PositionY, state)})",
            NodeKind.MouseDoubleClick => $"mouse_double_click({ResolveVectorInputExpression(plan, node, "position", node.Number, node.Number2, state)})",
            NodeKind.GetMousePosition => "get_mouse_position() -> position, result",
            NodeKind.Keyboard => $"keyboard({Quote(node.Key ?? string.Empty)})",
            NodeKind.ScrollWheel => $"scroll({node.ScrollAction})",
            NodeKind.StartProgram => $"start_program({Quote(node.ProgramPath ?? string.Empty)})",
            NodeKind.KeyChord => $"key_chord({Quote(node.Text ?? string.Empty)})",
            NodeKind.SelectWindow => $"select_window({ResolveStringInputExpression(plan, node, "process_name", node.ProcessName ?? string.Empty, state)})",
            NodeKind.WaitWindow => $"wait_window({ResolveStringInputExpression(plan, node, "process_name", node.Text ?? string.Empty, state)})",
            NodeKind.CloseWindow => $"close_window({ResolveStringInputExpression(plan, node, "process_name", node.Text ?? string.Empty, state)})",
            NodeKind.WindowExists => $"window_exists({ResolveStringInputExpression(plan, node, "process_name", node.Text ?? string.Empty, state)})",
            NodeKind.GetForegroundWindow => "get_foreground_window()",
            NodeKind.FindImage => $"find_image(target: {ResolveStringInputExpression(plan, node, "image_path", node.ImagePath ?? string.Empty, state)}, source: {ResolveStringInputExpression(plan, node, "source_image_path", node.SourceImagePath ?? string.Empty, state)})",
            NodeKind.WaitImage => $"wait_image(target: {ResolveStringInputExpression(plan, node, "image_path", node.ImagePath ?? string.Empty, state)}, source: {ResolveStringInputExpression(plan, node, "source_image_path", node.SourceImagePath ?? string.Empty, state)})",
            NodeKind.WaitImageDisappear => $"wait_image_disappear(target: {ResolveStringInputExpression(plan, node, "image_path", node.ImagePath ?? string.Empty, state)}, source: {ResolveStringInputExpression(plan, node, "source_image_path", node.SourceImagePath ?? string.Empty, state)})",
            NodeKind.ShowMessage => $"show_message({ResolveStringInputExpression(plan, node, "text", node.Text ?? string.Empty, state)})",
            NodeKind.SaveScreenshot => $"save_screenshot({ResolveStringInputExpression(plan, node, "path", node.Text ?? string.Empty, state)})",
            NodeKind.Compare or NodeKind.BooleanAnd or NodeKind.BooleanOr or NodeKind.BooleanNot or NodeKind.StringConcat => ResolvePureNodeExpression(plan, node, state, 0, new HashSet<string>(StringComparer.Ordinal)),
            _ => FormatNode(node),
        };
    }

    private static string NormalizeCompareOp(string? op) => (op ?? "Equal").ToLowerInvariant() switch
    {
        "greaterthan" or ">" => ">",
        "lessthan" or "<" => "<",
        "greaterorequal" or ">=" => ">=",
        "lessorequal" or "<=" => "<=",
        "notequal" or "!=" => "!=",
        "contains" => "contains",
        _ => "==",
    };

    private static string FormatNode(GraphRuntimeNode node) =>
        string.IsNullOrWhiteSpace(node.NodeNumber) ? node.Title : $"{node.Title} {node.NodeNumber}";

    private static string FormatCall(GraphRuntimeNode node) =>
        string.IsNullOrWhiteSpace(node.NodeNumber) ? node.Title : $"{node.Title} {node.NodeNumber}";

    private static string ParameterLabel(GraphRuntimeParameter parameter) =>
        string.IsNullOrWhiteSpace(parameter.Name) ? parameter.Id : parameter.Name;

    private static string GetOutputLabel(GraphRuntimeNode node, string pinName)
    {
        GraphRuntimeParameter? parameter = node.Parameters.FirstOrDefault(parameter => parameter.Id == pinName);
        if (parameter is not null)
            return ParameterLabel(parameter);

        return pinName switch
        {
            "result" => "result",
            "position" => "position",
            "process_name" => "process_name",
            "window_title" => "window_title",
            "image_path" => "image_path",
            "center" => "center",
            "value" => "value",
            _ => pinName,
        };
    }

    private static string Quote(string value) => $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n")}\"";

    private static string FormatPoint(double x, double y) => $"({x:0.##}, {y:0.##})";

    private static string FormatVectorDefault(string value)
    {
        var parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 &&
            double.TryParse(parts[0], out double x) &&
            double.TryParse(parts[1], out double y))
        {
            return FormatPoint(x, y);
        }

        return string.IsNullOrWhiteSpace(value) ? "(0, 0)" : value;
    }

    private static PinKind ToPinKind(GraphParameterType type) => type switch
    {
        GraphParameterType.Boolean => PinKind.Boolean,
        GraphParameterType.Vector2D => PinKind.Vector2D,
        _ => PinKind.String,
    };

    private static GraphParameterType ToParameterType(PinKind kind) => kind switch
    {
        PinKind.Boolean => GraphParameterType.Boolean,
        PinKind.Vector2D => GraphParameterType.Vector2D,
        _ => GraphParameterType.String,
    };

    private static void AppendLine(StringBuilder builder, string line) => builder.AppendLine(line);

    private static void EmitLine(StringBuilder builder, GenerationState state, int depth, string line)
    {
        if (state.LineCount >= MaxLines)
            return;

        if (string.IsNullOrEmpty(line))
        {
            builder.AppendLine();
            state.LastLineWasBlank = true;
        }
        else
        {
            builder.Append(' ', Math.Max(0, depth) * 4);
            builder.AppendLine(line);
            state.LastLineWasBlank = false;
        }

        state.LineCount++;
    }

    private static void EmitBlankLine(StringBuilder builder, GenerationState state)
    {
        if (state.LastLineWasBlank)
            return;

        EmitLine(builder, state, 0, string.Empty);
    }

    private sealed class GenerationState(
        IReadOnlyDictionary<string, GraphExecutionPlan> functionPlans,
        IReadOnlyDictionary<string, string> functionNames)
    {
        private static readonly IReadOnlyDictionary<string, string> EmptyParameterBindings = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Stack<IReadOnlyDictionary<string, string>> _parameterBindings = [];

        public IReadOnlyDictionary<string, GraphExecutionPlan> FunctionPlans { get; } = functionPlans;
        public IReadOnlyDictionary<string, string> FunctionNames { get; } = functionNames;
        public HashSet<string> CallStack { get; } = new(StringComparer.Ordinal);
        public int LineCount { get; set; }
        public bool LastLineWasBlank { get; set; }

        public bool TryGetParameterBinding(string parameterId, out string expression)
        {
            expression = string.Empty;
            var bindings = _parameterBindings.Count == 0 ? EmptyParameterBindings : _parameterBindings.Peek();
            return bindings.TryGetValue(parameterId, out expression!);
        }

        public void PushParameterBindings(IReadOnlyDictionary<string, string> bindings) =>
            _parameterBindings.Push(bindings);

        public void PopParameterBindings()
        {
            if (_parameterBindings.Count > 0)
                _parameterBindings.Pop();
        }
    }
}

internal sealed record FinalCodePreviewResult(string Text, string? ErrorMessage);
