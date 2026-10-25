using Throng.Core.Geometry;
using Throng.Core.Library;

namespace Throng.Sim.Nav;

/// <summary>A connector as the simulation sees it: its piece surfaces, floor portals and belt values.</summary>
public sealed class NavConnector
{
    /// <summary>Position in <see cref="NavWorld.Connectors"/>.</summary>
    public required int Index { get; init; }

    /// <summary>Source connector id.</summary>
    public required Guid ConnectorId { get; init; }

    /// <summary>Display name.</summary>
    public required string Name { get; init; }

    /// <summary>Stair, escalator, travelator or ramp.</summary>
    public required ConnectorKind Kind { get; init; }

    /// <summary>Stair riser height (for the per-profile stair correction).</summary>
    public double RiserM { get; init; } = 0.17;

    /// <summary>Stair going.</summary>
    public double GoingM { get; init; } = 0.28;

    /// <summary>Belt speed magnitude, m/s (escalators and travelators).</summary>
    public double BeltSpeedMps { get; init; }

    /// <summary>Step-loading cap at the entry, people per minute (escalators and travelators; 0 = none).</summary>
    public double ThroughputPerMin { get; init; }

    /// <summary>True when step-free profiles may use it (ramps and travelators).</summary>
    public bool StepFreeOk { get; init; }

    /// <summary>
    /// People per second it can carry, for the congestion term in route choice (D-111): the preset throughput for
    /// belts, flow per metre × width for stairs and ramps.
    /// </summary>
    public double CapacityPerSecond { get; init; } = 1;

    /// <summary>Its piece surfaces.</summary>
    public List<int> Pieces { get; } = [];

    /// <summary>Its route-graph portals (served floor ends).</summary>
    public List<int> Portals { get; } = [];

    /// <summary>Floor surfaces of excluded intermediate levels it passes over (D-093).</summary>
    public List<int> PassOverFloors { get; } = [];
}

/// <summary>
/// A served floor end of a connector: where people step between a level's floor and the connector. These are the
/// nodes of the portal graph (BRIEF §3.0).
/// </summary>
public sealed class NavPortal
{
    /// <summary>Position in <see cref="NavWorld.Portals"/>.</summary>
    public required int Index { get; init; }

    /// <summary>Owning connector.</summary>
    public required int Connector { get; init; }

    /// <summary>Floor surface.</summary>
    public required int Floor { get; init; }

    /// <summary>Piece surface.</summary>
    public required int Piece { get; init; }

    /// <summary>Level of the floor.</summary>
    public required Guid LevelId { get; init; }

    /// <summary>Height of the edge.</summary>
    public required double ElevationM { get; init; }

    /// <summary>True when people may step from the floor onto the connector here.</summary>
    public required bool AllowsEntry { get; init; }

    /// <summary>True when people may step off the connector onto the floor here.</summary>
    public required bool AllowsExit { get; init; }

    /// <summary>Edge end, plan.</summary>
    public required Point2 A { get; init; }

    /// <summary>Edge end, plan.</summary>
    public required Point2 B { get; init; }

    /// <summary>Unit plan direction off the connector onto the floor.</summary>
    public required Point2 Outward { get; init; }

    /// <summary>Floor cells just outside the edge (the target of the field leading to the portal).</summary>
    public required int[] FloorCells { get; init; }

    /// <summary>Piece cells just inside the edge (the target of the field leading off the connector here).</summary>
    public required int[] PieceCells { get; init; }

    /// <summary>Representative floor cell (middle of the edge).</summary>
    public int FloorCell => FloorCells[FloorCells.Length / 2];

    /// <summary>Representative piece cell.</summary>
    public int PieceCell => PieceCells[PieceCells.Length / 2];

    /// <summary>Distance from a plan point to the edge.</summary>
    public double DistanceTo(Point2 p) => SegmentDistance(p, A, B);

    internal static double SegmentDistance(Point2 p, Point2 a, Point2 b)
    {
        Point2 ab = b - a;
        double len2 = Point2.Dot(ab, ab);
        double t = len2 > 1e-12 ? Math.Clamp(Point2.Dot(p - a, ab) / len2, 0, 1) : 0;
        return Point2.Distance(p, a + (ab * t));
    }
}

/// <summary>What a route target is for (round 009).</summary>
public enum NavTargetRole
{
    /// <summary>A sink zone: people leave the study here.</summary>
    Exit,

    /// <summary>A device's service area or lanes (journey device steps, D-152).</summary>
    Device,

    /// <summary>A zone a journey passes through or dwells in (D-149).</summary>
    Zone,
}

/// <summary>
/// A route target: a sink zone where agents leave the study, or (round 009) a device or zone a journey step sends
/// them to. Every target gets a flow field and a row in the remaining-cost tables, so route choice works the same
/// for all of them.
/// </summary>
public sealed class NavSink
{
    /// <summary>Position in <see cref="NavWorld.Sinks"/>.</summary>
    public required int Index { get; init; }

    /// <summary>Source region id (exits and zones) or device id (devices).</summary>
    public required Guid RegionId { get; init; }

    /// <summary>What the target is for; only <see cref="NavTargetRole.Exit"/> targets remove people.</summary>
    public NavTargetRole Role { get; init; } = NavTargetRole.Exit;

    /// <summary>Display name.</summary>
    public required string Name { get; init; }

    /// <summary>Floor surface it's on.</summary>
    public required int Floor { get; init; }

    /// <summary>Walkable floor cells inside the zone.</summary>
    public required int[] Cells { get; init; }

    /// <summary>
    /// Floor cells this target's field treats as closed: for a device target, the lanes of every other pass-through
    /// device on the floor, so people heading for one gate don't walk through another unserved (r009b). Set once by
    /// the simulation set-up, before the fields are built.
    /// </summary>
    public int[] AvoidCells { get; set; } = [];
}

/// <summary>A directed step from a cell on one surface to a cell on another (joints and floor ends).</summary>
/// <param name="From">Global cell index of the start.</param>
/// <param name="To">Global cell index of the end.</param>
/// <param name="Seconds">Nominal time to make the step.</param>
/// <param name="Connector">Connector the link belongs to.</param>
/// <param name="PassOver">True for links at an excluded level's floor end.</param>
public readonly record struct NavLink(int From, int To, float Seconds, int Connector, bool PassOver);

/// <summary>
/// The navigation world compiled from a study (M2): floor and piece grids, connectors, the served floor ends that
/// make up the portal graph, sinks, and the cross-surface links used by the flow-field searches.
/// </summary>
public sealed class NavWorld
{
    /// <summary>Nominal walking speed used for field costs, m/s.</summary>
    public const double NominalSpeed = 1.34;

    /// <summary>All surfaces; floors first.</summary>
    public required IReadOnlyList<NavSurface> Surfaces { get; init; }

    /// <summary>Connectors in the simulation (staff-only ones are left out until M3, D-110).</summary>
    public required IReadOnlyList<NavConnector> Connectors { get; init; }

    /// <summary>Served floor ends.</summary>
    public required IReadOnlyList<NavPortal> Portals { get; init; }

    /// <summary>Route targets: sink zones first, then any device and zone targets added for journeys.</summary>
    public required IReadOnlyList<NavSink> Sinks { get; init; }

    /// <summary>First global cell index of each surface.</summary>
    public required int[] CellBase { get; init; }

    /// <summary>Total cells over all surfaces.</summary>
    public required int TotalCells { get; init; }

    /// <summary>Cross-surface links grouped by their end cell (CSR: <see cref="LinksIntoStart"/>).</summary>
    public required NavLink[] LinksInto { get; init; }

    /// <summary>Start of each global cell's run in <see cref="LinksInto"/> (length <see cref="TotalCells"/> + 1).</summary>
    public required int[] LinksIntoStart { get; init; }

    /// <summary>Plan bounds of every surface.</summary>
    public required Bounds2 Bounds { get; init; }

    /// <summary>Floor surface of each level.</summary>
    public required IReadOnlyDictionary<Guid, int> FloorByLevel { get; init; }

    /// <summary>
    /// A copy of this world with more route targets after the existing ones (round 009: devices and zones that
    /// journeys use). The extra targets must be numbered from <see cref="Sinks"/>.Count on.
    /// </summary>
    public NavWorld WithTargets(IReadOnlyList<NavSink> extra)
    {
        var targets = new List<NavSink>(Sinks.Count + extra.Count);
        targets.AddRange(Sinks);
        targets.AddRange(extra);
        return new NavWorld
        {
            Surfaces = Surfaces,
            Connectors = Connectors,
            Portals = Portals,
            Sinks = targets,
            CellBase = CellBase,
            TotalCells = TotalCells,
            LinksInto = LinksInto,
            LinksIntoStart = LinksIntoStart,
            Bounds = Bounds,
            FloorByLevel = FloorByLevel,
        };
    }

    /// <summary>Surface holding a global cell index.</summary>
    public int SurfaceOfGlobal(int global)
    {
        int lo = 0, hi = CellBase.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (CellBase[mid] <= global)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return lo;
    }
}
