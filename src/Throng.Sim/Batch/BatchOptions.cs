namespace Throng.Sim.Batch;

/// <summary>
/// How to run a batch (round 021, D-354–D-356, D-361): <see cref="Runs"/> runs on seeds <see cref="BaseSeed"/> …
/// <see cref="BaseSeed"/> + <see cref="Runs"/> − 1, each <see cref="DurationS"/> simulated seconds long, at most
/// <see cref="Parallelism"/> at once.
/// </summary>
/// <param name="BaseSeed">The first run's seed (the study's seed).</param>
/// <param name="Runs">Number of runs (2–20 in the app).</param>
/// <param name="DurationS">Simulated seconds per run: the study's duration, or the one asked for "until stopped".</param>
/// <param name="Parallelism">Runs at once; 0 for <see cref="DefaultParallelism"/>.</param>
public sealed record BatchOptions(int BaseSeed, int Runs, double DurationS, int Parallelism = 0)
{
    /// <summary>Fewest runs the app offers.</summary>
    public const int MinRuns = 2;

    /// <summary>Most runs the app offers.</summary>
    public const int MaxRuns = 20;

    /// <summary>Runs the app suggests.</summary>
    public const int DefaultRuns = 5;

    /// <summary>Runs at once: one fewer than the machine's cores (at least one), never more than the runs (D-356).</summary>
    public static int DefaultParallelism(int runs) => Math.Clamp(Environment.ProcessorCount - 1, 1, Math.Max(1, runs));

    /// <summary>The seed of run <paramref name="run"/> (0-based); wraps past <see cref="int.MaxValue"/> like the engine's seed cast.</summary>
    public int SeedOf(int run) => unchecked(BaseSeed + run);

    /// <summary>Runs at once once 0 is resolved and the value is capped at <see cref="Runs"/>.</summary>
    public int EffectiveParallelism => Parallelism > 0 ? Math.Clamp(Parallelism, 1, Math.Max(1, Runs)) : DefaultParallelism(Runs);
}
