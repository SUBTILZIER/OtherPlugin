using System.Text;
using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Runtime;

namespace AutomationStudioWpf.Services;

internal sealed partial class FinalCodePreviewGenerator
{
    private const int MaxDepth = 64;
    private const int MaxLines = 4000;

    public FinalCodePreviewResult Generate(
        GraphExecutionPlan plan,
        string assetName,
        GraphAssetKind graphKind,
        IReadOnlyDictionary<string, GraphExecutionPlan>? functionPlans = null,
        IReadOnlyDictionary<string, string>? functionNames = null,
        IReadOnlyDictionary<string, RuntimeCustomEventTarget>? customEvents = null)
    {
        var builder = new StringBuilder();
        var state = new GenerationState(
            functionPlans ?? new Dictionary<string, GraphExecutionPlan>(StringComparer.Ordinal),
            functionNames ?? new Dictionary<string, string>(StringComparer.Ordinal),
            customEvents ?? new Dictionary<string, RuntimeCustomEventTarget>(StringComparer.Ordinal));

        try
        {
            AppendLine(builder, $"# asset: {assetName}");
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
            NodeKind.MouseClick => FormatRepeat(node, $"mouse_{FormatMode(node.OperationMode)}({FormatMouseButton(node.MouseButton)}, position: {ResolveVectorInputExpression(plan, node, "position", node.PositionX, node.PositionY, state)})"),
            NodeKind.MouseMove => $"mouse_move({ResolveVectorInputExpression(plan, node, "position", node.PositionX, node.PositionY, state)})",
            NodeKind.GetMousePosition => "get_mouse_position() -> position, result",
            NodeKind.Keyboard => FormatRepeat(node, $"keyboard_{FormatMode(node.OperationMode)}({Quote(node.Key ?? string.Empty)})"),
            NodeKind.ScrollWheel => $"scroll({node.ScrollAction})",
            NodeKind.StartProgram => $"start_program({Quote(node.ProgramPath ?? string.Empty)})",
            NodeKind.KeyChord => FormatRepeat(node, $"key_chord_{FormatMode(node.OperationMode)}({Quote(node.Text ?? string.Empty)})"),
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

    private static string FormatRepeat(GraphRuntimeNode node, string expression)
    {
        int interval = Math.Max(1, node.TriggerIntervalMs);
        return node.TriggerCount switch
        {
            1 when interval == 1000 => expression,
            0 => $"repeat_forever(interval: {interval}ms) {{ {expression}; }}",
            _ => $"repeat({Math.Max(1, node.TriggerCount)}, interval: {interval}ms) {{ {expression}; }}",
        };
    }

    private static string FormatMode(PressReleaseMode mode) => mode switch
    {
        PressReleaseMode.Press => "press",
        PressReleaseMode.Release => "release",
        PressReleaseMode.Click => "click",
        _ => "click",
    };

    private static string FormatMouseButton(MouseButton button) => button switch
    {
        MouseButton.Left => "left",
        MouseButton.Right => "right",
        MouseButton.Middle => "middle",
        MouseButton.XButton1 => "xbutton1",
        MouseButton.XButton2 => "xbutton2",
        _ => "left",
    };

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
        IReadOnlyDictionary<string, string> functionNames,
        IReadOnlyDictionary<string, RuntimeCustomEventTarget> customEvents)
    {
        private static readonly IReadOnlyDictionary<string, string> EmptyParameterBindings = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Stack<IReadOnlyDictionary<string, string>> _parameterBindings = [];

        public IReadOnlyDictionary<string, GraphExecutionPlan> FunctionPlans { get; } = functionPlans;
        public IReadOnlyDictionary<string, string> FunctionNames { get; } = functionNames;
        public IReadOnlyDictionary<string, RuntimeCustomEventTarget> CustomEvents { get; } = customEvents;
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
