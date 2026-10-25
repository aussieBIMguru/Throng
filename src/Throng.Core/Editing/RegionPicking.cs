using Throng.Core.Geometry;
using Throng.Core.Model;

namespace Throng.Core.Editing;

/// <summary>
/// Plan hit testing for regions: click picking and window/crossing box selection (D-042).
/// </summary>
public static class RegionPicking
{
    /// <summary>True for regions whose outline is a closed ring (everything except walls).</summary>
    public static bool IsClosed(Region region) => region.Kind != RegionKind.Wall;

    /// <summary>
    /// Picks the region under <paramref name="point"/>. Edges within <paramref name="tolerance"/> win over interiors,
    /// and smaller regions win over larger ones, so a column inside a floor is picked before the floor.
    /// </summary>
    public static Region? Pick(IEnumerable<Region> regions, Point2 point, double tolerance)
    {
        Region? best = null;
        (int Rank, double Area) bestScore = (int.MaxValue, double.PositiveInfinity);

        foreach (Region region in regions)
        {
            if (region.Outline.Count < 2)
            {
                continue;
            }

            bool closed = IsClosed(region);
            double edgeDistance = SegmentMath.DistanceToOutline(region.Outline, point, closed);
            double capture = closed ? tolerance : Math.Max(tolerance, region.ThicknessM * 0.5);

            (int Rank, double Area) score;
            if (edgeDistance <= capture)
            {
                score = (0, closed ? PolygonMath.Area(region.Outline) : 0);
            }
            else if (closed && region.Outline.Count >= 3 && PolygonMath.Contains(region.Outline, point))
            {
                score = (1, PolygonMath.Area(region.Outline));
            }
            else
            {
                continue;
            }

            if (score.Rank < bestScore.Rank || (score.Rank == bestScore.Rank && score.Area < bestScore.Area))
            {
                best = region;
                bestScore = score;
            }
        }

        return best;
    }

    /// <summary>
    /// Pick score for a closed outline: rank 0 when within <paramref name="tolerance"/> of an edge, rank 1 when
    /// inside, null when missed. Area breaks ties so smaller shapes win.
    /// </summary>
    public static (int Rank, double Area)? Score(IReadOnlyList<Point2> outline, Point2 point, double tolerance)
    {
        if (outline.Count < 3)
        {
            return null;
        }

        if (SegmentMath.DistanceToOutline(outline, point, closed: true) <= tolerance)
        {
            return (0, PolygonMath.Area(outline));
        }

        return PolygonMath.Contains(outline, point) ? (1, PolygonMath.Area(outline)) : null;
    }

    /// <summary>Window test: every vertex of the region lies inside the box.</summary>
    public static bool IsInside(Region region, Bounds2 box) => IsInside(region.Outline, box);

    /// <summary>Window test for any outline: every vertex lies inside the box.</summary>
    public static bool IsInside(IReadOnlyList<Point2> outline, Bounds2 box) =>
        outline.Count > 0 && outline.All(box.Contains);

    /// <summary>
    /// Crossing test: the region touches the box at all (a vertex inside, an edge crossing the box boundary, or the
    /// box sitting entirely inside a closed region).
    /// </summary>
    public static bool Touches(Region region, Bounds2 box) => Touches(region.Outline, IsClosed(region), box);

    /// <summary>Crossing test for any outline (see <see cref="Touches(Region, Bounds2)"/>).</summary>
    public static bool Touches(IReadOnlyList<Point2> outline, bool closed, Bounds2 box)
    {
        if (outline.Count == 0 || box.IsEmpty)
        {
            return false;
        }

        if (outline.Any(box.Contains))
        {
            return true;
        }

        Point2[] corners = [new(box.MinX, box.MinY), new(box.MaxX, box.MinY), new(box.MaxX, box.MaxY), new(box.MinX, box.MaxY)];
        int n = outline.Count;
        int edges = closed ? n : n - 1;
        for (int i = 0; i < edges; i++)
        {
            Point2 a = outline[i];
            Point2 b = outline[(i + 1) % n];
            for (int k = 0; k < 4; k++)
            {
                if (SegmentMath.SegmentsIntersect(a, b, corners[k], corners[(k + 1) % 4]))
                {
                    return true;
                }
            }
        }

        return closed && n >= 3 && PolygonMath.Contains(outline, box.Center);
    }
}
