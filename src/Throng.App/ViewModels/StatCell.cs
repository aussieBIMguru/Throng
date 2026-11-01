using System.Globalization;
using Throng.Sim.Batch;

namespace Throng.App.ViewModels;

/// <summary>
/// One RESULTS cell (round 021, D-358): a single run's value, or a batch's mean with its min–max, shown as
/// "12.3 (10.1–14.0)". Formats through the column's StringFormat (the format applies to every number), and sorts by
/// the mean, so the tables' XAML is the same for both sources.
/// </summary>
public readonly record struct StatCell(double Mean, double Min, double Max, bool IsBatch) : IFormattable, IComparable<StatCell>, IComparable
{
    /// <summary>Format for cells without a StringFormat (whole numbers, a decimal only when a batch mean has one).</summary>
    private const string DefaultFormat = "0.#";

    /// <summary>A single run's value.</summary>
    public static StatCell Single(double value) => new(value, value, value, false);

    /// <summary>A batch's stat, rounded to <paramref name="decimals"/> places, optionally scaled (e.g. ×100 for percent).</summary>
    public static StatCell Batch(BatchStat stat, int decimals, double scale = 1) =>
        new(Math.Round(stat.Mean * scale, decimals), Math.Round(stat.Min * scale, decimals), Math.Round(stat.Max * scale, decimals), true);

    /// <inheritdoc />
    public override string ToString() => ToString(null, CultureInfo.CurrentCulture);

    /// <inheritdoc />
    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        string f = string.IsNullOrEmpty(format) ? DefaultFormat : format;
        string mean = Mean.ToString(f, formatProvider);
        if (!IsBatch)
        {
            return mean;
        }

        string min = Min.ToString(f, formatProvider), max = Max.ToString(f, formatProvider);
        return min == max ? mean : $"{mean} ({min}–{max})";
    }

    /// <inheritdoc />
    public int CompareTo(StatCell other) => Mean.CompareTo(other.Mean);

    /// <inheritdoc />
    public int CompareTo(object? obj) => obj is StatCell other ? CompareTo(other) : 1;
}
