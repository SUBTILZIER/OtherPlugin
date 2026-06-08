using System.Collections.ObjectModel;
using System.ComponentModel;
using AutomationStudioWpf.Services;
using Point = System.Windows.Point;
using Geometry = System.Windows.Media.Geometry;
using PathGeometry = System.Windows.Media.PathGeometry;
using PathFigure = System.Windows.Media.PathFigure;
using BezierSegment = System.Windows.Media.BezierSegment;

namespace AutomationStudioWpf.Graph;

/// <summary>
/// 表示一条经过路由节点的完整连线路径链。
/// 使用分段三次贝塞尔曲线（G1 连续）串联所有路径点，保证路由节点处角度圆滑。
/// </summary>
public sealed class ConnectionChain
{
    private readonly ConnectionSettings _settings;
    private Geometry? _chainGeometry;

    internal ConnectionChain(
        ReadOnlyCollection<Point> waypoints,
        ReadOnlyCollection<ConnectionViewModel> memberConnections,
        ConnectionSettings settings)
    {
        Waypoints = waypoints;
        MemberConnections = memberConnections;
        _settings = settings;

        // 订阅成员连接的属性变化，用于刷新链几何
        foreach (var conn in memberConnections)
        {
            conn.PropertyChanged += OnMemberConnectionChanged;
        }
    }

    /// <summary>路径点序列：源锚点 → 路由中心 → ... → 目标锚点</summary>
    public ReadOnlyCollection<Point> Waypoints { get; }

    /// <summary>构成此链的所有 ConnectionViewModel（用于重绑定和失效通知）</summary>
    public ReadOnlyCollection<ConnectionViewModel> MemberConnections { get; }

    /// <summary>链的贝塞尔几何（延迟计算）</summary>
    public Geometry ChainGeometry => _chainGeometry ??= BuildChainGeometry();

    internal void Invalidate()
    {
        _chainGeometry = null;
    }

    private void OnMemberConnectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ConnectionViewModel.PathGeometry) or nameof(PinViewModel.AnchorPoint))
        {
            Invalidate();
        }
    }

    private Geometry BuildChainGeometry()
    {
        if (Waypoints.Count < 2)
            return CreateEmptyGeometry();

        var figures = new List<PathFigure>();

        // 计算每个路径点的贝塞尔控制点偏移量
        var offsets = ComputeOffsets(Waypoints);

        // 为每对相邻路径点创建一段贝塞尔曲线
        for (int i = 0; i < Waypoints.Count - 1; i++)
        {
            var p0 = Waypoints[i];
            var p1 = Waypoints[i + 1];
            var offset0 = offsets[i];     // 离开 p0 的偏移
            var offset1 = offsets[i + 1]; // 进入 p1 的偏移

            // 防止绕圈：clamp offset.X
            var maxOffset0 = Math.Abs(p1.X - p0.X) / 2.0;
            var maxOffset1 = Math.Abs(p1.X - p0.X) / 2.0;

            var control1 = new Point(p0.X + offset0.X, p0.Y + offset0.Y);
            var control2 = new Point(p1.X - offset1.X, p1.Y - offset1.Y);

            // Clamp 防止绕圈
            if (offset0.X > maxOffset0) control1.X = p0.X + maxOffset0;
            if (offset0.X < -maxOffset0) control1.X = p0.X - maxOffset0;
            if (offset1.X > maxOffset1) control2.X = p1.X - maxOffset1;
            if (offset1.X < -maxOffset1) control2.X = p1.X + maxOffset1;

            var figure = new PathFigure
            {
                StartPoint = p0,
                IsClosed = false,
                IsFilled = false,
            };

            // 如果这是第一段且后面还有更多段，直接添加到同一个 PathFigure
            // 否则创建新的 PathFigure（每段独立，但视觉上连续）
            if (i == 0)
            {
                figure.Segments.Add(new BezierSegment(control1, control2, p1, true));
                figures.Add(figure);
            }
            else
            {
                // 后续段：从上一段的终点继续
                var prevFigure = figures[figures.Count - 1];
                prevFigure.Segments.Add(new BezierSegment(control1, control2, p1, true));
            }
        }

        var geometry = new PathGeometry();
        foreach (var figure in figures)
        {
            geometry.Figures.Add(figure);
        }

        if (geometry.CanFreeze)
            geometry.Freeze();

        return geometry;
    }

    /// <summary>
    /// 为所有路径点计算贝塞尔控制点偏移量（offset = tangent / 3）。
    /// - 端点：使用 SplineTangentCalculator.ComputeBezierOffset（已含 /3）
    /// - 内部点：Catmull-Rom 切线 / 3
    /// </summary>
    private Point[] ComputeOffsets(IReadOnlyList<Point> waypoints)
    {
        var n = waypoints.Count;
        var offsets = new Point[n];

        if (n == 2)
        {
            // 只有起点和终点：都用 SplineTangentCalculator（已含 /3）
            offsets[0] = SplineTangentCalculator.ComputeBezierOffset(
                waypoints[0], waypoints[1], _settings);
            offsets[1] = SplineTangentCalculator.ComputeBezierOffset(
                waypoints[1], waypoints[0], _settings);
            return offsets;
        }

        // 起点：使用 SplineTangentCalculator（已含 /3）
        offsets[0] = SplineTangentCalculator.ComputeBezierOffset(
            waypoints[0], waypoints[1], _settings);

        // 内部点：Catmull-Rom 切线 / 3
        for (int i = 1; i < n - 1; i++)
        {
            var prev = waypoints[i - 1];
            var next = waypoints[i + 1];
            offsets[i] = new Point(
                (next.X - prev.X) / 6.0,  // /2 for Catmull-Rom, then /3 for Bezier = /6
                (next.Y - prev.Y) / 6.0);
        }

        // 终点：使用 SplineTangentCalculator（已含 /3）
        offsets[n - 1] = SplineTangentCalculator.ComputeBezierOffset(
            waypoints[n - 1], waypoints[n - 2], _settings);

        return offsets;
    }

    private static Geometry CreateEmptyGeometry()
    {
        var figure = new PathFigure { IsClosed = false, IsFilled = false };
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }
}
