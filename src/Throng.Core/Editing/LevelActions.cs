using System.Globalization;
using Throng.Core.Common;
using Throng.Core.Model;

namespace Throng.Core.Editing;

/// <summary>What deleting a level would take with it (for the confirm message, D-142).</summary>
/// <param name="Regions">Regions drawn on the level.</param>
/// <param name="Connectors">Stairs, escalators and ramps starting on it.</param>
/// <param name="Shafts">Shafts based on it.</param>
/// <param name="Devices">Devices standing on it.</param>
/// <param name="Underlays">Underlays on it.</param>
public readonly record struct LevelContents(int Regions, int Connectors, int Shafts, int Devices, int Underlays)
{
    /// <summary>Total number of items.</summary>
    public int Total => Regions + Connectors + Shafts + Devices + Underlays;

    /// <summary>Short list such as "4 regions, 2 connectors"; empty when there's nothing.</summary>
    public string Describe()
    {
        List<string> parts = [];
        Add(parts, Regions, "region");
        Add(parts, Connectors, "connector");
        Add(parts, Shafts, "shaft");
        Add(parts, Devices, "device");
        Add(parts, Underlays, "underlay");
        return string.Join(", ", parts);
    }

    private static void Add(List<string> parts, int count, string noun)
    {
        if (count > 0)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{count} {noun}{(count == 1 ? "" : "s")}"));
        }
    }
}

/// <summary>
/// Adding and deleting levels (D-142, D-143). Inserting between two levels splits the active storey at
/// mid-height, so nothing that exists moves; above the top or below the bottom the new level takes the active
/// level's storey height. Deleting removes the level and everything whose home is on it; references from other
/// levels (a stair arriving there, a wall spanning to it) are left for CHECK to flag.
/// </summary>
public static class LevelActions
{
    /// <summary>Smallest floor-to-floor height a split may leave, in metres.</summary>
    public const double MinStoreyM = 1.0;

    /// <summary>
    /// Plans a new level next to <paramref name="active"/>. The level isn't added; pass it to <see cref="Add"/>.
    /// Fails when a split would leave less than <see cref="MinStoreyM"/> each side.
    /// </summary>
    /// <param name="above">True to insert above the active level, false below.</param>
    public static Result<Level> Plan(Study study, Level active, bool above)
    {
        ArgumentNullException.ThrowIfNull(study);
        ArgumentNullException.ThrowIfNull(active);

        Level? neighbour = above ? study.LevelAbove(active) : LevelBelow(study, active);
        double elevation;
        double height;
        if (neighbour is not null)
        {
            // Split the storey (D-143): mid-height, half the gap each side.
            double gap = Math.Abs(neighbour.ElevationM - active.ElevationM);
            if (gap / 2 < MinStoreyM - 1e-9)
            {
                return Result<Level>.Fail(string.Create(CultureInfo.InvariantCulture,
                    $"There's only {gap:0.##} m between '{active.Name}' and '{neighbour.Name}', too little to fit a level."));
            }

            elevation = (active.ElevationM + neighbour.ElevationM) / 2;
            height = gap / 2;
        }
        else
        {
            height = study.StoreyHeight(active);
            elevation = above ? active.ElevationM + height : active.ElevationM - height;
        }

        return Result<Level>.Ok(new Level
        {
            Name = UniqueName(study),
            ElevationM = Math.Round(elevation, 3),
            HeightM = Math.Round(height, 3),
        });
    }

    /// <summary>Adds a planned level; undo removes it again.</summary>
    public static IUndoableAction Add(Study study, Level level, Action? changed) =>
        new AddItemsAction<Level>($"Add level ({level.Name})", study.Levels, [level], changed);

    /// <summary>What deleting <paramref name="level"/> would remove.</summary>
    public static LevelContents ContentsOf(Study study, Level level)
    {
        Guid id = level.Id;
        return new LevelContents(
            study.Regions.Count(r => r.LevelId == id),
            study.Connectors.Count(c => c.BottomLevelId == id),
            study.Shafts.Count(s => s.BaseLevelId == id),
            study.Devices.Count(d => d.LevelId == id),
            study.Underlays.Count(u => u.LevelId == id));
    }

    /// <summary>
    /// Deletes a level with its regions, devices, underlays, the connectors that start on it and the shafts based
    /// on it, as one undo step. Fails for the study's only level.
    /// </summary>
    public static Result<IUndoableAction> Delete(Study study, Level level, Action? changed)
    {
        ArgumentNullException.ThrowIfNull(study);
        ArgumentNullException.ThrowIfNull(level);
        if (study.Levels.Count <= 1)
        {
            return Result<IUndoableAction>.Fail("A study needs at least one level.");
        }

        Guid id = level.Id;
        List<IUndoableAction> steps =
        [
            new RemoveItemsAction<Region>("Remove regions", study.Regions, [.. study.Regions.Where(r => r.LevelId == id)], null),
            new RemoveItemsAction<Connector>("Remove connectors", study.Connectors, [.. study.Connectors.Where(c => c.BottomLevelId == id)], null),
            new RemoveItemsAction<Shaft>("Remove shafts", study.Shafts, [.. study.Shafts.Where(s => s.BaseLevelId == id)], null),
            new RemoveItemsAction<Device>("Remove devices", study.Devices, [.. study.Devices.Where(d => d.LevelId == id)], null),
            new RemoveItemsAction<Underlay>("Remove underlays", study.Underlays, [.. study.Underlays.Where(u => u.LevelId == id)], null),
            new RemoveItemsAction<Level>("Remove level", study.Levels, [level], null),
        ];
        return Result<IUndoableAction>.Ok(new CompositeAction($"Delete level ({level.Name})", steps, changed));
    }

    /// <summary>The nearest level below, or null for the lowest.</summary>
    public static Level? LevelBelow(Study study, Level level) =>
        study.Levels.Where(l => l.ElevationM < level.ElevationM - 1e-6).OrderByDescending(l => l.ElevationM).FirstOrDefault();

    private static string UniqueName(Study study)
    {
        for (int n = study.Levels.Count + 1; ; n++)
        {
            string name = string.Create(CultureInfo.InvariantCulture, $"Level {n}");
            if (!study.Levels.Any(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                return name;
            }
        }
    }
}
