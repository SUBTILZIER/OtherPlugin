using System.Windows;
using System.Windows.Media;
using AutomationStudioWpf.Graph;

namespace AutomationStudio.CoreTests;

[TestClass]
public sealed class ConnectionSplinePlannerTests
{
    [TestMethod]
    public void DirectSplineKeepsControlsInsideEndpointBounds()
    {
        var geometry = ConnectionSplinePlanner.BuildGeometry([new Point(0, 0), new Point(120, 420)]);
        var segment = (BezierSegment)geometry.Figures[0].Segments.Single();

        Assert.IsTrue(segment.Point1.X is >= 0 and <= 120);
        Assert.IsTrue(segment.Point1.Y is >= 0 and <= 420);
        Assert.IsTrue(segment.Point2.X is >= 0 and <= 120);
        Assert.IsTrue(segment.Point2.Y is >= 0 and <= 420);
    }

    [TestMethod]
    public void TallDirectSplineLeavesHorizontalPinsBeforeTurning()
    {
        var geometry = ConnectionSplinePlanner.BuildGeometry([new Point(0, 0), new Point(180, 600)]);
        var segment = (BezierSegment)geometry.Figures[0].Segments.Single();

        Assert.AreEqual(0, segment.Point1.Y, 0.001);
        Assert.AreEqual(600, segment.Point2.Y, 0.001);
    }

    [TestMethod]
    public void RoutedSplineDoesNotReverseRouteToAvoidCrossing()
    {
        var geometry = ConnectionSplinePlanner.BuildGeometry(
        [
            new Point(0, 0),
            new Point(100, 100),
            new Point(0, 100),
            new Point(100, 0),
        ]);

        var samples = SampleGeometry(geometry);
        for (int first = 0; first < samples.Count - 1; first++)
        {
            for (int second = first + 2; second < samples.Count - 1; second++)
            {
                Assert.IsFalse(SegmentsCrossProperly(
                    samples[first], samples[first + 1], samples[second], samples[second + 1]));
            }
        }
    }

    private static List<Point> SampleGeometry(PathGeometry geometry)
    {
        List<Point> samples = [];
        foreach (var figure in geometry.Figures)
        {
            Point start = figure.StartPoint;
            if (samples.Count == 0)
                samples.Add(start);

            foreach (var segment in figure.Segments)
            {
                if (segment is LineSegment line)
                {
                    samples.Add(line.Point);
                    start = line.Point;
                    continue;
                }

                if (segment is not BezierSegment bezier)
                    continue;

                for (int index = 1; index <= 20; index++)
                {
                    double t = index / 20.0;
                    samples.Add(Cubic(start, bezier.Point1, bezier.Point2, bezier.Point3, t));
                }

                start = bezier.Point3;
            }
        }

        return samples;
    }

    private static Point Cubic(Point p0, Point p1, Point p2, Point p3, double t)
    {
        double u = 1 - t;
        return new Point(
            u * u * u * p0.X + 3 * u * u * t * p1.X + 3 * u * t * t * p2.X + t * t * t * p3.X,
            u * u * u * p0.Y + 3 * u * u * t * p1.Y + 3 * u * t * t * p2.Y + t * t * t * p3.Y);
    }

    private static bool SegmentsCrossProperly(Point a, Point b, Point c, Point d)
    {
        double abC = Cross(a, b, c);
        double abD = Cross(a, b, d);
        double cdA = Cross(c, d, a);
        double cdB = Cross(c, d, b);
        return OppositeSigns(abC, abD) && OppositeSigns(cdA, cdB);
    }

    private static double Cross(Point a, Point b, Point c) =>
        (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

    private static bool OppositeSigns(double first, double second) =>
        first > 0.001 && second < -0.001 || first < -0.001 && second > 0.001;
}
