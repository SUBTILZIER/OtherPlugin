using System.Text;
using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Logging;
using AutomationStudioWpf.Nodes;

namespace AutomationStudioWpf.Runtime;

public sealed partial class GraphRuntimeExecutor
{
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
        builder.AppendLine($"执行结果：{status}");
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

    private static string FormatRuntimeOutputKey(GraphExecutionPlan plan, string key)
    {
        int separator = key.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0 || separator == key.Length - 1)
            return key;

        string nodeId = key[..separator];
        string pinName = key[(separator + 1)..];
        GraphRuntimeNode? node = plan.Index.GetNode(nodeId);
        return node is null ? key : $"{NodeLogLabel(node)}.{pinName}";
    }
}
