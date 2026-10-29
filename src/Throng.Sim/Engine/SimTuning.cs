namespace Throng.Sim.Engine;

/// <summary>
/// Switches for the crowd rules (round 007 onwards: D-112, D-113, D-120, D-128, D-132 … D-174, D-175, D-179–D-182), so
/// each can be turned off to see what it does.
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

    /// <summary>
    /// Stair and ramp waits count stalled queuers at the entry twice (D-133). Off by default since D-138: it made
    /// stairs look slow and pulled people onto escalators too hard. Kept for the diagnostics.
    /// </summary>
    public bool StalledStairWait { get; init; }

    /// <summary>Stair time weighed by each profile's stair effort in route choice (D-174); off = time only.</summary>
    public bool StairEffort { get; init; } = true;

    /// <summary>Standing belt rides feel 0.6× their time in route choice (D-175); off = full time.</summary>
    public bool StandingRest { get; init; } = true;

    /// <summary>
    /// Stair and ramp waits measured like belts (D-181): people near the entry ÷ the measured flow onto it, plus a
    /// packed flight's extra time. Off = D-111's people heading for it ÷ capacity.
    /// </summary>
    public bool StairMeasuredWait { get; init; } = true;

    /// <summary>Live waits count only for options within each profile's sight radius (D-179); off = everyone knows every wait.</summary>
    public bool SightLimited { get; init; } = true;

    /// <summary>
    /// Look again every few seconds while the chosen connector or exit is in sight (D-180); off = D-113's re-choice
    /// only within 5 m of a connector entry with a wait over 20 s.
    /// </summary>
    public bool ApproachReview { get; init; } = true;

    /// <summary>Nearest-sink choice adds each exit's crowd wait (D-182); off = walking time only.</summary>
    public bool ExitCrowd { get; init; } = true;

    /// <summary>
    /// Stress counts walkers as held up by their progress along the route, not their speed (D-191), and queuers
    /// always; off = D-145's instantaneous speed, so people jiggling in a jam stay calm.
    /// </summary>
    public bool ProgressStress { get; init; } = true;

    /// <summary>Stress builds from the first second held up (D-193); off = 0 until half the patience (D-145).</summary>
    public bool EarlyStress { get; init; } = true;

    /// <summary>Density above the profile's tolerance counts as held up (D-194).</summary>
    public bool CrowdStress { get; init; } = true;

    /// <summary>
    /// Stair landings: carry deep into the landing on the outside of the turn and turn early on the inside (D-192);
    /// off = D-137's fixed 0.6 m carry (when <see cref="LandingCarry"/> is on).
    /// </summary>
    public bool LandingWaypoints { get; init; } = true;

    /// <summary>
    /// Gate lanes (D-209): the next person through a lane waits at the end of their service until whoever went before
    /// is past the middle of the gate, and the walk-through timeout only runs out without progress; off = r009's fixed
    /// 6 s timeout and no wait.
    /// </summary>
    public bool LaneClear { get; init; } = true;

    /// <summary>
    /// Accessible devices (D-213, D-224): people who need accessible devices use only accessible ones of a step's type
    /// on floors that have one, and others add <see cref="SimEngine.AccessibleAvoidS"/> to an accessible device's cost
    /// unless their profile favours it; off = r011a (preferences only).
    /// </summary>
    public bool AccessibleRule { get; init; } = true;

    /// <summary>
    /// Lift priority (D-247, D-248): people who need accessible devices (or are lift/ramp only) board first at every
    /// lift, and while one of them waits at a lift everyone else in sight of it sees it as
    /// <see cref="SimEngine.LiftPriorityPenaltyS"/> slower; off = r013a (priority only at accessible-priority presets,
    /// no penalty).
    /// </summary>
    public bool LiftPriority { get; init; } = true;
}

/// <summary>Where and how an agent was removed as stuck (D-106), for diagnostics.</summary>
/// <param name="TimeS">Simulated time of removal.</param>
/// <param name="Surface">Surface it was on.</param>
/// <param name="X">Plan position.</param>
/// <param name="Y">Plan position.</param>
/// <param name="Mode">What it was walking towards.</param>
/// <param name="TargetPortal">Portal it was heading for or stepping off at (−1 when walking to its sink).</param>
public readonly record struct StuckRecord(double TimeS, int Surface, double X, double Y, AgentMode Mode, int TargetPortal);
