namespace Throng.Core.Geometry;

/// <summary>
/// Small, dependency-free polygon utilities: area, winding, containment and triangulation.
/// Boolean and offset operations live behind <see cref="IPolygonOps"/> (Clipper2-backed from M1).
/// </summary>
public static class PolygonMath
{
    private const double Epsilon = 1e-9;

    /// <summary>Signed area (shoelace). Positive for counter-clockwise winding.</summary>
    public static double SignedArea(IReadOnlyList<Point2> polygon)
    {
        int n = polygon.Count;
        if (n < 3)
        {
            return 0;
        }

        double sum = 0;
        for (int i = 0; i < n; i++)
        {
            Point2 a = polygon[i];
            Point2 b = polygon[(i + 1) % n];
            sum += (a.X * b.Y) - (b.X * a.Y);
        }

        return sum * 0.5;
    }

    /// <summary>Unsigned area in square metres.</summary>
    public static double Area(IReadOnlyList<Point2> polygon) => Math.Abs(SignedArea(polygon));

    /// <summary>True when the polygon winds counter-clockwise.</summary>
    public static bool IsCounterClockwise(IReadOnlyList<Point2> polygon) => SignedArea(polygon) > 0;

    /// <summary>Area centroid; falls back to the vertex average for degenerate polygons.</summary>
    public static Point2 Centroid(IReadOnlyList<Point2> polygon)
    {
        int n = polygon.Count;
        if (n == 0)
        {
            return Point2.Zero;
        }

        double area = SignedArea(polygon);
        if (Math.Abs(area) < Epsilon)
        {
            double sx = 0, sy = 0;
            foreach (Point2 p in polygon)
            {
                sx += p.X;
                sy += p.Y;
            }

            return new Point2(sx / n, sy / n);
        }

        double cx = 0, cy = 0;
        for (int i = 0; i < n; i++)
        {
            Point2 a = polygon[i];
            Point2 b = polygon[(i + 1) % n];
            double cross = (a.X * b.Y) - (b.X * a.Y);
            cx += (a.X + b.X) * cross;
            cy += (a.Y + b.Y) * cross;
        }

        double factor = 1.0 / (6.0 * area);
        return new Point2(cx * factor, cy * factor);
    }

    /// <summary>Even-odd point-in-polygon test. Points exactly on an edge may report either way.</summary>
    public static bool Contains(IReadOnlyList<Point2> polygon, Point2 point)
    {
        bool inside = false;
        int n = polygon.Count;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            Point2 a = polygon[i];
            Point2 b = polygon[j];
            bool crosses = (a.Y > point.Y) != (b.Y > point.Y);
            if (crosses && point.X < ((b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y)) + a.X)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    /// <summary>
    /// Triangulates a simple polygon (no holes, no self-intersections) by ear clipping.
    /// Works for either winding. Returns index triples into <paramref name="polygon"/>, wound counter-clockwise.
    /// Degenerate or self-intersecting input falls back to a fan for whatever remains, so the call never throws.
    /// </summary>
    public static IReadOnlyList<int> Triangulate(IReadOnlyList<Point2> polygon)
    {
        int n = polygon.Count;
        var triangles = new List<int>(Math.Max(0, (n - 2) * 3));
        if (n < 3)
        {
            return triangles;
        }

        // Work on a CCW-ordered index list so "convex" always means a positive cross product.
        bool ccw = SignedArea(polygon) >= 0;
        var remaining = new List<int>(n);
        for (int i = 0; i < n; i++)
        {
            remaining.Add(ccw ? i : n - 1 - i);
        }

        int guard = n * n + 8;
        while (remaining.Count > 3 && guard-- > 0)
        {
            bool clipped = false;
            int count = remaining.Count;
            for (int i = 0; i < count; i++)
            {
                int ia = remaining[(i + count - 1) % count];
                int ib = remaining[i];
                int ic = remaining[(i + 1) % count];
                Point2 a = polygon[ia], b = polygon[ib], c = polygon[ic];

                double cross = Point2.Cross(b - a, c - b);
                if (Math.Abs(cross) < Epsilon)
                {
                    // Collinear vertex contributes no area: drop it without emitting a triangle.
                    remaining.RemoveAt(i);
                    clipped = true;
                    break;
                }

                if (cross < 0 || ContainsAnyOtherVertex(polygon, remaining, ia, ib, ic))
                {
                    continue;
                }

                triangles.Add(ia);
                triangles.Add(ib);
                triangles.Add(ic);
                remaining.RemoveAt(i);
                clipped = true;
                break;
            }

            if (!clipped)
            {
                break;
            }
        }

        // Emit what is left: a final triangle, or a fan if clipping stalled on bad input.
        for (int i = 1; i + 1 < remaining.Count; i++)
        {
            Point2 a = polygon[remaining[0]], b = polygon[remaining[i]], c = polygon[remaining[i + 1]];
            if (Math.Abs(Point2.Cross(b - a, c - b)) < Epsilon)
            {
                continue;
            }

            triangles.Add(remaining[0]);
            triangles.Add(remaining[i]);
            triangles.Add(remaining[i + 1]);
        }

        return triangles;
    }

    /// <summary>Bounds of a polygon.</summary>
    public static Bounds2 GetBounds(IReadOnlyList<Point2> polygon) => Bounds2.FromPoints(polygon);

    /// <summary>True if any vertex other than the ear's own lies inside or on the triangle a-b-c.</summary>
    private static bool ContainsAnyOtherVertex(IReadOnlyList<Point2> polygon, List<int> remaining, int ia, int ib, int ic)
    {
        Point2 a = polygon[ia], b = polygon[ib], c = polygon[ic];
        foreach (int index in remaining)
        {
            if (index == ia || index == ib || index == ic)
            {
                continue;
            }

            Point2 p = polygon[index];
            if (p == a || p == b || p == c)
            {
                continue;
            }

            if (PointInTriangle(p, a, b, c))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Inclusive point-in-triangle test for a CCW triangle.</summary>
    private static bool PointInTriangle(Point2 p, Point2 a, Point2 b, Point2 c)
    {
        double d1 = Point2.Cross(b - a, p - a);
        double d2 = Point2.Cross(c - b, p - b);
        double d3 = Point2.Cross(a - c, p - c);
        return d1 >= -Epsilon && d2 >= -Epsilon && d3 >= -Epsilon;
    }
}
