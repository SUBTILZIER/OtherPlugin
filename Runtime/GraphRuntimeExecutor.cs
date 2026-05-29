using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;
using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Logging;

namespace AutomationStudioWpf.Runtime;

public sealed class GraphRuntimeExecutor
{
    private readonly HashSet<byte> _pressedKeys = [];

    public GraphExecutionResult Execute(GraphExecutionPlan plan, string baseDirectory, CancellationToken ct = default)
    {
        Logger.Info("--------开始执行--------");

        GraphRuntimeNode? startNode = plan.Nodes.FirstOrDefault(node => node.NodeKind == NodeKind.Start);
        if (startNode is null)
        {
            Logger.Error("执行失败：图中没有开始节点。");
            return new GraphExecutionResult(false, "执行失败：图中没有开始节点。", false);
        }

        Dictionary<string, object> context = [];
        GraphExecutionResult result = ExecuteChain(plan, startNode.Id, "exec_out", context, baseDirectory, [], ct);

        Logger.Info("--------执行结束--------");
        return result;
    }

    private GraphExecutionResult ExecuteChain(
        GraphExecutionPlan plan,
        string startNodeId,
        string startPinName,
        Dictionary<string, object> context,
        string baseDirectory,
        HashSet<string> visitedNodes,
        CancellationToken ct)
    {
        GraphRuntimeNode? currentNode = GetNextExecutionNode(plan, startNodeId, startPinName);

        while (currentNode is not null)
        {
            ct.ThrowIfCancellationRequested();

            if (!visitedNodes.Add(currentNode.Id))
            {
                string message = $"执行失败：检测到执行环路，节点 {currentNode.Title} 被重复访问。";
                Logger.Error(message);
                return new GraphExecutionResult(false, message, false);
            }

            var (result, nextPinName) = ExecuteNode(plan, currentNode, context, baseDirectory, ct);
            if (!result.ContinueExecution)
            {
                return result;
            }

            if (nextPinName is null)
            {
                break;
            }

            currentNode = GetNextExecutionNode(plan, currentNode.Id, nextPinName);
        }

        return new GraphExecutionResult(true, "执行完成。");
    }

    private (GraphExecutionResult Result, string? NextPinName) ExecuteNode(
        GraphExecutionPlan plan,
        GraphRuntimeNode node,
        Dictionary<string, object> context,
        string baseDirectory,
        CancellationToken ct)
    {
        return node.NodeKind switch
        {
            NodeKind.FindImage => (ExecuteFindImageNode(node, context, baseDirectory, ct), "exec_out"),
            NodeKind.MouseClick => (ExecuteMouseClickNode(plan, node, context), "exec_out"),
            NodeKind.Delay => (ExecuteDelayNode(node), "exec_out"),
            NodeKind.MouseMove => (ExecuteMouseMoveNode(plan, node, context), "exec_out"),
            NodeKind.Keyboard => (ExecuteKeyboardNode(node, context), "exec_out"),
            NodeKind.ScrollWheel => (ExecuteScrollWheelNode(node, context, ct), "exec_out"),
            NodeKind.Reroute => (new GraphExecutionResult(true, string.Empty), node.RoutedKind == PinKind.Execution ? "out" : null),
            NodeKind.If => ExecuteIfNode(plan, node, context),
            NodeKind.ForLoop => ExecuteForLoopNode(plan, node, context, baseDirectory, ct),
            NodeKind.WhileLoop => ExecuteWhileLoopNode(plan, node, context, baseDirectory, ct),
            NodeKind.StartProgram => (ExecuteStartProgramNode(node, context, ct), "exec_out"),
            NodeKind.PrintLog => (ExecutePrintLogNode(plan, node, context), "exec_out"),
            NodeKind.SelectWindow => (ExecuteSelectWindowNode(plan, node, context), "exec_out"),
            NodeKind.FindText => (ExecuteFindTextNode(plan, node, context, baseDirectory, ct), "exec_out"),
            _ => (new GraphExecutionResult(true, $"已跳过节点：{node.Title}"), null),
        };
    }

    private GraphExecutionResult ExecuteFindImageNode(
        GraphRuntimeNode node,
        Dictionary<string, object> context,
        string baseDirectory,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(node.ImagePath))
        {
            context[$"{node.Id}:result"] = false;
            Logger.Warn("找图警告：图片路径为空。继续执行。");
            return new GraphExecutionResult(true, $"找图未执行：{node.Title} 未设置图片路径");
        }

        string imagePath = ResolvePath(node.ImagePath, baseDirectory);
        if (!File.Exists(imagePath))
        {
            context[$"{node.Id}:result"] = false;
            Logger.Warn($"找图警告：图片不存在：{imagePath}。继续执行。");
            return new GraphExecutionResult(true, $"找图未执行：{Path.GetFileName(imagePath)} 不存在");
        }

        string scriptPath = Path.Combine(AppContext.BaseDirectory, "Python", "find_image.py");
        if (!File.Exists(scriptPath))
        {
            context[$"{node.Id}:result"] = false;
            Logger.Error($"找图失败：Python 脚本不存在：{scriptPath}");
            return new GraphExecutionResult(false, $"执行失败：Python 脚本不存在：{scriptPath}", false);
        }

        try
        {
            Logger.Info($"找图开始：{imagePath}，相似度阈值：{node.SimilarityThresholdPercent}%");

            string pythonExe = ResolvePythonPath();
            using Process process = new()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = pythonExe,
                    Arguments = $"\"{scriptPath}\" \"{imagePath}\" {node.SimilarityThresholdPercent}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
            };

            process.Start();

            Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
            Task<string> errorTask = process.StandardError.ReadToEndAsync();
            DateTime deadline = DateTime.UtcNow.AddSeconds(30);

            while (!process.WaitForExit(100))
            {
                ct.ThrowIfCancellationRequested();

                if (DateTime.UtcNow >= deadline)
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                    context[$"{node.Id}:result"] = false;
                    Logger.Error("找图失败：Python 脚本执行超时。");
                    return new GraphExecutionResult(false, "执行失败：找图脚本超时。", false);
                }
            }

            string output = outputTask.GetAwaiter().GetResult();
            string stderr = errorTask.GetAwaiter().GetResult();

            if (process.ExitCode != 0)
            {
                Logger.Error($"Python 退出码：{process.ExitCode}");
            }

            if (!string.IsNullOrWhiteSpace(stderr))
            {
                if (process.ExitCode == 0)
                    Logger.Warn($"Python stderr: {stderr}");
                else
                    Logger.Error($"Python stderr: {stderr}");
            }

            if (process.ExitCode != 0)
            {
                context[$"{node.Id}:result"] = false;
                return new GraphExecutionResult(false, $"执行失败：找图脚本退出码 {process.ExitCode}", false);
            }

            if (!string.IsNullOrWhiteSpace(output))
                Logger.Info($"Python stdout: {output}");
            else
                Logger.Warn("Python stdout 为空");

            using JsonDocument doc = JsonDocument.Parse(output);
            JsonElement root = doc.RootElement;

            bool found = root.GetProperty("found").GetBoolean();
            context[$"{node.Id}:result"] = found;

            if (found)
            {
                int cx = root.GetProperty("centerX").GetInt32();
                int cy = root.GetProperty("centerY").GetInt32();
                context[$"{node.Id}:center"] = new Point(cx, cy);
                Logger.Info($"找图成功：({cx},{cy})");
                return new GraphExecutionResult(true, $"找图成功：{node.Title} -> ({cx},{cy})");
            }

            Logger.Warn($"找图未命中：{Path.GetFileName(imagePath)}。继续执行。");
            return new GraphExecutionResult(true, $"未找到图像：{Path.GetFileName(imagePath)}，继续执行");
        }
        catch (OperationCanceledException)
        {
            Logger.Warn("找图执行已取消。");
            throw;
        }
        catch (Exception ex) when (ex.Message.Contains("系统找不到指定的文件"))
        {
            context[$"{node.Id}:result"] = false;
            Logger.Error("找图失败：未找到 Python 环境。请重启程序以自动安装。");
            return new GraphExecutionResult(false, "执行失败：未找到 Python 环境，请重启程序", false);
        }
        catch (Exception ex)
        {
            context[$"{node.Id}:result"] = false;
            Logger.Error($"找图失败：{ex.Message}");
            return new GraphExecutionResult(false, $"执行失败：Python 脚本错误：{ex.Message}", false);
        }
    }

    private GraphExecutionResult ExecuteMouseClickNode(GraphExecutionPlan plan, GraphRuntimeNode node, Dictionary<string, object> context)
    {
        bool hasPositionInput = plan.Connections.Any(c => c.TargetNodeId == node.Id && c.TargetPinName == "position");
        if (!hasPositionInput && !HasUsablePosition(node.PositionX, node.PositionY))
        {
            context[$"{node.Id}:result"] = false;
            Logger.Warn("鼠标点击：未设置点击位置，也未连接位置输入。继续执行。");
            return new GraphExecutionResult(true, $"已跳过节点：{node.Title} 缺少点击位置");
        }

        try
        {
            Point targetPoint = ResolveMouseTargetPoint(plan, node, context);
            string buttonLabel = node.MouseButton switch
            {
                MouseButton.Left => "左键",
                MouseButton.Right => "右键",
                MouseButton.XButton1 => "侧键1",
                MouseButton.XButton2 => "侧键2",
                _ => "左键",
            };
            string modeLabel = node.OperationMode switch
            {
                PressReleaseMode.Press => "按下",
                PressReleaseMode.Release => "抬起",
                PressReleaseMode.Click => "点击",
                _ => "点击",
            };

            Logger.Info($"鼠标点击：{buttonLabel} {modeLabel} ({targetPoint.X},{targetPoint.Y})");

            SetCursorPos(targetPoint.X, targetPoint.Y);
            (uint downFlag, uint upFlag, uint xButtonData) = GetMouseEventFlags(node.MouseButton);

            switch (node.OperationMode)
            {
                case PressReleaseMode.Click:
                    mouse_event(downFlag, 0, 0, xButtonData, UIntPtr.Zero);
                    Thread.Sleep(50);
                    mouse_event(upFlag, 0, 0, xButtonData, UIntPtr.Zero);
                    break;
                case PressReleaseMode.Press:
                    mouse_event(downFlag, 0, 0, xButtonData, UIntPtr.Zero);
                    break;
                case PressReleaseMode.Release:
                    mouse_event(upFlag, 0, 0, xButtonData, UIntPtr.Zero);
                    break;
            }

            context[$"{node.Id}:result"] = true;
            return new GraphExecutionResult(true, $"鼠标{buttonLabel}{modeLabel}：({targetPoint.X},{targetPoint.Y})");
        }
        catch (Exception ex)
        {
            context[$"{node.Id}:result"] = false;
            Logger.Error($"鼠标点击失败：{ex.Message}");
            return new GraphExecutionResult(false, $"执行失败：鼠标点击错误：{ex.Message}", false);
        }
    }

    private GraphExecutionResult ExecuteKeyboardNode(GraphRuntimeNode node, Dictionary<string, object> context)
    {
        if (string.IsNullOrWhiteSpace(node.Key))
        {
            Logger.Warn("键盘节点：未设置按键，将使用默认值 A。");
        }

        string modeLabel = node.OperationMode == PressReleaseMode.Press ? "按下" : "抬起";
        Logger.Info($"键盘：{node.Key} {modeLabel}");

        byte vkCode = MapKeyToVirtualKeyCode(node.Key ?? "A");

        if (node.OperationMode == PressReleaseMode.Press)
        {
            keybd_event(vkCode, 0, 0, UIntPtr.Zero);
            lock (_pressedKeys) { _pressedKeys.Add(vkCode); }
        }
        else
        {
            keybd_event(vkCode, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            lock (_pressedKeys) { _pressedKeys.Remove(vkCode); }
        }

        context[$"{node.Id}:result"] = true;
        Logger.Info($"键盘完成：{node.Key} {modeLabel}");
        return new GraphExecutionResult(true, $"键盘{node.Key}{modeLabel}");
    }

    private GraphExecutionResult ExecuteStartProgramNode(GraphRuntimeNode node, Dictionary<string, object> context, CancellationToken ct)
    {
        string programPath = node.ImagePath ?? string.Empty;
        if (string.IsNullOrWhiteSpace(programPath))
        {
            Logger.Warn("启动程序：程序路径为空。");
            context[$"{node.Id}:result"] = false;
            context[$"{node.Id}:process_name"] = string.Empty;
            return new GraphExecutionResult(false, "执行失败：程序路径为空。");
        }

        if (!File.Exists(programPath))
        {
            Logger.Warn($"启动程序：文件不存在：{programPath}");
            context[$"{node.Id}:result"] = false;
            context[$"{node.Id}:process_name"] = string.Empty;
            return new GraphExecutionResult(false, $"执行失败：文件不存在：{programPath}");
        }

        int waitTimeoutMs = node.DelayMs > 0 ? node.DelayMs : 60000;
        ProgramStartFailureAction failureAction = node.FailureAction;
        int retryCount = node.RetryCount > 0 ? node.RetryCount : 3;
        string processName = Path.GetFileNameWithoutExtension(programPath);

        Logger.Info($"启动程序：{programPath}，超时：{waitTimeoutMs}ms，失败操作：{failureAction}，重试次数：{retryCount}");

        int attempts = 1 + (failureAction == ProgramStartFailureAction.Retry ? retryCount : 0);
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            if (attempt > 0)
                Logger.Info($"启动程序：第 {attempt} 次重试...");

            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(programPath)
                {
                    UseShellExecute = true,
                    CreateNoWindow = false,
                };
                System.Diagnostics.Process? proc = System.Diagnostics.Process.Start(psi);

                // Wait and check if process started successfully
                bool started = false;
                int checkInterval = 10000; // 10 seconds
                int waited = 0;
                while (waited < waitTimeoutMs)
                {
                    ct.ThrowIfCancellationRequested();
                    Thread.Sleep(Math.Min(checkInterval, waitTimeoutMs - waited));
                    waited += checkInterval;

                    try
                    {
                        if (proc is not null)
                        {
                            proc.Refresh();
                            if (!proc.HasExited)
                            {
                                started = true;
                                break;
                            }
                        }
                    }
                    catch
                    {
                        // Process may have exited; try again next cycle
                    }
                }

                if (started)
                {
                    Logger.Info($"启动程序成功：{processName}，耗时约 {waited}ms");
                    context[$"{node.Id}:result"] = true;
                    context[$"{node.Id}:process_name"] = processName;
                    return new GraphExecutionResult(true, $"程序已启动：{processName}");
                }

                // If we get here, the process didn't start or exited immediately
                if (attempt < attempts - 1)
                {
                    Logger.Warn($"启动程序超时（{waitTimeoutMs}ms），准备重试...");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"启动程序异常：{ex.Message}");
                if (attempt >= attempts - 1)
                {
                    context[$"{node.Id}:result"] = false;
                    context[$"{node.Id}:process_name"] = processName;
                    return new GraphExecutionResult(false, $"启动程序失败：{ex.Message}");
                }
                Logger.Warn($"启动程序异常，准备重试...");
            }
        }

        Logger.Warn($"程序启动失败：{processName}，已达最大重试次数");
        context[$"{node.Id}:result"] = false;
        context[$"{node.Id}:process_name"] = processName;
        return new GraphExecutionResult(false, $"执行失败：程序启动失败：{processName}，已达最大重试次数。");
    }

    private static GraphExecutionResult ExecutePrintLogNode(GraphExecutionPlan plan, GraphRuntimeNode node, Dictionary<string, object> context)
    {
        string message = node.ImagePath ?? string.Empty;

        // Check if message comes from a connected input pin
        var msgConn = plan.Connections.FirstOrDefault(c =>
            c.TargetNodeId == node.Id && c.TargetPinName == "message");
        if (msgConn is not null &&
            context.TryGetValue($"{msgConn.SourceNodeId}:{msgConn.SourcePinName}", out var val))
        {
            message = FormatValueForLog(val);
        }

        Logger.Info($"打印log：{message}");
        return new GraphExecutionResult(true, $"已打印log：{message}");
    }

    private static string FormatValueForLog(object? val) => val switch
    {
        null => "null",
        bool b => b ? "True" : "False",
        System.Drawing.Point p => $"({p.X}, {p.Y})",
        _ => val.ToString() ?? "null",
    };

    private GraphExecutionResult ExecuteSelectWindowNode(GraphExecutionPlan plan, GraphRuntimeNode node, Dictionary<string, object> context)
    {
        string processName = ResolveStringInput(plan, node, context, "process_name", node.ProcessName);
        processName = NormalizeProcessName(processName);

        if (string.IsNullOrWhiteSpace(processName))
        {
            context[$"{node.Id}:result"] = false;
            context[$"{node.Id}:process_name"] = string.Empty;
            Logger.Warn("选中窗口：进程名为空。继续执行。");
            return new GraphExecutionResult(true, $"已跳过节点：{node.Title} 缺少进程名");
        }

        try
        {
            var process = Process.GetProcessesByName(processName)
                .FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero);
            if (process is null)
            {
                context[$"{node.Id}:result"] = false;
                context[$"{node.Id}:process_name"] = processName;
                Logger.Warn($"选中窗口：未找到进程窗口：{processName}。继续执行。");
                return new GraphExecutionResult(true, $"未找到进程窗口：{processName}");
            }

            IntPtr hwnd = process.MainWindowHandle;
            ShowWindow(hwnd, SW_RESTORE);
            SetWindowPos(hwnd, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
            bool foreground = SetForegroundWindow(hwnd);

            context[$"{node.Id}:result"] = foreground;
            context[$"{node.Id}:process_name"] = processName;
            Logger.Info($"选中窗口：{processName}");
            return new GraphExecutionResult(true, $"已选中窗口：{processName}");
        }
        catch (Exception ex)
        {
            context[$"{node.Id}:result"] = false;
            context[$"{node.Id}:process_name"] = processName;
            Logger.Error($"选中窗口失败：{ex.Message}");
            return new GraphExecutionResult(false, $"执行失败：选中窗口错误：{ex.Message}", false);
        }
    }

    private GraphExecutionResult ExecuteFindTextNode(GraphExecutionPlan plan, GraphRuntimeNode node,
        Dictionary<string, object> context, string baseDirectory, CancellationToken ct)
    {
        string searchText = node.ImagePath ?? string.Empty;

        // Check if text comes from a connected input pin
        var textConn = plan.Connections.FirstOrDefault(c =>
            c.TargetNodeId == node.Id && c.TargetPinName == "text");
        if (textConn is not null &&
            context.TryGetValue($"{textConn.SourceNodeId}:{textConn.SourcePinName}", out var val))
        {
            searchText = val?.ToString() ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(searchText))
        {
            context[$"{node.Id}:result"] = false;
            Logger.Warn("找字警告：文字为空。继续执行。");
            return new GraphExecutionResult(true, $"找字未执行：{node.Title} 未设置文字");
        }

        string scriptPath = Path.Combine(AppContext.BaseDirectory, "Python", "find_text.py");
        if (!File.Exists(scriptPath))
        {
            context[$"{node.Id}:result"] = false;
            Logger.Error($"找字失败：Python 脚本不存在：{scriptPath}");
            return new GraphExecutionResult(false, $"执行失败：Python 脚本不存在：{scriptPath}", false);
        }

        try
        {
            int threshold = node.SimilarityThresholdPercent > 0 ? node.SimilarityThresholdPercent : 80;
            Logger.Info($"找字开始：\"{searchText}\"，相似度阈值：{threshold}%");

            string pythonExe = ResolvePythonPath();
            using Process process = new()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = pythonExe,
                    Arguments = $"\"{scriptPath}\" \"{searchText}\" {threshold}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
            };

            process.Start();
            Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
            Task<string> errorTask = process.StandardError.ReadToEndAsync();
            DateTime deadline = DateTime.UtcNow.AddSeconds(60);

            while (!process.WaitForExit(100))
            {
                ct.ThrowIfCancellationRequested();
                if (DateTime.UtcNow >= deadline)
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                    context[$"{node.Id}:result"] = false;
                    Logger.Error("找字失败：Python 脚本执行超时。");
                    return new GraphExecutionResult(false, "执行失败：找字脚本超时。", false);
                }
            }

            string output = outputTask.GetAwaiter().GetResult();
            string stderr = errorTask.GetAwaiter().GetResult();

            if (!string.IsNullOrWhiteSpace(stderr))
                Logger.Info($"找字 Python stderr:\n{stderr}");

            if (!string.IsNullOrWhiteSpace(output))
                Logger.Info($"找字 Python stdout: {output}");

            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
            {
                context[$"{node.Id}:result"] = false;
                Logger.Warn($"找字未命中：\"{searchText}\"（脚本退出码 {process.ExitCode}）。继续执行。");
                return new GraphExecutionResult(true, $"未找到文字：\"{searchText}\"，继续执行");
            }

            try
            {
                using JsonDocument doc = JsonDocument.Parse(output);
                JsonElement root = doc.RootElement;

                if (root.TryGetProperty("found", out var foundProp) && foundProp.GetBoolean())
                {
                    int cx = root.TryGetProperty("centerX", out var cxProp) ? cxProp.GetInt32() : 0;
                    int cy = root.TryGetProperty("centerY", out var cyProp) ? cyProp.GetInt32() : 0;
                    double score = root.TryGetProperty("score", out var sc) ? sc.GetDouble() : 0;
                    string matchedText = root.TryGetProperty("matchedText", out var mt) ? mt.GetString() ?? "" : "";
                    context[$"{node.Id}:result"] = true;
                    context[$"{node.Id}:center"] = new System.Drawing.Point(cx, cy);
                    Logger.Info($"找字成功：\"{matchedText}\"，({cx},{cy})，置信度：{score:F2}");
                    return new GraphExecutionResult(true, $"找字成功：\"{matchedText}\" -> ({cx},{cy})");
                }
            }
            catch (JsonException) { /* fall through to not-found */ }

            context[$"{node.Id}:result"] = false;
            Logger.Warn($"找字未命中：\"{searchText}\"。继续执行。");
            return new GraphExecutionResult(true, $"未找到文字：\"{searchText}\"，继续执行");
        }
        catch (Exception ex) when (ex.Message.Contains("系统找不到指定的文件"))
        {
            context[$"{node.Id}:result"] = false;
            Logger.Warn("找字警告：未找到 Python 环境。继续执行。");
            return new GraphExecutionResult(true, $"找字未执行：未找到 Python 环境，继续执行");
        }
        catch (Exception ex)
        {
            Logger.Error($"找字失败：{ex.Message}");
            context[$"{node.Id}:result"] = false;
            return new GraphExecutionResult(false, $"执行失败：找字脚本错误：{ex.Message}");
        }
    }

    public void ReleaseAllKeys()
    {
        lock (_pressedKeys)
        {
            foreach (byte vk in _pressedKeys)
            {
                keybd_event(vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            }

            _pressedKeys.Clear();
        }
    }

    private GraphExecutionResult ExecuteScrollWheelNode(GraphRuntimeNode node, Dictionary<string, object> context, CancellationToken ct)
    {
        if (node.ScrollSpeed <= 0)
        {
            Logger.Warn("滚轮节点：滚动速度未设置或无效，将使用默认值 120。");
        }

        Logger.Info($"滚轮：{node.ScrollAction}");
        switch (node.ScrollAction)
        {
            case ScrollWheelAction.Press:
                mouse_event(MOUSEEVENTF_MIDDLEDOWN, 0, 0, 0, UIntPtr.Zero);
                break;
            case ScrollWheelAction.Release:
                mouse_event(MOUSEEVENTF_MIDDLEUP, 0, 0, 0, UIntPtr.Zero);
                break;
            case ScrollWheelAction.ScrollForward:
            case ScrollWheelAction.ScrollBackward:
            {
                int speed = node.ScrollSpeed > 0 ? node.ScrollSpeed : 120;
                int delta = node.ScrollAction == ScrollWheelAction.ScrollForward ? speed : -speed;
                int interval = node.ScrollInterval > 0 ? node.ScrollInterval : 100;
                int duration = node.ScrollDuration > 0 ? node.ScrollDuration : 1000;
                int elapsed = 0;

                while (duration == 0 || elapsed < duration)
                {
                    ct.ThrowIfCancellationRequested();
                    mouse_event(MOUSEEVENTF_WHEEL, 0, 0, unchecked((uint)delta), UIntPtr.Zero);
                    Thread.Sleep(interval);
                    if (duration > 0)
                        elapsed += interval;
                }
                break;
            }
        }

        context[$"{node.Id}:result"] = true;
        Logger.Info($"滚轮完成：{node.ScrollAction}");
        return new GraphExecutionResult(true, $"滚轮：{node.ScrollAction}");
    }

    private static (uint downFlag, uint upFlag, uint xButtonData) GetMouseEventFlags(MouseButton button)
    {
        return button switch
        {
            MouseButton.Left => (MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP, 0),
            MouseButton.Right => (MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP, 0),
            MouseButton.XButton1 => (MOUSEEVENTF_XDOWN, MOUSEEVENTF_XUP, XBUTTON1),
            MouseButton.XButton2 => (MOUSEEVENTF_XDOWN, MOUSEEVENTF_XUP, XBUTTON2),
            _ => (MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP, 0),
        };
    }

    private GraphExecutionResult ExecuteDelayNode(GraphRuntimeNode node)
    {
        if (node.DelayMs <= 0)
        {
            Logger.Warn($"延迟节点：延迟时间未设置或无效 ({node.DelayMs}ms)，将使用默认值 500ms。");
        }

        int delayMs = node.DelayMs > 0 ? node.DelayMs : 500;
        Logger.Info($"延迟：{delayMs}ms");
        Thread.Sleep(delayMs);
        Logger.Info($"延迟完成：{delayMs}ms");
        return new GraphExecutionResult(true, $"延迟完成：{delayMs}ms");
    }

    private GraphExecutionResult ExecuteMouseMoveNode(GraphExecutionPlan plan, GraphRuntimeNode node, Dictionary<string, object> context)
    {
        bool hasPositionInput = plan.Connections.Any(c => c.TargetNodeId == node.Id && c.TargetPinName == "position");
        if (!hasPositionInput && !HasUsablePosition(node.PositionX, node.PositionY))
        {
            context[$"{node.Id}:result"] = false;
            Logger.Warn("鼠标移动：未设置目标位置，也未连接位置输入。继续执行。");
            return new GraphExecutionResult(true, $"已跳过节点：{node.Title} 缺少目标位置");
        }

        try
        {
            Point targetPoint = ResolveMouseTargetPoint(plan, node, context);
            Logger.Info($"鼠标移动：({targetPoint.X},{targetPoint.Y})");
            SetCursorPos(targetPoint.X, targetPoint.Y);

            context[$"{node.Id}:result"] = true;
            context[$"{node.Id}:position"] = targetPoint;

            Logger.Info($"鼠标移动完成：({targetPoint.X},{targetPoint.Y})");
            return new GraphExecutionResult(true, $"移动完成：{node.Title} -> ({targetPoint.X},{targetPoint.Y})");
        }
        catch (Exception ex)
        {
            context[$"{node.Id}:result"] = false;
            Logger.Error($"鼠标移动失败：{ex.Message}");
            return new GraphExecutionResult(false, $"执行失败：鼠标移动错误：{ex.Message}", false);
        }
    }

    private static GraphRuntimeNode? GetNextExecutionNode(GraphExecutionPlan plan, string sourceNodeId, string sourcePinName)
    {
        GraphRuntimeConnection? connection = plan.Connections.FirstOrDefault(connection =>
            connection.SourceNodeId == sourceNodeId &&
            connection.SourcePinName == sourcePinName &&
            connection.TargetPinKind == PinKind.Execution);

        if (connection is null)
            return null;

        return plan.Nodes.FirstOrDefault(node => node.Id == connection.TargetNodeId);
    }

    private (GraphExecutionResult, string?) ExecuteIfNode(
        GraphExecutionPlan plan,
        GraphRuntimeNode node,
        Dictionary<string, object> context)
    {
        bool condition = node.ConditionValue;
        GraphRuntimeConnection? condConn = plan.Connections.FirstOrDefault(c =>
            c.TargetNodeId == node.Id && c.TargetPinName == "condition");

        if (condConn is not null &&
            context.TryGetValue($"{condConn.SourceNodeId}:{condConn.SourcePinName}", out object? val) &&
            val is bool b)
        {
            condition = b;
        }

        string nextPin = condition ? "exec_true" : "exec_false";
        Logger.Info($"分支：{(condition ? "True" : "False")}");
        return (new GraphExecutionResult(true, $"分支：{(condition ? "True" : "False")}"), nextPin);
    }

    private (GraphExecutionResult, string?) ExecuteForLoopNode(
        GraphExecutionPlan plan,
        GraphRuntimeNode node,
        Dictionary<string, object> context,
        string baseDirectory,
        CancellationToken ct)
    {
        int count = node.LoopCount > 0 ? node.LoopCount : 1;
        if (node.LoopCount <= 0)
        {
            Logger.Warn($"For 循环节点：循环次数无效 ({node.LoopCount})，将使用默认值 1。");
        }

        Logger.Info($"For 循环开始：{count} 次");

        // Resolve end condition input connection once
        var endConn = plan.Connections.FirstOrDefault(c =>
            c.TargetNodeId == node.Id && c.TargetPinName == "end_condition");
        string endConditionKey = endConn is not null
            ? $"{endConn.SourceNodeId}:{endConn.SourcePinName}"
            : string.Empty;

        for (int i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();

            // Check end condition
            bool shouldEnd = node.ConditionValue;
            if (!string.IsNullOrEmpty(endConditionKey) &&
                context.TryGetValue(endConditionKey, out object? endVal) &&
                endVal is bool eb)
            {
                shouldEnd = eb;
            }
            if (shouldEnd)
            {
                Logger.Info($"For 循环提前结束：结束条件为真（第 {i} 次）");
                break;
            }

            context[$"{node.Id}:index"] = i;

            GraphRuntimeNode? bodyNode = GetNextExecutionNode(plan, node.Id, "exec_loop_body");
            if (bodyNode is not null)
            {
                HashSet<string> bodyVisited = [];
                GraphExecutionResult bodyResult = ExecuteChain(plan, node.Id, "exec_loop_body", context, baseDirectory, bodyVisited, ct);
                if (!bodyResult.ContinueExecution)
                    return (bodyResult, null);
            }
        }

        Logger.Info($"For 循环完成：{count} 次");
        return (new GraphExecutionResult(true, $"循环完成：{count} 次"), "exec_completed");
    }

    private (GraphExecutionResult, string?) ExecuteWhileLoopNode(
        GraphExecutionPlan plan,
        GraphRuntimeNode node,
        Dictionary<string, object> context,
        string baseDirectory,
        CancellationToken ct)
    {
        WhileLoopMode loopMode = (WhileLoopMode)node.ScrollSpeed;
        int maxIterations = loopMode == WhileLoopMode.Infinite ? int.MaxValue : (node.DelayMs > 0 ? node.DelayMs : 10000);
        string modeLabel = loopMode == WhileLoopMode.Infinite ? "无限" : $"最多 {maxIterations} 次";
        Logger.Info($"While 循环开始：{modeLabel}");
        int iteration = 0;
        string conditionKey = string.Empty;

        while (iteration < maxIterations)
        {
            ct.ThrowIfCancellationRequested();

            GraphRuntimeConnection? condConn = plan.Connections.FirstOrDefault(c =>
                c.TargetNodeId == node.Id && c.TargetPinName == "condition");

            if (condConn is not null && conditionKey.Length == 0)
            {
                conditionKey = $"{condConn.SourceNodeId}:{condConn.SourcePinName}";
            }

            bool exit = node.ConditionValue;
            if (!string.IsNullOrEmpty(conditionKey) &&
                context.TryGetValue(conditionKey, out object? val) &&
                val is bool b)
            {
                exit = b;
            }

            if (exit)
                break;

            context[$"{node.Id}:index"] = iteration;
            GraphRuntimeNode? bodyNode = GetNextExecutionNode(plan, node.Id, "exec_loop_body");
            if (bodyNode is not null)
            {
                HashSet<string> bodyVisited = [];
                GraphExecutionResult bodyResult = ExecuteChain(plan, node.Id, "exec_loop_body", context, baseDirectory, bodyVisited, ct);
                if (!bodyResult.ContinueExecution)
                    return (bodyResult, null);
            }

            iteration++;
        }

        if (loopMode != WhileLoopMode.Infinite && iteration >= maxIterations)
        {
            Logger.Error($"While 循环超过最大迭代次数 {maxIterations}，强制终止。");
            return (new GraphExecutionResult(false, "执行失败：While 循环超过最大迭代次数。", false), null);
        }

        Logger.Info($"While 循环完成：{iteration} 次");
        return (new GraphExecutionResult(true, $"While 循环完成：{iteration} 次"), "exec_completed");
    }

    private static Point ResolveMouseTargetPoint(GraphExecutionPlan plan, GraphRuntimeNode node, Dictionary<string, object> context)
    {
        GraphRuntimeConnection? connection = plan.Connections.FirstOrDefault(c =>
            c.TargetNodeId == node.Id &&
            c.TargetPinName == "position" &&
            c.SourcePinKind == PinKind.Vector2D);

        if (connection is not null &&
            context.TryGetValue($"{connection.SourceNodeId}:{connection.SourcePinName}", out object? value) &&
            value is Point point)
        {
            return point;
        }

        return new Point((int)Math.Round(node.PositionX), (int)Math.Round(node.PositionY));
    }

    private static string ResolveStringInput(
        GraphExecutionPlan plan,
        GraphRuntimeNode node,
        Dictionary<string, object> context,
        string pinName,
        string? fallback)
    {
        GraphRuntimeConnection? connection = plan.Connections.FirstOrDefault(c =>
            c.TargetNodeId == node.Id &&
            c.TargetPinName == pinName &&
            c.SourcePinKind == PinKind.String);

        if (connection is not null &&
            context.TryGetValue($"{connection.SourceNodeId}:{connection.SourcePinName}", out object? value))
        {
            return value?.ToString() ?? string.Empty;
        }

        return fallback ?? string.Empty;
    }

    private static string NormalizeProcessName(string processName)
    {
        string normalized = processName.Trim();
        return normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(normalized)
            : normalized;
    }

    private static bool HasUsablePosition(double x, double y)
    {
        if (double.IsNaN(x) || double.IsNaN(y) || double.IsInfinity(x) || double.IsInfinity(y))
            return false;

        return Math.Abs(x) > 0.001 || Math.Abs(y) > 0.001;
    }

    private static string ResolvePath(string? path, string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        return Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(baseDirectory, path));
    }

    private static string ResolvePythonPath()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string programsPython = Path.Combine(localAppData, "Programs", "Python");
        if (Directory.Exists(programsPython))
        {
            string[] dirs = Directory.GetDirectories(programsPython, "Python3*");
            if (dirs.Length > 0)
            {
                string exe = Path.Combine(dirs[0], "python.exe");
                if (File.Exists(exe))
                    return exe;
            }
        }

        return "python";
    }

    private static byte MapKeyToVirtualKeyCode(string key)
    {
        return key?.ToUpperInvariant() switch
        {
            "A" => 0x41, "B" => 0x42, "C" => 0x43, "D" => 0x44, "E" => 0x45,
            "F" => 0x46, "G" => 0x47, "H" => 0x48, "I" => 0x49, "J" => 0x4A,
            "K" => 0x4B, "L" => 0x4C, "M" => 0x4D, "N" => 0x4E, "O" => 0x4F,
            "P" => 0x50, "Q" => 0x51, "R" => 0x52, "S" => 0x53, "T" => 0x54,
            "U" => 0x55, "V" => 0x56, "W" => 0x57, "X" => 0x58, "Y" => 0x59, "Z" => 0x5A,
            "D0" => 0x30, "D1" => 0x31, "D2" => 0x32, "D3" => 0x33, "D4" => 0x34,
            "D5" => 0x35, "D6" => 0x36, "D7" => 0x37, "D8" => 0x38, "D9" => 0x39,
            "F1" => 0x70, "F2" => 0x71, "F3" => 0x72, "F4" => 0x73, "F5" => 0x74, "F6" => 0x75,
            "F7" => 0x76, "F8" => 0x77, "F9" => 0x78, "F10" => 0x79, "F11" => 0x7A, "F12" => 0x7B,
            "ENTER" => 0x0D, "ESCAPE" => 0x1B, "SPACE" => 0x20, "TAB" => 0x09, "BACKSPACE" => 0x08,
            "SHIFT" => 0x10, "CONTROL" => 0x11, "ALT" => 0x12,
            "LSHIFT" => 0xA0, "RSHIFT" => 0xA1, "LCONTROL" => 0xA2, "RCONTROL" => 0xA3, "LALT" => 0xA4, "RALT" => 0xA5,
            "LEFT" => 0x25, "UP" => 0x26, "RIGHT" => 0x27, "DOWN" => 0x28,
            "INSERT" => 0x2D, "DELETEKEY" => 0x2E, "HOME" => 0x24, "END" => 0x23,
            "PAGEUP" => 0x21, "PAGEDOWN" => 0x22,
            "NUMPAD0" => 0x60, "NUMPAD1" => 0x61, "NUMPAD2" => 0x62, "NUMPAD3" => 0x63, "NUMPAD4" => 0x64,
            "NUMPAD5" => 0x65, "NUMPAD6" => 0x66, "NUMPAD7" => 0x67, "NUMPAD8" => 0x68, "NUMPAD9" => 0x69,
            "ADD" => 0x6B, "SUBTRACT" => 0x6D, "MULTIPLY" => 0x6A, "DIVIDE" => 0x6F, "DECIMAL" => 0x6E,
            "LWIN" => 0x5B, "RWIN" => 0x5C, "APPS" => 0x5D,
            _ => 0x41,
        };
    }

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_XDOWN = 0x0080;
    private const uint MOUSEEVENTF_XUP = 0x0100;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;
    private const uint XBUTTON1 = 0x0001;
    private const uint XBUTTON2 = 0x0002;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private static readonly IntPtr HWND_TOP = IntPtr.Zero;
    private const int SW_RESTORE = 9;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_SHOWWINDOW = 0x0040;
}
