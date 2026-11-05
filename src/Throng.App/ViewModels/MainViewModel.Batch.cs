using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Throng.App.Services;
using Throng.Core.Model;
using Throng.Sim;
using Throng.Sim.Batch;

namespace Throng.App.ViewModels;

/// <summary>
/// Batch half of the main view model (round 021, D-352–D-365): STUDY ▸ RUN ▸ BATCH… asks for runs (and a duration
/// for "until stopped"), checks and compiles the study once like PLAY (CHECK errors block, D-365), then runs
/// <see cref="BatchRunner"/> on a worker thread with editing locked and nothing live in the viewport, a progress line
/// and CANCEL (D-357). The finished batch lives for the session (D-362); RESULTS' SOURCE chips switch every table,
/// graph, run map and the journey overlay between LAST RUN and BATCH (N) (D-363, D-364).
/// </summary>
public sealed partial class MainViewModel
{
    private BatchRunner? _batchRunner;
    private CancellationTokenSource? _batchCancel;
    private bool _isBatching;
    private BatchResults? _batch;
    private SimModel? _batchModel;
    private bool _showBatch;

    /// <summary>True once the study is edited after the batch ran: REPLAY warns its numbers may differ (D-376).</summary>
    private bool _batchStudyChanged;
    private RelayCommand? _batchCommand;

    /// <summary>STUDY ▸ RUN ▸ BATCH… (D-363): only while no run is active.</summary>
    public RelayCommand BatchCommand => _batchCommand ??= new RelayCommand(() => _ = StartBatchAsync(), () => !_isRunActive);

    /// <summary>True from START in the BATCH dialog until the batch finishes or is cancelled.</summary>
    public bool IsBatching => _isBatching;

    /// <summary>True when this session has a finished batch: RESULTS shows the SOURCE chips.</summary>
    public bool HasBatch => _batch is not null;

    /// <summary>The BATCH source chip's label, e.g. "BATCH (5)".</summary>
    public string BatchSourceLabel => _batch is { } batch ? string.Create(CultureInfo.InvariantCulture, $"BATCH ({batch.Count})") : "BATCH";

    /// <summary>RESULTS ▸ SOURCE ▸ LAST RUN chip (D-363).</summary>
    public bool IsSourceLastRun { get => !ShowsBatch; set => SelectSource(false, nameof(IsSourceLastRun)); }

    /// <summary>RESULTS ▸ SOURCE ▸ BATCH (N) chip (D-363).</summary>
    public bool IsSourceBatch { get => ShowsBatch; set => SelectSource(true, nameof(IsSourceBatch)); }

    /// <summary>The toolbar's RESET button reads CANCEL while a batch runs (D-357).</summary>
    public string ResetText => _isBatching ? "CANCEL" : "RESET";

    /// <summary>True while RESULTS, the run maps and the overlay show the batch rather than the last run.</summary>
    private bool ShowsBatch => _showBatch && _batch is not null;

    /// <summary>The status-bar progress line while a batch runs (D-357); compact for the bar, full for its tooltip.</summary>
    private string BatchStatusText(bool compact)
    {
        if (_batchRunner is not { } runner)
        {
            return compact ? "BATCH ▸ COMPILING…" : "BATCH ▸ CHECKING AND COMPILING…";
        }

        BatchProgress p = runner.Progress;
        return compact
            ? string.Create(CultureInfo.InvariantCulture, $"BATCH {p.Done}/{p.Total} · {p.Running} RUNNING · {p.Fraction * 100:0}%")
            : string.Create(CultureInfo.InvariantCulture,
                $"BATCH ▸ {p.Done} OF {p.Total} DONE · {p.Running} RUNNING ({runner.Parallelism} AT ONCE) · {p.Fraction * 100:0}% · CANCEL STOPS IT");
    }

    /// <summary>Asks for the batch, checks and compiles once, runs every seed in parallel and shows the result.</summary>
    private async Task StartBatchAsync()
    {
        if (_isRunActive || _isBatching)
        {
            Ticker = "RESET the run first ▸ then BATCH.";
            return;
        }

        StudySettings settings = _study.Settings;
        double? fixedMinutes = settings.EndCondition == StudyEndCondition.FixedDuration ? Math.Max(1, settings.DurationS) / 60.0 : null;
        if (_dialogs.AskBatch(settings.Seed, fixedMinutes) is not { } request)
        {
            return;
        }

        var cancel = new CancellationTokenSource();
        _batchCancel = cancel;
        SetBatching(true);
        Ticker = "BATCH ▸ checking and compiling the study…";

        EmbedReferencedFromMine();
        Study study = _study;
        SimSetupResult? setup = null;
        try
        {
            setup = await Task.Run(() => SimSetup.Build(study, null, cancel.Token), cancel.Token);
        }
        catch (OperationCanceledException)
        {
            // Cancelled (or a new study) while compiling.
        }
        catch (Exception ex)
        {
            // Boundary: a bug in the compiler must not take the app down.
            AppLog.Error("Batch set-up failed", ex);
            _dialogs.ShowError($"The batch couldn't start: {ex.Message}");
        }

        if (!ReferenceEquals(_batchCancel, cancel) || cancel.IsCancellationRequested || !ReferenceEquals(study, _study))
        {
            // CANCEL or a new study has already tidied up.
            cancel.Dispose();
            return;
        }

        if (setup?.Model is not { } model)
        {
            EndBatch(cancel);
            if (setup is not null)
            {
                // Blocking errors open the CHECK tab, as for PLAY (D-271, D-365).
                ShowCheck(Throng.Analysis.Checks.StudyCheck.Normalise(setup.Findings), "CHECK FAILED ▸ fix the errors (CHECK tab) and BATCH again.");
            }

            return;
        }

        var options = new BatchOptions(settings.Seed, request.Runs, request.DurationMinutes * 60);
        var runner = new BatchRunner(model, options);
        _batchRunner = runner;
        CultureInfo c = CultureInfo.InvariantCulture;
        Ticker = string.Create(c, $"BATCH ▸ {options.Runs} runs on seeds {options.SeedOf(0)}–{options.SeedOf(options.Runs - 1)}, {runner.Parallelism} at once ▸ editing is locked.");
        AppLog.Info(string.Create(c, $"Batch started: {options.Runs} runs from seed {options.BaseSeed}, {options.DurationS:0} s each, {runner.Parallelism} at once."));
        RefreshRunStatus();

        BatchResults? results = null;
        try
        {
            results = await Task.Run(() => runner.Run(cancel.Token));
        }
        catch (Exception) when (cancel.IsCancellationRequested)
        {
            // CANCEL: the workers stop at their next check.
        }
        catch (Exception ex)
        {
            // Boundary for the batch workers: log the run's fault, never crash.
            Exception inner = ex is AggregateException { InnerExceptions.Count: > 0 } agg ? agg.InnerExceptions[0] : ex;
            AppLog.Error("Batch run failed", ex);
            _dialogs.ShowError($"The batch stopped: {inner.Message} (details in the log).");
        }

        if (!ReferenceEquals(_batchCancel, cancel))
        {
            cancel.Dispose();
            return;
        }

        EndBatch(cancel);
        if (results is null)
        {
            return;
        }

        _batch = results;
        _batchModel = model;
        _batchStudyChanged = false;
        _showBatch = true;
        SetPredictionModel(model, "BATCH");
        OnBatchChanged();
        PropertiesTabIndex = PropertiesTabResults;
        TimeSpan took = results.Elapsed;
        Ticker = string.Create(c,
            $"BATCH DONE ▸ {results.Count} runs (seeds {results.SeedRange.First}–{results.SeedRange.Last}) in {(int)took.TotalMinutes}:{took.Seconds:00} ▸ RESULTS shows mean (lowest–highest run).");
        AppLog.Info(string.Create(c, $"Batch finished: {results.Count} runs in {took.TotalSeconds:0.0} s, {results.Parallelism} at once."));
    }

    /// <summary>Locks or unlocks editing for a batch and refreshes what depends on it.</summary>
    private void SetBatching(bool on)
    {
        _isBatching = on;
        if (!on)
        {
            _batchRunner = null;
        }

        IsRunActive = on;
        ResetRunCommand.NotifyCanExecuteChanged();
        if (on)
        {
            _runTimer.Start();
        }
        else
        {
            _runTimer.Stop();
        }

        OnPropertyChanged(nameof(IsBatching));
        OnPropertyChanged(nameof(ResetText));
        OnPropertyChanged(nameof(RunStatusText));
        OnPropertyChanged(nameof(ShowHeatLegend));
    }

    /// <summary>Ends the batch this token belongs to (finished, failed or check failed).</summary>
    private void EndBatch(CancellationTokenSource cancel)
    {
        if (ReferenceEquals(_batchCancel, cancel))
        {
            _batchCancel = null;
        }

        cancel.Dispose();
        SetBatching(false);
        RefreshRunStatus();
    }

    /// <summary>CANCEL (the RESET button while batching): stops the workers and unlocks editing straight away.</summary>
    private void CancelBatch()
    {
        if (!_isBatching)
        {
            return;
        }

        _batchCancel?.Cancel();
        _batchCancel = null;
        SetBatching(false);
        Ticker = "BATCH CANCELLED ▸ editing unlocked; the previous results are kept.";
        AppLog.Info("Batch cancelled.");
    }

    /// <summary>Drops the session's batch (a new or opened study, D-362).</summary>
    private void ClearBatch()
    {
        CancelBatch();
        if (_batch is null)
        {
            return;
        }

        _batch = null;
        _batchModel = null;
        _showBatch = false;
        OnBatchChanged();
    }

    /// <summary>PLAY shows the new run's results (the batch stays one click away).</summary>
    private void ShowLastRun()
    {
        if (_showBatch)
        {
            _showBatch = false;
            OnBatchChanged();
        }
    }

    private void SelectSource(bool batch, string chip)
    {
        bool want = batch && _batch is not null;
        if (_showBatch != want)
        {
            _showBatch = want;
            OnBatchChanged();
        }

        OnPropertyChanged(chip);
    }

    /// <summary>Everything that reads the source: tables, graph, run maps, overlay, chips.</summary>
    private void OnBatchChanged()
    {
        OnPropertyChanged(nameof(HasBatch));
        OnPropertyChanged(nameof(BatchSourceLabel));
        OnPropertyChanged(nameof(IsSourceLastRun));
        OnPropertyChanged(nameof(IsSourceBatch));
        OnPropertyChanged(nameof(SourceOptions));
        OnPropertyChanged(nameof(SelectedSource));
        OnPropertyChanged(nameof(ReplayRunOptions));
        OnPropertyChanged(nameof(GraphCursorS));
        ReplayRunCommand.NotifyCanExecuteChanged();
        if (_batch is not null && _replayRun >= _batch.Count)
        {
            SelectedReplayRun = 0;
        }

        RefreshResults();
        InvalidateHeat();
        RefreshJourneyOverlay();
    }
}
