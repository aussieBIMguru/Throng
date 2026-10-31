using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
using Throng.Analysis;
using Throng.App.Services;
using Throng.App.Views;
using Throng.Core.Common;
using Throng.Sim;
using Throng.Sim.Engine;

namespace Throng.App.ViewModels;

/// <summary>
/// RESULTS half of the main view model (round 011c): the RESULTS tab (D-219) with the device table (stats after the
/// warm-up, D-218), the hand-drawn graph (D-220, D-221) with its queue lines (ticked rows, else the five longest,
/// D-226), and CSV export (D-222). Round 012 adds the DEVICES / ZONES / JOURNEYS table chips (D-232), the ZONES graph,
/// zones.csv and journeys.csv (D-233). Results live until RESET; they aren't saved (D-215).
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>Right-panel tab index: RESULTS (D-219).</summary>
    public const int PropertiesTabResults = 3;

    private const int DefaultGraphedQueues = 5;

    // Queue line colours on the graph, in order (distinct on the dark panel).
    private static readonly Color[] QueueColours =
    [
        Color.FromRgb(0x22, 0xD3, 0xEE), Color.FromRgb(0xFA, 0xCC, 0x15), Color.FromRgb(0xF4, 0x72, 0xB6), Color.FromRgb(0x86, 0xEF, 0xAC),
        Color.FromRgb(0xFB, 0x92, 0x3C), Color.FromRgb(0xA7, 0x8B, 0xFA), Color.FromRgb(0xF8, 0x71, 0x71), Color.FromRgb(0x94, 0xA3, 0xB8),
    ];

    private SimRunner? _resultsRunner;
    private RunResults _results = RunResults.Empty;
    private long _resultsVersion = -1;
    private GraphMetric _graphMetric = GraphMetric.People;
    private GraphData _resultsGraph = GraphData.Empty;
    private DeviceResultRow? _selectedResultRow;
    private string _resultsSummary = "No run yet. PLAY to gather results; they stay until RESET.";
    private RelayCommand? _exportResultsCommand;
    private ResultsTable _resultsTable = ResultsTable.Devices;

    /// <summary>True while the RESULTS tab is showing.</summary>
    public bool IsResultsTab => _propertiesTabIndex == PropertiesTabResults;

    /// <summary>One row per device, updated in place while running (D-219).</summary>
    public ObservableCollection<DeviceResultRow> ResultRows { get; } = new();

    /// <summary>One row per measurement zone, updated in place while running (D-228).</summary>
    public ObservableCollection<ZoneResultRow> ZoneResultRows { get; } = new();

    /// <summary>Profile × journey rows, updated in place while running (D-229).</summary>
    public ObservableCollection<JourneyResultRow> JourneyResultRows { get; } = new();

    /// <summary>One row per lift bank, updated in place while running (D-260).</summary>
    public ObservableCollection<LiftResultRow> LiftResultRows { get; } = new();

    /// <summary>TABLE ▸ DEVICES chip (D-232).</summary>
    public bool IsTableDevices { get => _resultsTable == ResultsTable.Devices; set => SelectTable(ResultsTable.Devices, nameof(IsTableDevices)); }

    /// <summary>TABLE ▸ ZONES chip (D-232).</summary>
    public bool IsTableZones { get => _resultsTable == ResultsTable.Zones; set => SelectTable(ResultsTable.Zones, nameof(IsTableZones)); }

    /// <summary>TABLE ▸ JOURNEYS chip (D-232).</summary>
    public bool IsTableJourneys { get => _resultsTable == ResultsTable.Journeys; set => SelectTable(ResultsTable.Journeys, nameof(IsTableJourneys)); }

    /// <summary>TABLE ▸ LIFTS chip (D-260).</summary>
    public bool IsTableLifts { get => _resultsTable == ResultsTable.Lifts; set => SelectTable(ResultsTable.Lifts, nameof(IsTableLifts)); }

    /// <summary>The row of the device selected in the viewport, highlighted in the table (D-219).</summary>
    public DeviceResultRow? SelectedResultRow
    {
        get => _selectedResultRow;
        set => SetProperty(ref _selectedResultRow, value);
    }

    /// <summary>What the graph draws.</summary>
    public GraphData ResultsGraph => _resultsGraph;

    /// <summary>Line above the table: time measured, warm-up, totals.</summary>
    public string ResultsSummary
    {
        get => _resultsSummary;
        private set => SetProperty(ref _resultsSummary, value);
    }

    /// <summary>GRAPH ▸ PEOPLE chip.</summary>
    public bool IsGraphPeople { get => _graphMetric == GraphMetric.People; set => SelectGraph(GraphMetric.People, nameof(IsGraphPeople)); }

    /// <summary>GRAPH ▸ QUEUES chip.</summary>
    public bool IsGraphQueues { get => _graphMetric == GraphMetric.Queues; set => SelectGraph(GraphMetric.Queues, nameof(IsGraphQueues)); }

    /// <summary>GRAPH ▸ DENSITY chip.</summary>
    public bool IsGraphDensity { get => _graphMetric == GraphMetric.Density; set => SelectGraph(GraphMetric.Density, nameof(IsGraphDensity)); }

    /// <summary>GRAPH ▸ FLOW chip.</summary>
    public bool IsGraphFlow { get => _graphMetric == GraphMetric.Flow; set => SelectGraph(GraphMetric.Flow, nameof(IsGraphFlow)); }

    /// <summary>GRAPH ▸ ZONES chip (D-232).</summary>
    public bool IsGraphZones { get => _graphMetric == GraphMetric.Zones; set => SelectGraph(GraphMetric.Zones, nameof(IsGraphZones)); }

    /// <summary>EXPORT CSV (D-222, D-233, D-260): devices, zones, journeys, lifts and time series CSVs into a folder you pick.</summary>
    public RelayCommand ExportResultsCommand => _exportResultsCommand ??= new RelayCommand(ExportResults, () => _results.SampleCount > 0);

    /// <summary>Picks up a new results copy from the runner (run status tick); RESET clears the tab.</summary>
    private void RefreshResults()
    {
        if (_runner is not { } runner)
        {
            if (_resultsRunner is not null)
            {
                _resultsRunner = null;
                _results = RunResults.Empty;
                _resultsVersion = -1;
                ResultRows.Clear();
                ZoneResultRows.Clear();
                JourneyResultRows.Clear();
                LiftResultRows.Clear();
                SelectedResultRow = null;
                RefreshJourneyOverlay();
                ResultsSummary = "No run yet. PLAY to gather results; they stay until RESET.";
                RebuildGraph();
                ExportResultsCommand.NotifyCanExecuteChanged();
            }

            return;
        }

        RunResults results = runner.LatestResults;
        if (ReferenceEquals(runner, _resultsRunner) && results.Version == _resultsVersion)
        {
            return;
        }

        bool newRunner = !ReferenceEquals(runner, _resultsRunner);
        if (newRunner || ResultRows.Count != results.Devices.Count)
        {
            ResultRows.Clear();
            IReadOnlyList<SimDevice> devices = runner.Model.Devices;
            for (int d = 0; d < devices.Count; d++)
            {
                ResultRows.Add(new DeviceResultRow(d, devices[d].DeviceId, devices[d].Name, devices[d].TypeTag, RebuildGraph));
            }

            SyncResultSelection();
        }

        if (newRunner || ZoneResultRows.Count != results.Zones.Count)
        {
            ZoneResultRows.Clear();
            IReadOnlyList<SimZone> zones = runner.Model.Zones;
            for (int z = 0; z < zones.Count; z++)
            {
                ZoneResultRows.Add(new ZoneResultRow(z, zones[z].RegionId, zones[z].Name, zones[z].WidthM, RebuildGraph));
            }
        }

        if (newRunner || !JourneyRowsMatch(results.Journeys))
        {
            JourneyResultRows.Clear();
            foreach (JourneyResult journey in results.Journeys)
            {
                JourneyResultRows.Add(new JourneyResultRow(journey.Profile, journey.Journey, journey.IsAll, journey.AcceptableS, journey.Sink));
            }
        }

        if (newRunner || LiftResultRows.Count != results.Lifts.Count)
        {
            LiftResultRows.Clear();
            foreach (LiftResult lift in results.Lifts)
            {
                LiftResultRows.Add(new LiftResultRow(lift.Lift, lift.Name, lift.Cars));
            }
        }

        _resultsRunner = runner;
        _results = results;
        _resultsVersion = results.Version;
        foreach (DeviceResult device in results.Devices)
        {
            if (device.Device >= 0 && device.Device < ResultRows.Count)
            {
                ResultRows[device.Device].Update(device);
            }
        }

        foreach (ZoneResult zone in results.Zones)
        {
            if (zone.Zone >= 0 && zone.Zone < ZoneResultRows.Count)
            {
                ZoneResultRows[zone.Zone].Update(zone);
            }
        }

        for (int j = 0; j < results.Journeys.Count && j < JourneyResultRows.Count; j++)
        {
            JourneyResultRows[j].Update(results.Journeys[j]);
        }

        foreach (LiftResult lift in results.Lifts)
        {
            if (lift.Lift >= 0 && lift.Lift < LiftResultRows.Count)
            {
                LiftResultRows[lift.Lift].Update(lift);
            }
        }

        ResultsSummary = Summary(results);
        RebuildGraph();
        ExportResultsCommand.NotifyCanExecuteChanged();
    }

    /// <summary>True when the journey rows are the same rows, in the same order, as <paramref name="journeys"/>.</summary>
    private bool JourneyRowsMatch(IReadOnlyList<JourneyResult> journeys)
    {
        if (JourneyResultRows.Count != journeys.Count)
        {
            return false;
        }

        for (int j = 0; j < journeys.Count; j++)
        {
            if (!JourneyResultRows[j].Matches(journeys[j]))
            {
                return false;
            }
        }

        return true;
    }

    private void SelectTable(ResultsTable table, string chip)
    {
        if (_resultsTable != table)
        {
            _resultsTable = table;
            OnPropertyChanged(nameof(IsTableDevices));
            OnPropertyChanged(nameof(IsTableZones));
            OnPropertyChanged(nameof(IsTableJourneys));
            OnPropertyChanged(nameof(IsTableLifts));
            ResultsSummary = Summary(_results);
        }

        OnPropertyChanged(chip);
    }

    /// <summary>Highlights the viewport-selected device's row (D-219); called when the selection changes.</summary>
    private void SyncResultSelection()
    {
        Guid? id = _selectedDevice?.Model.Id;
        SelectedResultRow = id is { } d ? ResultRows.FirstOrDefault(r => r.DeviceId == d) : null;
    }

    private string Summary(RunResults results)
    {
        CultureInfo c = CultureInfo.InvariantCulture;
        if (_resultsRunner is null)
        {
            return "No run yet. PLAY to gather results; they stay until RESET.";
        }

        if (results.WarmUpS > 0 && results.TimeS < results.WarmUpS)
        {
            return string.Create(c, $"WARM-UP ▸ stats start at {Clock(results.WarmUpS)}. The graph records from the start.");
        }

        string warm = results.WarmUpS > 0 ? string.Create(c, $" after a {Clock(results.WarmUpS)} warm-up") : string.Empty;
        switch (_resultsTable)
        {
            case ResultsTable.Zones:
                return results.Zones.Count == 0
                    ? "No measurement zones. Draw a zone and set its kind to Measurement to count people through it."
                    : string.Create(c, $"{Clock(results.MeasuredS)} measured{warm} · {results.Zones.Sum(z => z.Entries):N0} entries. Flow per metre uses the zone's narrower side; tick ▸ graph a zone.");
            case ResultsTable.Journeys:
                long finished = results.Journeys.Where(j => !j.IsAll && !j.IsSink).Sum(j => j.Finished);
                return string.Create(c, $"People spawned{warm} · {finished:N0} finished. Long wait = one queue wait over {results.LongWaitS:0} s (STUDY ▸ RUN).");
            case ResultsTable.Lifts:
                return results.Lifts.Count == 0
                    ? "No lifts in this study. Add a lift to a shaft from LIBRARY ▸ LIFTS."
                    : string.Create(c, $"{Clock(results.MeasuredS)} measured{warm} · {results.Lifts.Sum(l => l.Boarded):N0} boarded · {results.Lifts.Sum(l => l.GaveUp):N0} gave up and went another way. Waits are on the landing, rides door to door.");
            default:
                long served = results.Devices.Sum(d => d.Served);
                return string.Create(c, $"{Clock(results.MeasuredS)} measured{warm} · {served:N0} served. Click a column to sort; tick ▸ graph a queue.");
        }
    }

    private void SelectGraph(GraphMetric metric, string chip)
    {
        if (_graphMetric != metric)
        {
            _graphMetric = metric;
            OnPropertyChanged(nameof(IsGraphPeople));
            OnPropertyChanged(nameof(IsGraphQueues));
            OnPropertyChanged(nameof(IsGraphDensity));
            OnPropertyChanged(nameof(IsGraphFlow));
            OnPropertyChanged(nameof(IsGraphZones));
            RebuildGraph();
        }

        OnPropertyChanged(chip);
    }

    /// <summary>Rebuilds the graph for the current metric from the latest results.</summary>
    private void RebuildGraph()
    {
        RunResults r = _results;
        double[] times = r.Times;
        _resultsGraph = _graphMetric switch
        {
            GraphMetric.Queues => QueueGraph(r),
            GraphMetric.Zones => ZoneGraph(r),
            GraphMetric.Density => DensityGraph(r),
            GraphMetric.Flow => new GraphData
            {
                Times = times,
                Lines =
                [
                    new GraphLine("IN / MIN", QueueColours[0], [.. r.ArrivalsPerMin.Select(v => (double)v)]),
                    new GraphLine("OUT / MIN", QueueColours[1], [.. r.ExitsPerMin.Select(v => (double)v)]),
                ],
                Bands = [],
                WarmUpS = r.WarmUpS,
                Unit = "PEOPLE / MIN",
                MinTop = 10,
            },
            _ => new GraphData
            {
                Times = times,
                Lines = [new GraphLine("IN MODEL", QueueColours[0], [.. r.InModel.Select(v => (double)v)])],
                Bands = [],
                WarmUpS = r.WarmUpS,
                Unit = "PEOPLE",
                MinTop = 10,
            },
        };
        OnPropertyChanged(nameof(ResultsGraph));
    }

    /// <summary>Queue lines for the ticked rows, else the five with the longest max queue (D-226).</summary>
    private GraphData QueueGraph(RunResults r)
    {
        List<DeviceResultRow> rows = [.. ResultRows.Where(row => row.IsGraphed)];
        if (rows.Count == 0)
        {
            rows = [.. ResultRows.Where(row => row.MaxQueue > 0).OrderByDescending(row => row.MaxQueue).ThenBy(row => row.Index).Take(DefaultGraphedQueues)];
        }

        var lines = new List<GraphLine>(rows.Count);
        int devices = r.Devices.Count;
        foreach (DeviceResultRow row in rows)
        {
            if (row.Index >= devices)
            {
                continue;
            }

            var values = new double[r.SampleCount];
            for (int s = 0; s < values.Length; s++)
            {
                values[s] = r.QueueAt(s, row.Index);
            }

            lines.Add(new GraphLine(row.Name, QueueColours[lines.Count % QueueColours.Length], values));
        }

        return new GraphData { Times = r.Times, Lines = lines, Bands = [], WarmUpS = r.WarmUpS, Unit = "IN LINE", MinTop = 5 };
    }

    /// <summary>Occupancy lines for the ticked zone rows, else every zone (D-232).</summary>
    private GraphData ZoneGraph(RunResults r)
    {
        List<ZoneResultRow> rows = [.. ZoneResultRows.Where(row => row.IsGraphed)];
        if (rows.Count == 0)
        {
            rows = [.. ZoneResultRows];
        }

        var lines = new List<GraphLine>(rows.Count);
        int zones = r.Zones.Count;
        bool complete = r.ZoneOccupancy.Length >= r.SampleCount * zones;
        foreach (ZoneResultRow row in rows)
        {
            if (!complete || row.Index >= zones)
            {
                continue;
            }

            var values = new double[r.SampleCount];
            for (int s = 0; s < values.Length; s++)
            {
                values[s] = r.ZoneAt(s, row.Index);
            }

            lines.Add(new GraphLine(row.Name, QueueColours[lines.Count % QueueColours.Length], values));
        }

        return new GraphData { Times = r.Times, Lines = lines, Bands = [], WarmUpS = r.WarmUpS, Unit = "IN ZONE", MinTop = 5 };
    }

    /// <summary>Peak density with the walkway Fruin bands behind it, in the heatmap's LoS colours (D-221).</summary>
    private GraphData DensityGraph(RunResults r)
    {
        IReadOnlyList<HeatLegendEntry> grades = HeatmapPainter.Legend(HeatmapMode.Los, HeatRamp);
        double[] starts = FruinLevelOfService.GradeStartDensities(LosAreaType.Walkway);
        string[] letters = ["A", "B", "C", "D", "E", "F"];
        var bands = new List<GraphBand>(letters.Length);
        for (int g = 0; g < letters.Length && g < grades.Count; g++)
        {
            double from = g == 0 ? 0 : starts[g - 1];
            double to = g < starts.Length ? starts[g] : double.PositiveInfinity;
            HeatLegendEntry e = grades[g];
            bands.Add(new GraphBand(from, to, Color.FromRgb(ToByte(e.R), ToByte(e.G), ToByte(e.B)), letters[g]));
        }

        return new GraphData
        {
            Times = r.Times,
            Lines = [new GraphLine("PEAK", Color.FromRgb(0xFF, 0xFF, 0xFF), [.. r.PeakDensity.Select(v => (double)v)])],
            Bands = bands,
            WarmUpS = r.WarmUpS,
            Unit = "P/M² · WALKWAY LOS",
            MinTop = 1.5,
        };
    }

    /// <summary>EXPORT CSV: asks for a folder and writes both files (D-222); problems are shown, never thrown.</summary>
    private void ExportResults()
    {
        RunResults results = _runner?.LatestResults ?? _results;
        if (results.SampleCount == 0)
        {
            return;
        }

        if (_dialogs.PickFolder("Export results (devices.csv, zones.csv, journeys.csv, lifts.csv, nodes.csv, timeseries.csv)") is not { } folder)
        {
            return;
        }

        Result written = ResultsCsv.Export(results, folder, (_runner ?? _resultsRunner)?.Model);
        if (!written.IsSuccess)
        {
            AppLog.Warn(written.Error ?? "Results export failed.");
            _dialogs.ShowError(written.Error ?? "The results couldn't be written.");
            return;
        }

        AppLog.Info($"Results exported to {folder}");
        Ticker = $"EXPORTED ▸ {ResultsCsv.DevicesFileName}, {ResultsCsv.ZonesFileName}, {ResultsCsv.JourneysFileName}, {ResultsCsv.LiftsFileName}, {ResultsCsv.NodesFileName}, {ResultsCsv.TimeSeriesFileName}";
    }
}
