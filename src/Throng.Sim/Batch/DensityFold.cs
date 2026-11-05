using Throng.Sim.Engine;

namespace Throng.Sim.Batch;

/// <summary>
/// Folds runs' cumulative density maps into a <see cref="BatchDensity"/> (round 021, D-360). Runs must be added in
/// batch order (<see cref="BatchRunner"/> holds early finishers back), so the sums, and so the maps, never depend on
/// which thread finished first.
/// </summary>
internal sealed class DensityFold
{
    private readonly DensityGrid _layout;
    private readonly float[] _max;
    private readonly double[] _meanSum;
    private readonly double[] _losSum;
    private readonly double[] _footfallSum;
    private double _accumulatedSum;
    private int _runs;

    public DensityFold(DensityGrid layout)
    {
        _layout = layout;
        int cells = layout.CellCount;
        _max = new float[cells];
        _meanSum = new double[cells];
        _losSum = new double[cells];
        _footfallSum = new double[cells];
    }

    /// <summary>Copies one finished run's cumulative maps (call on the run's own thread, before handing them over).</summary>
    public static RunMaps Capture(DensityGrid grid) => new(
        grid.Values(DensityMeasure.Max).ToArray(),
        grid.Values(DensityMeasure.Mean).ToArray(),
        grid.Values(DensityMeasure.LosEOrWorseS).ToArray(),
        grid.Values(DensityMeasure.Footfall).ToArray(),
        grid.AccumulatedS);

    /// <summary>Adds the next run's maps (in batch order).</summary>
    public void Add(RunMaps maps)
    {
        int cells = Math.Min(_max.Length, maps.Max.Length);
        for (int c = 0; c < cells; c++)
        {
            _max[c] = Math.Max(_max[c], maps.Max[c]);
            _meanSum[c] += maps.Mean[c];
            _losSum[c] += maps.LosEOrWorseS[c];
            _footfallSum[c] += maps.Footfall[c];
        }

        _accumulatedSum += maps.AccumulatedS;
        _runs++;
    }

    /// <summary>The batch's maps from the runs added so far.</summary>
    public BatchDensity Build()
    {
        int n = Math.Max(1, _runs);
        return new BatchDensity(_layout, (float[])_max.Clone(), Average(_meanSum, n), Average(_losSum, n), Average(_footfallSum, n), _accumulatedSum / n);
    }

    private static float[] Average(double[] sums, int runs)
    {
        var values = new float[sums.Length];
        for (int c = 0; c < sums.Length; c++)
        {
            values[c] = (float)(sums[c] / runs);
        }

        return values;
    }

    /// <summary>One run's cumulative maps, one value per cell.</summary>
    internal sealed record RunMaps(float[] Max, float[] Mean, float[] LosEOrWorseS, float[] Footfall, double AccumulatedS);
}
