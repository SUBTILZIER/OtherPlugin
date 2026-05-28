using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using AutomationStudioWpf.Graph;

namespace AutomationStudioWpf.Runtime;

/// <summary>
/// Minimal runtime for current blueprint graph.
/// v1 supports sequential execution from Start -> FindImage -> MouseLeftClick.
/// </summary>
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
            NodeKind.MouseClick => ExecuteMouseLeftClickNode(plan, node, context),
            NodeKind.Delay => ExecuteDelayNode(node),
            NodeKind.MouseMove => ExecuteMouseMoveNode(node),
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

    private GraphExecutionResult ExecuteMouseLeftClickNode(GraphExecutionPlan plan, GraphRuntimeNode node, Dictionary<string, object> context)
    {
        Point targetPoint = ResolveMouseTargetPoint(plan, node, context);
        SetCursorPos(targetPoint.X, targetPoint.Y);

        (uint downFlag, uint upFlag, uint xButtonData) = GetMouseEventFlags(node.MouseButton);

        if (node.ClickMode == MouseClickMode.Hold)
        {
            mouse_event(downFlag, 0, 0, xButtonData, UIntPtr.Zero);
            Thread.Sleep(600);
            mouse_event(upFlag, 0, 0, xButtonData, UIntPtr.Zero);
        }
        else
        {
            mouse_event(downFlag, 0, 0, xButtonData, UIntPtr.Zero);
            mouse_event(upFlag, 0, 0, xButtonData, UIntPtr.Zero);
        }

        string buttonLabel = node.MouseButton switch
        {
            MouseButton.Left => "左键",
            MouseButton.Right => "右键",
            MouseButton.XButton1 => "侧键1",
            MouseButton.XButton2 => "侧键2",
            _ => "左键",
        };
        return new GraphExecutionResult(true, $"点击完成：{node.Title} ({buttonLabel}) -> ({targetPoint.X},{targetPoint.Y})");
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
        {
            return null;
        }

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
        {
            return string.Empty;
        }

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
                        {
                            continue;
                        }

                        double score = 1.0 - (totalDiff / (sampleCount * 3.0 * 255.0));
                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestCenter = new Point(x + template.Width / 2, y + template.Height / 2);
                        }
                    }
                }

                if (bestScore >= similarityThreshold)
                {
                    return new ImageMatchResult(true, bestCenter, bestScore);
                }
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

    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_XDOWN = 0x0080;
    private const uint MOUSEEVENTF_XUP = 0x0100;
    private const uint XBUTTON1 = 0x0001;
    private const uint XBUTTON2 = 0x0002;

    private readonly record struct ImageMatchResult(bool Found, Point Center, double Score)
    {
        public static ImageMatchResult NotFound => new(false, Point.Empty, 0);
    }
}
