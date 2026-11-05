namespace Throng.Core.Validation;

/// <summary>
/// The CHECK rule catalogue (round 016, D-272). Ids are stable: studies store them in their ignore list
/// (<c>Study.IgnoredFindings</c>), so never rename one.
/// </summary>
public static class ValidationRules
{
    /// <summary>Findings no rule claimed.</summary>
    public const string General = "general";

    /// <summary>Levels: ids, heights, walkable area.</summary>
    public const string Levels = "levels";

    /// <summary>Regions: levels they're on, spans, areas, zone purposes.</summary>
    public const string Regions = "regions";

    /// <summary>Stairs, escalators, ramps, travelators and shafts: layout, landings, steepness, vertical links.</summary>
    public const string Vertical = "vertical";

    /// <summary>Devices and underlays.</summary>
    public const string Devices = "devices";

    /// <summary>Emission rules, mixes, journeys and the devices and zones they need.</summary>
    public const string Demand = "demand";

    /// <summary>Journey graph structure: one Spawn, no loops, wires and branch settings (D-302–D-306).</summary>
    public const string JourneyGraph = "journey-graph";

    /// <summary>Accessible devices for profiles that need them (D-213).</summary>
    public const string Accessible = "accessible";

    /// <summary>Train services (D-184–D-187).</summary>
    public const string Trains = "trains";

    /// <summary>Lifts (round 013).</summary>
    public const string Lifts = "lifts";

    /// <summary>Compiling the navigation world: floors under zones, devices and queues (D-106).</summary>
    public const string Setup = "setup";

    /// <summary>Spawns that can't reach their sinks or journey devices for their profiles' mobility (D-274).</summary>
    public const string Reachability = "reachability";

    /// <summary>Static capacity check: devices, lifts, train bursts and connector Level of Service (D-273).</summary>
    public const string Capacity = "capacity";

    /// <summary>Escalators running against all the demand between their levels (D-274).</summary>
    public const string EscalatorDemand = "escalator-demand";

    /// <summary>Devices no journey uses (D-274).</summary>
    public const string UnusedDevice = "unused-device";

    /// <summary>Walkable gaps narrower than the widest profile using the level (D-274).</summary>
    public const string NarrowGap = "narrow-gap";

    /// <summary>Journey nodes whose picks match nothing in the study, so they use their type / tag (round 020, D-349).</summary>
    public const string PicksFallback = "journey-picks-fallback";

    /// <summary>Every rule, in CHECK order.</summary>
    public static IReadOnlyList<ValidationRule> All { get; } =
    [
        new(Levels, "Model", "Levels"),
        new(Regions, "Model", "Regions"),
        new(Vertical, "Vertical", "Stairs, escalators, shafts"),
        new(Lifts, "Vertical", "Lifts"),
        new(Devices, "Model", "Devices and underlays"),
        new(Demand, "Demand", "Emission rules and journeys"),
        new(JourneyGraph, "Demand", "Journey graphs"),
        new(PicksFallback, "Demand", "Picks not in this study"),
        new(Accessible, "Demand", "Accessible devices"),
        new(Trains, "Demand", "Trains"),
        new(Setup, "Navigation", "Floors under zones and devices"),
        new(Reachability, "Navigation", "Reachable destinations"),
        new(Capacity, "Capacity", "Static capacity"),
        new(EscalatorDemand, "Capacity", "Escalator direction"),
        new(UnusedDevice, "Demand", "Unused devices"),
        new(NarrowGap, "Navigation", "Narrow gaps"),
        new(General, "Model", "General"),
    ];

    /// <summary>The rule with this id, or the general rule.</summary>
    public static ValidationRule Find(string? id) => All.FirstOrDefault(r => r.Id == id) ?? All[^1];
}
