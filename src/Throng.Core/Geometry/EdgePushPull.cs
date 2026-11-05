using Throng.Core.Common;

namespace Throng.Core.Geometry;

/// <summary>
/// Push/pull of one edge along its normal (D-047, D-048). The neighbouring edges keep their direction: each moved
/// end slides along its neighbour's line, so rectangles stay rectangles and chamfers keep their angle. When a
/// neighbour is parallel to the edge, or there is no neighbour (the ends of an open wall), that end moves straight
/// along the normal instead.
/// </summary>
public static class EdgePushPull
{
    /// <summary>Reason shown when an end would slide past its neighbour's far vertex.</summary>
    public const string TooFar = "EDGE TOO FAR";

    private const double ParallelTolerance = 1e-9;

    /// <summary>
    /// Unit normal of edge <paramref name="edgeIndex"/> (from vertex i to i+1). Closed outlines get the outward
    /// normal whatever their winding; open outlines (walls) get the left-hand normal.
    /// </summary>
    public static Point2 Normal(IReadOnlyList<Point2> outline, bool closed, int edgeIndex)
    {
        int n = outline.Count;
        Point2 a = outline[edgeIndex];
        Point2 b = outline[(edgeIndex + 1) % n];
        Point2 left = (b - a).Perpendicular().Normalised();

        // For a counter-clockwise ring the left side is inside, so outward is the right side.
        return closed && PolygonMath.IsCounterClockwise(outline) ? left * -1 : left;
    }

    /// <summary>
    /// Returns a copy of the outline with edge <paramref name="edgeIndex"/> moved <paramref name="distance"/> metres
    /// along <see cref="Normal"/> (positive = outward for closed shapes). Fails when an end would slide past the
    /// far end of its neighbouring edge.
    /// </summary>
    public static Result<List<Point2>> Apply(IReadOnlyList<Point2> outline, bool closed, int edgeIndex, double distance)
    {
        int n = outline.Count;
        int edges = closed ? n : n - 1;
        if (n < 2 || edgeIndex < 0 || edgeIndex >= edges)
        {
            return Result<List<Point2>>.Fail("NO SUCH EDGE");
        }

        int ia = edgeIndex;
        int ib = (edgeIndex + 1) % n;
        Point2 a = outline[ia];
        Point2 b = outline[ib];
        Point2 shift = Normal(outline, closed, edgeIndex) * distance;
        Point2 edgeDirection = b - a;

        // Neighbour vertices: the one before a, and the one after b (none at the ends of an open polyline).
        int? before = closed ? (ia - 1 + n) % n : ia > 0 ? ia - 1 : (int?)null;
        int? after = closed ? (ib + 1) % n : ib < n - 1 ? ib + 1 : (int?)null;

        // A triangle's two "neighbours" share the third vertex; that still works, as each end slides on its own line.
        Point2 newA = SlideEnd(a, shift, edgeDirection, before is int p ? outline[p] : (Point2?)null);
        Point2 newB = SlideEnd(b, shift, edgeDirection, after is int q ? outline[q] : (Point2?)null);

        if ((before is int pb && Reversed(outline[pb], a, newA)) || (after is int qa && Reversed(outline[qa], b, newB)))
        {
            return Result<List<Point2>>.Fail(TooFar);
        }

        List<Point2> result = [.. outline];
        result[ia] = newA;
        result[ib] = newB;
        return Result<List<Point2>>.Ok(result);
    }

    /// <summary>
    /// New position of an edge end: where the shifted edge line meets the neighbour's line through
    /// <paramref name="end"/>, or a straight shift when there's no usable neighbour.
    /// </summary>
    private static Point2 SlideEnd(Point2 end, Point2 shift, Point2 edgeDirection, Point2? neighbour)
    {
        if (neighbour is not { } far)
        {
            return end + shift;
        }

        Point2 neighbourDirection = end - far;
        double denominator = Point2.Cross(edgeDirection, neighbourDirection);
        if (Math.Abs(denominator) <= ParallelTolerance * edgeDirection.Length * neighbourDirection.Length)
        {
            return end + shift;
        }

        // Solve (end + shift) + t·edge = far + s·neighbour for t, then step along the shifted edge line.
        Point2 start = end + shift;
        double t = Point2.Cross(far - start, neighbourDirection) / denominator;
        return start + (edgeDirection * t);
    }

    /// <summary>True when the moved end has crossed to the other side of the neighbour's far vertex.</summary>
    private static bool Reversed(Point2 far, Point2 original, Point2 moved) =>
        Point2.Dot(moved - far, original - far) <= 0;
}
