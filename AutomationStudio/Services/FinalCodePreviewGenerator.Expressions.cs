using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Runtime;

namespace AutomationStudioWpf.Services;

internal sealed partial class FinalCodePreviewGenerator
{
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

    private string ResolveSourceExpression(GraphExecutionPlan plan, string sourceNodeId, string sourcePinName, PinKind sourcePinKind, GenerationState state, int depth, HashSet<string> pureStack)
    {
        if (depth > MaxDepth)
            return "# depth limit";
        GraphRuntimeNode? sourceNode = plan.Index.GetNode(sourceNodeId);
        if (sourceNode is null)
            return $"{sourceNodeId}.{sourcePinName}";
        if (sourceNode.NodeKind is NodeKind.FunctionEntry or NodeKind.CustomEvent && state.TryGetParameterBinding(sourcePinName, out string? binding))
            return binding;
        if (NodeTraits.IsPure(sourceNode.NodeKind))
        {
            if (!pureStack.Add(sourceNode.Id))
                return "# pure cycle";
            try { return ResolvePureNodeExpression(plan, sourceNode, state, depth + 1, pureStack); }
            finally { pureStack.Remove(sourceNode.Id); }
        }
        if (TryResolveKnownOutputExpression(plan, sourceNode, sourcePinName, sourcePinKind, state, pureStack, out string? known))
            return known!;
        return sourcePinKind == PinKind.Execution ? FormatNode(sourceNode) : $"{FormatNode(sourceNode)}.{GetOutputLabel(sourceNode, sourcePinName)}";
    }

    private bool TryResolveKnownOutputExpression(GraphExecutionPlan plan, GraphRuntimeNode sourceNode, string sourcePinName, PinKind sourcePinKind, GenerationState state, HashSet<string> pureStack, out string? expression)
    {
        expression = null;
        string stackKey = $"output:{sourceNode.Id}:{sourcePinName}";
        if (!pureStack.Add(stackKey))
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
                if (sourcePinName == "position" && sourcePinKind == PinKind.Vector2D) { expression = "get_mouse_position().position"; return true; }
                if (sourcePinName == "result" && sourcePinKind == PinKind.Boolean) { expression = "get_mouse_position().result"; return true; }
            }
            if (sourceNode.NodeKind == NodeKind.MouseMove)
            {
                string position = ResolveVectorInputExpression(plan, sourceNode, "position", sourceNode.PositionX, sourceNode.PositionY, state, pureStack);
                if (sourcePinName == "position" && sourcePinKind == PinKind.Vector2D) { expression = $"mouse_move({position}).position"; return true; }
                if (sourcePinName == "result" && sourcePinKind == PinKind.Boolean) { expression = $"mouse_move({position}).result"; return true; }
            }
            if (sourcePinKind == PinKind.String && sourcePinName == "process_name")
            {
                if (sourceNode.NodeKind == NodeKind.SelectWindow) { expression = ResolveStringInputExpression(plan, sourceNode, "process_name", sourceNode.ProcessName ?? string.Empty, state, pureStack); return true; }
                if (sourceNode.NodeKind is NodeKind.WaitWindow or NodeKind.CloseWindow or NodeKind.WindowExists) { expression = ResolveStringInputExpression(plan, sourceNode, "process_name", sourceNode.Text ?? string.Empty, state, pureStack); return true; }
            }
            if (sourcePinKind == PinKind.String && sourcePinName == "image_path")
            {
                if (sourceNode.NodeKind == NodeKind.WaitImage) { expression = ResolveStringInputExpression(plan, sourceNode, "image_path", sourceNode.ImagePath ?? string.Empty, state, pureStack); return true; }
                if (sourceNode.NodeKind == NodeKind.SaveScreenshot) { expression = ResolveStringInputExpression(plan, sourceNode, "path", sourceNode.Text ?? string.Empty, state, pureStack); return true; }
            }
            return false;
        }
        finally { pureStack.Remove(stackKey); }
    }

    private string ResolvePureNodeExpression(GraphExecutionPlan plan, GraphRuntimeNode node, GenerationState state, int depth, HashSet<string> pureStack)
    {
        if (depth > MaxDepth)
            return "# depth limit";
        return node.NodeKind switch
        {
            NodeKind.Compare => $"{ResolveStringInputExpression(plan, node, "left", node.Text ?? string.Empty, state, pureStack)} {NormalizeCompareOp(node.Text3)} {ResolveStringInputExpression(plan, node, "right", node.Text2 ?? string.Empty, state, pureStack)}",
            NodeKind.BooleanAnd => string.Join(" AND ", GetVariadicPinNames(node).Select(pin => ResolveValueExpression(plan, node, pin, GetVariadicBoolFallback(node, pin), state, pureStack))),
            NodeKind.BooleanOr => string.Join(" OR ", GetVariadicPinNames(node).Select(pin => ResolveValueExpression(plan, node, pin, GetVariadicBoolFallback(node, pin), state, pureStack))),
            NodeKind.BooleanNot => $"NOT {ResolveValueExpression(plan, node, "value", node.Flag ? "true" : "false", state, pureStack)}",
            NodeKind.StringConcat => string.Join(" + ", GetVariadicPinNames(node).Select(pin => ResolveStringInputExpression(plan, node, pin, GetVariadicFallback(node, pin), state, pureStack))),
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
        return pinName switch { "left" => node.Text ?? string.Empty, "right" => node.Text2 ?? string.Empty, _ => string.Empty };
    }

    private static string GetVariadicBoolFallback(GraphRuntimeNode node, string pinName) =>
        bool.TryParse(GetVariadicFallback(node, pinName), out bool parsed) && parsed ? "true" : "false";

    private static string ResolveToDoTarget(GraphExecutionPlan plan, GraphRuntimeNode node)
    {
        if (!string.IsNullOrWhiteSpace(node.TargetNodeTitle) && !string.IsNullOrWhiteSpace(node.TargetNodeNumber))
            return $"{node.TargetNodeTitle} {node.TargetNodeNumber}";
        if (!string.IsNullOrWhiteSpace(node.TargetNodeId) && plan.Index.GetNode(node.TargetNodeId) is { } target)
            return $"{target.Title} {target.NodeNumber}";
        return "<invalid_todo_target>";
    }

    private IReadOnlyDictionary<string, string> BuildParameterBindings(GraphExecutionPlan callerPlan, GraphRuntimeNode callNode, GraphRuntimeNode entryNode, GenerationState state)
    {
        var bindings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entryParameter in entryNode.Parameters)
        {
            GraphRuntimeParameter callParameter = callNode.Parameters.FirstOrDefault(parameter => parameter.Id == entryParameter.Id) ?? entryParameter;
            bindings[entryParameter.Id] = ResolveTypedInputExpression(callerPlan, callNode, entryParameter.Id, entryParameter.Type,
                string.IsNullOrWhiteSpace(callParameter.DefaultValue) ? entryParameter.DefaultValue : callParameter.DefaultValue,
                state, new HashSet<string>(StringComparer.Ordinal));
        }
        return bindings;
    }

    private string ResolveFunctionCallOutputExpression(GraphExecutionPlan callerPlan, GraphRuntimeNode callNode, string outputPinName, PinKind outputPinKind, GenerationState state, HashSet<string> pureStack)
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
            GraphRuntimeParameter parameter = returnNode.Parameters.FirstOrDefault(item => item.Id == outputPinName)
                ?? new GraphRuntimeParameter(outputPinName, outputPinName, ToParameterType(outputPinKind), string.Empty);
            state.PushParameterBindings(BuildParameterBindings(callerPlan, callNode, entry, state));
            try { return ResolveTypedInputExpression(functionPlan, returnNode, outputPinName, parameter.Type, parameter.DefaultValue, state, pureStack); }
            finally { state.PopParameterBindings(); }
        }
        finally { state.CallStack.Remove(stackKey); }
    }

    private string FormatParameterAssignments(GraphExecutionPlan plan, GraphRuntimeNode node, GenerationState state)
    {
        return string.Join(", ", node.Parameters.Select(parameter =>
            $"{ParameterLabel(parameter)}={ResolveParameterInputExpression(plan, node, parameter, state, new HashSet<string>(StringComparer.Ordinal))}"));
    }

    private string ResolveParameterInputExpression(GraphExecutionPlan plan, GraphRuntimeNode node, GraphRuntimeParameter parameter, GenerationState state, HashSet<string> pureStack) =>
        ResolveTypedInputExpression(plan, node, parameter.Id, parameter.Type, parameter.DefaultValue, state, pureStack);

    private string ResolveTypedInputExpression(GraphExecutionPlan plan, GraphRuntimeNode node, string pinName, GraphParameterType type, string fallback, GenerationState state, HashSet<string> pureStack)
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
}
