using Throng.Sim.Engine;

namespace Throng.Sim.Batch;

/// <summary>
/// A batch's run heatmaps (round 021, D-360): the average of every run's cumulative maps (mean density, time at LoS E
/// or worse, footfall), and for peak density the highest any run reached. There is no live map for a batch.
/// </summary>
public sealed class BatchDensity
{
    private readonly float[] _max;
    private readonly float[] _mean;
    private readonly float[] _losEOrWorseS;
    private readonly float[] _footfall;

    internal BatchDensity(DensityGrid layout, float[] max, float[] mean, float[] losEOrWorseS, float[] footfall, double accumulatedS)
    {
        Layout = layout;
        _max = max;
        _mean = mean;
        _losEOrWorseS = losEOrWorseS;
        _footfall = footfall;
        AccumulatedS = accumulatedS;
    }

    /// <summary>The map's layers and cell kinds (a fresh grid on the batch's model; its own values are unused).</summary>
    public DensityGrid Layout { get; }

    /// <summary>Mean seconds each run gathered into its cumulative maps (after the warm-up).</summary>
    public double AccumulatedS { get; }

    /// <summary>True for the measures a batch has (every cumulative one; not <see cref="DensityMeasure.Live"/>).</summary>
    public static bool Has(DensityMeasure measure) => measure != DensityMeasure.Live;

    /// <summary>The batch's values for <paramref name="measure"/>, one per cell in <see cref="Layout"/> order; empty for live.</summary>
    public ReadOnlySpan<float> Values(DensityMeasure measure) => measure switch
    {
        DensityMeasure.Max => _max,
        DensityMeasure.Mean => _mean,
        DensityMeasure.LosEOrWorseS => _losEOrWorseS,
        DensityMeasure.Footfall => _footfall,
        _ => ReadOnlySpan<float>.Empty,
    };
}
