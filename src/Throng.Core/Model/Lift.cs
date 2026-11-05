using Throng.Core.Library;

namespace Throng.Core.Model;

/// <summary>
/// A lift bank inside a shaft (round 013, D-236–D-244): 1–4 cars from a lift preset, side by side along the shaft's
/// front edge, with front and/or rear doors per served level, an automatic landing in front of each door, a lobby
/// level and a dispatch mode, optionally switched by time of day. Served levels are the shaft's levels minus its
/// no-stop levels (D-087). Format v9.
/// </summary>
public sealed class Lift
{
    /// <summary>Most cars in one bank (D-237).</summary>
    public const int MaxCars = 4;

    /// <summary>Default landing depth in front of a door, metres (D-241).</summary>
    public const double DefaultLandingDepthM = 2.5;

    /// <summary>Display name, e.g. "Lifts A".</summary>
    public string Name { get; set; } = "Lift";

    /// <summary>Id of the preset it was made from (embedded in the study library), if any.</summary>
    public Guid? TemplateId { get; set; }

    /// <summary>Preset key, e.g. "lift.passenger-13".</summary>
    public string PresetKey { get; set; } = string.Empty;

    /// <summary>Cars in the bank, 1–<see cref="MaxCars"/>.</summary>
    public int Cars { get; set; } = 1;

    /// <summary>Index of the shaft outline edge the front doors are on (edge i runs from vertex i to i + 1, D-242).</summary>
    public int FrontEdge { get; set; }

    /// <summary>Landing depth in front of each door, metres (D-241).</summary>
    public double LandingDepthM { get; set; } = DefaultLandingDepthM;

    /// <summary>Lobby level for Up-peak (D-244); null for the lowest served level.</summary>
    public Guid? LobbyLevelId { get; set; }

    /// <summary>Dispatch mode (copied from the preset; Destination dispatch runs as Collective for now).</summary>
    public LiftDispatch Dispatch { get; set; } = LiftDispatch.Collective;

    /// <summary>Time-of-day switches (D-238), in any order; the latest row at or before the clock applies.</summary>
    public List<LiftScheduleEntry> Schedule { get; set; } = [];

    /// <summary>
    /// Door use per level. A served level without a row has a front door for both ways and no rear door.
    /// </summary>
    public List<LiftLevelDoors> Doors { get; set; } = [];

    /// <summary>Door use at a level (the default when it has no row).</summary>
    public LiftLevelDoors DoorsAt(Guid levelId) =>
        Doors.FirstOrDefault(d => d.LevelId == levelId) ?? new LiftLevelDoors { LevelId = levelId };

    /// <summary>An independent copy.</summary>
    public Lift Clone() => new()
    {
        Name = Name,
        TemplateId = TemplateId,
        PresetKey = PresetKey,
        Cars = Cars,
        FrontEdge = FrontEdge,
        LandingDepthM = LandingDepthM,
        LobbyLevelId = LobbyLevelId,
        Dispatch = Dispatch,
        Schedule = [.. Schedule.Select(s => s.Clone())],
        Doors = [.. Doors.Select(d => d.Clone())],
    };
}
