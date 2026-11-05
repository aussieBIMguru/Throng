namespace Throng.Core.Geometry;

/// <summary>
/// Point/segment/polyline distance and intersection helpers used by snapping, picking and shape validation.
/// </summary>
public static class SegmentMath
{
    private const double Epsilon = 1e-9;

    /// <summary>Closest point to <paramref name="p"/> on segment a-b.</summary>
    public static Point2 ClosestPointOnSegment(Point2 p, Point2 a, Point2 b)
    {
        Point2 ab = b - a;
        double lengthSquared = Point2.Dot(ab, ab);
        if (lengthSquared < Epsilon)
        {
            return a;
        }

        double t = Math.Clamp(Point2.Dot(p - a, ab) / lengthSquared, 0, 1);
        return a + (ab * t);
    }

    /// <summary>Distance from <paramref name="p"/> to segment a-b.</summary>
    public static double DistanceToSegment(Point2 p, Point2 a, Point2 b) =>
        Point2.Distance(p, ClosestPointOnSegment(p, a, b));

    /// <summary>
    /// Distance from <paramref name="p"/> to the edges of an outline. Closed outlines include the edge from the
    /// last point back to the first. Returns +∞ for an empty outline.
    /// </summary>
    public static double DistanceToOutline(IReadOnlyList<Point2> outline, Point2 p, bool closed)
    {
        int n = outline.Count;
        if (n == 0)
        {
            return double.PositiveInfinity;
        }

        if (n == 1)
        {
            return Point2.Distance(p, outline[0]);
        }

        double best = double.PositiveInfinity;
        int edges = closed ? n : n - 1;
        for (int i = 0; i < edges; i++)
        {
            double d = DistanceToSegment(p, outline[i], outline[(i + 1) % n]);
            if (d < best)
            {
                best = d;
            }
        }

        return best;
    }

    /// <summary>
    /// True when segments a-b and c-d share at least one point (touching and collinear overlap count).
    /// </summary>
    public static bool SegmentsIntersect(Point2 a, Point2 b, Point2 c, Point2 d)
    {
        double o1 = Orientation(a, b, c);
        double o2 = Orientation(a, b, d);
        double o3 = Orientation(c, d, a);
        double o4 = Orientation(c, d, b);

        // Proper crossing: each segment's endpoints lie strictly on opposite sides of the other.
        if (((o1 > Epsilon && o2 < -Epsilon) || (o1 < -Epsilon && o2 > Epsilon))
            && ((o3 > Epsilon && o4 < -Epsilon) || (o3 < -Epsilon && o4 > Epsilon)))
        {
            return true;
        }

        // Touching or collinear cases.
        return (Math.Abs(o1) <= Epsilon && OnSegment(a, b, c))
            || (Math.Abs(o2) <= Epsilon && OnSegment(a, b, d))
            || (Math.Abs(o3) <= Epsilon && OnSegment(c, d, a))
            || (Math.Abs(o4) <= Epsilon && OnSegment(c, d, b));
    }

    /// <summary>Twice the signed area of triangle a-b-c; positive when c is left of a→b.</summary>
    private static double Orientation(Point2 a, Point2 b, Point2 c) => Point2.Cross(b - a, c - a);

    /// <summary>True when <paramref name="p"/> (known collinear with a-b) lies within the segment's box.</summary>
    private static bool OnSegment(Point2 a, Point2 b, Point2 p) =>
        p.X >= Math.Min(a.X, b.X) - Epsilon && p.X <= Math.Max(a.X, b.X) + Epsilon
        && p.Y >= Math.Min(a.Y, b.Y) - Epsilon && p.Y <= Math.Max(a.Y, b.Y) + Epsilon;
}
