using System.Windows.Media;

namespace Throng.App.Views;

/// <summary>One line on the time-series graph (round 011c, D-220), with an optional min–max band (round 021, D-359).</summary>
/// <param name="Label">Legend text.</param>
/// <param name="Colour">Line colour.</param>
/// <param name="Values">One value per sample time (a batch's mean).</param>
/// <param name="Low">Lowest run per sample time, or null for no band.</param>
/// <param name="High">Highest run per sample time, or null for no band.</param>
public sealed record GraphLine(string Label, Color Colour, double[] Values, double[]? Low = null, double[]? High = null)
{
    /// <summary>True when the line carries a min–max band.</summary>
    public bool HasBand => Low is not null && High is not null;
}
