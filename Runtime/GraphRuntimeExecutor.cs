using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using AutomationStudioWpf.Graph;

namespace AutomationStudioWpf.Runtime;

public sealed class GraphRuntimeExecutor
{
    public GraphExecutionResult Execute(GraphExecutionPlan plan, string baseDirectory)
    {
        GraphRuntimeNode? startNode = plan.Nodes.FirstOrDefault(node => node.NodeKind == NodeKind.Start);
        if (startNode is null)
        {
            return new GraphExecutionResult(false, "执行失败：图中没有开始节点。");
        }

        Dictionary<string, object> context = [];
        HashSet<string> visitedNodes = [];
        GraphRuntimeNode? currentNode = GetNextExecutionNode(plan, startNode.Id);

        while (currentNode is not null)
        {
            if (!visitedNodes.Add(currentNode.Id))
            {
                return new GraphExecutionResult(false, $"执行失败：检测到执行环路，节点 {currentNode.Title} 被重复访问。");
            }

            GraphExecutionResult nodeResult = ExecuteNode(plan, currentNode, context, baseDirectory);
            if (!nodeResult.Success)
            {
                return nodeResult;
            }

            currentNode = GetNextExecutionNode(plan, currentNode.Id);
        }

        return new GraphExecutionResult(true, "执行完成。");
    }

    private GraphExecutionResult ExecuteNode(GraphExecutionPlan plan, GraphRuntimeNode node, Dictionary<string, object> context, string baseDirectory)
    {
        return node.NodeKind switch
        {
            NodeKind.FindImage => ExecuteFindImageNode(node, context, baseDirectory),
            NodeKind.MouseClick => ExecuteMouseClickNode(plan, node, context),
            NodeKind.Delay => ExecuteDelayNode(node),
            NodeKind.MouseMove => ExecuteMouseMoveNode(node),
            NodeKind.Keyboard => ExecuteKeyboardNode(node, context),
            NodeKind.ScrollWheel => ExecuteScrollWheelNode(node, context),
            _ => new GraphExecutionResult(true, $"已跳过节点：{node.Title}")
        };
    }

    private GraphExecutionResult ExecuteFindImageNode(GraphRuntimeNode node, Dictionary<string, object> context, string baseDirectory)
    {
        string imagePath = ResolvePath(node.ImagePath, baseDirectory);
        if (!File.Exists(imagePath))
        {
            return new GraphExecutionResult(false, $"执行失败：找图节点图片不存在：{imagePath}");
        }

        using Bitmap template = new(imagePath);
        using Bitmap screenshot = CapturePrimaryScreen();
        ImageMatchResult match = FindTemplate(screenshot, template, node.SimilarityThresholdPercent / 100.0);

        context[$"{node.Id}:success"] = match.Found;
        context[$"{node.Id}:center"] = match.Center;

        if (!match.Found)
        {
            return new GraphExecutionResult(false, $"执行失败：未找到图像：{Path.GetFileName(imagePath)}");
        }

        return new GraphExecutionResult(true, $"找图成功：{node.Title} -> ({match.Center.X},{match.Center.Y})");
    }

    private GraphExecutionResult ExecuteMouseClickNode(GraphExecutionPlan plan, GraphRuntimeNode node, Dictionary<string, object> context)
    {
        Point targetPoint = ResolveMouseTargetPoint(plan, node, context);
        SetCursorPos(targetPoint.X, targetPoint.Y);

        (uint downFlag, uint upFlag, uint xButtonData) = GetMouseEventFlags(node.MouseButton);

        if (node.OperationMode == PressReleaseMode.Press)
            mouse_event(downFlag, 0, 0, xButtonData, UIntPtr.Zero);
        else
            mouse_event(upFlag, 0, 0, xButtonData, UIntPtr.Zero);

        context[$"{node.Id}:result"] = true;

        string buttonLabel = node.MouseButton switch
        {
            MouseButton.Left => "左键",
            MouseButton.Right => "右键",
            MouseButton.XButton1 => "侧键1",
            MouseButton.XButton2 => "侧键2",
            _ => "左键",
        };
        string modeLabel = node.OperationMode == PressReleaseMode.Press ? "按下" : "抬起";
        return new GraphExecutionResult(true, $"鼠标{buttonLabel}{modeLabel}：({targetPoint.X},{targetPoint.Y})");
    }

    private GraphExecutionResult ExecuteKeyboardNode(GraphRuntimeNode node, Dictionary<string, object> context)
    {
        byte vkCode = MapKeyToVirtualKeyCode(node.Key ?? "A");

        if (node.OperationMode == PressReleaseMode.Press)
            keybd_event(vkCode, 0, 0, UIntPtr.Zero);
        else
            keybd_event(vkCode, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

        context[$"{node.Id}:result"] = true;

        string modeLabel = node.OperationMode == PressReleaseMode.Press ? "按下" : "抬起";
        return new GraphExecutionResult(true, $"键盘{node.Key}{modeLabel}");
    }

    private GraphExecutionResult ExecuteScrollWheelNode(GraphRuntimeNode node, Dictionary<string, object> context)
    {
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
                int delta = node.ScrollAction == ScrollWheelAction.ScrollForward
                    ? (node.ScrollSpeed > 0 ? node.ScrollSpeed : 120)
                    : -(node.ScrollSpeed > 0 ? node.ScrollSpeed : 120);
                mouse_event(MOUSEEVENTF_WHEEL, 0, 0, unchecked((uint)delta), UIntPtr.Zero);
                break;
            }
        }

        context[$"{node.Id}:result"] = true;
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
        Thread.Sleep(Math.Max(0, node.DelayMs));
        return new GraphExecutionResult(true, $"延迟完成：{node.DelayMs}ms");
    }

    private GraphExecutionResult ExecuteMouseMoveNode(GraphRuntimeNode node)
    {
        Point targetPoint = new((int)Math.Round(node.PositionX), (int)Math.Round(node.PositionY));
        SetCursorPos(targetPoint.X, targetPoint.Y);
        return new GraphExecutionResult(true, $"移动完成：{node.Title} -> ({targetPoint.X},{targetPoint.Y})");
    }

    private static GraphRuntimeNode? GetNextExecutionNode(GraphExecutionPlan plan, string sourceNodeId)
    {
        GraphRuntimeConnection? connection = plan.Connections.FirstOrDefault(connection =>
            connection.SourceNodeId == sourceNodeId &&
            connection.SourcePinKind == PinKind.Execution &&
            connection.TargetPinKind == PinKind.Execution);

        if (connection is null)
            return null;

        return plan.Nodes.FirstOrDefault(node => node.Id == connection.TargetNodeId);
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

    private static string ResolvePath(string? path, string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        return Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(baseDirectory, path));
    }

    private static Bitmap CapturePrimaryScreen()
    {
        Rectangle bounds = Screen.PrimaryScreen?.Bounds ?? throw new InvalidOperationException("无法获取主屏幕。");
        Bitmap bitmap = new(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        return bitmap;
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

    private static ImageMatchResult FindTemplate(Bitmap screen, Bitmap template, double similarityThreshold)
    {
        Rectangle searchBounds = new(0, 0, screen.Width - template.Width + 1, screen.Height - template.Height + 1);
        if (searchBounds.Width <= 0 || searchBounds.Height <= 0)
        {
            return ImageMatchResult.NotFound;
        }

        BitmapData screenData = screen.LockBits(new Rectangle(0, 0, screen.Width, screen.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        BitmapData templateData = template.LockBits(new Rectangle(0, 0, template.Width, template.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

        try
        {
            unsafe
            {
                byte* screenBase = (byte*)screenData.Scan0;
                byte* templateBase = (byte*)templateData.Scan0;

                int step = template.Width * template.Height > 12000 ? 2 : 1;
                double bestScore = double.MinValue;
                Point bestCenter = Point.Empty;
                double maxAverageDiff = (1.0 - similarityThreshold) * 255.0;

                for (int y = 0; y < searchBounds.Height; y += step)
                {
                    for (int x = 0; x < searchBounds.Width; x += step)
                    {
                        double totalDiff = 0;
                        int sampleCount = 0;
                        bool reject = false;

                        for (int ty = 0; ty < template.Height && !reject; ty += step)
                        {
                            byte* templateRow = templateBase + ty * templateData.Stride;
                            byte* screenRow = screenBase + (y + ty) * screenData.Stride + x * 3;

                            for (int tx = 0; tx < template.Width; tx += step)
                            {
                                int templateOffset = tx * 3;
                                int bDiff = Math.Abs(screenRow[templateOffset] - templateRow[templateOffset]);
                                int gDiff = Math.Abs(screenRow[templateOffset + 1] - templateRow[templateOffset + 1]);
                                int rDiff = Math.Abs(screenRow[templateOffset + 2] - templateRow[templateOffset + 2]);
                                totalDiff += bDiff + gDiff + rDiff;
                                sampleCount++;

                                double currentAverageDiff = totalDiff / (sampleCount * 3.0);
                                if (currentAverageDiff > maxAverageDiff && bestScore >= similarityThreshold)
                                {
                                    reject = true;
                                    break;
                                }
                            }
                        }

                        if (reject || sampleCount == 0)
                            continue;

                        double score = 1.0 - (totalDiff / (sampleCount * 3.0 * 255.0));
                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestCenter = new Point(x + template.Width / 2, y + template.Height / 2);
                        }
                    }
                }

                if (bestScore >= similarityThreshold)
                    return new ImageMatchResult(true, bestCenter, bestScore);
            }
        }
        finally
        {
            screen.UnlockBits(screenData);
            template.UnlockBits(templateData);
        }

        return ImageMatchResult.NotFound;
    }

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

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

    private readonly record struct ImageMatchResult(bool Found, Point Center, double Score)
    {
        public static ImageMatchResult NotFound => new(false, Point.Empty, 0);
    }
}
