using Throng.Core.Common;
using Throng.Core.Geometry;
using Throng.Core.Model;

namespace Throng.Core.World;

/// <summary>
/// Computes a level's net walkable area (D-039): union(walkable) − union(obstacles + thickened walls).
/// Regions stay separate in the model; this is derived on demand and never stored.
/// </summary>
public static class WalkableArea
{
    /// <summary>
    /// Net walkable polygons for a level: outers counter-clockwise, holes clockwise. Fails (never throws) if the
    /// polygon library rejects the input.
    /// </summary>
    public static Result<IReadOnlyList<IReadOnlyList<Point2>>> Compute(Study study, Guid levelId, IPolygonOps ops)
    {
        try
        {
            var walkable = new List<IReadOnlyList<Point2>>();
            var blocked = new List<IReadOnlyList<Point2>>();
            foreach (Region region in study.RegionsOn(levelId))
            {
                switch (region.Kind)
                {
                    case RegionKind.Walkable when region.Outline.Count >= 3:
                        walkable.Add(region.Outline);
                        break;
                    case RegionKind.Obstacle when region.Outline.Count >= 3:
                        blocked.Add(region.Outline);
                        break;
                    case RegionKind.Wall when region.Outline.Count >= 2:
                        blocked.AddRange(ops.ThickenPolyline(region.Outline, region.ThicknessM));
                        break;
                }
            }

            IReadOnlyList<IReadOnlyList<Point2>> net = walkable.Count == 0 ? [] : ops.Difference(walkable, blocked);
            return Result<IReadOnlyList<IReadOnlyList<Point2>>>.Ok(net);
        }
        catch (Exception ex)
        {
            // Boundary around the third-party polygon library (which has its own exception types, e.g. for
            // coordinates out of range): report instead of throwing into the UI.
            return Result<IReadOnlyList<IReadOnlyList<Point2>>>.Fail($"Walkable area couldn't be computed: {ex.Message}");
        }
    }

    /// <summary>Net area in square metres of polygons from <see cref="Compute"/> (holes subtract).</summary>
    public static double NetAreaM2(IReadOnlyList<IReadOnlyList<Point2>> polygons)
    {
        double area = 0;
        foreach (IReadOnlyList<Point2> polygon in polygons)
        {
            area += PolygonMath.SignedArea(polygon);
        }

        return Math.Max(0, area);
    }
}
