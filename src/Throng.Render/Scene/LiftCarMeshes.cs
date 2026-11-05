using Throng.Core.Geometry;

namespace Throng.Render.Scene;

/// <summary>
/// Builds the 3D lift meshes (round 013b, sliding doors round 014): per car an opaque floor plate, translucent walls (so
/// the riders read through them) with the door openings left open, outlines and door frames (green while open); and
/// for every car and landing door two translucent leaves that slide apart as the door opens (D-258).
/// </summary>
public static class LiftCarMeshes
{
    /// <summary>Car wall tint and alpha.</summary>
    public static readonly ColorF Wall = ColorF.FromHex("#38BDF8", 0.22f);

    /// <summary>Car floor plate.</summary>
    public static readonly ColorF Floor = ColorF.FromHex("#1E3A4C");

    /// <summary>Car outline.</summary>
    public static readonly ColorF Edge = ColorF.FromHex("#7DD3FC");

    /// <summary>Door frames while the doors are open (or opening).</summary>
    public static readonly ColorF DoorOpen = ColorF.FromHex("#4ADE80");

    /// <summary>Door frames while shut.</summary>
    public static readonly ColorF DoorClosed = ColorF.FromHex("#E2E8F0", 0.85f);

    /// <summary>Car door leaves.</summary>
    public static readonly ColorF CarLeaf = ColorF.FromHex("#94A3B8", 0.55f);

    /// <summary>Landing door leaves.</summary>
    public static readonly ColorF LandingLeaf = ColorF.FromHex("#CBD5E1", 0.45f);

    /// <summary>Leaf outlines.</summary>
    public static readonly ColorF LeafEdge = ColorF.FromHex("#E2E8F0", 0.9f);

    /// <summary>Door opening height as a share of the car height.</summary>
    public const double DoorHeightShare = LiftLayout.DoorHeightShare;

    /// <summary>Builds the opaque, translucent and line meshes for every car and door.</summary>
    public static (MeshData Solid, MeshData Glass, MeshData Lines) Build(LiftSceneDraw scene)
    {
        var solid = new MeshBuilder(MeshPrimitive.Triangles);
        var glass = new MeshBuilder(MeshPrimitive.Triangles);
        var lines = new MeshBuilder(MeshPrimitive.Lines);
        foreach (LiftCarDraw car in scene.Cars)
        {
            AddCar(solid, glass, lines, car);
        }

        foreach (LiftDoorDraw door in scene.Doors)
        {
            AddLeaves(glass, lines, door);
        }

        return (solid.Build(), glass.Build(), lines.Build());
    }

    private static void AddCar(MeshBuilder solid, MeshBuilder glass, MeshBuilder lines, LiftCarDraw car)
    {
        if (car.Footprint.Count < 3 || car.HeightM <= 0)
        {
            return;
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

        ColorF frame = car.DoorOpenShare > 0 ? DoorOpen : DoorClosed;
        double zd = z0 + (car.HeightM * DoorHeightShare);
        foreach ((Point2 a, Point2 b) in car.Doors)
        {
            lines.Line(new Point3(a.X, a.Y, z0), new Point3(a.X, a.Y, zd), frame);
            lines.Line(new Point3(b.X, b.Y, z0), new Point3(b.X, b.Y, zd), frame);
            lines.Line(new Point3(a.X, a.Y, zd), new Point3(b.X, b.Y, zd), frame);
        }
    }

    /// <summary>
    /// Translucent walls round the footprint, leaving each door opening out up to the door head (the leaves close it).
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

            if (gapEnd - gapStart < 1e-3)
            {
                WallQuad(glass, a, b, z0, z1, Wall);
                continue;
            }

            Point2 g0 = a + (u * gapStart), g1 = a + (u * gapEnd);
            WallQuad(glass, a, g0, z0, z1, Wall);
            WallQuad(glass, g1, b, z0, z1, Wall);
            WallQuad(glass, g0, g1, zd, z1, Wall);
        }
    }

    /// <summary>
    /// Two leaves, each half the opening, meeting in the middle when shut; open, each has slid its own width aside
    /// (behind the jamb), so a fully open door shows a clear opening.
    /// </summary>
    private static void AddLeaves(MeshBuilder glass, MeshBuilder lines, LiftDoorDraw door)
    {
        Point2 d = door.B - door.A;
        double width = d.Length;
        if (width < 1e-6 || door.HeightM <= 0)
        {
            return;
        }

        Point2 u = d * (1 / width);
        double half = width / 2, slide = Math.Clamp(door.OpenShare, 0, 1) * half;
        Point2 mid = door.A + (u * half) + door.Offset;
        double z0 = door.FloorZ, z1 = door.FloorZ + door.HeightM;
        ColorF tint = door.IsLanding ? LandingLeaf : CarLeaf;
        for (int side = -1; side <= 1; side += 2)
        {
            // Leaf from the meeting line out to its jamb, moved aside by the slide.
            Point2 inner = mid + (u * (side * slide));
            Point2 outer = mid + (u * (side * (half + slide)));
            WallQuad(glass, inner, outer, z0, z1, tint);
            lines.Line(new Point3(inner.X, inner.Y, z0), new Point3(inner.X, inner.Y, z1), LeafEdge);
            lines.Line(new Point3(inner.X, inner.Y, z1), new Point3(outer.X, outer.Y, z1), LeafEdge);
        }
    }

    private static void WallQuad(MeshBuilder glass, Point2 a, Point2 b, double z0, double z1, ColorF color)
    {
        if ((b - a).Length < 1e-4 || z1 - z0 < 1e-4)
        {
            return;
        }

        glass.Quad(new Point3(a.X, a.Y, z0), new Point3(b.X, b.Y, z0), new Point3(b.X, b.Y, z1), new Point3(a.X, a.Y, z1), color);
    }
}
