using Throng.Core.Geometry;

namespace Throng.Render.Scene;

/// <summary>Primitive type of a <see cref="MeshData"/>.</summary>
public enum MeshPrimitive
{
    /// <summary>Independent triangles.</summary>
    Triangles,

    /// <summary>Independent line segments.</summary>
    Lines,
}

/// <summary>
/// CPU-side vertex data: interleaved x, y, z, r, g, b, a floats (<see cref="FloatsPerVertex"/> per vertex).
/// </summary>
/// <param name="Vertices">Interleaved vertex data.</param>
/// <param name="Primitive">How vertices are assembled.</param>
public sealed record MeshData(float[] Vertices, MeshPrimitive Primitive)
{
    /// <summary>Floats per vertex: position (3) + colour (4).</summary>
    public const int FloatsPerVertex = 7;

    /// <summary>An empty mesh.</summary>
    public static MeshData Empty(MeshPrimitive primitive) => new([], primitive);

    /// <summary>Number of vertices.</summary>
    public int VertexCount => Vertices.Length / FloatsPerVertex;
}

/// <summary>
/// Accumulates triangles or lines into a <see cref="MeshData"/>. Coordinates are world metres (Z up).
/// </summary>
public sealed class MeshBuilder
{
    // Fake directional light for flat-shaded prism sides, in plan.
    private static readonly Point2 LightDirection = new Point2(0.45, 0.89).Normalised();

    private readonly List<float> _data = [];

    /// <summary>Creates a builder for the given primitive.</summary>
    public MeshBuilder(MeshPrimitive primitive) => Primitive = primitive;

    /// <summary>Primitive this builder produces.</summary>
    public MeshPrimitive Primitive { get; }

    /// <summary>Freezes the accumulated data.</summary>
    public MeshData Build() => new([.. _data], Primitive);

    /// <summary>Adds one vertex.</summary>
    public void Vertex(double x, double y, double z, ColorF c)
    {
        _data.Add((float)x);
        _data.Add((float)y);
        _data.Add((float)z);
        _data.Add(c.R);
        _data.Add(c.G);
        _data.Add(c.B);
        _data.Add(c.A);
    }

    /// <summary>Adds a line segment (line builders).</summary>
    public void Line(Point2 a, Point2 b, double z, ColorF c)
    {
        Vertex(a.X, a.Y, z, c);
        Vertex(b.X, b.Y, z, c);
    }

    /// <summary>Adds a 3D line segment (line builders).</summary>
    public void Line(Point3 a, Point3 b, ColorF c)
    {
        Vertex(a.X, a.Y, a.Z, c);
        Vertex(b.X, b.Y, b.Z, c);
    }

    /// <summary>Adds the closed outline of a polygon (line builders).</summary>
    public void PolygonOutline(IReadOnlyList<Point2> polygon, double z, ColorF c)
    {
        for (int i = 0; i < polygon.Count; i++)
        {
            Line(polygon[i], polygon[(i + 1) % polygon.Count], z, c);
        }
    }

    /// <summary>Adds an open polyline (line builders).</summary>
    public void Polyline(IReadOnlyList<Point2> points, double z, ColorF c)
    {
        for (int i = 0; i + 1 < points.Count; i++)
        {
            Line(points[i], points[i + 1], z, c);
        }
    }

    /// <summary>Adds a filled horizontal polygon at elevation <paramref name="z"/> (triangle builders).</summary>
    public void PolygonFill(IReadOnlyList<Point2> polygon, double z, ColorF c)
    {
        IReadOnlyList<int> indices = PolygonMath.Triangulate(polygon);
        foreach (int index in indices)
        {
            Point2 p = polygon[index];
            Vertex(p.X, p.Y, z, c);
        }
    }

    /// <summary>
    /// Adds the vertical sides of an extruded polygon between <paramref name="z0"/> and <paramref name="z1"/>,
    /// flat-shaded by a fixed plan light (triangle builders).
    /// </summary>
    public void PrismSides(IReadOnlyList<Point2> polygon, double z0, double z1, ColorF side, bool closed = true)
    {
        int edges = closed ? polygon.Count : polygon.Count - 1;
        for (int i = 0; i < edges; i++)
        {
            Point2 a = polygon[i];
            Point2 b = polygon[(i + 1) % polygon.Count];
            Point2 normal = (b - a).Perpendicular().Normalised();
            float shade = 0.62f + (0.38f * (float)Math.Abs(Point2.Dot(normal, LightDirection)));
            ColorF c = side.Shade(shade);

            Vertex(a.X, a.Y, z0, c);
            Vertex(b.X, b.Y, z0, c);
            Vertex(b.X, b.Y, z1, c);
            Vertex(a.X, a.Y, z0, c);
            Vertex(b.X, b.Y, z1, c);
            Vertex(a.X, a.Y, z1, c);
        }
    }

    /// <summary>Adds a solid extruded polygon: sides plus top cap (triangle builders).</summary>
    public void Prism(IReadOnlyList<Point2> polygon, double z0, double z1, ColorF side, ColorF top)
    {
        PrismSides(polygon, z0, z1, side);
        PolygonFill(polygon, z1, top);
    }
}
