namespace Throng.Core.Library;

/// <summary>
/// The shape of a <see cref="Distribution"/> for the editor's small plot (round 017, D-289), and the consistency
/// fix-ups an edit needs: bounds in order, the mode inside them, and the mean the capacity check reads kept in step
/// with the bounds of uniform and triangular shapes.
/// </summary>
public static class DistributionShape
{
    /// <summary>The range worth plotting: the bounds, or mean ± 4 sd, or 0 to 4 × the mean for exponential.</summary>
    public static (double Lo, double Hi) Range(Distribution d)
    {
        (double lo, double hi) = d.Kind switch
        {
            DistributionKind.Fixed => (d.Value - 1, d.Value + 1),
            DistributionKind.Exponential => (0, Math.Max(1e-3, d.Mean) * 4),
            DistributionKind.NormalClamped => (
                Math.Max(d.Min, d.Mean - (4 * Math.Abs(d.StdDev))),
                Math.Min(d.Max, d.Mean + (4 * Math.Abs(d.StdDev)))),
            _ => (d.Min, d.Max),
        };

        if (!double.IsFinite(lo) || !double.IsFinite(hi))
        {
            return (0, 1);
        }

        if (hi - lo < 1e-6)
        {
            return (lo - 0.5, hi + 0.5);
        }

        return (lo, hi);
    }

    /// <summary>Relative density at <paramref name="x"/> (not normalised: the plot scales to its peak).</summary>
    public static double Density(Distribution d, double x)
    {
        switch (d.Kind)
        {
            case DistributionKind.Fixed:
                return Math.Abs(x - d.Value) < 1e-9 ? 1 : 0;

            case DistributionKind.Uniform:
                return x >= d.Min && x <= d.Max ? 1 : 0;

            case DistributionKind.Triangular:
            {
                if (x < d.Min || x > d.Max || d.Max - d.Min < 1e-9)
                {
                    return 0;
                }

                double mode = Math.Clamp(d.Mode, d.Min, d.Max);
                return x <= mode
                    ? (mode - d.Min < 1e-9 ? 1 : (x - d.Min) / (mode - d.Min))
                    : (d.Max - mode < 1e-9 ? 1 : (d.Max - x) / (d.Max - mode));
            }

            case DistributionKind.NormalClamped:
            {
                if (x < d.Min || x > d.Max)
                {
                    return 0;
                }

                double sd = Math.Max(1e-6, Math.Abs(d.StdDev));
                double z = (x - d.Mean) / sd;
                return Math.Exp(-0.5 * z * z);
            }

            case DistributionKind.Exponential:
            {
                double mean = Math.Max(1e-6, d.Mean);
                return x < 0 ? 0 : Math.Exp(-x / mean);
            }

            default:
                return 0;
        }
    }

    /// <summary>
    /// A consistent copy: non-negative where a time or speed can't be negative, Min ≤ Max, the mode inside them, a
    /// non-negative spread, and <see cref="Distribution.Mean"/> recomputed for Fixed, Uniform and Triangular (it's what
    /// <see cref="Distribution.ExpectedValue"/> and the static capacity check read).
    /// </summary>
    public static Distribution Normalised(Distribution d, bool nonNegative = true)
    {
        double Clip(double v) => !double.IsFinite(v) ? 0 : nonNegative ? Math.Max(0, v) : v;

        double min = Clip(d.Min), max = Clip(d.Max);
        if (max < min)
        {
            (min, max) = (max, min);
        }

        var next = new Distribution
        {
            Kind = d.Kind,
            Value = Clip(d.Value),
            Min = min,
            Max = max,
            Mode = Math.Clamp(Clip(d.Mode), min, max),
            Mean = Clip(d.Mean),
            StdDev = Math.Abs(Clip(d.StdDev)),
        };

        switch (next.Kind)
        {
            case DistributionKind.Fixed:
                next.Min = next.Max = next.Mean = next.Value;
                break;
            case DistributionKind.Uniform:
                next.Mean = (next.Min + next.Max) * 0.5;
                break;
            case DistributionKind.Triangular:
                next.Mean = (next.Min + next.Mode + next.Max) / 3.0;
                break;
        }

        return next;
    }

    /// <summary>
    /// The same distribution as another shape, keeping its centre and spread roughly (picking a new type in the
    /// editor shouldn't reset the numbers to zero).
    /// </summary>
    public static Distribution Converted(Distribution d, DistributionKind kind)
    {
        double centre = d.ExpectedValue;
        (double lo, double hi) = d.Kind is DistributionKind.Fixed or DistributionKind.Exponential
            ? (centre * 0.5, centre * 1.5)
            : (d.Min, d.Max);
        if (hi - lo < 1e-9)
        {
            (lo, hi) = (centre * 0.5, centre * 1.5);
        }

        double sd = d.Kind == DistributionKind.NormalClamped && d.StdDev > 0 ? d.StdDev : (hi - lo) / 4;
        Distribution next = kind switch
        {
            DistributionKind.Fixed => Distribution.Fixed(centre),
            DistributionKind.Uniform => Distribution.Uniform(lo, hi),
            DistributionKind.Triangular => Distribution.Triangular(lo, Math.Clamp(centre, lo, hi), hi),
            DistributionKind.NormalClamped => Distribution.Normal(centre, sd, lo, hi),
            DistributionKind.Exponential => Distribution.Exponential(centre),
            _ => Distribution.Fixed(centre),
        };
        return Normalised(next);
    }
}
