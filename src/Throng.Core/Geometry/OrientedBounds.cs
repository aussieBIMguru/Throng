namespace Throng.Core.Geometry;

/// <summary>
/// The smallest-area rectangle around a polygon at any rotation (round 012, D-228). Measurement zones use its shorter
/// side as the width people cross, for flow per metre.
/// </summary>
/// <param name="LongSideM">Length of the longer side, metres.</param>
/// <param name="ShortSideM">Length of the shorter side, metres.</param>
/// <param name="AngleRad">Direction of the longer side, radians counter-clockwise from +X.</param>
public readonly record struct OrientedBounds(double LongSideM, double ShortSideM, double AngleRad)
{
    /// <summary>An empty result (fewer than two distinct points).</summary>
    public static OrientedBounds Empty => new(0, 0, 0);

    /// <summary>
    /// The minimum-area bounding rectangle of <paramref name="points"/>. One side of that rectangle always lies along
    /// an edge of the convex hull, so each hull edge direction is tried in turn.
    /// </summary>
    public static OrientedBounds Of(IReadOnlyList<Point2> points)
    {
        List<Point2> hull = ConvexHull(points);
        if (hull.Count < 2)
        {
            return Empty;
        }

        double bestArea = double.PositiveInfinity;
        OrientedBounds best = Empty;
        for (int e = 0; e < hull.Count; e++)
        {
            Point2 edge = hull[(e + 1) % hull.Count] - hull[e];
            double length = edge.Length;
            if (length < 1e-9)
            {
                continue;
            }

            var u = new Point2(edge.X / length, edge.Y / length);
            var v = new Point2(-u.Y, u.X);
            double minU = double.PositiveInfinity, maxU = double.NegativeInfinity;
            double minV = double.PositiveInfinity, maxV = double.NegativeInfinity;
            foreach (Point2 p in hull)
            {
                double pu = Point2.Dot(p, u), pv = Point2.Dot(p, v);
                minU = Math.Min(minU, pu);
                maxU = Math.Max(maxU, pu);
                minV = Math.Min(minV, pv);
                maxV = Math.Max(maxV, pv);
            }

            double w = maxU - minU, h = maxV - minV;
            double area = w * h;
            if (area < bestArea - 1e-12)
            {
                bestArea = area;
                double angle = Math.Atan2(u.Y, u.X);
                best = w >= h ? new OrientedBounds(w, h, angle) : new OrientedBounds(h, w, angle + (Math.PI / 2));
            }
        }

        return best;
    }

    /// <summary>Convex hull by the monotone chain, counter-clockwise, without repeated or collinear points.</summary>
    private static List<Point2> ConvexHull(IReadOnlyList<Point2> points)
    {
        var sorted = new List<Point2>(points);
        sorted.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));
        if (sorted.Count < 3)
        {
            return sorted.Count == 2 && sorted[0] == sorted[1] ? [sorted[0]] : sorted;
        }

        var hull = new List<Point2>(sorted.Count * 2);
        foreach (Point2 p in sorted)
        {
            while (hull.Count >= 2 && Cross(hull[^2], hull[^1], p) <= 0)
            {
                hull.RemoveAt(hull.Count - 1);
            }

            hull.Add(p);
        }

        int lower = hull.Count + 1;
        for (int k = sorted.Count - 2; k >= 0; k--)
        {
            Point2 p = sorted[k];
            while (hull.Count >= lower && Cross(hull[^2], hull[^1], p) <= 0)
            {
                hull.RemoveAt(hull.Count - 1);
            }

            hull.Add(p);
        }

        hull.RemoveAt(hull.Count - 1);
        return hull;
    }

    private static double Cross(Point2 o, Point2 a, Point2 b) => ((a.X - o.X) * (b.Y - o.Y)) - ((a.Y - o.Y) * (b.X - o.X));
}
