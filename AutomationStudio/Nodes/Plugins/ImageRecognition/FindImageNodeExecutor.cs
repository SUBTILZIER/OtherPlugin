using System.Drawing;
using System.IO;
using System.Text.Json;
using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Logging;
using AutomationStudioWpf.Runtime;

namespace AutomationStudioWpf.Nodes.Plugins.ImageRecognition;

public sealed class FindImageNodeExecutor : INodeExecutor
{
    public NodeKind NodeKind => NodeKind.FindImage;

    public NodeExecutionResult Execute(NodeExecutionRequest request)
    {
        GraphRuntimeNode node = request.Node;
        if (string.IsNullOrWhiteSpace(node.ImagePath))
        {
            request.Context.Set(node.Id, "result", false);
            Logger.Warn("找图警告：图片路径为空。继续执行。");
            return NodeExecutionResult.Warn($"找图未执行：{node.Title} 未设置图片路径");
        }

        string imagePath = ResolvePath(node.ImagePath, request.BaseDirectory);
        if (!File.Exists(imagePath))
        {
            request.Context.Set(node.Id, "result", false);
            Logger.Warn($"找图警告：图片不存在：{imagePath}。继续执行。");
            return NodeExecutionResult.Warn($"找图未执行：{Path.GetFileName(imagePath)} 不存在");
        }

        string scriptPath = Path.Combine(AppContext.BaseDirectory, "Python", "find_image.py");
        int threshold = node.SimilarityThresholdPercent > 0 ? node.SimilarityThresholdPercent : 80;
        Logger.Info($"找图开始：{imagePath}，相似度阈值：{threshold}%");
        var result = request.Adapters.Python.RunJsonScript(
            scriptPath,
            new { template_path = imagePath, threshold_percent = threshold },
            TimeSpan.FromSeconds(30),
            request.CancellationToken);

        if (!string.IsNullOrWhiteSpace(result.Stderr))
            Logger.Warn($"找图 Python stderr: {result.Stderr}");
        if (!string.IsNullOrWhiteSpace(result.Stdout))
            Logger.Info($"找图 Python stdout: {result.Stdout}");

        if (!result.Success)
        {
            request.Context.Set(node.Id, "result", false);
            Logger.Error($"找图失败：{result.Message}");
            return NodeExecutionResult.Fatal($"执行失败：{result.Message}");
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(result.Stdout);
            JsonElement root = doc.RootElement;
            bool found = root.TryGetProperty("found", out JsonElement foundProp) && foundProp.GetBoolean();
            request.Context.Set(node.Id, "result", found);

            if (found)
            {
                int cx = root.GetProperty("centerX").GetInt32();
                int cy = root.GetProperty("centerY").GetInt32();
                request.Context.Set(node.Id, "center", new Point(cx, cy));
                Logger.Info($"找图成功：({cx},{cy})");
                return NodeExecutionResult.Ok($"找图成功：{node.Title} -> ({cx},{cy})");
            }

            Logger.Warn($"找图未命中：{Path.GetFileName(imagePath)}。继续执行。");
            return NodeExecutionResult.Warn($"未找到图像：{Path.GetFileName(imagePath)}，继续执行");
        }
        catch (JsonException ex)
        {
            request.Context.Set(node.Id, "result", false);
            Logger.Error($"找图失败：Python 输出 JSON 无效：{ex.Message}");
            return NodeExecutionResult.Fatal($"执行失败：找图输出 JSON 无效：{ex.Message}");
        }
    }

    private static string ResolvePath(string? path, string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        return Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(baseDirectory, path));
    }
}

