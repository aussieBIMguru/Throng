namespace Throng.App.Services;

/// <summary>What the title block and legend on an exported PNG say (D-234).</summary>
public sealed class PngExportInfo
{
    /// <summary>Study name.</summary>
    public required string Study { get; init; }

    /// <summary>Active level (plan) or "All visible levels" (3D), with the view mode.</summary>
    public required string Level { get; init; }

    /// <summary>Sim time line, e.g. "07:12:30 · 00:12:30 into the run".</summary>
    public required string SimTime { get; init; }

    /// <summary>What the map shows: the heatmap mode, or the agent colouring when no map is on.</summary>
    public required string Map { get; init; }

    /// <summary>Legend title (empty for no legend).</summary>
    public string LegendTitle { get; init; } = string.Empty;

    /// <summary>Legend rows, top to bottom.</summary>
    public IReadOnlyList<PngLegendRow> Legend { get; init; } = [];

    /// <summary>Small note under the legend (warm-up status, scale), or empty.</summary>
    public string LegendNote { get; init; } = string.Empty;
}
