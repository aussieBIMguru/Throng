using Throng.Core.Geometry;

namespace Throng.Core.World;

/// <summary>What produced a surface.</summary>
public enum SurfaceKind
{
    /// <summary>A level's floor, compiled from its walkable and obstacle regions.</summary>
    Floor,

    /// <summary>A stair, escalator, travelator or ramp (from M1).</summary>
    Connector,
}

/// <summary>How agents move between two surfaces.</summary>
public enum PortalKind
{
    /// <summary>Shared walkable edge (e.g. floor to connector landing).</summary>
    Edge,

    /// <summary>Lift landing door; travel is via a lift car.</summary>
    LiftDoor,
}

/// <summary>
/// A walkable surface in the simulation (§3.0). Positions on it are (u, v) in surface-local metres;
/// <see cref="ToWorld"/> maps them to world space using the height function.
/// </summary>
/// <remarks>
/// Floors use world X/Y as u/v with a flat height. Connectors (M1) set <see cref="Origin"/>, <see cref="AxisU"/>
/// and <see cref="SlopeAlongU"/> so u runs up the flight.
/// </remarks>
public sealed class Surface
{
    /// <summary>Stable id (for floors this is derived from the level id).</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>What produced this surface.</summary>
    public SurfaceKind Kind { get; init; }

    /// <summary>Owning level for floors; null for connectors spanning levels.</summary>
    public Guid? LevelId { get; init; }

    /// <summary>Id of the source object (level or connector).</summary>
    public Guid SourceId { get; init; }

    /// <summary>Display name for diagnostics.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>World plan position of surface (0, 0).</summary>
    public Point2 Origin { get; init; } = Point2.Zero;

    /// <summary>Unit world plan direction of the surface's u axis; v is 90° counter-clockwise.</summary>
    public Point2 AxisU { get; init; } = new(1, 0);

    /// <summary>World elevation at u = 0.</summary>
    public double BaseElevationM { get; init; }

    /// <summary>Rise in metres per metre of u (0 for flat floors).</summary>
    public double SlopeAlongU { get; init; }

    /// <summary>Walkable outlines in surface-local coordinates.</summary>
    public IReadOnlyList<IReadOnlyList<Point2>> Walkable { get; init; } = [];

    /// <summary>Obstacle outlines in surface-local coordinates.</summary>
    public IReadOnlyList<IReadOnlyList<Point2>> Obstacles { get; init; } = [];

    /// <summary>Maps a surface-local position to world space.</summary>
    public Point3 ToWorld(double u, double v)
    {
        Point2 axisV = AxisU.Perpendicular();
        Point2 plan = Origin + (AxisU * u) + (axisV * v);
        return new Point3(plan.X, plan.Y, BaseElevationM + (SlopeAlongU * u));
    }
}

/// <summary>
/// A connection agents can cross between two surfaces: an edge segment expressed on each side.
/// </summary>
public sealed class Portal
{
    /// <summary>Stable id.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>How the portal is crossed.</summary>
    public PortalKind Kind { get; init; }

    /// <summary>First surface.</summary>
    public Guid SurfaceA { get; init; }

    /// <summary>Second surface.</summary>
    public Guid SurfaceB { get; init; }

    /// <summary>Portal segment start/end on surface A, local coordinates.</summary>
    public (Point2 Start, Point2 End) EdgeOnA { get; init; }

    /// <summary>Portal segment start/end on surface B, local coordinates.</summary>
    public (Point2 Start, Point2 End) EdgeOnB { get; init; }
}

/// <summary>
/// The compiled simulation world: surfaces and portals. The portal graph for route choice is built from this in M2.
/// </summary>
public sealed class WorldModel
{
    /// <summary>All walkable surfaces.</summary>
    public IReadOnlyList<Surface> Surfaces { get; init; } = [];

    /// <summary>All portals between surfaces.</summary>
    public IReadOnlyList<Portal> Portals { get; init; } = [];

    /// <summary>Finds a surface by id.</summary>
    public Surface? FindSurface(Guid id) => Surfaces.FirstOrDefault(s => s.Id == id);
}
