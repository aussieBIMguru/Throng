using System.Text.Json.Serialization;
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

/// <summary>How tall an obstacle or wall is on each level it's on (D-077).</summary>
public enum RegionHeightMode
{
    /// <summary><see cref="Region.HeightM"/> metres above each level's floor (repeated per spanned level, D-079).</summary>
    Fixed,

    /// <summary>Floor to floor: up to the next level's floor (the topmost level uses its own floor-to-floor height).</summary>
    FullStorey,
}

/// <summary>
/// A sketched shape on a level: walkable floor, obstacle, wall or zone. Obstacles and walls can also span the
/// levels above their own up to <see cref="TopLevelId"/>, minus <see cref="ExcludedLevelIds"/> (D-076, D-080, D-087).
/// </summary>
public sealed class Region
{
    /// <summary>Stable identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Level this region is sketched on (its base level when it spans levels).</summary>
    public Guid LevelId { get; set; }

    /// <summary>
    /// Highest level an obstacle or wall reaches; null means this level only (D-080). Ignored for walkable
    /// regions and zones, which always stay on one level (D-076).
    /// </summary>
    public Guid? TopLevelId { get; set; }

    /// <summary>Levels within the span where the obstacle or wall is absent (D-087).</summary>
    public List<Guid> ExcludedLevelIds { get; set; } = [];

    /// <summary>Fixed height or floor to floor (D-077). Older files load as Fixed (D-086).</summary>
    public RegionHeightMode HeightMode { get; set; } = RegionHeightMode.Fixed;

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

    /// <summary>Extrusion height of obstacles and walls in metres, used when <see cref="HeightMode"/> is Fixed.</summary>
    public double HeightM { get; set; } = 1.2;

    /// <summary>Wall thickness in metres (walls only).</summary>
    public double ThicknessM { get; set; } = 0.2;

    /// <summary>Speed multiplier for <see cref="ZoneKind.Slow"/> zones (1 = no effect).</summary>
    public double SpeedFactor { get; set; } = 1.0;

    /// <summary>Optional free-text tag, e.g. "Void" for a floor opening.</summary>
    public string? Tag { get; set; }

    /// <summary>
    /// Agent streams emitted by a spawn zone (file format v4, D-096). Kept when the zone purpose changes, but only
    /// used while the region is a spawn.
    /// </summary>
    public List<EmissionRule> EmissionRules { get; set; } = [];

    /// <summary>True for kinds that can span levels and take a height mode (obstacles and walls, D-076).</summary>
    [JsonIgnore]
    public bool CanSpan => Kind is RegionKind.Obstacle or RegionKind.Wall;

    /// <summary>True for zones that emit agents (Spawn or SpawnSink).</summary>
    [JsonIgnore]
    public bool IsSpawn => Kind == RegionKind.Zone && ZoneKind is ZoneKind.Spawn or ZoneKind.SpawnSink;

    /// <summary>True for zones that absorb agents (Sink or SpawnSink).</summary>
    [JsonIgnore]
    public bool IsSink => Kind == RegionKind.Zone && ZoneKind is ZoneKind.Sink or ZoneKind.SpawnSink;
}
