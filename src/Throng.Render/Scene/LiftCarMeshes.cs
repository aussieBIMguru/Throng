using Throng.Core.Geometry;

namespace Throng.Render.Scene;

/// <summary>
/// Builds the 3D lift car meshes (round 013b): an opaque floor plate and roof edge, translucent walls (so the riders
/// read through them) with a gap where a door is open, and outlines with the door openings drawn as posts and a
/// header, green while open.
/// </summary>
public static class LiftCarMeshes
{
    /// <summary>Car wall tint and alpha.</summary>
    public static readonly ColorF Wall = ColorF.FromHex("#38BDF8", 0.22f);

    /// <summary>Car floor plate.</summary>
    public static readonly ColorF Floor = ColorF.FromHex("#1E3A4C");

    /// <summary>Car outline.</summary>
    public static readonly ColorF Edge = ColorF.FromHex("#7DD3FC");

    /// <summary>Door openings while the doors are open.</summary>
    public static readonly ColorF DoorOpen = ColorF.FromHex("#4ADE80");

    /// <summary>Door openings while closed.</summary>
    public static readonly ColorF DoorClosed = ColorF.FromHex("#E2E8F0", 0.85f);

    /// <summary>Door opening height as a share of the car height.</summary>
    private const double DoorHeightShare = 0.88;

    /// <summary>Builds the opaque, translucent and line meshes for every car.</summary>
    public static (MeshData Solid, MeshData Glass, MeshData Lines) Build(IReadOnlyList<LiftCarDraw> cars)
    {
        var solid = new MeshBuilder(MeshPrimitive.Triangles);
        var glass = new MeshBuilder(MeshPrimitive.Triangles);
        var lines = new MeshBuilder(MeshPrimitive.Lines);
        foreach (LiftCarDraw car in cars)
        {
            if (car.Footprint.Count < 3 || car.HeightM <= 0)
            {
                continue;
            }

            double z0 = car.FloorZ, z1 = car.FloorZ + car.HeightM;

            // The floor plate sits a hair under the riders' feet so they don't flicker into it.
            solid.PolygonFill(car.Footprint, z0 - 0.02, Floor);
            AddWalls(glass, car, z0, z1);

            lines.PolygonOutline(car.Footprint, z0, Edge);
            lines.PolygonOutline(car.Footprint, z1, Edge);
            foreach (Point2 p in car.Footprint)
            {
                lines.Line(new Point3(p.X, p.Y, z0), new Point3(p.X, p.Y, z1), Edge);
            }

            ColorF door = car.DoorsOpen ? DoorOpen : DoorClosed;
            double zd = z0 + (car.HeightM * DoorHeightShare);
            foreach ((Point2 a, Point2 b) in car.Doors)
            {
                lines.Line(new Point3(a.X, a.Y, z0), new Point3(a.X, a.Y, zd), door);
                lines.Line(new Point3(b.X, b.Y, z0), new Point3(b.X, b.Y, zd), door);
                lines.Line(new Point3(a.X, a.Y, zd), new Point3(b.X, b.Y, zd), door);
                if (!car.DoorsOpen)
                {
                    // Closed doors: the meeting line of the two leaves.
                    Point2 m = (a + b) * 0.5;
                    lines.Line(new Point3(m.X, m.Y, z0), new Point3(m.X, m.Y, zd), door);
                }
            }
        }

        return (solid.Build(), glass.Build(), lines.Build());
    }

    /// <summary>
    /// Translucent walls round the footprint. While the doors are open, the part of each wall under a door opening is
    /// left out up to the door head, so you can see into the car through the doorway.
    /// </summary>
    private static void AddWalls(MeshBuilder glass, LiftCarDraw car, double z0, double z1)
    {
        IReadOnlyList<Point2> f = car.Footprint;
        double zd = z0 + (car.HeightM * DoorHeightShare);
        for (int e = 0; e < f.Count; e++)
        {
            Point2 a = f[e], b = f[(e + 1) % f.Count];
            Point2 d = b - a;
            double length = d.Length;
            if (length < 1e-9)
            {
                continue;
            }

            Point2 u = d * (1 / length);
            double gapStart = 0, gapEnd = 0;
            if (car.DoorsOpen)
            {
                foreach ((Point2 da, Point2 db) in car.Doors)
                {
                    // A door on this wall: both ends within a few cm of the edge line.
                    if (Math.Abs(Point2.Cross(u, da - a)) > 0.05 || Math.Abs(Point2.Cross(u, db - a)) > 0.05)
                    {
                        continue;
                    }

                    double s0 = Point2.Dot(da - a, u), s1 = Point2.Dot(db - a, u);
                    gapStart = Math.Clamp(Math.Min(s0, s1), 0, length);
                    gapEnd = Math.Clamp(Math.Max(s0, s1), 0, length);
                    break;
                }
            }

            if (gapEnd - gapStart < 1e-3)
            {
                WallQuad(glass, a, b, z0, z1);
                continue;
            }

            Point2 g0 = a + (u * gapStart), g1 = a + (u * gapEnd);
            WallQuad(glass, a, g0, z0, z1);
            WallQuad(glass, g1, b, z0, z1);
            WallQuad(glass, g0, g1, zd, z1);
        }
    }

    private static void WallQuad(MeshBuilder glass, Point2 a, Point2 b, double z0, double z1)
    {
        if ((b - a).Length < 1e-4 || z1 - z0 < 1e-4)
        {
            return;
        }

        glass.Quad(new Point3(a.X, a.Y, z0), new Point3(b.X, b.Y, z0), new Point3(b.X, b.Y, z1), new Point3(a.X, a.Y, z1), Wall);
    }
}
