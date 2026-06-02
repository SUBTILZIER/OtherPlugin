using System.Drawing;
using System.IO;
using System.Text.Json;
using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Logging;
using AutomationStudioWpf.Runtime;

namespace AutomationStudioWpf.Nodes.Plugins.Ocr;

public sealed class FindTextNodeExecutor : INodeExecutor
{
    public NodeKind NodeKind => NodeKind.FindText;

    public NodeExecutionResult Execute(NodeExecutionRequest request)
    {
        GraphRuntimeNode node = request.Node;
        if (!request.Context.TryResolveStringInput(request.Plan, node, "text", out string searchText, out bool hasConnection))
        {
            if (hasConnection)
            {
                request.Context.Set(node.Id, "result", false);
                Logger.Warn("找字警告：文字输入已连接，但上游没有输出。继续执行。");
                return NodeExecutionResult.Warn($"找字未执行：{node.Title} 上游文字缺失");
            }

            searchText = node.ImagePath ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(searchText))
        {
            request.Context.Set(node.Id, "result", false);
            Logger.Warn("找字警告：文字为空。继续执行。");
            return NodeExecutionResult.Warn($"找字未执行：{node.Title} 未设置文字");
        }

        string scriptPath = Path.Combine(AppContext.BaseDirectory, "Python", "find_text.py");
        int threshold = node.SimilarityThresholdPercent > 0 ? node.SimilarityThresholdPercent : 80;
        Logger.Info($"找字开始：\"{searchText}\"，相似度阈值：{threshold}%");
        var result = request.Adapters.Python.RunJsonScript(
            scriptPath,
            new { search_text = searchText, threshold_percent = threshold },
            TimeSpan.FromSeconds(60),
            request.CancellationToken);

        if (!string.IsNullOrWhiteSpace(result.Stderr))
            Logger.Info($"找字 Python stderr:\n{result.Stderr}");
        if (!string.IsNullOrWhiteSpace(result.Stdout))
            Logger.Info($"找字 Python stdout: {result.Stdout}");

        if (!result.Success || string.IsNullOrWhiteSpace(result.Stdout))
        {
            request.Context.Set(node.Id, "result", false);
            Logger.Warn($"找字未命中或未执行：\"{searchText}\"（{result.Message}）。继续执行。");
            return NodeExecutionResult.Warn($"未找到文字：\"{searchText}\"，继续执行");
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(result.Stdout);
            JsonElement root = doc.RootElement;
            bool found = root.TryGetProperty("found", out JsonElement foundProp) && foundProp.GetBoolean();
            request.Context.Set(node.Id, "result", found);

            if (found)
            {
                int cx = root.TryGetProperty("centerX", out JsonElement cxProp) ? cxProp.GetInt32() : 0;
                int cy = root.TryGetProperty("centerY", out JsonElement cyProp) ? cyProp.GetInt32() : 0;
                double score = root.TryGetProperty("score", out JsonElement scoreProp) ? scoreProp.GetDouble() : 0;
                string matchedText = root.TryGetProperty("matchedText", out JsonElement textProp) ? textProp.GetString() ?? string.Empty : string.Empty;
                request.Context.Set(node.Id, "center", new Point(cx, cy));
                Logger.Info($"找字成功：\"{matchedText}\"，({cx},{cy})，置信度：{score:F2}");
                return NodeExecutionResult.Ok($"找字成功：\"{matchedText}\" -> ({cx},{cy})");
            }
        }
        catch (JsonException)
        {
            // Invalid plugin output is nonfatal for OCR so user automation can continue.
        }

        request.Context.Set(node.Id, "result", false);
        Logger.Warn($"找字未命中：\"{searchText}\"。继续执行。");
        return NodeExecutionResult.Warn($"未找到文字：\"{searchText}\"，继续执行");
    }
}
