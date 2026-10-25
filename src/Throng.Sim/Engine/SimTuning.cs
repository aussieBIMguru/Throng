namespace Throng.Sim.Engine;

/// <summary>
/// Switches for the round 007 crowd rules (D-112, D-113, D-120, D-128, D-132), so each can be turned off to see what it does.
/// All on by default; used by the diagnostics test and available for later tuning UI.
/// </summary>
public sealed record SimTuning
{
    /// <summary>The defaults: every rule on.</summary>
    public static SimTuning Default { get; } = new();

    /// <summary>Keep-clear run-offs in front of one-way exits (D-112).</summary>
    public bool RunOffs { get; init; } = true;

    /// <summary>Walk straight out of a one-way exit for a moment after stepping off (D-112).</summary>
    public bool WalkClear { get; init; } = true;

    /// <summary>Hold boarding while the connector's own exit landing is jammed (D-112).</summary>
    public bool BoardingHold { get; init; } = true;

    /// <summary>Cost belts per agent, stander or walker (D-113).</summary>
    public bool BeltCost { get; init; } = true;

    /// <summary>Belt waits from measured boardings and the queue at the entry; off = D-111's load ÷ capacity (D-113).</summary>
    public bool MeasuredWait { get; init; } = true;

    /// <summary>Queuers re-choose when the wait is long (D-113).</summary>
    public bool Balk { get; init; } = true;

    /// <summary>
    /// Keep-side rules at connector ends (D-124, D-125): constant lanes on and at two-way stairs and ramps; fading
    /// near one-way entries and when barely moving. Off = the plain oncoming-only nudge everywhere.
    /// </summary>
    public bool KeepSideFade { get; init; } = true;

    /// <summary>Weaker crowd push at and on metered belts, so people close up and board near the cap (D-128).</summary>
    public bool CombSpacing { get; init; } = true;

    /// <summary>
    /// Stair and ramp lanes only with real counterflow (D-132); off = constant lanes as in D-125 (when
    /// <see cref="KeepSideFade"/> is on).
    /// </summary>
    public bool CounterflowLanes { get; init; } = true;

    /// <summary>Weaker crowd push between people going the same way on a stair or ramp landing (D-135).</summary>
    public bool LandingSqueeze { get; init; } = true;

    /// <summary>People without progress for 4 s drop the keep-side push and push others softly (D-136).</summary>
    public bool StallEscape { get; init; } = true;

    /// <summary>Carry straight on 0.6 m into a landing before turning (D-137).</summary>
    public bool LandingCarry { get; init; } = true;
}

/// <summary>Where and how an agent was removed as stuck (D-106), for diagnostics.</summary>
/// <param name="TimeS">Simulated time of removal.</param>
/// <param name="Surface">Surface it was on.</param>
/// <param name="X">Plan position.</param>
/// <param name="Y">Plan position.</param>
/// <param name="Mode">What it was walking towards.</param>
/// <param name="TargetPortal">Portal it was heading for or stepping off at (−1 when walking to its sink).</param>
public readonly record struct StuckRecord(double TimeS, int Surface, double X, double Y, AgentMode Mode, int TargetPortal);
