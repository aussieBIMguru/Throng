using System.Text.Json.Serialization;
using Throng.Core.Geometry;
using Throng.Core.Library;

namespace Throng.Core.Model;

/// <summary>
/// The root document: levels, sketched regions, settings and the embedded template library.
/// Devices, connectors, lifts and spawns join this type in later milestones.
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
