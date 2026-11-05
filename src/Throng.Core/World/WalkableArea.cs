using Throng.Core.Common;
using Throng.Core.Geometry;
using Throng.Core.Model;

namespace Throng.Core.World;

/// <summary>
/// Computes a level's net walkable area (D-039): union(walkable) − union(obstacles + thickened walls + vertical
/// cuts + devices). Obstacles and walls spanning up from lower levels count too (D-076); devices block their
/// footprint, or just their lane dividers when pass-through (D-089). Vertical cuts are connector footprints and openings and the shafts passing through the level
/// (D-055, D-056, see <see cref="VerticalCuts"/>). Regions stay separate in the model; this is derived on demand
/// and never stored.
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
                if (region.Kind == RegionKind.Walkable && region.Outline.Count >= 3)
                {
                    walkable.Add(region.Outline);
                }
            }

            // Obstacles and walls drawn here or spanning onto this level (D-076, D-087).
            foreach (Region region in study.RegionsPresentOn(levelId))
            {
                switch (region.Kind)
                {
                    case RegionKind.Obstacle when region.Outline.Count >= 3:
                        blocked.Add(region.Outline);
                        break;
                    case RegionKind.Wall when region.Outline.Count >= 2:
                        blocked.AddRange(ops.ThickenPolyline(region.Outline, region.ThicknessM));
                        break;
                }
            }

            blocked.AddRange(VerticalCuts.PolygonsForLevel(study, levelId, ops));
            foreach (Device device in study.DevicesOn(levelId))
            {
                blocked.AddRange(DeviceGeometry.Blocking(device));
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

    /// <summary>
    /// True when <paramref name="point"/> is inside polygons from <see cref="Compute"/>: inside an outer
    /// (counter-clockwise) ring more often than inside a hole (clockwise) ring.
    /// </summary>
    public static bool Contains(IReadOnlyList<IReadOnlyList<Point2>> polygons, Point2 point)
    {
        int winding = 0;
        foreach (IReadOnlyList<Point2> polygon in polygons)
        {
            if (polygon.Count >= 3 && PolygonMath.Contains(polygon, point))
            {
                winding += PolygonMath.SignedArea(polygon) >= 0 ? 1 : -1;
            }
        }

        return winding > 0;
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
