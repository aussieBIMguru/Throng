using System.Diagnostics;

namespace Throng.Sim.Engine;

/// <summary>
/// Step timings by phase (round 025, D-380): the PERF overlay and the stadium bench read where a step's time goes.
/// Reading the clock never changes the run (no random draws, nothing the simulation reads).
/// </summary>
public sealed partial class SimEngine
{
    /// <summary>The phases a step is timed in, in step order.</summary>
    public static IReadOnlyList<string> PerfPhases { get; } =
    [
        "groups, arrivals",
        "trains, lifts",
        "waits, spawn",
        "review",
        "journeys",
        "plan",
        "move",
        "leave",
        "maps, results",
    ];

    private readonly long[] _phaseTicks = new long[9];
    private long _perfSteps;

    /// <summary>Steps timed since the engine was made (or reset).</summary>
    public long PerfSteps => _perfSteps;

    /// <summary>
    /// Copies the time spent in each <see cref="PerfPhases"/> phase since the engine was made, in
    /// <see cref="Stopwatch"/> ticks, into <paramref name="ticks"/> (at least <see cref="PerfPhases"/>.Count long).
    /// </summary>
    public void CopyPhaseTicks(Span<long> ticks) => _phaseTicks.AsSpan().CopyTo(ticks);

    /// <summary>Adds the time since <paramref name="mark"/> to <paramref name="phase"/> and moves the mark on.</summary>
    private void Lap(ref long mark, int phase)
    {
        long now = Stopwatch.GetTimestamp();
        _phaseTicks[phase] += now - mark;
        mark = now;
    }

    private void ResetPerf()
    {
        Array.Clear(_phaseTicks);
        _perfSteps = 0;
    }
}
