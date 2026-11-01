using System.Diagnostics;
using System.Globalization;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using Throng.Sim;
using Throng.Sim.Batch;
using Throng.Sim.Replay;

namespace Throng.App.ViewModels;

/// <summary>
/// Replay half of the main view model (round 023, D-373–D-376): the transport bar's scrubber over the run's
/// <see cref="ReplayRecording"/>. Dragging pauses the run and shows the past (people, lift cars, trains, clock and
/// counters, D-374); PLAY replays forward at the chosen speed and carries on live at the end; LIVE jumps to now;
/// ← / → step 0.5 s (Shift 10 s); clicking the RESULTS graph jumps there. With a batch on show, RESULTS ▸ REPLAY runs
/// a chosen batch run on its seed into the viewport at MAX (D-376).
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>Replay speed for MAX, simulated seconds per real second.</summary>
    private const double ReplayMaxMultiplier = 32;

    private double? _scrubTimeS;
    private DispatcherTimer? _replayTimer;
    private readonly Stopwatch _replayClock = new();
    private bool _replayPlaying;
    private int _replayRun;
    private string? _replayLabel;
    private RelayCommand? _liveCommand;
    private RelayCommand<string?>? _scrubStepCommand;
    private RelayCommand<double>? _jumpToTimeCommand;
    private RelayCommand? _replayRunCommand;

    /// <summary>True while the viewport shows a replayed time rather than the live run.</summary>
    public bool IsScrubbed => _scrubTimeS is not null;

    /// <summary>True when the run has at least two frames to scrub between.</summary>
    public bool CanScrub => _runner is { } runner && !_isBatching && runner.Recording.FrameCount >= 2;

    /// <summary>The slider's end: the latest recorded time, seconds.</summary>
    public double ScrubMaximum => _runner?.Recording.EndS ?? 0;

    /// <summary>The slider's position: the replayed time, or the latest recorded time while live.</summary>
    public double ScrubPositionS
    {
        get => _scrubTimeS ?? ScrubMaximum;
        set
        {
            if (_scrubTimeS is null && Math.Abs(value - ScrubMaximum) < 1e-6)
            {
                return;
            }

            StopReplay();
            ScrubTo(value);
        }
    }

    /// <summary>The scrubber's label: "LIVE · 12:34" or "REPLAY · 08:15 / 12:34".</summary>
    public string ScrubTimeText
    {
        get
        {
            if (_runner is null)
            {
                return "NO RUN";
            }

            double end = ScrubMaximum;
            return _scrubTimeS is { } t
                ? string.Create(CultureInfo.InvariantCulture, $"REPLAY · {Clock(t)} / {Clock(end)}")
                : string.Create(CultureInfo.InvariantCulture, $"LIVE · {Clock(end)}");
        }
    }

    /// <summary>The RESULTS graph's cursor: the replayed time, or NaN (live, or a batch on show).</summary>
    public double GraphCursorS => !ShowsBatch && _scrubTimeS is { } t ? t : double.NaN;

    /// <summary>LIVE: back to the run as it stands (D-375).</summary>
    public RelayCommand LiveCommand => _liveCommand ??= new RelayCommand(GoLive, () => _scrubTimeS is not null);

    /// <summary>Steps the replay by the parameter in seconds ("-0.5", "10"…); from live it starts at the end (D-375).</summary>
    public RelayCommand<string?> ScrubStepCommand => _scrubStepCommand ??= new RelayCommand<string?>(ScrubStep, _ => CanScrub);

    /// <summary>Jumps the replay to a time clicked on the RESULTS graph (D-375).</summary>
    public RelayCommand<double> JumpToTimeCommand => _jumpToTimeCommand ??= new RelayCommand<double>(t =>
    {
        if (CanScrub && !ShowsBatch)
        {
            StopReplay();
            ScrubTo(t);
        }
    });

    /// <summary>RESULTS ▸ RUN options while a batch shows: "RUN 3 · SEED 44" (D-376).</summary>
    public IReadOnlyList<LabelledOption<int>> ReplayRunOptions =>
        _batch is { } batch
            ? batch.Runs.Select(r => new LabelledOption<int>(r.Index, string.Create(CultureInfo.InvariantCulture, $"RUN {r.Index + 1} · SEED {r.Seed}"))).ToList()
            : new List<LabelledOption<int>>();

    /// <summary>RESULTS ▸ RUN: which batch run REPLAY runs.</summary>
    public int SelectedReplayRun
    {
        get => _replayRun;
        set => SetProperty(ref _replayRun, value);
    }

    /// <summary>RESULTS ▸ REPLAY (D-376).</summary>
    public RelayCommand ReplayRunCommand => _replayRunCommand ??= new RelayCommand(ReplayBatchRun, () => _batch is not null && !_isBatching);

    /// <summary>Shows the recording at <paramref name="timeS"/> (pausing the run); at or past the latest frame goes live.</summary>
    private void ScrubTo(double timeS)
    {
        if (_runner is not { } runner || _isBatching || runner.Recording.FrameCount < 2)
        {
            return;
        }

        double start = runner.Recording.StartS, end = runner.Recording.EndS;
        double t = Math.Clamp(timeS, start, end);
        if (t >= end - 1e-6)
        {
            GoLive();
            return;
        }

        if (runner.IsPlaying)
        {
            runner.Pause();
            Ticker = "REPLAY ▸ the run is paused while you look back. PLAY replays forward, LIVE jumps to now.";
        }

        _scrubTimeS = t;
        runner.ViewTimeS = t;
        RefreshScrub();
        RefreshRunStatus();
    }

    /// <summary>Back to the live run (not resumed: press PLAY to carry on).</summary>
    private void GoLive()
    {
        StopReplay();
        if (_scrubTimeS is null)
        {
            return;
        }

        _scrubTimeS = null;
        if (_runner is { } runner)
        {
            runner.ViewTimeS = null;
        }

        RefreshScrub();
        RefreshRunStatus();
    }

    private void ScrubStep(string? parameter)
    {
        if (_runner is not { } runner
            || !double.TryParse(parameter, NumberStyles.Float, CultureInfo.InvariantCulture, out double step))
        {
            return;
        }

        StopReplay();
        ScrubTo((_scrubTimeS ?? runner.Recording.EndS) + step);
    }

    /// <summary>PLAY while scrubbed: plays the recording forward at the chosen speed (PAUSE stops it).</summary>
    private void ToggleReplay()
    {
        if (_replayPlaying)
        {
            StopReplay();
            RefreshRunStatus();
            return;
        }

        _replayTimer ??= CreateReplayTimer();
        _replayPlaying = true;
        _replayClock.Restart();
        _replayTimer.Start();
        Ticker = "REPLAYING ▸ PAUSE stops; at the end the run carries on live.";
        RefreshRunStatus();
    }

    private DispatcherTimer CreateReplayTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Render, _dispatcher) { Interval = TimeSpan.FromMilliseconds(33) };
        timer.Tick += (_, _) => AdvanceReplay();
        return timer;
    }

    private void AdvanceReplay()
    {
        if (!_replayPlaying || _runner is not { } runner || _scrubTimeS is not { } t)
        {
            StopReplay();
            return;
        }

        double wall = _replayClock.Elapsed.TotalSeconds;
        _replayClock.Restart();
        double rate = _speed.IsMax ? ReplayMaxMultiplier : _speed.Multiplier;
        double next = t + (Math.Min(wall, 0.25) * rate);
        if (next >= runner.Recording.EndS - 1e-6)
        {
            // Caught up: carry on live (D-375), unless the run had already finished.
            GoLive();
            if (!runner.IsFinished)
            {
                runner.Play();
                Ticker = "LIVE ▸ the replay caught up; the run carries on.";
            }

            RefreshRunStatus();
            return;
        }

        _scrubTimeS = next;
        runner.ViewTimeS = next;
        RefreshScrub();
    }

    private void StopReplay()
    {
        if (!_replayPlaying)
        {
            return;
        }

        _replayPlaying = false;
        _replayTimer?.Stop();
    }

    /// <summary>Drops any replay state (RESET, a new run).</summary>
    private void ClearScrub()
    {
        StopReplay();
        _scrubTimeS = null;
        RefreshScrub();
    }

    /// <summary>Notifies everything the scrubber shows (called on the run tick and on every scrub).</summary>
    private void RefreshScrub()
    {
        OnPropertyChanged(nameof(ScrubMaximum));
        OnPropertyChanged(nameof(ScrubPositionS));
        OnPropertyChanged(nameof(ScrubTimeText));
        OnPropertyChanged(nameof(IsScrubbed));
        OnPropertyChanged(nameof(CanScrub));
        OnPropertyChanged(nameof(GraphCursorS));
        OnPropertyChanged(nameof(ClockText));
        OnPropertyChanged(nameof(PlayText));
        OnPropertyChanged(nameof(PlayGlyph));
        OnPropertyChanged(nameof(IsPlaying));
        LiveCommand.NotifyCanExecuteChanged();
        ScrubStepCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// RESULTS ▸ REPLAY (D-376): runs the chosen batch run on its seed (and the batch's duration) into the viewport at
    /// MAX, recording; it pauses at the end ready to scrub. The study's seed is untouched; the batch stays.
    /// </summary>
    private void ReplayBatchRun()
    {
        if (_batch is not { } batch || _isBatching)
        {
            return;
        }

        BatchRun? run = batch.Runs.FirstOrDefault(r => r.Index == _replayRun) ?? batch.Runs.FirstOrDefault();
        if (run is null)
        {
            return;
        }

        if (_batchStudyChanged
            && !_dialogs.Confirm("Replay a batch run",
                "The study has changed since the batch ran, so this replay may not match the batch's numbers. Replay anyway?", "REPLAY"))
        {
            return;
        }

        if (_isRunActive)
        {
            if (!_dialogs.Confirm("Replay a batch run", "This ends the current run and replays the batch run in the viewport.", "REPLAY"))
            {
                return;
            }

            ResetRun();
        }

        _replayLabel = string.Create(CultureInfo.InvariantCulture, $"RUN {run.Index + 1} · SEED {run.Seed}");
        SetSpeed("MAX");
        _ = StartRunAsync(play: true, seedOverride: run.Seed, durationOverride: batch.Options.DurationS);
    }
}
