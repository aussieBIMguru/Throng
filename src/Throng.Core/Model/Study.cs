using System.Text.Json.Serialization;
using Throng.Core.Geometry;
using Throng.Core.Library;

namespace Throng.Core.Model;

/// <summary>
/// The root document: levels, sketched regions, connectors, shafts, devices, underlays, settings and the embedded
/// template library. Lifts and spawns join this type in later milestones.
/// </summary>
public sealed class Study
{
    /// <summary>Stable identifier of the study.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Display name.</summary>
    public string Name { get; set; } = "Untitled study";

    /// <summary>Free-text description.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Run settings.</summary>
    public StudySettings Settings { get; set; } = new();

    /// <summary>Authoring levels, in any order (use <see cref="LevelsByElevation"/> for display).</summary>
    public List<Level> Levels { get; set; } = [];

    /// <summary>All sketched regions across all levels.</summary>
    public List<Region> Regions { get; set; } = [];

    /// <summary>Stairs, escalators and ramps linking levels (file format v2, D-064).</summary>
    public List<Connector> Connectors { get; set; } = [];

    /// <summary>Shafts and voids spanning levels (file format v2, D-056, D-064).</summary>
    public List<Shaft> Shafts { get; set; } = [];

    /// <summary>Placed devices (file format v3, D-089).</summary>
    public List<Device> Devices { get; set; } = [];

    /// <summary>Calibrated plan images (file format v3, D-083).</summary>
    public List<Underlay> Underlays { get; set; } = [];

    /// <summary>Train services: door zones driven by a timetable (file format v7, D-184).</summary>
    public List<TrainService> TrainServices { get; set; } = [];

    /// <summary>Saved named views (file format v10, D-276).</summary>
    public List<SavedView> Views { get; set; } = [];

    /// <summary>CHECK findings ignored for this study, by <c>ValidationMessage.IgnoreKey</c> (file format v10, D-272).</summary>
    public List<string> IgnoredFindings { get; set; } = [];

    /// <summary>
    /// Underlay image bytes by underlay id, stored as separate zip entries (D-083), so excluded from the study JSON.
    /// Entries can outlive their underlay (after a delete that might be undone); only underlays still in the
    /// study are saved.
    /// </summary>
    [JsonIgnore]
    public Dictionary<Guid, byte[]> UnderlayImages { get; set; } = new();

    /// <summary>
    /// Copies of every template the study uses. Stored in its own <c>library.json</c> entry inside the
    /// .throng file, so it is excluded from the main study JSON.
    /// </summary>
    [JsonIgnore]
    public TemplateLibrary Library { get; set; } = new();

    /// <summary>Creates a blank study with a single ground level.</summary>
    public static Study CreateDefault()
    {
        var study = new Study();
        study.Levels.Add(new Level { Name = "Ground", ElevationM = 0, HeightM = 4.0 });
        return study;
    }

    /// <summary>Finds a level by id.</summary>
    public Level? FindLevel(Guid id) => Levels.FirstOrDefault(l => l.Id == id);

    /// <summary>Finds a region by id.</summary>
    public Region? FindRegion(Guid id) => Regions.FirstOrDefault(r => r.Id == id);

    /// <summary>Finds a train service by id.</summary>
    public TrainService? FindTrainService(Guid id) => TrainServices.FirstOrDefault(t => t.Id == id);

    /// <summary>The train service a door zone belongs to (the first one listing it), or null.</summary>
    public TrainService? TrainServiceOfDoor(Guid regionId) => TrainServices.FirstOrDefault(t => t.DoorZoneIds.Contains(regionId));

    /// <summary>Finds a device by id.</summary>
    public Device? FindDevice(Guid id) => Devices.FirstOrDefault(d => d.Id == id);

    /// <summary>Finds an underlay by id.</summary>
    public Underlay? FindUnderlay(Guid id) => Underlays.FirstOrDefault(u => u.Id == id);

    /// <summary>Devices standing on a level.</summary>
    public IEnumerable<Device> DevicesOn(Guid levelId) => Devices.Where(d => d.LevelId == levelId);

    /// <summary>Underlays on a level, in drawing order.</summary>
    public IEnumerable<Underlay> UnderlaysOn(Guid levelId) => Underlays.Where(u => u.LevelId == levelId);

    /// <summary>The nearest level above, or null for the topmost level.</summary>
    public Level? LevelAbove(Level level) =>
        Levels.Where(l => l.ElevationM > level.ElevationM + 1e-6).OrderBy(l => l.ElevationM).FirstOrDefault();

    /// <summary>
    /// Floor-to-floor height of a level: up to the next level's floor, or the level's own height when it's the
    /// topmost (D-077).
    /// </summary>
    public double StoreyHeight(Level level) =>
        LevelAbove(level) is { } above ? above.ElevationM - level.ElevationM : Math.Max(0.1, level.HeightM);

    /// <summary>
    /// Levels an obstacle or wall's span covers, lowest first, including excluded ones (for the SELECTION
    /// checklist). Just its own level unless it spans (D-080).
    /// </summary>
    public IReadOnlyList<Level> SpannedLevels(Region region)
    {
        Level? bottom = FindLevel(region.LevelId);
        if (bottom is null)
        {
            return [];
        }

        Level? top = region.CanSpan && region.TopLevelId is { } topId ? FindLevel(topId) : null;
        if (top is null || top.ElevationM <= bottom.ElevationM + 1e-6)
        {
            return [bottom];
        }

        return [.. LevelsBetween(bottom, top)];
    }

    /// <summary>True when the region is present on the level: its own level, or a spanned level not excluded (D-087).</summary>
    public bool RegionPresentOn(Region region, Guid levelId)
    {
        if (region.ExcludedLevelIds.Contains(levelId) && region.CanSpan)
        {
            return false;
        }

        if (region.LevelId == levelId)
        {
            return true;
        }

        return region.CanSpan && region.TopLevelId is not null && SpannedLevels(region).Any(l => l.Id == levelId);
    }

    /// <summary>Regions drawn or spanning onto a level (obstacles and walls from below included, D-081).</summary>
    public IEnumerable<Region> RegionsPresentOn(Guid levelId) => Regions.Where(r => RegionPresentOn(r, levelId));

    /// <summary>
    /// Bottom and top elevation of an obstacle or wall on one level: Fixed = the level's floor plus the height
    /// (repeated per level, D-079); Full storey = floor to the next level's floor (D-077).
    /// </summary>
    public (double Bottom, double Top) RegionExtentOn(Region region, Level level)
    {
        double height = region.CanSpan && region.HeightMode == RegionHeightMode.FullStorey ? StoreyHeight(level) : region.HeightM;
        return (level.ElevationM, level.ElevationM + Math.Max(0.01, height));
    }

    /// <summary>Levels with elevations from <paramref name="bottom"/> to <paramref name="top"/>, inclusive, lowest first.</summary>
    public IEnumerable<Level> LevelsBetween(Level bottom, Level top) =>
        Levels.Where(l => l.ElevationM >= bottom.ElevationM - 1e-6 && l.ElevationM <= top.ElevationM + 1e-6).OrderBy(l => l.ElevationM);

    /// <summary>
    /// True when a per-level connector (D-091) has a circuit landing on this intermediate level: strictly between
    /// its bottom and top.
    /// </summary>
    public bool IsIntermediateLevel(Connector connector, Guid levelId)
    {
        if (!connector.UsesCircuits || levelId == connector.BottomLevelId || levelId == connector.TopLevelId)
        {
            return false;
        }

        Level? level = FindLevel(levelId);
        Level? bottom = FindLevel(connector.BottomLevelId);
        Level? top = FindLevel(connector.TopLevelId);
        return level is not null && bottom is not null && top is not null
            && level.ElevationM > bottom.ElevationM + 1e-6 && level.ElevationM < top.ElevationM - 1e-6;
    }

    /// <summary>
    /// True when people can get on or off the connector at this level: its ends, or a per-level stair's
    /// intermediate levels that aren't excluded (D-093). Travelators link nothing vertically.
    /// </summary>
    public bool ConnectorServes(Connector connector, Guid levelId) =>
        !connector.IsTravelator
        && (connector.BottomLevelId == levelId || connector.TopLevelId == levelId
            || (IsIntermediateLevel(connector, levelId) && !connector.ExcludedLevelIds.Contains(levelId)));

    /// <summary>Finds a connector by id.</summary>
    public Connector? FindConnector(Guid id) => Connectors.FirstOrDefault(c => c.Id == id);

    /// <summary>Finds a shaft by id.</summary>
    public Shaft? FindShaft(Guid id) => Shafts.FirstOrDefault(s => s.Id == id);

    /// <summary>The other unit of an escalator pair, or null.</summary>
    public Connector? FindPartner(Connector connector) =>
        connector.PairId is { } pair ? Connectors.FirstOrDefault(c => c.PairId == pair && c.Id != connector.Id) : null;

    /// <summary>
    /// Connectors drawn and editable on the given level: they start or end there, or a per-level stair has a
    /// circuit landing there (D-091).
    /// </summary>
    public IEnumerable<Connector> ConnectorsTouching(Guid levelId) =>
        Connectors.Where(c => c.BottomLevelId == levelId || c.TopLevelId == levelId || IsIntermediateLevel(c, levelId));

    /// <summary>
    /// True when the level's elevation lies within the shaft's base and top elevations (inclusive, D-056).
    /// False when either end level is missing.
    /// </summary>
    public bool ShaftSpans(Shaft shaft, Guid levelId)
    {
        Level? level = FindLevel(levelId);
        Level? bottom = FindLevel(shaft.BaseLevelId);
        Level? top = FindLevel(shaft.TopLevelId);
        if (level is null || bottom is null || top is null)
        {
            return false;
        }

        const double tolerance = 1e-6;
        return level.ElevationM >= bottom.ElevationM - tolerance && level.ElevationM <= top.ElevationM + tolerance;
    }

    /// <summary>
    /// Levels a shaft's lift serves (D-236): the levels it spans minus its no-stop levels (D-087), lowest first.
    /// </summary>
    public IReadOnlyList<Level> LiftStops(Shaft shaft) =>
        [.. Levels.Where(l => ShaftSpans(shaft, l.Id) && !shaft.NoStopLevelIds.Contains(l.Id)).OrderBy(l => l.ElevationM)];

    /// <summary>Shafts passing through the given level.</summary>
    public IEnumerable<Shaft> ShaftsSpanning(Guid levelId) => Shafts.Where(s => ShaftSpans(s, levelId));

    /// <summary>Levels ordered from lowest to highest elevation.</summary>
    public IReadOnlyList<Level> LevelsAscending() => [.. Levels.OrderBy(l => l.ElevationM)];

    /// <summary>Regions sketched on the given level.</summary>
    public IEnumerable<Region> RegionsOn(Guid levelId) => Regions.Where(r => r.LevelId == levelId);

    /// <summary>Levels ordered from highest to lowest elevation (the natural order for a level list).</summary>
    public IReadOnlyList<Level> LevelsByElevation() => [.. Levels.OrderByDescending(l => l.ElevationM)];

    /// <summary>Plan bounds of every region on a level; empty when the level has none.</summary>
    public Bounds2 GetLevelBounds(Guid levelId) =>
        RegionsOn(levelId).Aggregate(Bounds2.Empty, (b, r) => b.Union(Bounds2.FromPoints(r.Outline)));

    /// <summary>Plan bounds of every region in the study.</summary>
    public Bounds2 GetBounds() =>
        Regions.Aggregate(Bounds2.Empty, (b, r) => b.Union(Bounds2.FromPoints(r.Outline)));
}
