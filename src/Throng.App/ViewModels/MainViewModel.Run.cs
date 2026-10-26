using System.Globalization;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using Throng.App.Editing;
using Throng.App.Services;
using Throng.Core.Geometry;
using Throng.Core.Model;
using Throng.Core.Validation;
using Throng.Render.Gl;
using Throng.Sim;
using Throng.Sim.Engine;

namespace Throng.App.ViewModels;

/// <summary>
/// Run half of the main view model (M2, round 006): PLAY / PAUSE, STEP, RESET and speed chips (D-104), the edit
/// lock while a run is active (D-097), the CHECK gate before a run starts (D-106), the status-bar counters and
/// the STUDY tab's RUN settings (D-103, D-105). The run itself lives on a <see cref="SimRunner"/> thread.
/// </summary>
public sealed partial class MainViewModel
{
    private static readonly IReadOnlyList<StudyEndCondition> EndOptions = [StudyEndCondition.FixedDuration, StudyEndCondition.Indefinite];
    private static readonly IReadOnlyList<KeepSide> KeepSides = Enum.GetValues<KeepSide>();

    private Dispatcher _dispatcher = null!;
    private DispatcherTimer _runTimer = null!;
    private SimRunner? _runner;
    private CancellationTokenSource? _setupCancel;
    private bool _isRunActive;
    private AgentColoring _agentColoring = AgentColoring.Profile;
    private bool _isPreparing;
    private RunSpeed _speed = RunSpeed.RealTime;
    private SimStats _stats;
    private float[] _rawAgents = new float[64 * SimEngine.FloatsPerAgent];

    public RelayCommand PlayPauseCommand { get; private set; } = null!;
    public RelayCommand StepCommand { get; private set; } = null!;
    public RelayCommand ResetRunCommand { get; private set; } = null!;
    public RelayCommand<string?> SetSpeedCommand { get; private set; } = null!;
    public RelayCommand ReRollSeedCommand { get; private set; } = null!;

    /// <summary>True from PLAY until RESET (including while the world compiles): editing is locked (D-097).</summary>
    public bool IsRunActive
    {
        get => _isRunActive;
        private set
        {
            if (_isRunActive == value)
            {
                return;
            }

            _isRunActive = value;
            Editor.IsLocked = value;
            if (value)
            {
                Editor.Reset();
            }

            UndoCommand.NotifyCanExecuteChanged();
            RedoCommand.NotifyCanExecuteChanged();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsEditable));
            OnPropertyChanged(nameof(RunStatusText));
            OnPropertyChanged(nameof(ClockText));
        }
    }

    /// <summary>False while a run is active; the draw strip, LIBRARY and the right panel bind to it.</summary>
    public bool IsEditable => !_isRunActive;

    /// <summary>True while the run is playing.</summary>
    public bool IsPlaying => _runner?.IsPlaying == true;

    /// <summary>PLAY or PAUSE.</summary>
    public string PlayText => IsPlaying ? "PAUSE" : "PLAY";

    /// <summary>Glyph for the play/pause button (Segoe MDL2).</summary>
    public string PlayGlyph => IsPlaying ? "" : "";

    public bool IsSpeedHalf { get => IsSpeed(0.5); set => SetSpeedChip(value, "0.5"); }

    public bool IsSpeed1 { get => IsSpeed(1); set => SetSpeedChip(value, "1"); }

    public bool IsSpeed2 { get => IsSpeed(2); set => SetSpeedChip(value, "2"); }

    public bool IsSpeed4 { get => IsSpeed(4); set => SetSpeedChip(value, "4"); }

    public bool IsSpeed8 { get => IsSpeed(8); set => SetSpeedChip(value, "8"); }

    public bool IsSpeedMax { get => _speed.IsMax; set => SetSpeedChip(value, "MAX"); }

    /// <summary>What agent bodies are coloured by (D-146); a view setting, not saved and not undoable.</summary>
    public AgentColoring AgentColoring
    {
        get => _agentColoring;
        set
        {
            if (SetProperty(ref _agentColoring, value))
            {
                OnPropertyChanged(nameof(IsColourProfile));
                OnPropertyChanged(nameof(IsColourStress));
            }
        }
    }

    /// <summary>COLOUR ▸ PROFILE chip.</summary>
    public bool IsColourProfile
    {
        get => _agentColoring == AgentColoring.Profile;
        set
        {
            // A chip click always selects; clicking the lit chip re-lights it rather than leaving neither lit.
            AgentColoring = AgentColoring.Profile;
            OnPropertyChanged(nameof(IsColourProfile));
        }
    }

    /// <summary>COLOUR ▸ STRESS chip.</summary>
    public bool IsColourStress
    {
        get => _agentColoring == AgentColoring.Stress;
        set
        {
            AgentColoring = AgentColoring.Stress;
            OnPropertyChanged(nameof(IsColourStress));
        }
    }

    /// <summary>
    /// Status-bar counters (D-104), with people queuing, stressed (stress ≥ 0.5) and given up (D-168), or a hint when
    /// no run is active.
    /// </summary>
    public string RunStatusText
    {
        get
        {
            if (!_isRunActive)
            {
                return "PLAY to run";
            }

            if (_isPreparing)
            {
                return "COMPILING…";
            }

            CultureInfo c = CultureInfo.InvariantCulture;
            TimeSpan journey = TimeSpan.FromSeconds(Math.Round(_stats.MeanJourneyS));
            string speed = _speed.IsMax ? "MAX" : string.Create(c, $"×{_speed.Multiplier:0.#}");
            string stuck = _stats.StuckRemoved > 0
                ? string.Create(c, $"{_stats.StuckNow} (+{_stats.StuckRemoved} removed)")
                : _stats.StuckNow.ToString(c);
            string leftBehind = _runner?.Model.Trains.Count > 0 ? string.Create(c, $" · LEFT BEHIND {_stats.LeftBehind}") : string.Empty;
            return string.Create(c,
                $"{speed} · AGENTS {_stats.InSystem} · IN {_stats.Spawned} · OUT {_stats.Arrived} · JOURNEY {(int)journey.TotalMinutes}:{journey.Seconds:00} · QUEUING {_stats.Queuing} · STRESSED {_stats.Stressed} · GAVE UP {_stats.GaveUp}{leftBehind} · STUCK {stuck}");
        }
    }

    // ------------------------------------------------------------------ STUDY ▸ RUN settings (D-103, D-105)

    /// <summary>End conditions offered in round 006: fixed duration or run until stopped.</summary>
    public IReadOnlyList<StudyEndCondition> EndConditionOptions => EndOptions;

    /// <summary>How the run ends (undoable).</summary>
    public StudyEndCondition EndCondition
    {
        get => _study.Settings.EndCondition;
        set => Apply(value == StudyEndCondition.FixedDuration ? "Run for a fixed duration" : "Run until stopped",
            () => _study.Settings.EndCondition, v => _study.Settings.EndCondition = v, value);
    }

    /// <summary>True when the duration field applies.</summary>
    public bool HasDuration => _study.Settings.EndCondition == StudyEndCondition.FixedDuration;

    /// <summary>Keep-side options.</summary>
    public IReadOnlyList<KeepSide> KeepSideOptions => KeepSides;

    /// <summary>Keep-side bias (undoable).</summary>
    public KeepSide KeepSide
    {
        get => _study.Settings.KeepSide;
        set => Apply("Change keep side", () => _study.Settings.KeepSide, v => _study.Settings.KeepSide = v, value);
    }

    /// <summary>
    /// Fills <paramref name="buffer"/> with the agents to draw for the current view (D-102): in plan only those on
    /// the active level's height band, in 3D those on visible levels. Returns the count; 0 when no run is active.
    /// Called by the viewport every frame on the UI thread.
    /// </summary>
    public int FetchAgents(ViewMode mode, ref float[] buffer)
    {
        if (_runner is null)
        {
            return 0;
        }

        int n = _runner.CopySnapshot(ref _rawAgents, out _, out _);
        if (buffer.Length < _rawAgents.Length)
        {
            buffer = new float[_rawAgents.Length];
        }

        // Height bands: from just below each level's floor to just below the next level's floor.
        IReadOnlyList<Level> levels = _study.LevelsAscending();
        Span<double> lo = stackalloc double[Math.Max(1, levels.Count)];
        Span<double> hi = stackalloc double[Math.Max(1, levels.Count)];
        Span<bool> show = stackalloc bool[Math.Max(1, levels.Count)];
        Guid? active = _activeLevel?.Id;
        for (int k = 0; k < levels.Count; k++)
        {
            lo[k] = k == 0 ? double.NegativeInfinity : levels[k].ElevationM - 0.5;
            hi[k] = k == levels.Count - 1 ? double.PositiveInfinity : levels[k + 1].ElevationM - 0.5;
            show[k] = mode == ViewMode.Plan ? levels[k].Id == active : levels[k].IsVisible;
        }

        const int F = SimEngine.FloatsPerAgent;
        int count = 0;
        for (int i = 0; i < n; i++)
        {
            double z = _rawAgents[(i * F) + 2];
            bool visible = levels.Count == 0;
            for (int k = 0; k < levels.Count; k++)
            {
                if (z >= lo[k] && z < hi[k])
                {
                    visible = show[k];
                    break;
                }
            }

            if (visible)
            {
                Array.Copy(_rawAgents, i * F, buffer, count * F, F);
                count++;
            }
        }

        return count;
    }

    /// <summary>A LIBRARY row dropped on the viewport (D-098): picks the tool and preset, then drops at the point.</summary>
    public void DropTemplate(TemplateItemViewModel item, Point2? planPoint, double metresPerDip)
    {
        if (_isRunActive)
        {
            Ticker = "RUNNING ▸ RESET TO EDIT";
            return;
        }

        UseTemplate(item);
        if (planPoint is not { } point)
        {
            Ticker = "Switched to plan ▸ click to place it.";
            return;
        }

        if (Editor.Tool is EditorTool.Place or EditorTool.Connect)
        {
            Editor.DropAt(point, metresPerDip);
        }
    }

    /// <summary>Stops any run and its thread (window closing).</summary>
    public void Shutdown() => ResetRun();

    /// <summary>Creates the run commands and the 4 Hz status timer. Called once from the constructor.</summary>
    private void InitialiseRun()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        PlayPauseCommand = new RelayCommand(PlayPause);
        StepCommand = new RelayCommand(StepRun);
        ResetRunCommand = new RelayCommand(ResetRun, () => _isRunActive);
        SetSpeedCommand = new RelayCommand<string?>(SetSpeed);
        ReRollSeedCommand = new RelayCommand(() => Seed = Random.Shared.Next(1, int.MaxValue));
        _runTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = TimeSpan.FromMilliseconds(250) };
        _runTimer.Tick += (_, _) => RefreshRunStatus();
    }

    private void PlayPause()
    {
        if (_runner is { } runner)
        {
            if (runner.IsPlaying)
            {
                runner.Pause();
                Ticker = "PAUSED ▸ STEP advances 0.1 s, RESET unlocks editing.";
            }
            else if (runner.IsFinished)
            {
                Ticker = "RUN FINISHED ▸ RESET to run again, or set the end to 'run until stopped' in STUDY.";
            }
            else
            {
                runner.Play();
                Ticker = "RUNNING ▸ editing is locked until RESET.";
            }

            RefreshRunStatus();
            return;
        }

        _ = StartRunAsync(play: true);
    }

    private void StepRun()
    {
        if (_runner is { } runner)
        {
            runner.Pause();
            runner.Step();
            RefreshRunStatus();
            return;
        }

        _ = StartRunAsync(play: false);
    }

    /// <summary>
    /// Locks editing, compiles the world on a worker thread (CHECK first, D-106) and starts the runner. Errors
    /// unlock again and show the findings.
    /// </summary>
    private async Task StartRunAsync(bool play)
    {
        if (_isPreparing)
        {
            return;
        }

        _isPreparing = true;
        IsRunActive = true;
        ResetRunCommand.NotifyCanExecuteChanged();
        Ticker = "COMPILING ▸ navigation grids, portals and flow fields…";
        OnPropertyChanged(nameof(RunStatusText));

        var cancel = new CancellationTokenSource();
        _setupCancel = cancel;
        Study study = _study;
        SimSetupResult? result = null;
        try
        {
            result = await Task.Run(() => SimSetup.Build(study, null, cancel.Token), cancel.Token);
        }
        catch (OperationCanceledException)
        {
            // Reset (or a new study) while compiling.
        }
        catch (Exception ex)
        {
            // Boundary: a bug in the compiler must not take the app down.
            AppLog.Error("Simulation set-up failed", ex);
            _dialogs.ShowError($"The run couldn't start: {ex.Message}");
        }

        if (cancel.IsCancellationRequested || !ReferenceEquals(_setupCancel, cancel) || !ReferenceEquals(study, _study))
        {
            cancel.Dispose();
            return;
        }

        _setupCancel = null;
        cancel.Dispose();
        _isPreparing = false;

        if (result?.Model is not { } model)
        {
            IsRunActive = false;
            ResetRunCommand.NotifyCanExecuteChanged();
            if (result is not null)
            {
                ShowFindings(result.Findings, "CHECK FAILED ▸ fix the errors and press PLAY again.");
            }

            return;
        }

        var runner = new SimRunner(model) { Speed = _speed };
        runner.Faulted += ex => _dispatcher.InvokeAsync(() =>
        {
            AppLog.Error("Simulation step failed", ex);
            Ticker = $"RUN STOPPED ▸ {ex.Message} (details in the log)";
            RefreshRunStatus();
        });
        runner.Finished += () => _dispatcher.InvokeAsync(() =>
        {
            Ticker = "RUN FINISHED ▸ RESET to edit or run again.";
            RefreshRunStatus();
        });
        _runner = runner;

        int warnings = result.Findings.Count(f => f.Severity == ValidationSeverity.Warning);
        string emitters = model.Emitters.Count == 0 ? "no emission rules, so no one will appear" : string.Create(CultureInfo.InvariantCulture, $"{model.Emitters.Count} emission rules");
        Ticker = string.Create(CultureInfo.InvariantCulture,
            $"{(play ? "RUNNING" : "READY")} ▸ {model.World.Surfaces.Count} surfaces, {model.World.Portals.Count} portals, {emitters}{(warnings > 0 ? $" ▸ {warnings} warnings (CHECK lists them)" : string.Empty)}");
        AppLog.Info($"Run started: {model.World.Surfaces.Count} surfaces, {model.World.Portals.Count} portals, {model.Emitters.Count} emitters.");

        if (play)
        {
            runner.Play();
        }
        else
        {
            runner.Step();
        }

        _runTimer.Start();
        RefreshRunStatus();
    }

    /// <summary>Stops the run, unlocks editing (D-097) and clears the agents.</summary>
    private void ResetRun()
    {
        _setupCancel?.Cancel();
        _setupCancel = null;
        _isPreparing = false;
        _runner?.Dispose();
        _runner = null;
        _runTimer?.Stop();
        _stats = default;
        bool wasActive = _isRunActive;
        IsRunActive = false;
        ResetRunCommand?.NotifyCanExecuteChanged();
        RefreshRunStatus();
        if (wasActive)
        {
            Ticker = "RESET ▸ editing unlocked.";
        }
    }

    private void SetSpeed(string? value)
    {
        _speed = value switch
        {
            "MAX" => RunSpeed.Max,
            _ when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double m) && m > 0 => new RunSpeed(m, false),
            _ => _speed,
        };

        if (_runner is { } runner)
        {
            runner.Speed = _speed;
        }

        OnPropertyChanged(nameof(IsSpeedHalf));
        OnPropertyChanged(nameof(IsSpeed1));
        OnPropertyChanged(nameof(IsSpeed2));
        OnPropertyChanged(nameof(IsSpeed4));
        OnPropertyChanged(nameof(IsSpeed8));
        OnPropertyChanged(nameof(IsSpeedMax));
        OnPropertyChanged(nameof(RunStatusText));
    }

    private bool IsSpeed(double multiplier) => !_speed.IsMax && Math.Abs(_speed.Multiplier - multiplier) < 1e-9;

    private void SetSpeedChip(bool on, string value)
    {
        if (on)
        {
            SetSpeed(value);
        }
        else
        {
            // Chips behave like radio buttons: clicking the lit one keeps it lit.
            SetSpeed(null);
        }
    }

    /// <summary>
    /// Hover text while running (D-183): a connector's or exit's current crowd wait, e.g. "~14 S"; null otherwise.
    /// </summary>
    public string? RunHoverInfo(Guid id) =>
        _runner is { } runner && runner.TryGetCrowdWait(id, out double seconds)
            ? string.Create(CultureInfo.InvariantCulture, $"~{seconds:0} S")
            : null;

    /// <summary>Trains in on a level while running, for the plan overlay (D-190).</summary>
    public IReadOnlyList<RunTrainView> RunTrains(Guid levelId)
    {
        if (_runner is not { } runner)
        {
            return [];
        }

        var views = new List<RunTrainView>();
        foreach ((SimTrain train, bool isIn, bool boarding) in runner.CopyTrainStates())
        {
            if (!isIn || train.LevelId != levelId)
            {
                continue;
            }

            List<IReadOnlyList<Point2>> doors = [.. train.DoorRegionIds.Select(_study.FindRegion).OfType<Region>().Select(r => (IReadOnlyList<Point2>)r.Outline)];
            views.Add(new RunTrainView(train.Box, doors, boarding));
        }

        return views;
    }

    private void RefreshRunStatus()
    {
        _stats = _runner?.LatestStats ?? default;
        Editor?.RefreshRunHover();
        OnPropertyChanged(nameof(RunStatusText));
        OnPropertyChanged(nameof(ClockText));
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(PlayText));
        OnPropertyChanged(nameof(PlayGlyph));
    }

    /// <summary>Lists findings in a dialog (errors first) and puts a summary in the ticker.</summary>
    private void ShowFindings(IReadOnlyList<ValidationMessage> findings, string ticker)
    {
        string lines = string.Join(Environment.NewLine,
            findings.OrderByDescending(f => f.Severity).Select(f => $"[{f.Severity}] {f.Message}"));
        _dialogs.ShowInfo("Check", lines.Length == 0 ? "No problems found." : lines);
        Ticker = ticker;
    }

    /// <summary>Study clock: the run's time of day while a run is active, otherwise the start time.</summary>
    private string RunClockText()
    {
        TimeSpan start = _study.Settings.StartTimeOfDay;
        if (!_isRunActive || _runner is null)
        {
            return SimClock.FormatTimeOfDay(start);
        }

        TimeSpan absolute = start + TimeSpan.FromSeconds(_stats.TimeS);
        int day = (int)Math.Floor(absolute.TotalDays);
        return SimClock.FormatTimeOfDay(absolute - TimeSpan.FromDays(day), day);
    }
}
