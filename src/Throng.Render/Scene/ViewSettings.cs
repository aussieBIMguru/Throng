using Throng.Core.Geometry;

namespace Throng.Render.Scene;

/// <summary>
/// Cutaway and exploded-level settings for the 3D and ISO views (round 015, D-264–D-266), session only (never saved).
/// The section box is given as shares of the scene's extents (0 = west / south / bottom edge, 1 = east / north / top),
/// so it stays sensible when the study changes. Plan view ignores all of it.
/// </summary>
public sealed record ViewSettings
{
    /// <summary>Nothing cut, nothing exploded.</summary>
    public static ViewSettings None { get; } = new();

    /// <summary>Extra height between consecutive levels, metres (0 = true elevation).</summary>
    public double ExplodeGapM { get; init; }

    /// <summary>Everything above this height is cut away (clip above a level), or null for no clip.</summary>
    public double? ClipAboveZ { get; init; }

    /// <summary>True when the section box cuts the view.</summary>
    public bool SectionBox { get; init; }

    /// <summary>Section box west edge, share of the scene's X extent.</summary>
    public double BoxWest { get; init; }

    /// <summary>Section box east edge, share of the scene's X extent.</summary>
    public double BoxEast { get; init; } = 1;

    /// <summary>Section box south edge, share of the scene's Y extent.</summary>
    public double BoxSouth { get; init; }

    /// <summary>Section box north edge, share of the scene's Y extent.</summary>
    public double BoxNorth { get; init; } = 1;

    /// <summary>Section box bottom, share of the scene's height range.</summary>
    public double BoxBottom { get; init; }

    /// <summary>Section box top, share of the scene's height range.</summary>
    public double BoxTop { get; init; } = 1;

    /// <summary>True when anything is clipped.</summary>
    public bool Clips => ClipAboveZ is not null || SectionBox;

    /// <summary>Margin added round the scene's extents before the box shares apply, metres.</summary>
    public const double BoxMarginM = 1.0;

    /// <summary>
    /// The world box everything outside is cut from: the section box over the scene's extents (plus a margin), capped
    /// at <see cref="ClipAboveZ"/>. Unbounded sides are ±1e6 m. Heights are true elevations, before any explode.
    /// </summary>
    public (Point3 Min, Point3 Max) ClipBox(Bounds2 bounds, double minZ, double maxZ)
    {
        const double far = 1e6;
        double x0 = -far, x1 = far, y0 = -far, y1 = far, z0 = -far, z1 = far;
        if (SectionBox && !bounds.IsEmpty)
        {
            (Point3 lo, Point3 hi) = Extents(bounds, minZ, maxZ);
            double bx0 = lo.X, bx1 = hi.X, by0 = lo.Y, by1 = hi.Y, bz0 = lo.Z, bz1 = hi.Z;
            x0 = Lerp(bx0, bx1, Math.Min(BoxWest, BoxEast));
            x1 = Lerp(bx0, bx1, Math.Max(BoxWest, BoxEast));
            y0 = Lerp(by0, by1, Math.Min(BoxSouth, BoxNorth));
            y1 = Lerp(by0, by1, Math.Max(BoxSouth, BoxNorth));
            z0 = Lerp(bz0, bz1, Math.Min(BoxBottom, BoxTop));
            z1 = Lerp(bz0, bz1, Math.Max(BoxBottom, BoxTop));
        }

        if (ClipAboveZ is { } clip)
        {
            z1 = Math.Min(z1, clip);
        }

        return (new Point3(x0, y0, z0), new Point3(x1, y1, z1));
    }

    /// <summary>
    /// What the box shares are shares of (D-277): the scene's extents plus <see cref="BoxMarginM"/> on every side, in
    /// true heights.
    /// </summary>
    public static (Point3 Min, Point3 Max) Extents(Bounds2 bounds, double minZ, double maxZ) =>
        (new Point3(bounds.MinX - BoxMarginM, bounds.MinY - BoxMarginM, minZ - BoxMarginM),
         new Point3(bounds.MaxX + BoxMarginM, bounds.MaxY + BoxMarginM, maxZ + BoxMarginM));

    private static double Lerp(double a, double b, double t) => a + ((b - a) * Math.Clamp(t, 0, 1));
}
