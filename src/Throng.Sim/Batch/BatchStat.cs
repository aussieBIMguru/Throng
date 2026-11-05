namespace Throng.Sim.Batch;

/// <summary>
/// One number across a batch's runs (round 021, D-358): the mean with the lowest and highest run, and how many runs it
/// covers. Times (waits, journey times) only count runs that had someone to time; counts cover every run.
/// </summary>
/// <param name="Mean">Mean over the runs counted.</param>
/// <param name="Min">Lowest run.</param>
/// <param name="Max">Highest run.</param>
/// <param name="Runs">Runs counted (0 when none had a value: all three are then 0).</param>
public readonly record struct BatchStat(double Mean, double Min, double Max, int Runs)
{
    /// <summary>A single run's value (mean = min = max).</summary>
    public static BatchStat Of(double value) => new(value, value, value, 1);

    /// <summary>Mean, min and max of <paramref name="values"/>, summed in order (so the result never depends on threads).</summary>
    public static BatchStat Of(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return default;
        }

        double sum = 0, min = double.PositiveInfinity, max = double.NegativeInfinity;
        for (int i = 0; i < values.Count; i++)
        {
            double v = values[i];
            sum += v;
            min = Math.Min(min, v);
            max = Math.Max(max, v);
        }

        return new BatchStat(sum / values.Count, min, max, values.Count);
    }

    /// <summary>The mean rounded to a whole number (counts on the journey overlay, D-364).</summary>
    public long RoundedMean => (long)Math.Round(Mean, MidpointRounding.AwayFromZero);
}
