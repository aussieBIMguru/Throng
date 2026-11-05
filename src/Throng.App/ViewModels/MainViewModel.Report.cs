using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using Throng.Analysis;
using Throng.Analysis.Checks;
using Throng.App.Services;
using Throng.Core.Common;
using Throng.Core.Model;
using Throng.Core.Validation;
using Throng.Render.Gl;
using Throng.Sim.Batch;
using Throng.Sim.Engine;

namespace Throng.App.ViewModels;

/// <summary>
/// The one-page PDF report (round 025, D-400–D-403): FILE ▸ EXPORT PDF REPORT… and the RESULTS button ask which map to
/// show, then draw every level in plan with it (the view is put back afterwards), gather the headline numbers, the
/// people-in-model graph and the CHECK findings, and write an A3 landscape PDF with PDFsharp. The numbers are the ones
/// RESULTS shows: the last run, or a batch's means when SOURCE is BATCH.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>Multiple of the viewport's size each level plan is captured at.</summary>
    public const double ReportCaptureScale = 1.5;

    private AsyncRelayCommand? _exportPdfCommand;
    private bool _isExportingReport;

    /// <summary>EXPORT PDF REPORT…: the A3 report to a file you pick (D-400).</summary>
    public AsyncRelayCommand ExportPdfCommand => _exportPdfCommand ??= new AsyncRelayCommand(ExportPdfAsync, () => _captureViewport is not null && !_isExportingReport);

    /// <summary>The maps the report can show, labelled as the legend titles them.</summary>
    private static IReadOnlyList<LabelledOption<HeatmapMode>> ReportMaps { get; } =
    [
        new(HeatmapMode.LosEOrWorse, "Run · time at LoS E or worse"),
        new(HeatmapMode.MaxDensity, "Run · peak LoS (Fruin)"),
        new(HeatmapMode.MeanDensity, "Run · mean density"),
        new(HeatmapMode.Footfall, "Run · footfall"),
        new(HeatmapMode.Los, "Live · level of service now"),
        new(HeatmapMode.Density, "Live · density now"),
        new(HeatmapMode.Off, "No map (plans and people only)"),
    ];

    /// <summary>Asks for the map and a file, captures every level, gathers the numbers and writes the PDF.</summary>
    private async Task ExportPdfAsync()
    {
        if (_captureViewport is not { } capture || _isExportingReport)
        {
            return;
        }

        bool hasRun = _runner is not null || _shownBatch is not null;
        HeatmapMode start = _heatmapMode != HeatmapMode.Off ? _heatmapMode : hasRun ? HeatmapMode.LosEOrWorse : HeatmapMode.Off;
        string note = hasRun
            ? "Headline numbers, the graph and run maps come from what RESULTS shows (this run, or the batch with SOURCE ▸ BATCH)."
            : "Nothing has run yet: the plans show the layout and the report has no numbers. PLAY or BATCH first for results.";
        if (_dialogs.AskReportMap(ReportMaps, start, note) is not { } map)
        {
            return;
        }

        if (_dialogs.PickPdfSavePath(SafeFileName(_study.Name + " report") + ".pdf") is not { } path)
        {
            return;
        }

        _isExportingReport = true;
        ExportPdfCommand.NotifyCanExecuteChanged();
        Ticker = "PDF REPORT ▸ drawing every level…";
        try
        {
            // Numbers and findings first, so they match the moment the export was asked for.
            List<(string, string)> headlines = ReportHeadlines(out string source, out ReportData.Series? graph);
            Study study = _study;
            IReadOnlyList<ValidationMessage> findings = await Task.Run(() => StudyCheck.Run(study));
            List<ReportData.LevelPlan> plans = await CaptureLevelPlansAsync(capture, map);
            if (plans.Count == 0 && _study.Levels.Count > 0)
            {
                _dialogs.ShowError("The level plans couldn't be captured (is the viewport showing?).");
                return;
            }

            var data = new ReportData
            {
                Study = _study.Name,
                Source = source,
                MapTitle = map == HeatmapMode.Off ? "NO MAP" : ReportMaps.First(m => m.Value == map).Label.ToUpperInvariant(),
                Headlines = headlines,
                Levels = plans,
                Legend = _reportLegend,
                LegendNote = _reportLegendNote,
                Graph = graph,
                Findings = ReportFindings(findings),
            };

            Result written = PdfReport.Write(data, path);
            if (!written.IsSuccess)
            {
                AppLog.Warn(written.Error ?? "PDF export failed.");
                _dialogs.ShowError(written.Error ?? "The PDF couldn't be written.");
                return;
            }

            AppLog.Info($"PDF report exported to {path}");
            int pages = Math.Max(1, (int)Math.Ceiling(plans.Count / (double)PdfReport.PlansPerPage));
            Ticker = string.Create(CultureInfo.InvariantCulture, $"EXPORTED ▸ {Path.GetFileName(path)} ({pages} page{(pages == 1 ? string.Empty : "s")}, {plans.Count} level plan{(plans.Count == 1 ? string.Empty : "s")})");
        }
        finally
        {
            _isExportingReport = false;
            ExportPdfCommand.NotifyCanExecuteChanged();
        }
    }

    private IReadOnlyList<PngLegendRow> _reportLegend = [];
    private string _reportLegendNote = string.Empty;

    /// <summary>
    /// Every level in plan with <paramref name="map"/>, lowest first; the legend is read while the map shows. The view
    /// (mode, level, map, camera) is put back afterwards, even when a capture fails.
    /// </summary>
    private async Task<List<ReportData.LevelPlan>> CaptureLevelPlansAsync(Func<double, Task<Result<BitmapSource>>> capture, HeatmapMode map)
    {
        var plans = new List<ReportData.LevelPlan>();
        ViewMode oldMode = _viewMode;
        LevelViewModel? oldLevel = ActiveLevel;
        HeatmapMode oldMap = _heatmapMode;
        var camera = new SavedView
        {
            Mode = _viewMode switch { ViewMode.Plan => "Plan", ViewMode.Iso => "Iso", _ => "Orbit" },
            LevelId = ActiveLevel?.Id,
            IsoPreset = _isoPreset,
        };
        WriteCamera?.Invoke(camera);
        try
        {
            HeatmapMode = map;
            ViewMode = ViewMode.Plan;
            _reportLegend = ShowHeatLegend ? [.. HeatLegend.Select(r => new PngLegendRow(r.Swatch is SolidColorBrush b ? b.Color : Colors.Gray, r.Label))] : [];
            _reportLegendNote = ShowHeatLegend ? HeatLegendNote : string.Empty;
            foreach (LevelViewModel level in Levels.OrderBy(l => l.Model.ElevationM))
            {
                ActiveLevel = level;
                ZoomExtentsRequested?.Invoke(this, EventArgs.Empty);

                // Let the viewport rebuild the level's scene and frame it before the capture's frame.
                await Task.Delay(150);
                Result<BitmapSource> shot = await capture(ReportCaptureScale);
                if (!shot.IsSuccess || shot.Value is not { } bitmap)
                {
                    AppLog.Warn(shot.Error ?? $"Report capture of {level.Model.Name} failed.");
                    continue;
                }

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = new MemoryStream();
                encoder.Save(stream);
                string name = string.Create(CultureInfo.InvariantCulture, $"{level.Model.Name} · {level.Model.ElevationM:+0.0;-0.0;0.0} m");
                plans.Add(new ReportData.LevelPlan(name, stream.ToArray(), bitmap.PixelWidth, bitmap.PixelHeight));
            }
        }
        finally
        {
            HeatmapMode = oldMap;
            ViewMode = oldMode;
            if (oldLevel is not null)
            {
                ActiveLevel = oldLevel;
            }

            ReadCamera?.Invoke(camera);
        }

        return plans;
    }

    /// <summary>The headline numbers from what RESULTS shows, the line saying what they are, and the graph.</summary>
    private List<(string, string)> ReportHeadlines(out string source, out ReportData.Series? graph)
    {
        CultureInfo c = CultureInfo.InvariantCulture;
        RunResults results = _results;
        var lines = new List<(string, string)>();
        graph = null;
        if (_shownBatch is { } batch)
        {
            int first = batch.Runs.Min(r => r.Seed), last = batch.Runs.Max(r => r.Seed);
            source = string.Create(c, $"Mean of {batch.Runs.Count} runs (seeds {first}–{last}), {Clock(batch.Options.DurationS)} each, after a {Clock(batch.WarmUpS)} warm-up.");
        }
        else if (_runner is { } runner)
        {
            source = string.Create(c, $"One run on seed {runner.Seed}, {RunClockText()}, {Clock(_stats.TimeS)} in; stats after a {Clock(results.WarmUpS)} warm-up.");
        }
        else
        {
            source = "Not run yet: layout only. PLAY or BATCH… for numbers.";
            return lines;
        }

        long finished = 0, gaveUp = 0, stuck = 0;
        double journeySum = 0;
        foreach (JourneyResult j in results.Journeys)
        {
            if (!j.IsAll && !j.IsSink)
            {
                finished += j.Finished;
                gaveUp += j.GaveUp;
                stuck += j.Stuck;
                journeySum += j.MeanS * j.Finished;
            }
        }

        int peakPeople = results.InModel.DefaultIfEmpty(0).Max();
        float peakDensity = results.PeakDensity.DefaultIfEmpty(0f).Max();
        LevelOfService los = FruinLevelOfService.FromDensity(peakDensity, LosAreaType.Walkway);
        lines.Add(("People finished their journey", finished.ToString("N0", c)));
        lines.Add(("Mean journey", finished > 0 ? Clock(journeySum / finished) : "—"));
        lines.Add(("Most people in the model", peakPeople.ToString("N0", c)));
        lines.Add(("Peak density (walkway LoS)", string.Create(c, $"{peakDensity:0.00} p/m² · {los}")));
        if (results.Devices.Count > 0 && results.Devices.MaxBy(d => d.P95WaitS) is { } worst && worst.Served > 0)
        {
            lines.Add(("Longest 95th wait", string.Create(c, $"{worst.Name} · {worst.P95WaitS:0} s")));
            lines.Add(("Longest queue", string.Create(c, $"{results.Devices.Max(d => d.MaxQueue):N0} people")));
        }

        if (results.Groups.Count > 0)
        {
            long groups = results.Groups.Sum(g => g.Finished);
            double groupMean = groups > 0 ? results.Groups.Sum(g => g.MeanS * g.Finished) / groups : 0;
            lines.Add(("Groups finished · mean", string.Create(c, $"{groups:N0} · {Clock(groupMean)}")));
        }

        lines.Add(("Gave up in a queue", gaveUp.ToString("N0", c)));
        lines.Add(("Removed as stuck", stuck.ToString("N0", c)));

        int samples = results.SampleCount;
        if (samples >= 2)
        {
            var queuing = new double[samples];
            for (int s = 0; s < samples; s++)
            {
                for (int d = 0; d < results.Devices.Count; d++)
                {
                    queuing[s] += results.QueueAt(s, d);
                }
            }

            graph = new ReportData.Series(results.Times, [.. results.InModel.Select(v => (double)v)], queuing, results.WarmUpS);
        }

        return lines;
    }

    /// <summary>CHECK findings not ignored, errors then warnings then info.</summary>
    private List<ReportData.Finding> ReportFindings(IReadOnlyList<ValidationMessage> findings) =>
        [.. findings
            .Where(f => !StudyCheck.IsIgnored(_study, f))
            .OrderBy(f => f.Severity switch { ValidationSeverity.Error => 0, ValidationSeverity.Warning => 1, _ => 2 })
            .Select(f => new ReportData.Finding(
                f.Severity.ToString().ToUpperInvariant(),
                f.Message,
                f.Severity switch
                {
                    ValidationSeverity.Error => Color.FromRgb(0xDC, 0x26, 0x26),
                    ValidationSeverity.Warning => Color.FromRgb(0xCA, 0x8A, 0x04),
                    _ => Color.FromRgb(0x25, 0x63, 0xEB),
                }))];
}
