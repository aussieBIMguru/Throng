using Throng.Core.Model;

namespace Throng.Core.Geometry;

/// <summary>Plan geometry of a placed <see cref="Device"/> (D-089).</summary>
public static class DeviceGeometry
{
    /// <summary>Thickness of the dividers between pass-through lanes, in metres.</summary>
    public const double DividerM = 0.1;

    /// <summary>Counter-clockwise footprint rectangle.</summary>
    public static Point2[] Footprint(Device device) => Footprint(device.Position, device.Facing(), device.WidthM, device.DepthM);

    /// <summary>Counter-clockwise rectangle centred on <paramref name="centre"/>, depth along <paramref name="facing"/>.</summary>
    public static Point2[] Footprint(Point2 centre, Point2 facing, double width, double depth)
    {
        Point2 across = facing.Perpendicular() * (width * 0.5);
        Point2 along = facing * (depth * 0.5);
        return [centre - along - across, centre + along - across, centre + along + across, centre - along + across];
    }

    /// <summary>Centre of the service (front) edge.</summary>
    public static Point2 ServicePoint(Device device) => device.Position + (device.Facing() * (device.DepthM * 0.5));

    /// <summary>
    /// What the device removes from the walkable area: the whole footprint, or for pass-through devices only the
    /// dividers either side of and between their lanes, so people can walk through the lanes.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<Point2>> Blocking(Device device)
    {
        if (!device.PassThrough)
        {
            return [Footprint(device)];
        }

        int lanes = Math.Max(1, device.Lanes);
        Point2 facing = device.Facing();
        Point2 left = facing.Perpendicular();
        double half = device.WidthM * 0.5;
        var dividers = new List<IReadOnlyList<Point2>>(lanes + 1);
        for (int i = 0; i <= lanes; i++)
        {
            // Outer dividers sit inside the footprint edge; inner ones are centred on the lane boundaries.
            double t = -half + (device.WidthM * i / lanes);
            t = Math.Clamp(t, -half + (DividerM * 0.5), half - (DividerM * 0.5));
            dividers.Add(Footprint(device.Position + (left * t), facing, DividerM, device.DepthM));
        }

        return dividers;
    }

    /// <summary>Lane boundary lines across the footprint (for the plan symbol), excluding the outer edges.</summary>
    public static IEnumerable<(Point2 A, Point2 B)> LaneLines(Device device)
    {
        int lanes = Math.Max(1, device.Lanes);
        Point2 facing = device.Facing();
        Point2 left = facing.Perpendicular();
        Point2 along = facing * (device.DepthM * 0.5);
        for (int i = 1; i < lanes; i++)
        {
            double t = (-device.WidthM * 0.5) + (device.WidthM * i / lanes);
            Point2 c = device.Position + (left * t);
            yield return (c - along, c + along);
        }
    }
}

/// <summary>Plan placement maths for <see cref="Underlay"/> images (D-084).</summary>
public static class UnderlayGeometry
{
    /// <summary>
    /// Corners in plan: bottom-left, bottom-right, top-right, top-left of the image (counter-clockwise), matching
    /// image UVs (0,1), (1,1), (1,0), (0,0).
    /// </summary>
    public static Point2[] Corners(Underlay underlay)
    {
        double w = underlay.PixelWidth * underlay.MetresPerPixel * 0.5;
        double h = underlay.PixelHeight * underlay.MetresPerPixel * 0.5;
        double r = underlay.AngleDeg * Math.PI / 180.0;
        var u = new Point2(Math.Cos(r), Math.Sin(r));
        Point2 v = u.Perpendicular();
        Point2 c = underlay.Centre;
        return [c - (u * w) - (v * h), c + (u * w) - (v * h), c + (u * w) + (v * h), c - (u * w) + (v * h)];
    }

    /// <summary>Plan width and height of the image in metres.</summary>
    public static (double Width, double Height) Size(Underlay underlay) =>
        (underlay.PixelWidth * underlay.MetresPerPixel, underlay.PixelHeight * underlay.MetresPerPixel);

    /// <summary>
    /// Reference-distance calibration (D-084): the image is scaled about <paramref name="a"/> so the plan distance
    /// between <paramref name="a"/> and <paramref name="b"/> becomes <paramref name="realDistanceM"/>. Returns the
    /// new state, or null when the two points coincide or the distance isn't positive.
    /// </summary>
    public static Underlay? Calibrated(Underlay underlay, Point2 a, Point2 b, double realDistanceM)
    {
        double current = Point2.Distance(a, b);
        if (current < 1e-9 || !(realDistanceM > 0))
        {
            return null;
        }

        double k = realDistanceM / current;
        Underlay next = underlay.Clone();
        next.MetresPerPixel = underlay.MetresPerPixel * k;
        next.Centre = a + ((underlay.Centre - a) * k);
        return next;
    }
}
