namespace Throng.Sim.Batch;

/// <summary>
/// One time series across a batch's runs (round 021, D-359): per sample time, the mean with the lowest and highest
/// run. A sample only counts the runs that reached it.
/// </summary>
/// <param name="Mean">Mean per sample.</param>
/// <param name="Min">Lowest run per sample.</param>
/// <param name="Max">Highest run per sample.</param>
public sealed record BatchSeries(double[] Mean, double[] Min, double[] Max)
{
    /// <summary>No samples.</summary>
    public static BatchSeries Empty { get; } = new([], [], []);

    /// <summary>Number of samples.</summary>
    public int Count => Mean.Length;
}
