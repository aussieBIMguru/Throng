namespace Throng.Sim;

/// <summary>
/// What the PERF overlay shows about the simulation (round 025, D-380): milliseconds per step in each
/// <see cref="Engine.SimEngine.PerfPhases"/> phase and in all, simulated seconds per wall second, and people, averaged
/// over about the last second of running.
/// </summary>
/// <param name="PhaseMs">Mean ms per step in each phase.</param>
/// <param name="StepMs">Mean ms per step in all.</param>
/// <param name="SpeedUp">Simulated seconds per wall-clock second (1 = real time).</param>
/// <param name="People">People in the model.</param>
public sealed record PerfSample(double[] PhaseMs, double StepMs, double SpeedUp, int People)
{
    /// <summary>Nothing measured yet.</summary>
    public static PerfSample Empty { get; } = new([], 0, 0, 0);
}
