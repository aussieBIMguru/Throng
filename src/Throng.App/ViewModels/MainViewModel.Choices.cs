using System.Globalization;
using Throng.Analysis;
using Throng.Render.Gl;

namespace Throng.App.ViewModels;

/// <summary>
/// Drop-down choices (round 022, D-368, D-370): the transport bar's SPEED, COLOUR and MAP and RESULTS' SOURCE, TABLE and
/// GRAPH. Each drives the same state as the chip properties it replaced in the window (those stay, so the logic in one
/// place serves both).
/// </summary>
public sealed partial class MainViewModel
{
    private static readonly IReadOnlyList<LabelledOption<string>> SpeedChoices =
    [
        new("0.5", "×½"), new("1", "×1"), new("2", "×2"), new("4", "×4"), new("8", "×8"), new("MAX", "MAX"),
    ];

    private static readonly IReadOnlyList<LabelledOption<AgentColoring>> ColouringChoices =
    [
        new(AgentColoring.Profile, "PROFILE"), new(AgentColoring.Stress, "STRESS"), new(AgentColoring.Group, "GROUP"),
    ];

    private static readonly IReadOnlyList<LabelledOption<HeatmapMode>> HeatmapChoices =
    [
        new(HeatmapMode.Off, "NO MAP"),
        new(HeatmapMode.Density, "LIVE ▸ DENSITY"),
        new(HeatmapMode.Los, "LIVE ▸ LOS"),
        new(HeatmapMode.MaxDensity, "RUN ▸ PEAK LOS"),
        new(HeatmapMode.LosEOrWorse, "RUN ▸ E+ TIME"),
        new(HeatmapMode.MeanDensity, "RUN ▸ MEAN"),
        new(HeatmapMode.Footfall, "RUN ▸ TRAIL"),
    ];

    private static readonly IReadOnlyList<LabelledOption<ResultsTable>> TableChoices =
    [
        new(ResultsTable.Devices, "DEVICES"), new(ResultsTable.Zones, "ZONES"), new(ResultsTable.Journeys, "JOURNEYS"), new(ResultsTable.Lifts, "LIFTS"),
    ];

    private static readonly IReadOnlyList<LabelledOption<GraphMetric>> GraphChoices =
    [
        new(GraphMetric.People, "PEOPLE"), new(GraphMetric.Queues, "QUEUES"), new(GraphMetric.Density, "DENSITY"), new(GraphMetric.Flow, "FLOW"),
        new(GraphMetric.Zones, "ZONES"),
    ];

    /// <summary>Transport bar ▸ SPEED options.</summary>
    public IReadOnlyList<LabelledOption<string>> SpeedOptions => SpeedChoices;

    /// <summary>Transport bar ▸ SPEED: "0.5" … "8" or "MAX".</summary>
    public string SpeedChoice
    {
        get => _speed.IsMax ? "MAX" : _speed.Multiplier.ToString("0.###", CultureInfo.InvariantCulture);
        set => SetSpeed(value);
    }

    /// <summary>Transport bar ▸ COLOUR options (bound to <see cref="AgentColoring"/>).</summary>
    public IReadOnlyList<LabelledOption<AgentColoring>> ColouringOptions => ColouringChoices;

    /// <summary>Transport bar ▸ MAP options, live then run maps (bound to <see cref="HeatmapMode"/>).</summary>
    public IReadOnlyList<LabelledOption<HeatmapMode>> HeatmapOptions => HeatmapChoices;

    /// <summary>RESULTS ▸ TABLE options.</summary>
    public IReadOnlyList<LabelledOption<ResultsTable>> TableOptions => TableChoices;

    /// <summary>RESULTS ▸ TABLE.</summary>
    public ResultsTable SelectedTable
    {
        get => _resultsTable;
        set => SelectTable(value, nameof(SelectedTable));
    }

    /// <summary>RESULTS ▸ GRAPH options.</summary>
    public IReadOnlyList<LabelledOption<GraphMetric>> GraphOptions => GraphChoices;

    /// <summary>RESULTS ▸ GRAPH.</summary>
    public GraphMetric SelectedGraph
    {
        get => _graphMetric;
        set => SelectGraph(value, nameof(SelectedGraph));
    }

    /// <summary>RESULTS ▸ SOURCE options: LAST RUN, and BATCH (N) once a batch exists.</summary>
    public IReadOnlyList<LabelledOption<bool>> SourceOptions =>
        _batch is null
            ? new LabelledOption<bool>[] { new(false, "LAST RUN") }
            : new LabelledOption<bool>[] { new(false, "LAST RUN"), new(true, BatchSourceLabel) };

    /// <summary>RESULTS ▸ SOURCE: true shows the batch (D-363).</summary>
    public bool SelectedSource
    {
        get => ShowsBatch;
        set => SelectSource(value, nameof(SelectedSource));
    }
}
