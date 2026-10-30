namespace Throng.Sim.Engine;

/// <summary>
/// A copy of a run's results (round 011c; zones and journeys round 012): device stats after the warm-up and the time series from the start, sampled
/// every <see cref="SeriesIntervalS"/> (D-218–D-222). Built on the simulation thread and immutable after, so the UI
/// may read it freely.
/// </summary>
public sealed class RunResults
{
    /// <summary>Simulated seconds between time-series samples.</summary>
    public const double SeriesIntervalS = 5.0;

    /// <summary>An empty result (nothing run yet).</summary>
    public static RunResults Empty { get; } = new()
    {
        Devices = [],
        DeviceNames = [],
        Times = [],
        InModel = [],
        ArrivalsPerMin = [],
        ExitsPerMin = [],
        PeakDensity = [],
        Queues = [],
    };

    /// <summary>Bumped by the engine when anything in the results changes.</summary>
    public long Version { get; init; }

    /// <summary>Simulated time the copy was taken, seconds.</summary>
    public double TimeS { get; init; }

    /// <summary>Seconds at the start left out of the device stats (D-218).</summary>
    public double WarmUpS { get; init; }

    /// <summary>Simulated seconds after the warm-up the device stats cover.</summary>
    public double MeasuredS { get; init; }

    /// <summary>One row per device, in <see cref="SimModel.Devices"/> order.</summary>
    public required IReadOnlyList<DeviceResult> Devices { get; init; }

    /// <summary>Device names, in device order (column headers for the queue series).</summary>
    public required IReadOnlyList<string> DeviceNames { get; init; }

    /// <summary>Sample times, seconds from the start.</summary>
    public required double[] Times { get; init; }

    /// <summary>People in the model at each sample.</summary>
    public required int[] InModel { get; init; }

    /// <summary>People emitted per minute over the interval before each sample.</summary>
    public required float[] ArrivalsPerMin { get; init; }

    /// <summary>People who left (reached an exit or boarded) per minute over the interval before each sample.</summary>
    public required float[] ExitsPerMin { get; init; }

    /// <summary>Highest smoothed density anywhere on the map at each sample, people per square metre.</summary>
    public required float[] PeakDensity { get; init; }

    /// <summary>Queue length per device at each sample: sample-major, <c>Queues[sample * devices + device]</c>.</summary>
    public required int[] Queues { get; init; }

    /// <summary>A single queue wait longer than this counted as a long wait, seconds (D-231).</summary>
    public double LongWaitS { get; init; } = 120;

    /// <summary>One row per measurement zone, in <see cref="SimModel.Zones"/> order (D-228).</summary>
    public IReadOnlyList<ZoneResult> Zones { get; init; } = [];

    /// <summary>Zone names, in zone order (column headers for the occupancy series).</summary>
    public IReadOnlyList<string> ZoneNames { get; init; } = [];

    /// <summary>
    /// People in each zone at each sample: sample-major, <c>ZoneOccupancy[sample * zones + zone]</c> (D-228, D-233).
    /// </summary>
    public int[] ZoneOccupancy { get; init; } = [];

    /// <summary>Journey rows: profile × journey, with an ALL row after a profile's rows when it has two or more (D-229).</summary>
    public IReadOnlyList<JourneyResult> Journeys { get; init; } = [];

    /// <summary>One row per lift bank, in <see cref="SimModel.Lifts"/> order (round 015, D-260).</summary>
    public IReadOnlyList<LiftResult> Lifts { get; init; } = [];

    /// <summary>Number of time-series samples.</summary>
    public int SampleCount => Times.Length;

    /// <summary>Queue length of one device at one sample.</summary>
    public int QueueAt(int sample, int device) => Queues[(sample * Devices.Count) + device];

    /// <summary>People in one zone at one sample.</summary>
    public int ZoneAt(int sample, int zone) => ZoneOccupancy[(sample * Zones.Count) + zone];
}
