using Point = System.Windows.Point;
using Vector = System.Windows.Vector;
using Geometry = System.Windows.Media.Geometry;
using PathFigure = System.Windows.Media.PathFigure;
using BezierSegment = System.Windows.Media.BezierSegment;
using PathGeometry = System.Windows.Media.PathGeometry;

namespace AutomationStudioWpf.Graph;

public static class ConnectionSplinePlanner
{
    private const double Epsilon = 0.001;
    private const double SegmentHandleScale = 0.35;
    private const double SegmentHandleMaxFraction = 0.49;
    private const double LinearFallbackScale = 1.0 / 3.0;
    private const double DuplicatePointDistance = 0.25;

    public static Geometry BuildPinConnectionGeometry(PinViewModel sourcePin, PinViewModel targetPin)
    {
        return BuildGeometry([GetAbsolutePinAnchor(sourcePin), GetAbsolutePinAnchor(targetPin)]);
    }

    public static PathGeometry BuildGeometry(IReadOnlyList<Point> rawPoints)
    {
        var points = Deduplicate(rawPoints);
        if (points.Count == 0)
        {
            return new PathGeometry();
        }

        PathFigure figure = new()
        {
            StartPoint = points[0],
            IsClosed = false,
            IsFilled = false,
        };

        if (points.Count > 1)
        {
            AddConstrainedSegments(figure, points);
        }

        PathGeometry geometry = new();
        geometry.Figures.Add(figure);
        if (geometry.CanFreeze)
        {
            geometry.Freeze();
        }

        return geometry;
    }

    public static Point GetAbsolutePinAnchor(PinViewModel pin)
    {
        Point anchor = pin.Owner.GetPinAnchor(pin);
        return new Point(pin.Owner.X + anchor.X, pin.Owner.Y + anchor.Y);
    }

    public static double DistanceToSegment(Point point, Point start, Point end)
    {
        Vector segment = end - start;
        double lengthSquared = segment.X * segment.X + segment.Y * segment.Y;
        if (lengthSquared < Epsilon)
        {
            return Math.Sqrt(DistanceSquared(point, start));
        }

        Vector fromStart = point - start;
        double t = Math.Clamp(Vector.Multiply(fromStart, segment) / lengthSquared, 0.0, 1.0);
        Point projection = new(start.X + segment.X * t, start.Y + segment.Y * t);
        return Math.Sqrt(DistanceSquared(point, projection));
    }

    private static void AddConstrainedSegments(PathFigure figure, IReadOnlyList<Point> points)
    {
        for (int i = 0; i < points.Count - 1; i++)
        {
            Point start = points[i];
            Point end = points[i + 1];
            if (TryCreateConstrainedControls(start, end, out Point control1, out Point control2))
            {
                figure.Segments.Add(new BezierSegment(control1, control2, end, true));
            }
        }
    }

    private static bool TryCreateConstrainedControls(Point start, Point end, out Point control1, out Point control2)
    {
        Vector segment = end - start;
        double segmentLength = segment.Length;
        if (segmentLength < Epsilon)
        {
            control1 = start;
            control2 = end;
            return false;
        }

        double absDx = Math.Abs(segment.X);
        double absDy = Math.Abs(segment.Y);
        bool horizontalDominant = absDx >= absDy;

        if (horizontalDominant)
        {
            double handle = Math.Min(segmentLength * SegmentHandleScale, absDx * SegmentHandleMaxFraction);
            if (handle < Epsilon)
            {
                return BuildLinearControls(start, end, out control1, out control2);
            }

            double direction = Math.Sign(segment.X);
            if (direction == 0)
            {
                return BuildLinearControls(start, end, out control1, out control2);
            }

            control1 = new Point(start.X + direction * handle, start.Y);
            control2 = new Point(end.X - direction * handle, end.Y);
            return true;
        }

        double verticalHandle = Math.Min(segmentLength * SegmentHandleScale, absDy * SegmentHandleMaxFraction);
        if (verticalHandle < Epsilon)
        {
            return BuildLinearControls(start, end, out control1, out control2);
        }

        double verticalDirection = Math.Sign(segment.Y);
        if (verticalDirection == 0)
        {
            return BuildLinearControls(start, end, out control1, out control2);
        }

        control1 = new Point(start.X, start.Y + verticalDirection * verticalHandle);
        control2 = new Point(end.X, end.Y - verticalDirection * verticalHandle);
        return true;
    }

    private static bool BuildLinearControls(Point start, Point end, out Point control1, out Point control2)
    {
        Vector segment = end - start;
        control1 = new Point(
            start.X + segment.X * LinearFallbackScale,
            start.Y + segment.Y * LinearFallbackScale);
        control2 = new Point(
            end.X - segment.X * LinearFallbackScale,
            end.Y - segment.Y * LinearFallbackScale);
        return true;
    }

    private static List<Point> Deduplicate(IEnumerable<Point> rawPoints)
    {
        List<Point> points = [];
        foreach (Point point in rawPoints)
        {
            if (points.Count == 0 || Math.Sqrt(DistanceSquared(points[^1], point)) > DuplicatePointDistance)
            {
                points.Add(point);
            }
        }

        return points;
    }

    private static double DistanceSquared(Point first, Point second)
    {
        double dx = second.X - first.X;
        double dy = second.Y - first.Y;
        return dx * dx + dy * dy;
    }
}
