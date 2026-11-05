using System.Numerics;
using Throng.Core.Geometry;

namespace Throng.Render.Cameras;

/// <summary>
/// Orthographic top-down camera for plan editing (§12.1). Screen right is +X (east), screen up is +Y (north).
/// Sizes are in device-independent pixels (DIPs) so behaviour matches the WPF mouse coordinates.
/// </summary>
public sealed class PlanCamera
{
    private const double MinMetresPerDip = 0.002;
    private const double MaxMetresPerDip = 20.0;

    /// <summary>Plan point at the centre of the view.</summary>
    public Point2 Center { get; set; } = Point2.Zero;

    /// <summary>Zoom: metres per device-independent pixel.</summary>
    public double MetresPerDip { get; set; } = 0.08;

    /// <summary>Elevation of the level being viewed; the camera sits above it.</summary>
    public double ElevationM { get; set; }

    /// <summary>Combined view-projection matrix (System.Numerics row-vector convention).</summary>
    public Matrix4x4 ViewProjection(double widthDip, double heightDip)
    {
        float cx = (float)Center.X, cy = (float)Center.Y, z = (float)ElevationM;
        Matrix4x4 view = Matrix4x4.CreateLookAt(new Vector3(cx, cy, z + 500f), new Vector3(cx, cy, z), Vector3.UnitY);
        Matrix4x4 projection = Matrix4x4.CreateOrthographic(
            (float)(Math.Max(1, widthDip) * MetresPerDip),
            (float)(Math.Max(1, heightDip) * MetresPerDip),
            1f,
            2000f);
        return view * projection;
    }

    /// <summary>Plan point under a screen position (DIPs, origin top-left).</summary>
    public Point2 ScreenToPlan(double x, double y, double widthDip, double heightDip) =>
        new(Center.X + ((x - (widthDip * 0.5)) * MetresPerDip), Center.Y - ((y - (heightDip * 0.5)) * MetresPerDip));

    /// <summary>Screen position (DIPs, origin top-left) of a plan point; the inverse of <see cref="ScreenToPlan"/>.</summary>
    public (double X, double Y) PlanToScreen(Point2 p, double widthDip, double heightDip) =>
        (((p.X - Center.X) / MetresPerDip) + (widthDip * 0.5), ((Center.Y - p.Y) / MetresPerDip) + (heightDip * 0.5));

    /// <summary>Moves the view by a screen drag in DIPs (content follows the cursor).</summary>
    public void Pan(double dxDip, double dyDip) =>
        Center = new Point2(Center.X - (dxDip * MetresPerDip), Center.Y + (dyDip * MetresPerDip));

    /// <summary>Zooms by <paramref name="factor"/> keeping the point under the cursor fixed.</summary>
    public void ZoomAt(double x, double y, double widthDip, double heightDip, double factor)
    {
        Point2 before = ScreenToPlan(x, y, widthDip, heightDip);
        MetresPerDip = Math.Clamp(MetresPerDip * factor, MinMetresPerDip, MaxMetresPerDip);
        Point2 after = ScreenToPlan(x, y, widthDip, heightDip);
        Center += before - after;
    }

    /// <summary>Frames the given bounds with a margin.</summary>
    public void Fit(Bounds2 bounds, double widthDip, double heightDip)
    {
        if (bounds.IsEmpty)
        {
            Center = Point2.Zero;
            MetresPerDip = 0.08;
            return;
        }

        Center = bounds.Center;
        double fit = Math.Max(bounds.Width / Math.Max(1, widthDip), bounds.Height / Math.Max(1, heightDip));
        MetresPerDip = Math.Clamp(fit * 1.15, MinMetresPerDip, MaxMetresPerDip);
    }
}
