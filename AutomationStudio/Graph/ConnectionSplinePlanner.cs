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
    private const double OrdinaryTangentScale = 0.45;
    private const double SplineHandleScale = 0.35;
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

        if (points.Count == 2)
        {
            AddOrdinarySegment(figure, points[0], points[1]);
        }
        else
        {
            AddSplineSegments(figure, points);
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

    private static void AddOrdinarySegment(PathFigure figure, Point start, Point end)
    {
        double tangent = Math.Abs(end.X - start.X) * OrdinaryTangentScale;
        Point control1 = new(start.X + tangent, start.Y);
        Point control2 = new(end.X - tangent, end.Y);
        figure.Segments.Add(new BezierSegment(control1, control2, end, true));
    }

    private static void AddSplineSegments(PathFigure figure, IReadOnlyList<Point> points)
    {
        for (int i = 0; i < points.Count - 1; i++)
        {
            Point start = points[i];
            Point end = points[i + 1];
            Vector segment = end - start;
            double segmentLength = segment.Length;

            if (segmentLength < Epsilon)
            {
                continue;
            }

            Vector controlOut = TangentForPoint(points, i, segment, segmentLength);
            Vector controlIn = TangentForPoint(points, i + 1, segment, segmentLength);
            Point control1 = start + controlOut;
            Point control2 = end - controlIn;
            figure.Segments.Add(new BezierSegment(control1, control2, end, true));
        }
    }

    private static Vector TangentForPoint(IReadOnlyList<Point> points, int index, Vector segment, double segmentLength)
    {
        Vector tangent;
        if (index <= 0)
        {
            tangent = points[1] - points[0];
        }
        else if (index >= points.Count - 1)
        {
            tangent = points[^1] - points[^2];
        }
        else
        {
            tangent = points[index + 1] - points[index - 1];
        }

        double scale = segmentLength * SplineHandleScale;
        return AlignAndScale(tangent, segment, scale);
    }

    private static Vector AlignAndScale(Vector tangent, Vector segment, double scale)
    {
        if (tangent.Length < Epsilon)
        {
            tangent = segment;
        }

        if (Vector.Multiply(tangent, segment) <= 0)
        {
            tangent = segment;
        }

        if (tangent.Length < Epsilon)
        {
            return new Vector();
        }

        tangent.Normalize();
        return tangent * scale;
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
