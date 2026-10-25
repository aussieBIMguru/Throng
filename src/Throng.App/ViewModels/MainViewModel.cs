using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Throng.App.Editing;
using Throng.App.Services;
using Throng.Core.Editing;
using Throng.Core.Library;
using Throng.Core.Model;
using Throng.Core.Validation;
using Throng.IO;
using Throng.Render.Gl;
using Throng.Sim;

namespace Throng.App.ViewModels;

/// <summary>
/// Main window state: the open study, file commands, undo/redo, levels, library browser and view mode.
/// All model edits go through <see cref="Apply{T}"/> (or <see cref="Execute"/> for editor actions) so they are
/// undoable and mark the study dirty. Editing members live in <c>MainViewModel.Editing.cs</c>.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private static readonly string[] TimeFormats = [@"h\:mm", @"hh\:mm", @"h\:mm\:ss", @"hh\:mm\:ss"];

    private readonly IDialogService _dialogs;
    private readonly UndoStack _undo = new();
    private Study _study;
    private string? _filePath;
    private DateTime? _createdUtc;
    private LevelViewModel? _activeLevel;
    private ViewMode _viewMode = ViewMode.Orbit;
    private string _ticker = "WELCOME TO THRONG ▸ DEMO LOADS THE INTERCHANGE STATION ▸ R, P, C, W DRAW ▸ T PLACES STAIRS AND ESCALATORS, O DRAWS SHAFTS";
    private bool _rebuildingLevels;

    /// <summary>Creates the view model with a blank study.</summary>
    public MainViewModel(IDialogService dialogs)
    {
        _dialogs = dialogs;
        _study = Study.CreateDefault();
        Library = new LibraryBrowserViewModel(BuiltInLibrary.Instance);
        InitialiseEditing();
        InitialiseRun();
        InitialiseLevels();

        NewCommand = new RelayCommand(New);
        NewFromDemoCommand = new RelayCommand(NewFromDemo);
        OpenCommand = new RelayCommand(Open);
        SaveCommand = new RelayCommand(() => Save());
        SaveAsCommand = new RelayCommand(() => SaveAs());
        UndoCommand = new RelayCommand(Undo, () => _undo.CanUndo && !_isRunActive);
        RedoCommand = new RelayCommand(Redo, () => _undo.CanRedo && !_isRunActive);
        PlanViewCommand = new RelayCommand(() => ViewMode = ViewMode.Plan);
        OrbitViewCommand = new RelayCommand(() => ViewMode = ViewMode.Orbit);
        ZoomExtentsCommand = new RelayCommand(() => ZoomExtentsRequested?.Invoke(this, EventArgs.Empty));
        ValidateCommand = new RelayCommand(Validate);
        AboutCommand = new RelayCommand(ShowAbout);

        _undo.Changed += (_, _) => OnUndoChanged();
        RefreshLevels();
    }

    /// <summary>Raised when the viewport should rebuild its scene.</summary>
    public event EventHandler? SceneInvalidated;

    /// <summary>Raised when the viewport should frame everything.</summary>
    public event EventHandler? ZoomExtentsRequested;

    /// <summary>The open study.</summary>
    public Study Study => _study;

    /// <summary>Levels, highest first.</summary>
    public ObservableCollection<LevelViewModel> Levels { get; } = [];

    /// <summary>Library browser: category tabs plus search (D-021, D-031).</summary>
    public LibraryBrowserViewModel Library { get; }

    public RelayCommand NewCommand { get; }
    public RelayCommand NewFromDemoCommand { get; }
    public RelayCommand OpenCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand SaveAsCommand { get; }
    public RelayCommand UndoCommand { get; }
    public RelayCommand RedoCommand { get; }
    public RelayCommand PlanViewCommand { get; }
    public RelayCommand OrbitViewCommand { get; }
    public RelayCommand ZoomExtentsCommand { get; }
    public RelayCommand ValidateCommand { get; }
    public RelayCommand AboutCommand { get; }

    /// <summary>Level being edited / shown in plan.</summary>
    public LevelViewModel? ActiveLevel
    {
        get => _activeLevel;
        set
        {
            // The list clears its selection while it is rebuilt; ignore that transient null.
            if (_rebuildingLevels && value is null)
            {
                return;
            }

            if (SetProperty(ref _activeLevel, value))
            {
                Editor.Reset();
                Editor.ResetView(quiet: true);
                RefreshEditing();
                OnPropertyChanged(nameof(HudLevelText));
                SceneInvalidated?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>Plan or orbit.</summary>
    public ViewMode ViewMode
    {
        get => _viewMode;
        set
        {
            if (SetProperty(ref _viewMode, value))
            {
                // Editing is plan-only (D-026): leaving plan drops any shape in progress and returns to select.
                if (value != ViewMode.Plan)
                {
                    Editor.Reset();
                    Editor.Tool = EditorTool.Select;
                }

                OnPropertyChanged(nameof(IsPlanView));
                OnPropertyChanged(nameof(IsOrbitView));
                OnPropertyChanged(nameof(HudModeText));
                Ticker = value == ViewMode.Plan ? "Plan view: editing the active level." : "3D view: all visible levels at true elevation.";
            }
        }
    }

    /// <summary>True in plan view (toolbar highlight).</summary>
    public bool IsPlanView => _viewMode == ViewMode.Plan;

    /// <summary>True in 3D view (toolbar highlight).</summary>
    public bool IsOrbitView => _viewMode == ViewMode.Orbit;

    /// <summary>Viewport HUD: view mode.</summary>
    public string HudModeText => _viewMode == ViewMode.Plan ? "PLAN" : "3D ORBIT";

    /// <summary>Viewport HUD: active level.</summary>
    public string HudLevelText => _activeLevel is null ? "No level" : $"{_activeLevel.Name.ToUpperInvariant()}  {_activeLevel.ElevationText}";

    /// <summary>Study name (undoable).</summary>
    public string StudyName
    {
        get => _study.Name;
        set
        {
            string trimmed = (value ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                Ticker = "A study needs a name.";
                OnPropertyChanged();
                return;
            }

            Apply("Rename study", () => _study.Name, v => _study.Name = v, trimmed);
        }
    }

    /// <summary>Start time of day as HH:MM (undoable).</summary>
    public string StartTimeText
    {
        get => _study.Settings.StartTimeOfDay.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
        set
        {
            if (TimeSpan.TryParseExact((value ?? string.Empty).Trim(), TimeFormats, CultureInfo.InvariantCulture, out TimeSpan time)
                && time >= TimeSpan.Zero && time < TimeSpan.FromDays(1))
            {
                Apply("Change start time", () => _study.Settings.StartTimeOfDay, v => _study.Settings.StartTimeOfDay = v, time);
            }
            else
            {
                Ticker = "Start time must be 24-hour HH:MM, e.g. 07:30.";
                OnPropertyChanged();
            }
        }
    }

    /// <summary>RNG seed (undoable).</summary>
    public int Seed
    {
        get => _study.Settings.Seed;
        set => Apply("Change seed", () => _study.Settings.Seed, v => _study.Settings.Seed = v, value);
    }

    /// <summary>Study duration in minutes (undoable).</summary>
    public double DurationMinutes
    {
        get => Math.Round(_study.Settings.DurationS / 60.0, 2);
        set
        {
            if (value <= 0)
            {
                Ticker = "Duration must be greater than zero.";
                OnPropertyChanged();
                return;
            }

            Apply("Change duration", () => _study.Settings.DurationS, v => _study.Settings.DurationS = v, Math.Round(value * 60.0, 1));
        }
    }

    /// <summary>Study clock: the run's time of day while running (D-104), otherwise the start time.</summary>
    public string ClockText => RunClockText();

    /// <summary>Counts for the status bar.</summary>
    public string SummaryText =>
        $"{_study.Levels.Count} levels · {_study.Regions.Count} regions · {_study.Connectors.Count} connectors · {_study.Shafts.Count} shafts · {_study.Devices.Count} devices · {_study.Underlays.Count} underlays · {_study.Library.Count} templates in study";

    /// <summary>File path or "not saved".</summary>
    public string FileText => _filePath ?? "Not saved yet";

    /// <summary>Window title with dirty marker.</summary>
    public string WindowTitle => $"{_study.Name}{(_undo.IsDirty ? " •" : string.Empty)} — Throng";

    /// <summary>News-ticker style status message.</summary>
    public string Ticker
    {
        get => _ticker;
        set => SetProperty(ref _ticker, value);
    }

    /// <summary>Undo menu header.</summary>
    public string UndoHeader => _undo.UndoDescription is { } d ? $"_Undo {d}" : "_Undo";

    /// <summary>Redo menu header.</summary>
    public string RedoHeader => _undo.RedoDescription is { } d ? $"_Redo {d}" : "_Redo";

    /// <summary>
    /// Applies an undoable property change to the model. No-op edits are skipped so they don't pollute history.
    /// </summary>
    public void Apply<T>(string description, Func<T> getter, Action<T> setter, T value)
    {
        var action = new PropertyChangeAction<T>(description, getter, setter, value, OnModelChanged);
        if (action.IsNoOp)
        {
            return;
        }

        _undo.Execute(action);
        Ticker = description;
    }

    /// <summary>Asks to save unsaved changes. Returns false if the user cancelled.</summary>
    public bool ConfirmDiscardChanges()
    {
        if (!_undo.IsDirty)
        {
            return true;
        }

        bool? answer = _dialogs.AskSaveChanges(_study.Name);
        return answer switch
        {
            true => Save(),
            false => true,
            _ => false,
        };
    }

    /// <summary>Opens a study from a path (used by Open and by command-line startup).</summary>
    public void OpenPath(string path)
    {
        var result = ThrongFile.Load(path);
        if (!result.IsSuccess || result.Value is null)
        {
            AppLog.Warn(result.Error ?? "Open failed.");
            _dialogs.ShowError(result.Error ?? "The study couldn't be opened.");
            return;
        }

        var manifest = ThrongFile.ReadManifest(path);
        LoadStudy(result.Value, path, manifest.IsSuccess ? manifest.Value?.CreatedUtc : null, $"Opened {Path.GetFileName(path)}");
    }

    private void New()
    {
        if (ConfirmDiscardChanges())
        {
            LoadStudy(Study.CreateDefault(), null, null, "New study ▸ press F2 for plan view, then R, P, C or W to draw.");
        }
    }

    private void NewFromDemo()
    {
        if (!ConfirmDiscardChanges())
        {
            return;
        }

        Study demo = DemoStudyBuilder.Build();
        IReadOnlyList<ValidationMessage> findings = StudyValidator.Validate(demo);
        int errors = findings.Count(f => f.Severity == ValidationSeverity.Error);
        int warnings = findings.Count(f => f.Severity == ValidationSeverity.Warning);
        LoadStudy(demo, null, null,
            $"DEMO LOADED ▸ {demo.Levels.Count} levels, {demo.Regions.Count} regions, {demo.Connectors.Count} connectors, {demo.Shafts.Count} shafts, {demo.Devices.Count} devices, {demo.Library.Count} templates ▸ validation: {errors} errors, {warnings} warnings");
    }

    private void Open()
    {
        if (!ConfirmDiscardChanges())
        {
            return;
        }

        string? path = _dialogs.PickOpenPath();
        if (path is not null)
        {
            OpenPath(path);
        }
    }

    private bool Save() => _filePath is null ? SaveAs() : SaveTo(_filePath);

    private bool SaveAs()
    {
        string? path = _dialogs.PickSavePath(SuggestFileName(_study.Name));
        return path is not null && SaveTo(path);
    }

    private bool SaveTo(string path)
    {
        var result = ThrongFile.Save(_study, path, _createdUtc);
        if (!result.IsSuccess)
        {
            AppLog.Warn(result.Error ?? "Save failed.");
            _dialogs.ShowError(result.Error ?? "The study couldn't be saved.");
            return false;
        }

        _filePath = path;
        _createdUtc ??= DateTime.UtcNow;
        _undo.MarkSaved();
        OnPropertyChanged(nameof(FileText));
        Ticker = $"Saved {Path.GetFileName(path)}";
        AppLog.Info($"Saved {path}");
        return true;
    }

    private void Undo()
    {
        if (_isRunActive)
        {
            Ticker = "RUNNING ▸ RESET TO EDIT";
            return;
        }

        string? description = _undo.UndoDescription;
        if (_undo.Undo())
        {
            Ticker = $"Undid: {description}";
        }
    }

    private void Redo()
    {
        if (_isRunActive)
        {
            Ticker = "RUNNING ▸ RESET TO EDIT";
            return;
        }

        string? description = _undo.RedoDescription;
        if (_undo.Redo())
        {
            Ticker = $"Redid: {description}";
        }
    }

    private void Validate()
    {
        // The static rule set; PLAY adds the reachability checks (D-106).
        IReadOnlyList<ValidationMessage> findings = StudyValidator.Validate(_study);
        if (findings.Count == 0)
        {
            Ticker = "VALIDATION ▸ no problems found.";
            return;
        }

        string lines = string.Join(Environment.NewLine, findings.Select(f => $"[{f.Severity}] {f.Message}"));
        _dialogs.ShowInfo("Validation", lines);
        Ticker = $"VALIDATION ▸ {findings.Count} findings.";
    }

    private void ShowAbout()
    {
        string version = typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
        _dialogs.ShowInfo(
            "About Throng",
            $"Throng {version}\nConceptual pedestrian simulation for architects.\n\n" +
            "Results are indicative only and are not compliance evidence.\n\n" +
            "Copyright (c) 2026 Gavin Nicholls. MIT licence.\nThird-party components: see THIRD-PARTY-NOTICES.md next to Throng.exe.");
    }

    private void LoadStudy(Study study, string? path, DateTime? createdUtc, string message)
    {
        // A new study ends any run on the old one (D-097).
        ResetRun();
        _study = study;
        _filePath = path;
        _createdUtc = createdUtc;
        _activeLevel = null;
        _rebuildingLevels = true;
        Levels.Clear();
        _rebuildingLevels = false;
        _undo.Clear();
        Editor.Reset();
        Editor.ResetView(quiet: true);

        RefreshLevels();
        RefreshEditing();
        OnPropertyChanged(string.Empty);
        Ticker = message;
        AppLog.Info(message);
        SceneInvalidated?.Invoke(this, EventArgs.Empty);
        ZoomExtentsRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Called after every do/undo/redo of a model change.</summary>
    private void OnModelChanged()
    {
        RefreshLevels();
        RefreshEditing();
        OnPropertyChanged(nameof(StudyName));
        OnPropertyChanged(nameof(StartTimeText));
        OnPropertyChanged(nameof(Seed));
        OnPropertyChanged(nameof(DurationMinutes));
        OnPropertyChanged(nameof(EndCondition));
        OnPropertyChanged(nameof(HasDuration));
        OnPropertyChanged(nameof(KeepSide));
        OnPropertyChanged(nameof(ClockText));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(HudLevelText));
        SceneInvalidated?.Invoke(this, EventArgs.Empty);
    }

    private void OnUndoChanged()
    {
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(UndoHeader));
        OnPropertyChanged(nameof(RedoHeader));
    }

    /// <summary>
    /// Syncs the level list with the model (highest first). Existing rows are refreshed in place when the order
    /// is unchanged; otherwise the list is rebuilt and the active level is restored by id.
    /// </summary>
    private void RefreshLevels()
    {
        RefreshLevelOptions();
        IReadOnlyList<Level> ordered = _study.LevelsByElevation();
        Guid? activeId = _activeLevel?.Id;

        if (ordered.Select(l => l.Id).SequenceEqual(Levels.Select(v => v.Id)))
        {
            foreach (LevelViewModel row in Levels)
            {
                row.Refresh();
            }
        }
        else
        {
            _rebuildingLevels = true;
            Levels.Clear();
            foreach (Level level in ordered)
            {
                Levels.Add(new LevelViewModel(level, this));
            }

            _rebuildingLevels = false;
        }

        LevelViewModel? target = Levels.FirstOrDefault(v => v.Id == activeId)
            ?? Levels.FirstOrDefault(v => Math.Abs(v.Model.ElevationM) < 1e-9)
            ?? Levels.FirstOrDefault();

        if (!ReferenceEquals(target, _activeLevel))
        {
            ActiveLevel = target;
        }
        else
        {
            // Same row object, but force the list to re-select it after a rebuild.
            OnPropertyChanged(nameof(ActiveLevel));
        }
    }

    private static string SuggestFileName(string studyName)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string safe = new string(studyName.Select(c => invalid.Contains(c) ? '-' : c).ToArray());
        return (string.IsNullOrWhiteSpace(safe) ? "Study" : safe.Trim()) + ThrongFile.Extension;
    }
}
