using Throng.Core.Geometry;

namespace Throng.Render.Scene;

/// <summary>
/// The section box outline and its six face handles as a line mesh (round 016, D-277). Handles are squares at each
/// face's centre, square to the face, sized from the box (a tenth of its smallest side, 0.5–4 m); the hovered one is
/// drawn in the hot colour with a cross.
/// </summary>
public static class SectionBoxMesh
{
    /// <summary>Builds the mesh for a box from <paramref name="min"/> to <paramref name="max"/> (true heights).</summary>
    /// <param name="min">Box minimum corner.</param>
    /// <param name="max">Box maximum corner.</param>
    /// <param name="hovered">Hovered face 0–5 (west, east, south, north, bottom, top), or −1.</param>
    public static MeshData Build(Point3 min, Point3 max, int hovered)
    {
        var lines = new MeshBuilder(MeshPrimitive.Lines);
        ColorF edge = RenderPalette.SectionBox;
        Point3[] c =
        [
            new(min.X, min.Y, min.Z), new(max.X, min.Y, min.Z), new(max.X, max.Y, min.Z), new(min.X, max.Y, min.Z),
            new(min.X, min.Y, max.Z), new(max.X, min.Y, max.Z), new(max.X, max.Y, max.Z), new(min.X, max.Y, max.Z),
        ];
        for (int k = 0; k < 4; k++)
        {
            lines.Line(c[k], c[(k + 1) % 4], edge);
            lines.Line(c[k + 4], c[((k + 1) % 4) + 4], edge);
            lines.Line(c[k], c[k + 4], edge);
        }

        double sx = max.X - min.X, sy = max.Y - min.Y, sz = max.Z - min.Z;
        double h = Math.Clamp(Math.Min(sx, Math.Min(sy, sz)) * 0.1, 0.5, 4.0) * 0.5;
        var centre = new Point3((min.X + max.X) * 0.5, (min.Y + max.Y) * 0.5, (min.Z + max.Z) * 0.5);
        for (int face = 0; face < 6; face++)
        {
            Point3 at = face switch
            {
                0 => centre with { X = min.X },
                1 => centre with { X = max.X },
                2 => centre with { Y = min.Y },
                3 => centre with { Y = max.Y },
                4 => centre with { Z = min.Z },
                _ => centre with { Z = max.Z },
            };

            // Two axes in the face's plane.
            (Point3 u, Point3 v) = face switch
            {
                0 or 1 => (new Point3(0, h, 0), new Point3(0, 0, h)),
                2 or 3 => (new Point3(h, 0, 0), new Point3(0, 0, h)),
                _ => (new Point3(h, 0, 0), new Point3(0, h, 0)),
            };
            ColorF color = face == hovered ? RenderPalette.SectionBoxHot : edge;
            Point3 a = Add(at, -1, u, -1, v), b = Add(at, 1, u, -1, v), d = Add(at, 1, u, 1, v), e = Add(at, -1, u, 1, v);
            lines.Line(a, b, color);
            lines.Line(b, d, color);
            lines.Line(d, e, color);
            lines.Line(e, a, color);
            if (face == hovered)
            {
                lines.Line(a, d, color);
                lines.Line(b, e, color);
            }
        }

        return lines.Build();
    }

    private static Point3 Add(Point3 p, double su, Point3 u, double sv, Point3 v) =>
        new(p.X + (su * u.X) + (sv * v.X), p.Y + (su * u.Y) + (sv * v.Y), p.Z + (su * u.Z) + (sv * v.Z));
}
