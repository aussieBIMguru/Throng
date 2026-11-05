using Throng.Core.Library;

namespace Throng.Sim;

/// <summary>Draws samples from a template <see cref="Distribution"/> with a <see cref="DeterministicRandom"/>.</summary>
public static class DistributionSampler
{
    /// <summary>One sample. Bad parameters (inverted bounds, negative spread) degrade to the nearest sensible value.</summary>
    public static double Sample(Distribution distribution, DeterministicRandom random)
    {
        double lo = Math.Min(distribution.Min, distribution.Max);
        double hi = Math.Max(distribution.Min, distribution.Max);
        switch (distribution.Kind)
        {
            case DistributionKind.Fixed:
                return distribution.Value;

            case DistributionKind.Uniform:
                return random.NextRange(lo, hi);

            case DistributionKind.Triangular:
            {
                double mode = Math.Clamp(distribution.Mode, lo, hi);
                double span = hi - lo;
                if (span <= 1e-12)
                {
                    return lo;
                }

                double u = random.NextDouble();
                double f = (mode - lo) / span;
                return u < f
                    ? lo + Math.Sqrt(u * span * (mode - lo))
                    : hi - Math.Sqrt((1 - u) * span * (hi - mode));
            }

            case DistributionKind.NormalClamped:
            {
                double value = distribution.Mean + (Math.Max(0, distribution.StdDev) * random.NextGaussian());
                return hi > lo ? Math.Clamp(value, lo, hi) : value;
            }

            case DistributionKind.Exponential:
                return random.NextExponential(distribution.Mean);

            default:
                return distribution.ExpectedValue;
        }
    }
}
