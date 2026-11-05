using System.Text.Json.Serialization;

namespace Throng.Core.Geometry;

/// <summary>
/// An immutable 2D point or vector in metres, used for plan-space geometry.
/// X runs east, Y runs north; world Z (elevation) is carried separately.
/// </summary>
/// <param name="X">X coordinate in metres.</param>
/// <param name="Y">Y coordinate in metres.</param>
public readonly record struct Point2(double X, double Y)
{
    /// <summary>The origin / zero vector.</summary>
    public static Point2 Zero => new(0, 0);

    /// <summary>Euclidean length when treated as a vector.</summary>
    [JsonIgnore]
    public double Length => Math.Sqrt((X * X) + (Y * Y));

    public static Point2 operator +(Point2 a, Point2 b) => new(a.X + b.X, a.Y + b.Y);

    public static Point2 operator -(Point2 a, Point2 b) => new(a.X - b.X, a.Y - b.Y);

    public static Point2 operator *(Point2 a, double s) => new(a.X * s, a.Y * s);

    public static Point2 operator *(double s, Point2 a) => new(a.X * s, a.Y * s);

    /// <summary>Dot product of two vectors.</summary>
    public static double Dot(Point2 a, Point2 b) => (a.X * b.X) + (a.Y * b.Y);

    /// <summary>Z component of the 3D cross product; positive when <paramref name="b"/> is counter-clockwise from <paramref name="a"/>.</summary>
    public static double Cross(Point2 a, Point2 b) => (a.X * b.Y) - (a.Y * b.X);

    /// <summary>Distance between two points.</summary>
    public static double Distance(Point2 a, Point2 b) => (a - b).Length;

    /// <summary>Unit vector in the same direction, or zero for a degenerate vector.</summary>
    public Point2 Normalised()
    {
        double length = Length;
        return length > 1e-12 ? new Point2(X / length, Y / length) : Zero;
    }

    /// <summary>The vector rotated 90° counter-clockwise.</summary>
    public Point2 Perpendicular() => new(-Y, X);
}
