using Throng.Sim.Engine;

namespace Throng.Sim.Batch;

/// <summary>
/// A finished batch (round 021, D-352–D-365): every run's results, each table as mean with min–max (D-358), each time
/// series as mean with a min–max band (D-359), the averaged run heatmaps (D-360) and a rounded mean in
/// <see cref="RunResults"/> form for the journey overlay (D-364). Built once and immutable; lives for the session
/// (D-362).
/// </summary>
public sealed class BatchResults
{
    /// <summary>How the batch ran.</summary>
    public required BatchOptions Options { get; init; }

    /// <summary>Runs that ran at once.</summary>
    public int Parallelism { get; init; } = 1;

    /// <summary>Wall-clock time the batch took.</summary>
    public TimeSpan Elapsed { get; init; }

    /// <summary>Every run, in seed order.</summary>
    public required IReadOnlyList<BatchRun> Runs { get; init; }

    /// <summary>Number of runs.</summary>
    public int Count => Runs.Count;

    /// <summary>Seconds at the start left out of the stats (the study's warm-up, D-365).</summary>
    public double WarmUpS { get; init; }

    /// <summary>Mean simulated seconds after the warm-up the stats cover.</summary>
    public double MeasuredS { get; init; }

    /// <summary>The long-wait threshold the journey rows used, seconds (D-231).</summary>
    public double LongWaitS { get; init; } = 120;

    /// <summary>One row per device, in model order.</summary>
    public required IReadOnlyList<DeviceBatchResult> Devices { get; init; }

    /// <summary>One row per measurement zone, in model order.</summary>
    public required IReadOnlyList<ZoneBatchResult> Zones { get; init; }

    /// <summary>Journey rows: every row any run had, the first run's order, later rows after their profile's rows.</summary>
    public required IReadOnlyList<JourneyBatchResult> Journeys { get; init; }

    /// <summary>One row per lift bank, in model order.</summary>
    public required IReadOnlyList<LiftBatchResult> Lifts { get; init; }

    /// <summary>Sample times, seconds from the start (the longest run's).</summary>
    public required double[] Times { get; init; }

    /// <summary>People in the model at each sample.</summary>
    public required BatchSeries InModel { get; init; }

    /// <summary>People emitted per minute before each sample.</summary>
    public required BatchSeries ArrivalsPerMin { get; init; }

    /// <summary>People who left per minute before each sample.</summary>
    public required BatchSeries ExitsPerMin { get; init; }

    /// <summary>Highest density anywhere at each sample, people per m².</summary>
    public required BatchSeries PeakDensity { get; init; }

    /// <summary>Queue length per device (model order) at each sample.</summary>
    public required IReadOnlyList<BatchSeries> Queues { get; init; }

    /// <summary>People in each measurement zone (model order) at each sample.</summary>
    public required IReadOnlyList<BatchSeries> ZoneOccupancy { get; init; }

    /// <summary>
    /// The means as a <see cref="RunResults"/> (counts rounded to whole people): the journey overlay's ACTUAL counts and
    /// shares (D-364), and anything else that reads a single run's results.
    /// </summary>
    public required RunResults Mean { get; init; }

    /// <summary>The averaged run heatmaps (D-360), or null when they weren't gathered.</summary>
    public BatchDensity? Density { get; init; }

    /// <summary>Number of time-series samples.</summary>
    public int SampleCount => Times.Length;

    /// <summary>The lowest and highest seed, e.g. for "SEEDS 42–46".</summary>
    public (int First, int Last) SeedRange => Runs.Count == 0 ? (Options.BaseSeed, Options.BaseSeed) : (Runs[0].Seed, Runs[^1].Seed);
}
