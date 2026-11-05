namespace Throng.Core.Geometry;

/// <summary>
/// Axis-aligned 2D bounding box in metres. Use <see cref="Empty"/> as the seed when accumulating.
/// </summary>
/// <param name="MinX">Minimum X.</param>
/// <param name="MinY">Minimum Y.</param>
/// <param name="MaxX">Maximum X.</param>
/// <param name="MaxY">Maximum Y.</param>
public readonly record struct Bounds2(double MinX, double MinY, double MaxX, double MaxY)
{
    /// <summary>An empty box that any <see cref="Include"/> will replace.</summary>
    public static Bounds2 Empty => new(double.PositiveInfinity, double.PositiveInfinity, double.NegativeInfinity, double.NegativeInfinity);

    /// <summary>True when nothing has been included yet.</summary>
    public bool IsEmpty => MinX > MaxX || MinY > MaxY;

    /// <summary>Width (X extent); zero when empty.</summary>
    public double Width => IsEmpty ? 0 : MaxX - MinX;

    /// <summary>Height (Y extent); zero when empty.</summary>
    public double Height => IsEmpty ? 0 : MaxY - MinY;

    /// <summary>Centre point; origin when empty.</summary>
    public Point2 Center => IsEmpty ? Point2.Zero : new Point2((MinX + MaxX) * 0.5, (MinY + MaxY) * 0.5);

    /// <summary>Returns a box grown to contain <paramref name="p"/>.</summary>
    public Bounds2 Include(Point2 p) =>
        new(Math.Min(MinX, p.X), Math.Min(MinY, p.Y), Math.Max(MaxX, p.X), Math.Max(MaxY, p.Y));

    /// <summary>Returns the union of two boxes.</summary>
    public Bounds2 Union(Bounds2 other)
    {
        if (other.IsEmpty)
        {
            return this;
        }

        if (IsEmpty)
        {
            return other;
        }

        return new Bounds2(Math.Min(MinX, other.MinX), Math.Min(MinY, other.MinY), Math.Max(MaxX, other.MaxX), Math.Max(MaxY, other.MaxY));
    }

    /// <summary>True when <paramref name="p"/> lies inside or on the box.</summary>
    public bool Contains(Point2 p) => p.X >= MinX && p.X <= MaxX && p.Y >= MinY && p.Y <= MaxY;

    /// <summary>The box spanned by two opposite corners, in any order.</summary>
    public static Bounds2 FromCorners(Point2 a, Point2 b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

    /// <summary>Returns the box expanded by <paramref name="margin"/> on every side.</summary>
    public Bounds2 Inflate(double margin) =>
        IsEmpty ? this : new Bounds2(MinX - margin, MinY - margin, MaxX + margin, MaxY + margin);

    /// <summary>Builds the bounds of a set of points (empty for no points).</summary>
    public static Bounds2 FromPoints(IEnumerable<Point2> points)
    {
        Bounds2 bounds = Empty;
        foreach (Point2 p in points)
        {
            bounds = bounds.Include(p);
        }

        return bounds;
    }
}
