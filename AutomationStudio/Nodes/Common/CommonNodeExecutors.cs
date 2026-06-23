using System.Drawing;
using System.IO;
using System.Text.Json;
using AutomationStudioWpf.Adapters;
using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Logging;
using AutomationStudioWpf.Runtime;
using Point = System.Drawing.Point;

namespace AutomationStudioWpf.Nodes.Common;

public sealed class CommonNodeExecutor(NodeKind nodeKind) : INodeExecutor
{
    public NodeKind NodeKind { get; } = nodeKind;

    public NodeExecutionResult Execute(NodeExecutionRequest request)
    {
        return NodeKind switch
        {
            NodeKind.MouseDoubleClick => ExecuteMouseDoubleClick(request),
            NodeKind.GetMousePosition => ExecuteGetMousePosition(request),
            NodeKind.KeyChord => ExecuteKeyChord(request),
            NodeKind.WaitImage => ExecuteWaitImage(request, waitDisappear: false),
            NodeKind.WaitImageDisappear => ExecuteWaitImage(request, waitDisappear: true),
            NodeKind.Compare => ExecuteCompare(request),
            NodeKind.BooleanAnd => ExecuteBoolean(request, "and"),
            NodeKind.BooleanOr => ExecuteBoolean(request, "or"),
            NodeKind.BooleanNot => ExecuteBoolean(request, "not"),
            NodeKind.StringConcat => ExecuteStringConcat(request),
            NodeKind.WaitWindow => ExecuteWaitWindow(request),
            NodeKind.CloseWindow => ExecuteCloseWindow(request),
            NodeKind.WindowExists => ExecuteWindowExists(request),
            NodeKind.GetForegroundWindow => ExecuteGetForegroundWindow(request),
            NodeKind.SaveScreenshot => ExecuteSaveScreenshot(request),
            NodeKind.ShowMessage => ExecuteShowMessage(request),
            _ => NodeExecutionResult.Warn($"未实现节点：{request.Node.Title}"),
        };
    }

    private static NodeExecutionResult ExecuteMouseDoubleClick(NodeExecutionRequest request)
    {
        if (!ResolvePoint(request, "position", request.Node.Number, request.Node.Number2, out Point point, out string warn))
            return WarnResult(request, warn);

        request.Adapters.Mouse.MoveTo(point);
        request.Adapters.Mouse.DoubleClick(MouseButton.Left);
        request.Context.Set(request.Node.Id, "result", true);
        Logger.Info($"鼠标双击：({point.X},{point.Y})");
        return NodeExecutionResult.Ok("鼠标双击完成。");
    }

    private static NodeExecutionResult ExecuteGetMousePosition(NodeExecutionRequest request)
    {
        Point point = request.Adapters.Mouse.GetPosition();
        request.Context.Set(request.Node.Id, "position", point);
        request.Context.Set(request.Node.Id, "result", true);
        Logger.Info($"获取鼠标位置：({point.X},{point.Y})");
        return NodeExecutionResult.Ok("获取鼠标位置完成。");
    }

    private static NodeExecutionResult ExecuteKeyChord(NodeExecutionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Node.Text))
            return WarnResult(request, "组合键：未设置组合键。继续执行。");

        request.Adapters.Keyboard.ExecuteChord(request.Node.Text, Math.Max(0, (int)request.Node.Number), request.CancellationToken);
        request.Context.Set(request.Node.Id, "result", true);
        Logger.Info($"组合键完成：{request.Node.Text}");
        return NodeExecutionResult.Ok("组合键完成。");
    }

    private static NodeExecutionResult ExecuteWaitImage(NodeExecutionRequest request, bool waitDisappear)
    {
        string rawPath = ResolveString(request, "image_path", request.Node.Text, out bool missingInput);
        if (missingInput)
            return WarnResult(request, "图像节点：查找目标输入已连接，但上游没有输出。继续执行。");

        string imagePath = ResolvePath(rawPath, request.BaseDirectory);
        request.Context.Set(request.Node.Id, "image_path", imagePath);

        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            return WarnResult(request, $"图像节点：查找目标不存在：{imagePath}。继续执行。");

        ImageSearchSourceMode sourceMode = ParseImageSourceMode(request.Node.Text2);
        string rawSourcePath = ResolveString(request, "source_image_path", request.Node.Text3, out bool missingSourceInput);
        if (sourceMode == ImageSearchSourceMode.ManualImage && missingSourceInput)
            return WarnResult(request, "图像节点：查找源输入已连接，但上游没有输出。继续执行。");

        string sourcePath = sourceMode == ImageSearchSourceMode.ManualImage ? ResolvePath(rawSourcePath, request.BaseDirectory) : string.Empty;
        if (sourceMode == ImageSearchSourceMode.ManualImage && (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)))
            return WarnResult(request, $"图像节点：查找源不存在：{sourcePath}。继续执行。");

        int timeoutMs = request.Node.Number >= 0 ? (int)request.Node.Number : 5000;
        int intervalMs = request.Node.Number2 > 0 ? (int)request.Node.Number2 : 200;
        int threshold = request.Node.Number3 > 0 ? (int)request.Node.Number3 : 80;
        long start = Environment.TickCount64;
        double bestScore = 0;
        int attempt = 0;
        string sourceLabel = sourceMode == ImageSearchSourceMode.RealtimeScreenshot ? "实时截屏" : sourcePath;
        Logger.Info($"等待图片开始：源={sourceLabel}，目标={imagePath}，超时={(timeoutMs == 0 ? "不超时" : timeoutMs + "ms")}，间隔={intervalMs}ms，阈值={threshold}%");

        while (timeoutMs == 0 || Environment.TickCount64 - start <= timeoutMs)
        {
            request.CancellationToken.ThrowIfCancellationRequested();
            attempt++;
            var match = RunFindImage(request, imagePath, sourceMode, sourcePath, threshold);
            if (!match.ScriptSuccess)
            {
                request.Context.Set(request.Node.Id, "result", false);
                Logger.Error($"图像节点失败：{match.Message}");
                return NodeExecutionResult.Fatal(match.Message);
            }
            bestScore = Math.Max(bestScore, match.Score);

            long elapsedMs = Math.Max(0, Environment.TickCount64 - start);
            string checkState = waitDisappear
                ? (match.Found ? "图片仍存在" : "图片已消失")
                : (match.Found ? "图片已命中" : "图片未命中");
            Logger.Info($"{(waitDisappear ? "图片消失" : "等待图片")}检查 #{attempt}：{checkState}，相似度={match.Score:P1}，阈值={threshold}%，已等待={elapsedMs}ms");

            bool conditionMet = waitDisappear ? !match.Found : match.Found;
            if (conditionMet)
            {
                request.Context.Set(request.Node.Id, "result", true);
                if (match.Found)
                {
                    var center = new Point(match.CenterX, match.CenterY);
                    request.Context.Set(request.Node.Id, "center", center);
                    Logger.Info($"图片已命中：({match.CenterX},{match.CenterY})");
                }
                else
                {
                    Logger.Info("图片已消失。");
                }

                return NodeExecutionResult.Ok("图像节点完成。");
            }

            Thread.Sleep(intervalMs);
        }

        request.Context.Set(request.Node.Id, "result", false);
        string msg = waitDisappear ? "等待图片消失超时。" : "等待图片超时。";
        Logger.Warn($"{msg} 最高相似度：{bestScore:P1}，阈值：{threshold}%。继续执行。");
        return NodeExecutionResult.Warn(msg);
    }

    private static NodeExecutionResult ExecuteCompare(NodeExecutionRequest request)
    {
        string left = ResolveString(request, "left", request.Node.Text, out bool leftMissing);
        string right = ResolveString(request, "right", request.Node.Text2, out bool rightMissing);
        if (leftMissing || rightMissing)
            return WarnResult(request, "比较：前置输入已连接，但上游没有输出。继续执行。");

        string op = string.IsNullOrWhiteSpace(request.Node.Text3) ? "Equal" : request.Node.Text3;
        bool result = Compare(left, right, op);
        request.Context.Set(request.Node.Id, "result", result);
        Logger.Info($"比较：{left} {op} {right} = {result}");
        return NodeExecutionResult.Ok("比较完成。");
    }

    private static NodeExecutionResult ExecuteBoolean(NodeExecutionRequest request, string mode)
    {
        if (!ResolveBool(request, "left", request.Node.Flag, out bool left, out string leftWarn))
            return WarnResult(request, leftWarn);
        if (!ResolveBool(request, "right", ParseBool(request.Node.Text), out bool right, out string rightWarn))
            return WarnResult(request, rightWarn);
        if (!ResolveBool(request, "value", request.Node.Flag, out bool value, out string valueWarn))
            return WarnResult(request, valueWarn);

        bool result = mode switch
        {
            "and" => left && right,
            "or" => left || right,
            "not" => !value,
            _ => false,
        };
        request.Context.Set(request.Node.Id, "result", result);
        Logger.Info($"布尔运算：{mode} = {result}");
        return NodeExecutionResult.Ok("布尔运算完成。");
    }

    private static NodeExecutionResult ExecuteStringConcat(NodeExecutionRequest request)
    {
        string left = ResolveString(request, "left", request.Node.Text, out bool leftMissing);
        string right = ResolveString(request, "right", request.Node.Text2, out bool rightMissing);
        if (leftMissing || rightMissing)
            return WarnResult(request, "字符串拼接：前置输入已连接，但上游没有输出。继续执行。");

        string value = left + right;
        request.Context.Set(request.Node.Id, "value", value);
        Logger.Info($"字符串拼接：{value}");
        return NodeExecutionResult.Ok("字符串拼接完成。");
    }

    private static NodeExecutionResult ExecuteWaitWindow(NodeExecutionRequest request)
    {
        string processName = ResolveString(request, "process_name", request.Node.Text, out bool missing);
        if (missing)
            return WarnResult(request, "等待窗口：进程名输入已连接，但上游没有输出。继续执行。");

        var result = request.Adapters.Window.WaitWindowByProcessName(processName, (int)request.Node.Number, (int)request.Node.Number2, request.CancellationToken);
        SetWindowResult(request, result);
        return result.Success ? NodeExecutionResult.Ok(result.Message) : NodeExecutionResult.Warn(result.Message);
    }

    private static NodeExecutionResult ExecuteCloseWindow(NodeExecutionRequest request)
    {
        string processName = ResolveString(request, "process_name", request.Node.Text, out bool missing);
        if (missing)
            return WarnResult(request, "关闭窗口：进程名输入已连接，但上游没有输出。继续执行。");

        var result = request.Adapters.Window.CloseWindowByProcessName(processName);
        SetWindowResult(request, result);
        return result.Success ? NodeExecutionResult.Ok(result.Message) : NodeExecutionResult.Warn(result.Message);
    }

    private static NodeExecutionResult ExecuteWindowExists(NodeExecutionRequest request)
    {
        string processName = ResolveString(request, "process_name", request.Node.Text, out bool missing);
        if (missing)
            return WarnResult(request, "窗口是否存在：进程名输入已连接，但上游没有输出。继续执行。");

        var result = request.Adapters.Window.WindowExists(processName);
        request.Context.Set(request.Node.Id, "result", result.Success);
        request.Context.Set(request.Node.Id, "process_name", result.ProcessName);
        Logger.Info(result.Message);
        return NodeExecutionResult.Ok(result.Message);
    }

    private static NodeExecutionResult ExecuteGetForegroundWindow(NodeExecutionRequest request)
    {
        var result = request.Adapters.Window.GetForegroundWindowInfo();
        request.Context.Set(request.Node.Id, "result", result.Success);
        request.Context.Set(request.Node.Id, "process_name", result.ProcessName);
        request.Context.Set(request.Node.Id, "window_title", result.WindowTitle);
        Logger.Info(result.Message);
        return result.Success ? NodeExecutionResult.Ok(result.Message) : NodeExecutionResult.Warn(result.Message);
    }

    private static NodeExecutionResult ExecuteSaveScreenshot(NodeExecutionRequest request)
    {
        string saveMode = string.IsNullOrWhiteSpace(request.Node.Text2) ? "Auto" : request.Node.Text2;
        bool manual = string.Equals(saveMode, "Manual", StringComparison.OrdinalIgnoreCase);
        bool missing = false;
        string path = manual
            ? ResolveString(request, "path", request.Node.Text, out missing)
            : CreateAutoScreenshotPath(request);
        if (manual && missing)
            return WarnResult(request, "截图：保存路径输入已连接，但上游没有输出。继续执行。");

        var result = request.Adapters.Screenshot.SaveScreenshot(
            path,
            (int)request.Node.Number,
            (int)request.Node.Number2,
            (int)request.Node.Number3,
            (int)request.Node.Number4);
        request.Context.Set(request.Node.Id, "image_path", result.Path);
        if (result.Success)
        {
            Logger.Info($"{result.Message} 保存模式：{(manual ? "手动配置" : "自动保存")}");
            return NodeExecutionResult.Ok(result.Message);
        }

        Logger.Warn($"{result.Message} 继续执行。");
        return NodeExecutionResult.Warn(result.Message);
    }

    private static string CreateAutoScreenshotPath(NodeExecutionRequest request)
    {
        string safeNodeId = string.Concat(request.Node.Id.Select(ch => char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_'));
        string fileName = $"screenshot_{safeNodeId}_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png";
        return Path.Combine(AppContext.BaseDirectory, "Temp", "Screenshots", fileName);
    }

    private static NodeExecutionResult ExecuteShowMessage(NodeExecutionRequest request)
    {
        string message = ResolveString(request, "text", request.Node.Text, out bool missing);
        if (missing)
            return WarnResult(request, "弹窗提示：文本输入已连接，但上游没有输出。继续执行。");

        string title = string.IsNullOrWhiteSpace(request.Node.Text2) ? "自动化提示" : request.Node.Text2;
        global::System.Windows.Application.Current.Dispatcher.Invoke(() => global::System.Windows.MessageBox.Show(message, title));
        request.Context.Set(request.Node.Id, "result", true);
        return NodeExecutionResult.Ok("弹窗提示完成。");
    }

    private static void SetWindowResult(NodeExecutionRequest request, WindowSelectionResult result)
    {
        request.Context.Set(request.Node.Id, "result", result.Success);
        request.Context.Set(request.Node.Id, "process_name", result.ProcessName);
        if (result.Success)
            Logger.Info(result.Message);
        else
            Logger.Warn($"{result.Message} 继续执行。");
    }

    private static bool ResolvePoint(NodeExecutionRequest request, string pinName, double x, double y, out Point point, out string warn)
    {
        if (request.Context.TryResolvePointInput(request.Plan, request.Node, pinName, out point, out bool hasConnection))
        {
            warn = string.Empty;
            return true;
        }

        if (hasConnection)
        {
            warn = $"{request.Node.Title}：坐标输入已连接，但上游没有输出。继续执行。";
            return false;
        }

        if (x == 0 && y == 0)
        {
            warn = $"{request.Node.Title}：坐标无效。继续执行。";
            return false;
        }

        point = new Point((int)x, (int)y);
        warn = string.Empty;
        return true;
    }

    private static bool ResolveBool(NodeExecutionRequest request, string pinName, bool fallback, out bool value, out string warn)
    {
        if (request.Context.TryResolveBoolInput(request.Plan, request.Node, pinName, out value, out bool hasConnection))
        {
            warn = string.Empty;
            return true;
        }

        if (hasConnection)
        {
            warn = $"{request.Node.Title}：布尔输入已连接，但上游没有输出。继续执行。";
            return false;
        }

        value = fallback;
        warn = string.Empty;
        return true;
    }

    private static NodeExecutionResult WarnResult(NodeExecutionRequest request, string message)
    {
        request.Context.Set(request.Node.Id, "result", false);
        Logger.Warn(message);
        return NodeExecutionResult.Warn(message);
    }

    private static string ResolveString(NodeExecutionRequest request, string pinName, string? fallback, out bool missingConnectedInput)
    {
        if (request.Context.TryResolveStringInput(request.Plan, request.Node, pinName, out string value, out bool hasConnection))
        {
            missingConnectedInput = false;
            return value;
        }

        missingConnectedInput = hasConnection;
        return hasConnection ? string.Empty : fallback ?? string.Empty;
    }

    private static bool Compare(string left, string right, string op)
    {
        if (double.TryParse(left, out double leftNumber) && double.TryParse(right, out double rightNumber))
        {
            return op.ToLowerInvariant() switch
            {
                "greaterthan" or ">" => leftNumber > rightNumber,
                "lessthan" or "<" => leftNumber < rightNumber,
                "greaterorequal" or ">=" => leftNumber >= rightNumber,
                "lessorequal" or "<=" => leftNumber <= rightNumber,
                "notequal" or "!=" => Math.Abs(leftNumber - rightNumber) > double.Epsilon,
                _ => Math.Abs(leftNumber - rightNumber) <= double.Epsilon,
            };
        }

        return op.ToLowerInvariant() switch
        {
            "contains" => left.Contains(right, StringComparison.OrdinalIgnoreCase),
            "notequal" or "!=" => !string.Equals(left, right, StringComparison.OrdinalIgnoreCase),
            _ => string.Equals(left, right, StringComparison.OrdinalIgnoreCase),
        };
    }

    private static bool ParseBool(string? value) =>
        bool.TryParse(value, out bool result) && result;

    private static string ResolvePath(string? path, string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;
        return Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(baseDirectory, path));
    }

    private static ImageSearchSourceMode ParseImageSourceMode(string? value) =>
        Enum.TryParse(value, true, out ImageSearchSourceMode mode)
            ? mode
            : ImageSearchSourceMode.RealtimeScreenshot;

    private static ImageMatchResult RunFindImage(
        NodeExecutionRequest request,
        string imagePath,
        ImageSearchSourceMode sourceMode,
        string sourcePath,
        int threshold)
    {
        string scriptPath = Path.Combine(AppContext.BaseDirectory, "Python", "find_image.py");
        var result = request.Adapters.Python.RunJsonScript(
            scriptPath,
            new
            {
                template_path = imagePath,
                source_mode = sourceMode.ToString(),
                source_image_path = sourcePath,
                threshold_percent = threshold,
            },
            TimeSpan.FromSeconds(30),
            request.CancellationToken);

        if (!result.Success)
            return new ImageMatchResult(false, false, 0, 0, 0, result.Message);

        using JsonDocument doc = JsonDocument.Parse(result.Stdout);
        JsonElement root = doc.RootElement;
        bool found = root.TryGetProperty("found", out JsonElement foundProp) && foundProp.GetBoolean();
        int centerX = found ? root.GetProperty("centerX").GetInt32() : 0;
        int centerY = found ? root.GetProperty("centerY").GetInt32() : 0;
        double score = root.TryGetProperty("score", out JsonElement scoreProp) ? scoreProp.GetDouble() : 0;
        return new ImageMatchResult(true, found, centerX, centerY, score, string.Empty);
    }

    private sealed record ImageMatchResult(bool ScriptSuccess, bool Found, int CenterX, int CenterY, double Score, string Message);
}
