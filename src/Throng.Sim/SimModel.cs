using Throng.Core.Library;
using Throng.Core.Model;
using Throng.Core.Validation;
using Throng.Sim.Nav;

namespace Throng.Sim;

/// <summary>An agent profile resolved for a run: the template, its routing class and its colour.</summary>
public sealed class SimProfile
{
    /// <summary>Position in <see cref="SimModel.Profiles"/>.</summary>
    public required int Index { get; init; }

    /// <summary>The template values.</summary>
    public required AgentProfileTemplate Template { get; init; }

    /// <summary>Routing class.</summary>
    public required MobilityClass Mobility { get; init; }

    /// <summary>Display colour, 0..1.</summary>
    public required float R { get; init; }

    /// <summary>Display colour, 0..1.</summary>
    public required float G { get; init; }

    /// <summary>Display colour, 0..1.</summary>
    public required float B { get; init; }
}

/// <summary>One emission rule ready to run: where people appear and where they may go.</summary>
public sealed class SimEmitter
{
    /// <summary>Position in <see cref="SimModel.Emitters"/>.</summary>
    public required int Index { get; init; }

    /// <summary>Spawn zone id.</summary>
    public required Guid RegionId { get; init; }

    /// <summary>"Zone name rule n", for messages.</summary>
    public required string Label { get; init; }

    /// <summary>The rule.</summary>
    public required EmissionRule Rule { get; init; }

    /// <summary>Index into <see cref="SimModel.Profiles"/>.</summary>
    public required int Profile { get; init; }

    /// <summary>Floor surface of the zone.</summary>
    public required int Floor { get; init; }

    /// <summary>Walkable floor cells inside the zone.</summary>
    public required int[] Cells { get; init; }

    /// <summary>True for nearest-sink rules.</summary>
    public required bool Nearest { get; init; }

    /// <summary>Reachable destination sinks (nav sink index) with weights; for nearest-sink rules, the candidates (weight 1).</summary>
    public required (int Sink, double Weight)[] Destinations { get; init; }
}

/// <summary>Everything a run needs, compiled once by <see cref="SimSetup"/>; immutable while running.</summary>
public sealed class SimModel
{
    /// <summary>Fields and portal graph.</summary>
    public required NavRoutes Routes { get; init; }

    /// <summary>Profiles in use.</summary>
    public required IReadOnlyList<SimProfile> Profiles { get; init; }

    /// <summary>Emission rules in use.</summary>
    public required IReadOnlyList<SimEmitter> Emitters { get; init; }

    /// <summary>RNG seed.</summary>
    public required int Seed { get; init; }

    /// <summary>Clock start.</summary>
    public required TimeSpan StartTimeOfDay { get; init; }

    /// <summary>Fixed duration in seconds, or null to run until stopped (D-103).</summary>
    public required double? DurationS { get; init; }

    /// <summary>Fixed step in seconds.</summary>
    public required double TimeStepS { get; init; }

    /// <summary>Keep-side bias (D-105).</summary>
    public required KeepSide KeepSide { get; init; }

    /// <summary>Warnings and info found while compiling.</summary>
    public required IReadOnlyList<ValidationMessage> Findings { get; init; }

    /// <summary>Navigation world.</summary>
    public NavWorld World => Routes.World;
}
