using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Throng.Analysis.Checks;
using Throng.App.Services;
using Throng.Core.Common;
using Throng.Core.Geometry;
using Throng.Core.Model;
using Throng.Core.Validation;
using Throng.Render.Gl;
using Throng.Sim;

namespace Throng.App.ViewModels;

/// <summary>
/// The CHECK tab (round 016, D-271, D-272): CHECK (F7) runs every rule (<see cref="StudyCheck"/>) on a worker thread
/// and lists the findings, errors first, with filter chips; clicking a row switches to plan view on the object's level,
/// selects it and zooms to it; IGNORE keeps a finding out of the counts for this study (undoable, saved in v10). PLAY's
/// blocking errors land here too. The tab sits outside the edit lock, so it can be read while running.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>Index of the CHECK tab.</summary>
    public const int PropertiesTabCheck = 4;

    /// <summary>Half the size of the area shown round a finding about a place, metres.</summary>
    private const double PlaceZoomM = 6;

    private readonly List<CheckRowViewModel> _allCheckRows = [];
    private ValidationSeverity? _checkFilter;
    private bool _showIgnored;
    private bool _isChecking;
    private bool _hasChecked;
    private bool _checkStale;
    private bool _togglingIgnore;
    private string _checkSummary = "Press CHECK (F7) to run every rule over the study.";
    private RelayCommand? _runCheckCommand;

    /// <summary>Raised with the plan bounds the viewport should frame (plan view) after a CHECK row is clicked.</summary>
    public event EventHandler<Bounds2>? ZoomToRequested;

    /// <summary>True while the CHECK tab is showing.</summary>
    public bool IsCheckTab => _propertiesTabIndex == PropertiesTabCheck;

    /// <summary>The rows shown, after the filter chips.</summary>
    public ObservableCollection<CheckRowViewModel> CheckRows { get; } = [];

    /// <summary>Counts line above the list.</summary>
    public string CheckSummary
    {
        get => _checkSummary;
        private set => SetProperty(ref _checkSummary, value);
    }

    /// <summary>
    /// True when the study changed after the findings shown were made (round 017, D-295): CHECK stays on demand, and
    /// the tab says so until it runs again.
    /// </summary>
    public bool IsCheckStale => _checkStale;

    /// <summary>CHECK ▸ ALL chip.</summary>
    public bool IsCheckAll { get => _checkFilter is null; set => SetCheckFilter(null); }

    /// <summary>CHECK ▸ ERRORS chip.</summary>
    public bool IsCheckErrors { get => _checkFilter == ValidationSeverity.Error; set => SetCheckFilter(ValidationSeverity.Error); }

    /// <summary>CHECK ▸ WARNINGS chip.</summary>
    public bool IsCheckWarnings { get => _checkFilter == ValidationSeverity.Warning; set => SetCheckFilter(ValidationSeverity.Warning); }

    /// <summary>CHECK ▸ INFO chip.</summary>
    public bool IsCheckInfo { get => _checkFilter == ValidationSeverity.Info; set => SetCheckFilter(ValidationSeverity.Info); }

    /// <summary>Show ignored findings (greyed).</summary>
    public bool ShowIgnoredFindings
    {
        get => _showIgnored;
        set
        {
            if (SetProperty(ref _showIgnored, value))
            {
                RefreshCheckRows();
            }
        }
    }

    /// <summary>RUN CHECK on the CHECK tab (same as the toolbar CHECK).</summary>
    public RelayCommand RunCheckCommand => _runCheckCommand ??= new RelayCommand(() => _ = RunCheckAsync());

    /// <summary>Runs every rule on a worker thread, then shows the CHECK tab.</summary>
    private async Task RunCheckAsync()
    {
        if (_isChecking)
        {
            return;
        }

        _isChecking = true;
        Ticker = "CHECKING ▸ rules, routes and capacity…";
        Study study = _study;
        IReadOnlyList<ValidationMessage>? findings = null;
        SimModel? model = null;
        try
        {
            (findings, model) = await Task.Run(() => StudyCheck.RunWithModel(study));
        }
        catch (Exception ex)
        {
            // Boundary: a bug in a rule must not take the app down.
            AppLog.Error("CHECK failed", ex);
            _dialogs.ShowError($"CHECK couldn't finish: {ex.Message}");
        }
        finally
        {
            _isChecking = false;
        }

        if (findings is not null && ReferenceEquals(study, _study))
        {
            // CHECK's compiled model gives the JOURNEY overlay's predicted shares (D-334), unless a run has its own.
            if (model is not null && !_isRunActive)
            {
                SetPredictionModel(model, "CHECK");
            }

            ShowCheck(findings, null);
        }
    }

    /// <summary>Fills the CHECK tab with <paramref name="findings"/> and brings it forward.</summary>
    /// <param name="findings">Findings, any order.</param>
    /// <param name="ticker">Ticker text, or null for a summary.</param>
    private void ShowCheck(IReadOnlyList<ValidationMessage> findings, string? ticker)
    {
        _allCheckRows.Clear();
        foreach (ValidationMessage finding in findings.OrderByDescending(f => f.Severity))
        {
            _allCheckRows.Add(new CheckRowViewModel(finding, StudyCheck.IsIgnored(_study, finding), GoToFinding, ToggleIgnore));
        }

        _hasChecked = true;
        SetCheckStale(false);
        RefreshCheckRows();
        PropertiesTabIndex = PropertiesTabCheck;
        Ticker = ticker ?? (Count(ValidationSeverity.Error) + Count(ValidationSeverity.Warning) == 0
            ? "CHECK ▸ no problems found."
            : string.Create(CultureInfo.InvariantCulture, $"CHECK ▸ {Count(ValidationSeverity.Error)} errors, {Count(ValidationSeverity.Warning)} warnings, {Count(ValidationSeverity.Info)} notes ▸ click a row to go there."));
    }

    /// <summary>Findings of a severity that aren't ignored.</summary>
    private int Count(ValidationSeverity severity) => _allCheckRows.Count(r => r.Severity == severity && !r.IsIgnored);

    private void SetCheckFilter(ValidationSeverity? filter)
    {
        _checkFilter = filter;
        OnPropertyChanged(nameof(IsCheckAll));
        OnPropertyChanged(nameof(IsCheckErrors));
        OnPropertyChanged(nameof(IsCheckWarnings));
        OnPropertyChanged(nameof(IsCheckInfo));
        RefreshCheckRows();
    }

    /// <summary>Rebuilds the visible rows from the filter and the ignore toggle, and the summary.</summary>
    private void RefreshCheckRows()
    {
        CheckRows.Clear();
        foreach (CheckRowViewModel row in _allCheckRows)
        {
            if ((_checkFilter is null || row.Severity == _checkFilter) && (_showIgnored || !row.IsIgnored))
            {
                CheckRows.Add(row);
            }
        }

        int ignored = _allCheckRows.Count(r => r.IsIgnored);
        CheckSummary = !_hasChecked
            ? "Press CHECK (F7) to run every rule over the study."
            : string.Create(CultureInfo.InvariantCulture,
                $"{Count(ValidationSeverity.Error)} errors · {Count(ValidationSeverity.Warning)} warnings · {Count(ValidationSeverity.Info)} notes{(ignored > 0 ? $" · {ignored} ignored" : string.Empty)}. Click a row to go to it; CHECK again after edits.");
    }

    /// <summary>After undo/redo or a new study: the rows' ignored flags follow the study's list.</summary>
    private void SyncCheckIgnored()
    {
        foreach (CheckRowViewModel row in _allCheckRows)
        {
            row.IsIgnored = StudyCheck.IsIgnored(_study, row.Finding);
        }

        RefreshCheckRows();
    }

    /// <summary>A new study: its findings aren't the old study's.</summary>
    private void ClearCheck()
    {
        _allCheckRows.Clear();
        _hasChecked = false;
        SetCheckStale(false);
        RefreshCheckRows();
    }

    /// <summary>After a model change: findings already shown are out of date (ignoring one isn't a change to the study's design).</summary>
    private void MarkCheckStale()
    {
        if (_hasChecked && !_togglingIgnore && !_checkStale)
        {
            SetCheckStale(true);
        }
    }

    private void SetCheckStale(bool stale)
    {
        if (_checkStale != stale)
        {
            _checkStale = stale;
            OnPropertyChanged(nameof(IsCheckStale));
        }
    }

    private void ToggleIgnore(CheckRowViewModel row)
    {
        if (_isRunActive)
        {
            Ticker = "RUNNING ▸ RESET TO EDIT";
            return;
        }

        string key = row.Finding.IgnoreKey;
        bool ignore = !_study.IgnoredFindings.Contains(key);
        List<string> next = ignore ? [.. _study.IgnoredFindings, key] : [.. _study.IgnoredFindings.Where(k => k != key)];
        _togglingIgnore = true;
        try
        {
            Apply(ignore ? "Ignore CHECK finding" : "Unignore CHECK finding", () => _study.IgnoredFindings, v => _study.IgnoredFindings = v, next);
        }
        finally
        {
            _togglingIgnore = false;
        }
    }

    /// <summary>Plan view on the finding's level, the object selected (when editing is allowed), and the view framed on it.</summary>
    private void GoToFinding(CheckRowViewModel row)
    {
        ValidationMessage finding = row.Finding;
        (Guid? levelId, Bounds2 bounds) = Locate(finding);
        if (levelId is null || bounds.IsEmpty)
        {
            Ticker = "CHECK ▸ that finding isn't about a place in the plan.";
            return;
        }

        ViewMode = ViewMode.Plan;
        if (Levels.FirstOrDefault(l => l.Id == levelId) is { } level && !ReferenceEquals(level, ActiveLevel))
        {
            ActiveLevel = level;
        }

        if (finding.ObjectId is { } id && !_isRunActive && id != levelId)
        {
            Editor.Select(id);
        }

        // Selecting brings SELECTION forward; stay on CHECK so the next row is a click away.
        PropertiesTabIndex = PropertiesTabCheck;
        ZoomToRequested?.Invoke(this, bounds);
        Ticker = "CHECK ▸ " + finding.Message;
    }

    /// <summary>
    /// 3D right-click ▸ VIEW IN PLAN (round 017b, D-299): plan view on the object's level, the object selected (when
    /// editing is allowed) and framed.
    /// </summary>
    public void ViewInPlan(Guid objectId)
    {
        (Guid? levelId, Bounds2 bounds) = Locate(new ValidationMessage(ValidationSeverity.Info, string.Empty, objectId));
        if (levelId is null || bounds.IsEmpty)
        {
            return;
        }

        ViewMode = ViewMode.Plan;
        if (Levels.FirstOrDefault(l => l.Id == levelId) is { } level && !ReferenceEquals(level, ActiveLevel))
        {
            ActiveLevel = level;
        }

        if (!_isRunActive)
        {
            Editor.Select(objectId);
        }

        ZoomToRequested?.Invoke(this, bounds);
    }

    /// <summary>The level and plan bounds a finding is about, or (null, empty).</summary>
    private (Guid? LevelId, Bounds2 Bounds) Locate(ValidationMessage finding)
    {
        if (finding.At is { } at)
        {
            return (finding.LevelId, new Bounds2(at.X - PlaceZoomM, at.Y - PlaceZoomM, at.X + PlaceZoomM, at.Y + PlaceZoomM));
        }

        if (finding.ObjectId is not { } id)
        {
            return (null, Bounds2.Empty);
        }

        if (_study.FindLevel(id) is { } level)
        {
            return (level.Id, _study.GetLevelBounds(level.Id));
        }

        if (_study.FindRegion(id) is { } region)
        {
            return (region.LevelId, Bounds2.FromPoints(region.Outline));
        }

        if (_study.FindDevice(id) is { } device)
        {
            return (device.LevelId, Bounds2.FromPoints(DeviceGeometry.Footprint(device)));
        }

        if (_study.FindConnector(id) is { } connector)
        {
            Result<ConnectorLayout> layout = ConnectorLayout.Compute(connector, _study);
            return (connector.BottomLevelId, layout.IsSuccess && layout.Value is { } l ? Bounds2.FromPoints(l.Footprint) : Bounds2.Empty);
        }

        if (_study.FindShaft(id) is { } shaft)
        {
            Guid levelId = ActiveLevel is { } active && _study.ShaftSpans(shaft, active.Id) ? active.Id : shaft.BaseLevelId;
            return (levelId, Bounds2.FromPoints(shaft.Outline));
        }

        if (_study.FindUnderlay(id) is { } underlay)
        {
            return (underlay.LevelId, Bounds2.FromPoints(UnderlayGeometry.Corners(underlay)));
        }

        if (_study.TrainServices.FirstOrDefault(t => t.Id == id) is { } train
            && train.DoorZoneIds.Select(_study.FindRegion).FirstOrDefault(r => r is not null) is { } door)
        {
            return (door.LevelId, Bounds2.FromPoints(door.Outline));
        }

        return (null, Bounds2.Empty);
    }
}
