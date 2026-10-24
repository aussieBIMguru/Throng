namespace Throng.Core.Geometry;

/// <summary>
/// Builds plan outlines for the sketch tools. Every shape is an ordinary polygon wound counter-clockwise, so
/// there is only one shape type in the model (D-036).
/// </summary>
public static class ShapeFactory
{
    /// <summary>Maximum distance between a true circle and its polygon approximation, in metres (D-036).</summary>
    public const double CircleChordToleranceM = 0.05;

    /// <summary>Fewest sides a circle gets, however small.</summary>
    public const int MinCircleSegments = 16;

    /// <summary>Most sides a circle gets, however large (keeps huge drums cheap to edit and render).</summary>
    public const int MaxCircleSegments = 256;

    /// <summary>Axis-aligned rectangle with opposite corners <paramref name="a"/> and <paramref name="b"/>.</summary>
    public static List<Point2> Rectangle(Point2 a, Point2 b)
    {
        double x0 = Math.Min(a.X, b.X), x1 = Math.Max(a.X, b.X);
        double y0 = Math.Min(a.Y, b.Y), y1 = Math.Max(a.Y, b.Y);
        return [new(x0, y0), new(x1, y0), new(x1, y1), new(x0, y1)];
    }

    /// <summary>
    /// Number of sides for a circle of the given radius so the chord error stays within
    /// <see cref="CircleChordToleranceM"/>, clamped to [<see cref="MinCircleSegments"/>, <see cref="MaxCircleSegments"/>].
    /// </summary>
    public static int CircleSegmentCount(double radius)
    {
        if (!(radius > CircleChordToleranceM))
        {
            return MinCircleSegments;
        }

        // Sagitta s = r(1 − cos(θ/2)) ≤ tolerance  ⇒  θ ≤ 2·acos(1 − tolerance / r).
        double maxAngle = 2.0 * Math.Acos(1.0 - (CircleChordToleranceM / radius));
        int segments = (int)Math.Ceiling(2.0 * Math.PI / maxAngle);
        return Math.Clamp(segments, MinCircleSegments, MaxCircleSegments);
    }

    /// <summary>Counter-clockwise polygon approximating a circle; empty for a non-positive radius.</summary>
    public static List<Point2> Circle(Point2 center, double radius)
    {
        if (!(radius > 0))
        {
            return [];
        }

        int n = CircleSegmentCount(radius);
        var points = new List<Point2>(n);
        for (int i = 0; i < n; i++)
        {
            double angle = 2.0 * Math.PI * i / n;
            points.Add(new Point2(center.X + (radius * Math.Cos(angle)), center.Y + (radius * Math.Sin(angle))));
        }

        return points;
    }

    /// <summary>Copy of an outline moved by <paramref name="offset"/>.</summary>
    public static List<Point2> Translate(IEnumerable<Point2> outline, Point2 offset) => [.. outline.Select(p => p + offset)];
}
