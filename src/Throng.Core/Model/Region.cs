using Throng.Core.Geometry;

namespace Throng.Core.Model;

/// <summary>What a sketched region means to the simulation.</summary>
public enum RegionKind
{
    /// <summary>Floor area agents can occupy. A level's walkable area is the union of these.</summary>
    Walkable,

    /// <summary>Solid area subtracted from walkable (columns, furniture, voids, kiosks).</summary>
    Obstacle,

    /// <summary>Open polyline with thickness; a thin obstacle.</summary>
    Wall,

    /// <summary>Non-blocking tagged area (see <see cref="ZoneKind"/>).</summary>
    Zone,
}

/// <summary>Purpose of a <see cref="RegionKind.Zone"/> region.</summary>
public enum ZoneKind
{
    /// <summary>Not a zone, or an untyped tag area.</summary>
    None,

    /// <summary>Agents appear here.</summary>
    Spawn,

    /// <summary>Agents leave the study here.</summary>
    Sink,

    /// <summary>Both spawn and sink (entrances, vehicle doors).</summary>
    SpawnSink,

    /// <summary>Queueing area attached to a node.</summary>
    Queue,

    /// <summary>Counting/measurement area for analytics.</summary>
    Measurement,

    /// <summary>Agents linger here for a dwell time (retail, concessions).</summary>
    Dwell,

    /// <summary>Agents wait here until a trigger (seating, platform waiting).</summary>
    Wait,

    /// <summary>Speed multiplier applies inside (platform edge strip, congested floor finish).</summary>
    Slow,
}

/// <summary>
/// A sketched shape on a level: walkable floor, obstacle, wall or zone.
/// </summary>
public sealed class Region
{
    /// <summary>Stable identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Level this region is sketched on.</summary>
    public Guid LevelId { get; set; }

    /// <summary>Display name.</summary>
    public string Name { get; set; } = "Region";

    /// <summary>Region meaning.</summary>
    public RegionKind Kind { get; set; } = RegionKind.Walkable;

    /// <summary>Zone purpose; only meaningful when <see cref="Kind"/> is <see cref="RegionKind.Zone"/>.</summary>
    public ZoneKind ZoneKind { get; set; } = ZoneKind.None;

    /// <summary>
    /// Outline in plan coordinates (metres). Closed implicitly for walkable/obstacle/zone;
    /// an open polyline for walls.
    /// </summary>
    public List<Point2> Outline { get; set; } = [];

    /// <summary>Visual extrusion height of obstacles and walls in metres.</summary>
    public double HeightM { get; set; } = 1.2;

    /// <summary>Wall thickness in metres (walls only).</summary>
    public double ThicknessM { get; set; } = 0.2;

    /// <summary>Speed multiplier for <see cref="ZoneKind.Slow"/> zones (1 = no effect).</summary>
    public double SpeedFactor { get; set; } = 1.0;

    /// <summary>Optional free-text tag, e.g. "Void" for a floor opening.</summary>
    public string? Tag { get; set; }
}
