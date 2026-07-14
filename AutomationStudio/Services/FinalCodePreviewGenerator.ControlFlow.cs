using System.Text;
using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Runtime;

namespace AutomationStudioWpf.Services;

internal sealed partial class FinalCodePreviewGenerator
{
    private void EmitNextChain(GraphExecutionPlan plan, string scopeKey, GraphRuntimeNode sourceNode, string sourcePinName, StringBuilder builder, GenerationState state, int depth, HashSet<string> stack)
    {
        if (plan.Index.GetExecutionConnection(sourceNode.Id, sourcePinName) is null)
            return;
        EmitBlankLine(builder, state);
        EmitChain(plan, scopeKey, sourceNode, sourcePinName, builder, state, depth, stack);
    }

    private void EmitChain(GraphExecutionPlan plan, string scopeKey, GraphRuntimeNode sourceNode, string sourcePinName, StringBuilder builder, GenerationState state, int depth, HashSet<string> stack)
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

        try { EmitNode(plan, scopeKey, node, builder, state, depth, stack); }
        finally { stack.Remove(stackKey); }
    }

    private void EmitNode(GraphExecutionPlan plan, string scopeKey, GraphRuntimeNode node, StringBuilder builder, GenerationState state, int depth, HashSet<string> stack)
    {
        switch (node.NodeKind)
        {
            case NodeKind.Reroute: EmitChain(plan, scopeKey, node, "out", builder, state, depth, stack); return;
            case NodeKind.If: EmitIf(plan, scopeKey, node, builder, state, depth, stack); return;
            case NodeKind.ForLoop: EmitFor(plan, scopeKey, node, builder, state, depth, stack); return;
            case NodeKind.WhileLoop: EmitWhile(plan, scopeKey, node, builder, state, depth, stack); return;
            case NodeKind.ToDo: EmitToDo(plan, node, builder, state, depth); return;
            case NodeKind.MultiThread: EmitMultiThread(plan, scopeKey, node, builder, state, depth, stack); return;
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

    private void EmitFunctionCall(GraphExecutionPlan callerPlan, GraphRuntimeNode node, StringBuilder builder, GenerationState state, int depth, HashSet<string> stack)
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
            try { EmitChain(functionPlan, callStackKey, entry, "exec_out", builder, state, depth + 1, stack); }
            finally { state.PopParameterBindings(); }
            EmitLine(builder, state, depth, "}");
        }
        finally { state.CallStack.Remove(callStackKey); }
    }

    private void EmitCustomEventCall(GraphExecutionPlan plan, string scopeKey, GraphRuntimeNode node, StringBuilder builder, GenerationState state, int depth, HashSet<string> stack)
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
            try { EmitChain(plan, scopeKey, entry, "exec_out", builder, state, depth + 1, stack); }
            finally { state.PopParameterBindings(); }
            EmitLine(builder, state, depth, "}");
        }
        finally { state.CallStack.Remove(callStackKey); }
    }
}
