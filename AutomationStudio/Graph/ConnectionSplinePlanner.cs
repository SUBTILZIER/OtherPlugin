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
    private const double RoutedHandleScale = 0.35;
    private const double RoutedProjectionBudget = 0.86;
    private const double ReverseTurnEpsilon = 0.02;
    private const int CurveIntersectionSamples = 40;
    private const double IntersectionEpsilon = 0.001;
    private static readonly double[] SmoothnessFactors = [1.0, 0.75, 0.5, 0.25, 0.0];

    public static Geometry BuildPinConnectionGeometry(PinViewModel sourcePin, PinViewModel targetPin)
    {
        return BuildGeometry([GetAbsolutePinAnchor(sourcePin), GetAbsolutePinAnchor(targetPin)]);
    }

    public static PathGeometry BuildGeometry(IReadOnlyList<Point> rawPoints)
    {
        var points = UncrossWaypoints(Deduplicate(rawPoints));
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
        if (points.Count == 2)
        {
            if (TryCreateConstrainedControls(points[0], points[1], out Point control1, out Point control2))
            {
                figure.Segments.Add(new BezierSegment(control1, control2, points[1], true));
            }

            return;
        }

        Vector[] tangents = BuildRoutedTangents(points);
        foreach (double smoothness in SmoothnessFactors)
        {
            List<(Point Control1, Point Control2)> controls = [];
            for (int i = 0; i < points.Count - 1; i++)
            {
                if (TryCreateRoutedControls(points, tangents, i, smoothness, out Point control1, out Point control2))
                {
                    controls.Add((control1, control2));
                }
            }

            if (controls.Count != points.Count - 1 || HasSelfIntersection(points, controls))
            {
                continue;
            }

            for (int i = 0; i < controls.Count; i++)
            {
                figure.Segments.Add(new BezierSegment(
                    controls[i].Control1,
                    controls[i].Control2,
                    points[i + 1],
                    true));
            }

            return;
        }

        AddLinearSegments(figure, points);
    }

    private static Vector[] BuildRoutedTangents(IReadOnlyList<Point> points)
    {
        Vector[] tangents = new Vector[points.Count];
        tangents[0] = GetAxisAlignedTangent(points[1] - points[0]);
        tangents[^1] = GetAxisAlignedTangent(points[^1] - points[^2]);

        for (int i = 1; i < points.Count - 1; i++)
        {
            Vector incoming = Normalize(points[i] - points[i - 1]);
            Vector outgoing = Normalize(points[i + 1] - points[i]);
            Vector bisector = incoming + outgoing;
            tangents[i] = bisector.LengthSquared <= ReverseTurnEpsilon * ReverseTurnEpsilon
                ? new Vector()
                : Normalize(bisector);
        }

        return tangents;
    }

    private static bool TryCreateRoutedControls(
        IReadOnlyList<Point> points,
        IReadOnlyList<Vector> tangents,
        int segmentIndex,
        double smoothness,
        out Point control1,
        out Point control2)
    {
        Point start = points[segmentIndex];
        Point end = points[segmentIndex + 1];
        Vector segment = end - start;
        double segmentLength = segment.Length;
        if (segmentLength < Epsilon)
        {
            control1 = start;
            control2 = end;
            return false;
        }

        Vector segmentDirection = segment / segmentLength;
        Vector startTangent = tangents[segmentIndex];
        Vector endTangent = tangents[segmentIndex + 1];
        double startHandle = GetRoutedHandleLength(points, segmentIndex, segmentLength);
        double endHandle = GetRoutedHandleLength(points, segmentIndex + 1, segmentLength);
        startHandle *= smoothness;
        endHandle *= smoothness;

        double startAlignment = Vector.Multiply(startTangent, segmentDirection);
        double endAlignment = Vector.Multiply(endTangent, segmentDirection);
        if (startAlignment <= 0.0)
        {
            startHandle = 0.0;
        }

        if (endAlignment <= 0.0)
        {
            endHandle = 0.0;
        }

        double startProjection = Math.Max(0.0, startAlignment) * startHandle;
        double endProjection = Math.Max(0.0, endAlignment) * endHandle;
        double projectionTotal = startProjection + endProjection;
        double projectionBudget = segmentLength * RoutedProjectionBudget;
        if (projectionTotal > projectionBudget && projectionTotal > Epsilon)
        {
            double scale = projectionBudget / projectionTotal;
            startHandle *= scale;
            endHandle *= scale;
        }

        control1 = start + startTangent * startHandle;
        control2 = end - endTangent * endHandle;
        return true;
    }

    private static List<Point> UncrossWaypoints(List<Point> points)
    {
        if (points.Count < 4)
        {
            return points;
        }

        List<Point> ordered = [.. points];
        while (true)
        {
            bool changed = false;
            for (int first = 0; first < ordered.Count - 1 && !changed; first++)
            {
                for (int second = first + 2; second < ordered.Count - 1; second++)
                {
                    if (!SegmentsCrossProperly(
                            ordered[first],
                            ordered[first + 1],
                            ordered[second],
                            ordered[second + 1]))
                    {
                        continue;
                    }

                    ordered.Reverse(first + 1, second - first);
                    changed = true;
                    break;
                }
            }

            if (!changed)
            {
                break;
            }
        }

        return ordered;
    }

    private static bool HasSelfIntersection(
        IReadOnlyList<Point> points,
        IReadOnlyList<(Point Control1, Point Control2)> controls)
    {
        List<Point> samples = [points[0]];
        for (int segmentIndex = 0; segmentIndex < controls.Count; segmentIndex++)
        {
            Point start = points[segmentIndex];
            Point end = points[segmentIndex + 1];
            var control = controls[segmentIndex];
            for (int sampleIndex = 1; sampleIndex <= CurveIntersectionSamples; sampleIndex++)
            {
                double t = sampleIndex / (double)CurveIntersectionSamples;
                samples.Add(Cubic(start, control.Control1, control.Control2, end, t));
            }
        }

        for (int first = 0; first < samples.Count - 1; first++)
        {
            for (int second = first + 2; second < samples.Count - 1; second++)
            {
                if (SegmentsCrossProperly(samples[first], samples[first + 1], samples[second], samples[second + 1]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static Point Cubic(Point p0, Point p1, Point p2, Point p3, double t)
    {
        double u = 1.0 - t;
        double tt = t * t;
        double uu = u * u;
        return new Point(
            uu * u * p0.X + 3.0 * uu * t * p1.X + 3.0 * u * tt * p2.X + tt * t * p3.X,
            uu * u * p0.Y + 3.0 * uu * t * p1.Y + 3.0 * u * tt * p2.Y + tt * t * p3.Y);
    }

    private static bool SegmentsCrossProperly(Point a, Point b, Point c, Point d)
    {
        double firstSideA = Cross(a, b, c);
        double firstSideB = Cross(a, b, d);
        double secondSideA = Cross(c, d, a);
        double secondSideB = Cross(c, d, b);
        return OppositeSigns(firstSideA, firstSideB) && OppositeSigns(secondSideA, secondSideB);
    }

    private static void AddLinearSegments(PathFigure figure, IReadOnlyList<Point> points)
    {
        for (int i = 0; i < points.Count - 1; i++)
        {
            BuildLinearControls(points[i], points[i + 1], out Point control1, out Point control2);
            figure.Segments.Add(new BezierSegment(control1, control2, points[i + 1], true));
        }
    }

    private static bool OppositeSigns(double first, double second) =>
        first > IntersectionEpsilon && second < -IntersectionEpsilon ||
        first < -IntersectionEpsilon && second > IntersectionEpsilon;

    private static double Cross(Point a, Point b, Point c) =>
        (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

    private static double GetRoutedHandleLength(IReadOnlyList<Point> points, int pointIndex, double currentSegmentLength)
    {
        if (pointIndex == 0 || pointIndex == points.Count - 1)
        {
            return currentSegmentLength * SegmentHandleScale;
        }

        double incomingLength = (points[pointIndex] - points[pointIndex - 1]).Length;
        double outgoingLength = (points[pointIndex + 1] - points[pointIndex]).Length;
        if (incomingLength < Epsilon || outgoingLength < Epsilon)
        {
            return 0.0;
        }

        Vector incoming = (points[pointIndex] - points[pointIndex - 1]) / incomingLength;
        Vector outgoing = (points[pointIndex + 1] - points[pointIndex]) / outgoingLength;
        double turnDot = Math.Clamp(Vector.Multiply(incoming, outgoing), -1.0, 1.0);
        double turnScale = Math.Sqrt(Math.Max(0.0, (1.0 + turnDot) * 0.5));
        double localLength = Math.Min(currentSegmentLength, Math.Min(incomingLength, outgoingLength));
        return localLength * RoutedHandleScale * turnScale;
    }

    private static Vector GetAxisAlignedTangent(Vector segment)
    {
        if (Math.Abs(segment.X) >= Math.Abs(segment.Y))
        {
            return new Vector(Math.Sign(segment.X), 0.0);
        }

        return new Vector(0.0, Math.Sign(segment.Y));
    }

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
