using Point = System.Windows.Point;
using Vector = System.Windows.Vector;
using Geometry = System.Windows.Media.Geometry;
using PathFigure = System.Windows.Media.PathFigure;
using BezierSegment = System.Windows.Media.BezierSegment;
using LineSegment = System.Windows.Media.LineSegment;
using PathGeometry = System.Windows.Media.PathGeometry;

namespace AutomationStudioWpf.Graph;

public static class ConnectionSplinePlanner
{
    private const double Epsilon = 0.001;
    private const double SegmentHandleScale = 0.35;
    private const double SegmentHandleMaxFraction = 0.49;
    private const double MinimumHorizontalSeparation = 96.0;
    private const double LinearFallbackScale = 1.0 / 3.0;
    private const double DuplicatePointDistance = 0.25;
    private const double IntersectionEpsilon = 0.001;

    public static Geometry BuildPinConnectionGeometry(PinViewModel sourcePin, PinViewModel targetPin)
    {
        return BuildGeometry([GetAbsolutePinAnchor(sourcePin), GetAbsolutePinAnchor(targetPin)]);
    }

    public static PathGeometry BuildGeometry(IReadOnlyList<Point> rawPoints)
    {
        var points = RemoveSelfCrossingWaypoints(Deduplicate(rawPoints));
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
            if (points.Count == 2)
            {
                AddConstrainedSegments(figure, points);
            }
            else
            {
                AddRoundedRoute(figure, points);
            }
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
        if (points.Count == 2)
        {
            if (TryCreateConstrainedControls(points[0], points[1], out Point control1, out Point control2))
            {
                figure.Segments.Add(new BezierSegment(control1, control2, points[1], true));
            }

            return;
        }

        AddRoundedRoute(figure, points);
    }

    private static void AddRoundedRoute(PathFigure figure, IReadOnlyList<Point> points)
    {
        const double cornerRadius = 24.0;
        const double cornerHandleScale = 0.55;
        Point current = points[0];

        for (int index = 1; index < points.Count - 1; index++)
        {
            Point corner = points[index];
            Point next = points[index + 1];
            Vector incoming = Normalize(corner - points[index - 1]);
            Vector outgoing = Normalize(next - corner);
            double turn = Vector.Multiply(incoming, outgoing);
            double radius = turn < -0.95
                ? 0.0
                : Math.Min(cornerRadius, Math.Min((corner - points[index - 1]).Length, (next - corner).Length) * 0.24);

            Point entry = corner - incoming * radius;
            Point exit = corner + outgoing * radius;
            AddLine(figure, current, entry);
            if (radius > Epsilon)
            {
                double handle = radius * cornerHandleScale;
                figure.Segments.Add(new BezierSegment(
                    entry + incoming * handle,
                    exit - outgoing * handle,
                    exit,
                    true));
            }
            else
            {
                AddLine(figure, entry, exit);
            }

            current = exit;
        }

        AddLine(figure, current, points[^1]);
    }

    private static void AddLine(PathFigure figure, Point start, Point end)
    {
        if ((end - start).Length > Epsilon)
        {
            figure.Segments.Add(new LineSegment(end, true));
        }
    }

    private static List<Point> RemoveSelfCrossingWaypoints(List<Point> points)
    {
        if (points.Count < 4)
        {
            return points;
        }

        List<Point> filtered = [points[0]];
        for (int index = 1; index < points.Count; index++)
        {
            Point candidate = points[index];
            while (filtered.Count >= 2 && SegmentCrossesExisting(filtered[^1], candidate, filtered))
            {
                filtered.RemoveAt(filtered.Count - 1);
            }

            filtered.Add(candidate);
        }

        return filtered;
    }

    private static bool SegmentCrossesExisting(Point start, Point end, IReadOnlyList<Point> path)
    {
        for (int index = 0; index < path.Count - 2; index++)
        {
            if (SegmentsCrossProperly(start, end, path[index], path[index + 1]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool SegmentsCrossProperly(Point a, Point b, Point c, Point d)
    {
        double firstSideA = Cross(a, b, c);
        double firstSideB = Cross(a, b, d);
        double secondSideA = Cross(c, d, a);
        double secondSideB = Cross(c, d, b);
        return OppositeSigns(firstSideA, firstSideB) && OppositeSigns(secondSideA, secondSideB);
    }

    private static bool OppositeSigns(double first, double second) =>
        first > IntersectionEpsilon && second < -IntersectionEpsilon ||
        first < -IntersectionEpsilon && second > IntersectionEpsilon;

    private static double Cross(Point a, Point b, Point c) =>
        (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

    private static Vector Normalize(Vector vector)
    {
        double length = vector.Length;
        return length < Epsilon ? new Vector() : vector / length;
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
        // Blueprint wires leave horizontal pins horizontally whenever there is
        // enough separation. This avoids the steep vertical arcs produced by
        // choosing the dominant axis for tall but otherwise normal links.
        bool useHorizontalTangent = absDx >= MinimumHorizontalSeparation;
        Vector tangent = useHorizontalTangent
            ? new Vector(Math.Sign(segment.X), 0.0)
            : absDy >= absDx * 1.35
                ? new Vector(0.0, Math.Sign(segment.Y))
                : segment / segmentLength;
        double axisLength = tangent.X != 0.0 ? absDx : tangent.Y != 0.0 ? absDy : segmentLength;
        double handle = Math.Min(segmentLength * SegmentHandleScale, axisLength * SegmentHandleMaxFraction);
        if (handle < Epsilon)
        {
            return BuildLinearControls(start, end, out control1, out control2);
        }

        control1 = start + tangent * handle;
        control2 = end - tangent * handle;
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
