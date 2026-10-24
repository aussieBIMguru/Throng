using System.Text.Json.Serialization;

namespace Throng.Core.Geometry;

/// <summary>
/// An immutable 3D point or vector in world space (metres, Z up).
/// </summary>
/// <param name="X">X coordinate (east), metres.</param>
/// <param name="Y">Y coordinate (north), metres.</param>
/// <param name="Z">Z coordinate (elevation), metres.</param>
public readonly record struct Point3(double X, double Y, double Z)
{
    /// <summary>Euclidean length when treated as a vector.</summary>
    [JsonIgnore]
    public double Length => Math.Sqrt((X * X) + (Y * Y) + (Z * Z));

    /// <summary>The plan (XY) component.</summary>
    [JsonIgnore]
    public Point2 Plan => new(X, Y);

    public static Point3 operator +(Point3 a, Point3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static Point3 operator -(Point3 a, Point3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static Point3 operator *(Point3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);

    /// <summary>Lifts a plan point to the given elevation.</summary>
    public static Point3 FromPlan(Point2 p, double z) => new(p.X, p.Y, z);
}
