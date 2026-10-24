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

    /// <summary>Window test: every vertex of the region lies inside the box.</summary>
    public static bool IsInside(Region region, Bounds2 box) =>
        region.Outline.Count > 0 && region.Outline.All(box.Contains);

    /// <summary>
    /// Crossing test: the region touches the box at all (a vertex inside, an edge crossing the box boundary, or the
    /// box sitting entirely inside a closed region).
    /// </summary>
    public static bool Touches(Region region, Bounds2 box)
    {
        List<Point2> outline = region.Outline;
        if (outline.Count == 0 || box.IsEmpty)
        {
            return false;
        }

        if (outline.Any(box.Contains))
        {
            return true;
        }

        Point2[] corners = [new(box.MinX, box.MinY), new(box.MaxX, box.MinY), new(box.MaxX, box.MaxY), new(box.MinX, box.MaxY)];
        bool closed = IsClosed(region);
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
