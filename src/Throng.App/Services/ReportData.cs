using System.Windows.Media;

namespace Throng.App.Services;

/// <summary>
/// Everything the one-page PDF report shows (round 025, D-400–D-403), gathered by the app before
/// <see cref="PdfReport.Write"/> lays it out: the title block and headline numbers, a plan heatmap of every level with
/// its legend, the people-in-model graph, and the CHECK findings.
/// </summary>
public sealed record ReportData
{
    /// <summary>Study name.</summary>
    public required string Study { get; init; }

    /// <summary>What the numbers are: "Last run, seed 42, 07:30–08:30" or "Mean of 5 runs (seeds 42–46)".</summary>
    public required string Source { get; init; }

    /// <summary>The map on the level plans, e.g. "TIME AT LOS E OR WORSE", or "NO MAP".</summary>
    public required string MapTitle { get; init; }

    /// <summary>Headline numbers, label then value, in order.</summary>
    public required IReadOnlyList<(string Label, string Value)> Headlines { get; init; }

    /// <summary>One plan per level, lowest first.</summary>
    public required IReadOnlyList<LevelPlan> Levels { get; init; }

    /// <summary>Map legend rows (empty without a map).</summary>
    public IReadOnlyList<PngLegendRow> Legend { get; init; } = [];

    /// <summary>A line under the legend (e.g. the map's scale), or empty.</summary>
    public string LegendNote { get; init; } = string.Empty;

    /// <summary>The graph, or null when there's no run to plot.</summary>
    public Series? Graph { get; init; }

    /// <summary>CHECK findings not ignored, errors first.</summary>
    public IReadOnlyList<Finding> Findings { get; init; } = [];

    /// <summary>When the report was made (local time).</summary>
    public DateTime Made { get; init; } = DateTime.Now;

    /// <summary>One level's plan image.</summary>
    /// <param name="Name">Level name and elevation.</param>
    /// <param name="Png">The captured plan as PNG bytes.</param>
    /// <param name="PixelWidth">Image width, pixels.</param>
    /// <param name="PixelHeight">Image height, pixels.</param>
    public sealed record LevelPlan(string Name, byte[] Png, int PixelWidth, int PixelHeight);

    /// <summary>The graph: people in the model and people queuing over time, with the warm-up shaded.</summary>
    /// <param name="TimesS">Sample times, seconds from the start.</param>
    /// <param name="InModel">People in the model at each sample.</param>
    /// <param name="Queuing">People in device queues at each sample.</param>
    /// <param name="WarmUpS">Warm-up length, seconds.</param>
    public sealed record Series(double[] TimesS, double[] InModel, double[] Queuing, double WarmUpS);

    /// <summary>One CHECK finding.</summary>
    /// <param name="Severity">"ERROR", "WARNING" or "INFO".</param>
    /// <param name="Message">The finding.</param>
    /// <param name="Colour">Its tag colour.</param>
    public sealed record Finding(string Severity, string Message, Color Colour);
}
