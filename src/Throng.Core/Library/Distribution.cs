using System.Globalization;
using System.Text.Json.Serialization;

namespace Throng.Core.Library;

/// <summary>Shape of a <see cref="Distribution"/>.</summary>
public enum DistributionKind
{
    /// <summary>Always <see cref="Distribution.Value"/>.</summary>
    Fixed,

    /// <summary>Uniform between <see cref="Distribution.Min"/> and <see cref="Distribution.Max"/>.</summary>
    Uniform,

    /// <summary>Triangular with <see cref="Distribution.Min"/>, <see cref="Distribution.Mode"/>, <see cref="Distribution.Max"/>.</summary>
    Triangular,

    /// <summary>Normal (<see cref="Distribution.Mean"/>, <see cref="Distribution.StdDev"/>) clamped to Min..Max.</summary>
    NormalClamped,

    /// <summary>Exponential with <see cref="Distribution.Mean"/>.</summary>
    Exponential,
}

/// <summary>
/// A simple parametric distribution for service times, dwell times and speeds.
/// Data only in M0; sampling arrives with the engine in M2.
/// </summary>
public sealed class Distribution
{
    /// <summary>Distribution shape.</summary>
    public DistributionKind Kind { get; set; } = DistributionKind.Fixed;

    /// <summary>Value for <see cref="DistributionKind.Fixed"/>.</summary>
    public double Value { get; set; }

    /// <summary>Lower bound (uniform, triangular, clamped normal).</summary>
    public double Min { get; set; }

    /// <summary>Upper bound (uniform, triangular, clamped normal).</summary>
    public double Max { get; set; }

    /// <summary>Most likely value (triangular).</summary>
    public double Mode { get; set; }

    /// <summary>Mean (normal, exponential).</summary>
    public double Mean { get; set; }

    /// <summary>Standard deviation (normal).</summary>
    public double StdDev { get; set; }

    /// <summary>A constant.</summary>
    public static Distribution Fixed(double value) => new() { Kind = DistributionKind.Fixed, Value = value, Min = value, Max = value, Mean = value };

    /// <summary>Uniform between two bounds.</summary>
    public static Distribution Uniform(double min, double max) => new() { Kind = DistributionKind.Uniform, Min = min, Max = max, Mean = (min + max) * 0.5 };

    /// <summary>Triangular between bounds with a most likely value.</summary>
    public static Distribution Triangular(double min, double mode, double max) =>
        new() { Kind = DistributionKind.Triangular, Min = min, Mode = mode, Max = max, Mean = (min + mode + max) / 3.0 };

    /// <summary>Normal distribution clamped to a range.</summary>
    public static Distribution Normal(double mean, double stdDev, double min, double max) =>
        new() { Kind = DistributionKind.NormalClamped, Mean = mean, StdDev = stdDev, Min = min, Max = max };

    /// <summary>Exponential with the given mean.</summary>
    public static Distribution Exponential(double mean) => new() { Kind = DistributionKind.Exponential, Mean = mean };

    /// <summary>Approximate mean, used by static capacity checks.</summary>
    [JsonIgnore]
    public double ExpectedValue => Kind == DistributionKind.Fixed ? Value : Mean;

    /// <summary>Short human-readable form, e.g. "20–60" or "1.34 ± 0.26".</summary>
    public string Describe()
    {
        CultureInfo c = CultureInfo.InvariantCulture;
        return Kind switch
        {
            DistributionKind.Fixed => Value.ToString("0.##", c),
            DistributionKind.Uniform => $"{Min.ToString("0.##", c)}–{Max.ToString("0.##", c)}",
            DistributionKind.Triangular => $"{Min.ToString("0.##", c)}–{Max.ToString("0.##", c)} (≈{Mode.ToString("0.##", c)})",
            DistributionKind.NormalClamped => $"{Mean.ToString("0.##", c)} ± {StdDev.ToString("0.##", c)}",
            DistributionKind.Exponential => $"exp, mean {Mean.ToString("0.##", c)}",
            _ => "?",
        };
    }
}
