using Throng.Core.Common;

namespace Throng.Core.Geometry;

/// <summary>
/// Cleans and checks sketched outlines before they enter the model (D-040). Duplicate and collinear points are
/// removed silently; shapes that are still invalid are rejected with a short HUD-style reason.
/// </summary>
public static class ShapeValidator
{
    /// <summary>Points closer than this are treated as the same point (0.1 mm).</summary>
    public const double PointToleranceM = 1e-4;

    /// <summary>Smallest closed shape accepted, in square metres.</summary>
    public const double MinAreaM2 = 0.01;

    /// <summary>Reason shown when edges cross or touch.</summary>
    public const string EdgesCross = "EDGES CROSS";

    /// <summary>Reason shown when a closed shape has too few distinct points.</summary>
    public const string NeedThreePoints = "NEED 3 POINTS";

    /// <summary>Reason shown when an open shape has too few distinct points.</summary>
    public const string NeedTwoPoints = "NEED 2 POINTS";

    /// <summary>Reason shown for a sliver or zero-area shape.</summary>
    public const string NoArea = "NO AREA";

    /// <summary>
    /// Returns a cleaned copy: consecutive duplicates removed (including a closing point equal to the first),
    /// then collinear and spike vertices removed. Open shapes keep their end points.
    /// </summary>
    public static List<Point2> Clean(IReadOnlyList<Point2> points, bool closed)
    {
        var result = new List<Point2>(points.Count);
        foreach (Point2 p in points)
        {
            if (result.Count == 0 || Point2.Distance(result[^1], p) > PointToleranceM)
            {
                result.Add(p);
            }
        }

        if (closed)
        {
            while (result.Count > 1 && Point2.Distance(result[0], result[^1]) <= PointToleranceM)
            {
                result.RemoveAt(result.Count - 1);
            }
        }

        RemoveCollinear(result, closed);
        return result;
    }

    /// <summary>
    /// Cleans the outline and checks it. Success carries the cleaned outline; failure carries the reason.
    /// </summary>
    public static Result<List<Point2>> Validate(IReadOnlyList<Point2> points, bool closed)
    {
        List<Point2> cleaned = Clean(points, closed);
        if (!closed)
        {
            return cleaned.Count >= 2 ? Result<List<Point2>>.Ok(cleaned) : Result<List<Point2>>.Fail(NeedTwoPoints);
        }

        if (cleaned.Count < 3)
        {
            return Result<List<Point2>>.Fail(NeedThreePoints);
        }

        if (HasSelfIntersection(cleaned, closed: true))
        {
            return Result<List<Point2>>.Fail(EdgesCross);
        }

        return PolygonMath.Area(cleaned) < MinAreaM2
            ? Result<List<Point2>>.Fail(NoArea)
            : Result<List<Point2>>.Ok(cleaned);
    }

    /// <summary>
    /// True when any two non-adjacent edges cross or touch. Adjacent edges share a vertex by construction and are
    /// skipped. O(n²), which is fine for hand-sketched outlines.
    /// </summary>
    public static bool HasSelfIntersection(IReadOnlyList<Point2> points, bool closed)
    {
        int n = points.Count;
        int edges = closed ? n : n - 1;
        if (edges < 3)
        {
            return false;
        }

        for (int i = 0; i < edges; i++)
        {
            Point2 a = points[i];
            Point2 b = points[(i + 1) % n];
            for (int j = i + 2; j < edges; j++)
            {
                // In a closed ring the last edge is adjacent to the first.
                if (closed && i == 0 && j == edges - 1)
                {
                    continue;
                }

                if (SegmentMath.SegmentsIntersect(a, b, points[j], points[(j + 1) % n]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Removes vertices that sit on the straight line between their neighbours, and zero-width spikes where the
    /// outline doubles back. Repeats until stable.
    /// </summary>
    private static void RemoveCollinear(List<Point2> points, bool closed)
    {
        bool changed = true;
        while (changed && points.Count >= 3)
        {
            changed = false;
            int first = closed ? 0 : 1;
            int last = closed ? points.Count - 1 : points.Count - 2;
            for (int i = first; i <= last; i++)
            {
                int count = points.Count;
                Point2 a = points[(i - 1 + count) % count];
                Point2 b = points[i];
                Point2 c = points[(i + 1) % count];
                if (SegmentMath.DistanceToSegment(b, a, c) <= PointToleranceM
                    || IsSpike(a, b, c))
                {
                    points.RemoveAt(i);
                    changed = true;
                    break;
                }
            }

            if (closed && points.Count < 3)
            {
                break;
            }
        }
    }

    /// <summary>True when b lies on the infinite line a-c but outside the segment (the outline reverses at b).</summary>
    private static bool IsSpike(Point2 a, Point2 b, Point2 c)
    {
        Point2 ab = b - a;
        Point2 bc = c - b;
        double lengths = ab.Length * bc.Length;
        if (lengths < 1e-12)
        {
            return true;
        }

        // Collinear (sine of the turn angle ~0) and pointing back on itself (cosine negative).
        return Math.Abs(Point2.Cross(ab, bc)) / lengths < 1e-9 && Point2.Dot(ab, bc) < 0;
    }
}
