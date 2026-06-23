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
        string rawImagePath;
        if (request.Context.TryResolveStringInput(request.Plan, node, "image_path", out string resolvedImagePath, out bool hasImageConnection))
        {
            rawImagePath = resolvedImagePath;
        }
        else if (hasImageConnection)
        {
            request.Context.Set(node.Id, "result", false);
            Logger.Warn("找图警告：查找目标输入已连接，但上游没有输出。继续执行。");
            return NodeExecutionResult.Warn($"找图未执行：{node.Title} 上游查找目标为空");
        }
        else
        {
            rawImagePath = node.ImagePath ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(rawImagePath))
        {
            request.Context.Set(node.Id, "result", false);
            Logger.Warn("找图警告：查找目标为空。继续执行。");
            return NodeExecutionResult.Warn($"找图未执行：{node.Title} 未设置查找目标");
        }

        string imagePath = ResolvePath(rawImagePath, request.BaseDirectory);
        request.Context.Set(node.Id, "image_path", imagePath);
        if (!File.Exists(imagePath))
        {
            request.Context.Set(node.Id, "result", false);
            Logger.Warn($"找图警告：图片不存在：{imagePath}。继续执行。");
            return NodeExecutionResult.Warn($"找图未执行：{Path.GetFileName(imagePath)} 不存在");
        }

        string rawSourcePath;
        if (request.Context.TryResolveStringInput(request.Plan, node, "source_image_path", out string resolvedSourcePath, out bool hasSourceConnection))
        {
            rawSourcePath = resolvedSourcePath;
        }
        else if (node.ImageSearchSourceMode == ImageSearchSourceMode.ManualImage && hasSourceConnection)
        {
            request.Context.Set(node.Id, "result", false);
            Logger.Warn("找图警告：查找源输入已连接，但上游没有输出。继续执行。");
            return NodeExecutionResult.Warn($"找图未执行：{node.Title} 上游查找源为空");
        }
        else
        {
            rawSourcePath = node.SourceImagePath ?? string.Empty;
        }

        string sourcePath = node.ImageSearchSourceMode == ImageSearchSourceMode.ManualImage
            ? ResolvePath(rawSourcePath, request.BaseDirectory)
            : string.Empty;
        if (node.ImageSearchSourceMode == ImageSearchSourceMode.ManualImage &&
            (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)))
        {
            request.Context.Set(node.Id, "result", false);
            Logger.Warn($"找图警告：查找源不存在：{sourcePath}。继续执行。");
            return NodeExecutionResult.Warn($"找图未执行：查找源不存在");
        }

        string scriptPath = Path.Combine(AppContext.BaseDirectory, "Python", "find_image.py");
        int threshold = node.SimilarityThresholdPercent > 0 ? node.SimilarityThresholdPercent : 80;
        if (node.UseFindImageRegion && (node.FindImageRegionWidth <= 0 || node.FindImageRegionHeight <= 0))
        {
            request.Context.Set(node.Id, "result", false);
            Logger.Warn("找图警告：已启用区域，但区域宽高无效。继续执行。");
            return NodeExecutionResult.Warn($"找图未执行：{node.Title} 区域宽高无效");
        }

        string regionLabel = node.UseFindImageRegion
            ? $"区域：({node.FindImageRegionX:0},{node.FindImageRegionY:0},{node.FindImageRegionWidth:0},{node.FindImageRegionHeight:0})"
            : "区域：全屏";
        string sourceLabel = node.ImageSearchSourceMode == ImageSearchSourceMode.RealtimeScreenshot ? "实时截屏" : sourcePath;
        Logger.Info($"找图开始：源={sourceLabel}，目标={imagePath}，相似度阈值：{threshold}%，{regionLabel}");
        var result = request.Adapters.Python.RunJsonScript(
            scriptPath,
            new
            {
                template_path = imagePath,
                source_mode = node.ImageSearchSourceMode.ToString(),
                source_image_path = sourcePath,
                threshold_percent = threshold,
                use_region = node.UseFindImageRegion,
                region_x = node.FindImageRegionX,
                region_y = node.FindImageRegionY,
                region_width = node.FindImageRegionWidth,
                region_height = node.FindImageRegionHeight,
            },
            TimeSpan.FromSeconds(30),
            request.CancellationToken);

        if (!result.Success)
        {
            request.Context.Set(node.Id, "result", false);
            string detail = ExtractPythonError(result.Stdout);
            string message = string.IsNullOrWhiteSpace(detail)
                ? result.Message
                : detail;
            Logger.Error($"找图失败：{message}");
            return NodeExecutionResult.Fatal($"执行失败：{message}");
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

            Logger.Info($"找图未命中：{Path.GetFileName(imagePath)}。继续执行。");
            return NodeExecutionResult.Ok($"未找到图像：{Path.GetFileName(imagePath)}，继续执行");
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

    private static string ExtractPythonError(string stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout))
            return string.Empty;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(stdout);
            return doc.RootElement.TryGetProperty("error", out JsonElement error)
                ? error.GetString() ?? string.Empty
                : string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }
}
