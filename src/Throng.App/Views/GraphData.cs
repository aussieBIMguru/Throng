namespace Throng.App.Views;

/// <summary>What the time-series graph draws (round 011c, D-220, D-221): sample times, lines, bands and the warm-up.</summary>
public sealed class GraphData
{
    /// <summary>Nothing to draw.</summary>
    public static GraphData Empty { get; } = new() { Times = [], Lines = [], Bands = [] };

    /// <summary>Sample times, seconds from the start.</summary>
    public required double[] Times { get; init; }

    /// <summary>The lines, each with one value per time.</summary>
    public required IReadOnlyList<GraphLine> Lines { get; init; }

    /// <summary>Bands behind the lines (may be empty).</summary>
    public required IReadOnlyList<GraphBand> Bands { get; init; }

    /// <summary>Warm-up length, shaded from the start (D-218); 0 for none.</summary>
    public double WarmUpS { get; init; }

    /// <summary>Unit shown above the value axis, e.g. "PEOPLE".</summary>
    public string Unit { get; init; } = string.Empty;

    /// <summary>Lowest top for the value axis, so a quiet start doesn't fill the graph with noise.</summary>
    public double MinTop { get; init; } = 1;
}
