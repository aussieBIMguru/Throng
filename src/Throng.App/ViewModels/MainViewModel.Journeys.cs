using CommunityToolkit.Mvvm.Input;
using Throng.Core.Editing;
using Throng.Core.Library;
using Throng.Core.Model;
using Throng.Render.Gl;

namespace Throng.App.ViewModels;

/// <summary>
/// Journeys (round 009b, D-161, D-162; graphs since round 018, D-302, D-308): selecting a journey in LIBRARY ▸
/// JOURNEYS shows it in SELECTION and its graph in the JOURNEY view, which takes over the viewport until PLAN, 3D,
/// ISO or Esc; DUPLICATE makes an editable copy in the study library. Selecting something in the viewport clears the
/// journey selection and the other way round.
/// </summary>
public sealed partial class MainViewModel
{
    private JourneyViewModel? _selectedJourney;
    private bool _isJourneyView;
    private RelayCommand? _journeyViewCommand;
    private RelayCommand? _closeJourneyViewCommand;

    /// <summary>True while the JOURNEY view covers the viewport (D-308).</summary>
    public bool IsJourneyView
    {
        get => _isJourneyView;
        private set
        {
            if (SetProperty(ref _isJourneyView, value))
            {
                OnPropertyChanged(nameof(IsPlanButtonLit));
                OnPropertyChanged(nameof(IsOrbitButtonLit));
                OnPropertyChanged(nameof(IsIsoButtonLit));
                if (value)
                {
                    Ticker = _selectedJourney is { } j
                        ? $"JOURNEY ▸ {j.Journey.Name}: click a node to see it in SELECTION ▸ Esc, PLAN, 3D or ISO to go back."
                        : "JOURNEY ▸ pick a journey in LIBRARY ▸ JOURNEYS.";
                }
            }
        }
    }

    /// <summary>Toolbar highlight: PLAN (not while the JOURNEY view covers it).</summary>
    public bool IsPlanButtonLit => IsPlanView && !_isJourneyView;

    /// <summary>Toolbar highlight: 3D.</summary>
    public bool IsOrbitButtonLit => IsOrbitView && !_isJourneyView;

    /// <summary>Toolbar highlight: ISO.</summary>
    public bool IsIsoButtonLit => IsIsoView && !_isJourneyView;

    /// <summary>Toolbar JOURNEY (F5): shows the selected journey's graph.</summary>
    public RelayCommand JourneyViewCommand => _journeyViewCommand ??= new RelayCommand(ShowJourneyView);

    /// <summary>Esc on the canvas: back to the view underneath.</summary>
    public RelayCommand CloseJourneyViewCommand => _closeJourneyViewCommand ??= new RelayCommand(CloseJourneyView);

    /// <summary>Opens the JOURNEY view (D-308).</summary>
    public void ShowJourneyView() => IsJourneyView = true;

    /// <summary>Back to the plan / 3D / ISO view underneath.</summary>
    public void CloseJourneyView() => IsJourneyView = false;

    /// <summary>The journey shown in SELECTION, or null.</summary>
    public JourneyViewModel? SelectedJourney => _selectedJourney;

    /// <summary>True when a journey is shown in SELECTION.</summary>
    public bool HasJourneySelection => _selectedJourney is not null;

    /// <summary>A click on a LIBRARY row: journeys open in SELECTION (D-162); other templates are left alone.</summary>
    public void SelectLibraryItem(TemplateItemViewModel? item)
    {
        if (item?.Template is JourneyTemplate journey)
        {
            SelectJourney(journey);
        }
        else if (item?.Template is AgentProfileTemplate profile)
        {
            // Opens the profile's definition (round 017) and picks it for EXPORT (D-275).
            SelectDefinition(profile);
            SelectLibraryProfile(profile);
        }
        else if (item?.Template is DeviceTemplate device)
        {
            SelectDefinition(device);
            Ticker = $"{device.Name} ▸ edit its definition in SELECTION; double-click or drag to place it.";
        }
    }

    /// <summary>Number of mix lines (on every spawn zone's rules) that follow a journey, directly or as their profile's default (D-304).</summary>
    public int JourneyUseCount(Guid journeyId)
    {
        int uses = 0;
        foreach (Region region in _study.Regions)
        {
            foreach (EmissionRule rule in region.EmissionRules)
            {
                foreach (MixLine line in rule.Mix ?? [])
                {
                    if (line is not null && StudyTemplates.JourneyIdFor(_study, line) == journeyId)
                    {
                        uses++;
                    }
                }
            }
        }

        return uses;
    }

    /// <summary>
    /// One undoable edit of a journey (D-325–D-328): <paramref name="change"/> edits a copy and says whether to keep
    /// it; nothing is recorded when it refuses or changes nothing. A built-in's first edit embeds the study's copy
    /// under the built-in's id (outside the undo stack, as for devices and profiles, D-297), so undoing leaves a
    /// pristine copy.
    /// </summary>
    public bool EditJourney(Guid id, string description, Func<JourneyTemplate, bool> change)
    {
        if (!CheckDefinitionEditable() || StudyTemplates.FindJourney(_study, id, Mine) is not { } current)
        {
            return false;
        }

        JourneyGraph.Ensure(current);
        var next = (JourneyTemplate)TemplateCloner.Clone(current);
        if (!change(next) || StudyDefinitions.SameValues(current, next))
        {
            _selectedJourney?.Refresh();
            return false;
        }

        _study.Library.Journeys ??= [];
        JourneyTemplate target = StudyDefinitions.EnsureStudyCopy(_study, current);
        Execute(new CompositeAction(description, [DefinitionState(description, target, next)], OnModelChanged));
        Ticker = description;
        return true;
    }

    /// <summary>RESET TO BUILT-IN (D-328): the study's copy of a built-in journey takes the built-in's values again.</summary>
    public void ResetJourney(Guid id)
    {
        if (BuiltInLibrary.Instance.Find(id) is not JourneyTemplate builtIn || StudyTemplates.FindJourney(_study, id) is not { } current
            || !StudyDefinitions.IsModified(_study, current))
        {
            return;
        }

        EditJourney(id, $"Reset {builtIn.Name} to built-in", j =>
        {
            StudyDefinitions.CopyValues(j, builtIn);
            return true;
        });
    }

    // ------------------------------------------------------------------ PICK IN PLAN (D-324)

    private (Guid Journey, Guid Node)? _journeyPick;
    private bool _handlingPick;
    private RelayCommand? _finishJourneyPickCommand;

    /// <summary>True while clicks in plan add to (or remove from) a journey node's pick list.</summary>
    public bool IsJourneyPicking => _journeyPick is not null;

    /// <summary>Enter / DONE: back to the graph.</summary>
    public RelayCommand FinishJourneyPickCommand => _finishJourneyPickCommand ??= new RelayCommand(FinishJourneyPick, () => _journeyPick is not null);

    /// <summary>Starts PICK IN PLAN for a node: plan view, and each device or zone clicked toggles in its pick list.</summary>
    public void StartJourneyPick(Guid journeyId, Guid nodeId)
    {
        if (!CheckDefinitionEditable())
        {
            return;
        }

        string what = StudyTemplates.FindJourney(_study, journeyId, Mine) is { } journey && JourneyGraph.Find(journey, nodeId) is { Kind: JourneyNodeKind.Device } ? "devices" : "zones";
        _journeyPick = (journeyId, nodeId);
        ViewMode = ViewMode.Plan;
        _handlingPick = true;
        Editor.ClearSelection();
        _handlingPick = false;
        OnPropertyChanged(nameof(IsJourneyPicking));
        FinishJourneyPickCommand.NotifyCanExecuteChanged();
        _selectedJourney?.Refresh();
        Ticker = $"PICK IN PLAN ▸ click {what} to add or remove them (any level) ▸ Enter or DONE in SELECTION goes back to the graph.";
    }

    /// <summary>Ends PICK IN PLAN and shows the graph again.</summary>
    public void FinishJourneyPick()
    {
        if (_journeyPick is null)
        {
            return;
        }

        _journeyPick = null;
        OnPropertyChanged(nameof(IsJourneyPicking));
        FinishJourneyPickCommand.NotifyCanExecuteChanged();
        _selectedJourney?.Refresh();
        if (_selectedJourney is not null)
        {
            IsJourneyView = true;
        }
    }

    /// <summary>
    /// While picking: whatever was just selected in plan toggles in the node's pick list (devices for a device node,
    /// zones for zone nodes, sink zones for a zone sink), then the plan selection clears. True when it handled the
    /// selection change (so the journey stays in SELECTION).
    /// </summary>
    private bool HandleJourneyPick()
    {
        if (_journeyPick is not { } pick)
        {
            return false;
        }

        if (_handlingPick || Editor.SelectionCount == 0)
        {
            return true;
        }

        if (StudyTemplates.FindJourney(_study, pick.Journey, Mine) is not { } journey || JourneyGraph.Find(journey, pick.Node) is not { } node)
        {
            _journeyPick = null;
            return false;
        }

        IEnumerable<Guid> ids = node.Kind == JourneyNodeKind.Device
            ? Editor.SelectedDevices.Select(d => d.Id)
            : Editor.SelectedRegions.Where(r => r.Kind == RegionKind.Zone && (node.Kind != JourneyNodeKind.Sink || r.IsSink)).Select(r => r.Id);
        JourneyNode next = node;
        int toggled = 0;
        foreach (Guid id in ids.ToList())
        {
            next = JourneyEdits.TogglePick(next, id);
            toggled++;
        }

        _handlingPick = true;
        Editor.ClearSelection();
        _handlingPick = false;
        if (toggled == 0)
        {
            Ticker = node.Kind == JourneyNodeKind.Device ? "PICK IN PLAN ▸ click devices for this node." : node.Kind == JourneyNodeKind.Sink ? "PICK IN PLAN ▸ click sink zones for this node." : "PICK IN PLAN ▸ click zones for this node.";
            return true;
        }

        int count = next.Picks?.Count ?? 0;
        EditJourney(pick.Journey, "Change picked " + (node.Kind == JourneyNodeKind.Device ? "devices" : "zones"), j => JourneyEdits.Replace(j, next));
        Ticker = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"PICK IN PLAN ▸ {count} picked ▸ Enter or DONE goes back to the graph.");
        return true;
    }

    /// <summary>
    /// Embeds a journey in the study library (outside the undo stack, as placing does) when it isn't there yet: yours
    /// (MINE, D-346) or the built-in.
    /// </summary>
    public void EmbedBuiltInJourney(Guid journeyId) => EmbedForUse(journeyId);

    /// <summary>Number of the study's profiles (and built-ins) with a journey as their default (D-304).</summary>
    public int JourneyDefaultCount(Guid journeyId) => StudyTemplates.AvailableProfiles(_study).Count(p => p.DefaultJourneyId == journeyId);

    /// <summary>Copies a journey into the study library as an editable journey (undoable) and selects the copy (D-161).</summary>
    public void DuplicateJourney(JourneyTemplate source)
    {
        if (_isRunActive)
        {
            Ticker = "RUNNING ▸ RESET TO EDIT";
            return;
        }

        _study.Library.Journeys ??= [];
        JourneyTemplate copy = StudyTemplates.CreateJourneyCopy(_study, source);
        Execute(new AddItemsAction<JourneyTemplate>($"Duplicate journey {source.Name}", _study.Library.Journeys, [copy], OnModelChanged));
        SelectJourney(copy);
        Ticker = $"{copy.Name} ▸ rename it here, then pick it on a spawn zone's mix line (graph editing arrives in 018b).";
    }

    /// <summary>Deletes one of the study's own journeys (undoable) when no mix line uses it.</summary>
    public void DeleteJourney(JourneyTemplate journey)
    {
        if (_isRunActive)
        {
            Ticker = "RUNNING ▸ RESET TO EDIT";
            return;
        }

        if (!StudyTemplates.IsEditableJourney(_study, journey))
        {
            Ticker = "Built-in journeys can't be deleted.";
            return;
        }

        int uses = JourneyUseCount(journey.Id);
        if (uses > 0)
        {
            Ticker = $"{journey.Name} is used by {uses} mix line{(uses == 1 ? string.Empty : "s")}: pick another journey there first.";
            return;
        }

        int defaults = JourneyDefaultCount(journey.Id);
        if (defaults > 0)
        {
            Ticker = $"{journey.Name} is the default journey of {defaults} profile{(defaults == 1 ? string.Empty : "s")}: change it in the profile's definition first.";
            return;
        }

        ClearJourneySelection();
        Execute(new RemoveItemsAction<JourneyTemplate>($"Delete journey {journey.Name}", _study.Library.Journeys, [journey], OnModelChanged));
    }

    private void SelectJourney(JourneyTemplate journey)
    {
        // One thing in SELECTION at a time: the viewport selection and any definition go.
        Editor.ClearSelection();
        ClearDefinitionSelection();
        if (_selectedJourney?.Id != journey.Id)
        {
            _selectedJourney = new JourneyViewModel(journey, this);
            OnPropertyChanged(nameof(SelectedJourney));
            OnPropertyChanged(nameof(HasJourneySelection));
        }

        Library.MarkSelected(journey.Id);
        PropertiesTabIndex = PropertiesTabSelection;
        OnPropertyChanged(nameof(SelectionText));

        // The graph takes over the viewport (D-308).
        IsJourneyView = true;
    }

    private void ClearJourneySelection()
    {
        if (_selectedJourney is null)
        {
            return;
        }

        _selectedJourney = null;
        Library.MarkSelected(null);
        OnPropertyChanged(nameof(SelectedJourney));
        OnPropertyChanged(nameof(HasJourneySelection));
        OnPropertyChanged(nameof(SelectionText));
        IsJourneyView = false;
    }

    /// <summary>
    /// After a model change: refreshes the JOURNEYS tab from the study, and the journey panel (dropping it when its
    /// journey left the study, e.g. an undone DUPLICATE).
    /// </summary>
    private void RefreshJourneys()
    {
        // The study's profiles, then yours (MINE), then built-ins (D-275, D-343).
        IReadOnlyList<AgentProfileTemplate> profiles = StudyTemplates.AvailableProfiles(_study, Mine);
        Library.SetProfiles(profiles);
        if (_libraryProfile is { } picked && !profiles.Contains(picked))
        {
            // The first edit of a built-in (or one of yours) swaps in the study's copy (D-287, D-346): follow it by id.
            _libraryProfile = StudyTemplates.FindProfile(_study, picked.Id, Mine);
        }

        RefreshDefinitions();

        // The study's own journeys, then yours not in the study (MINE), then the rest (D-343).
        IReadOnlyList<JourneyTemplate> available = StudyTemplates.AvailableJourneys(_study, Mine);
        Library.SetJourneys(
        [
            .. available.Where(j => StudyTemplates.IsEditableJourney(_study, j)),
            .. available.Where(j => !StudyTemplates.IsEditableJourney(_study, j) && IsMineOnly(j.Id)),
            .. available.Where(j => !StudyTemplates.IsEditableJourney(_study, j) && !IsMineOnly(j.Id)),
        ]);
        Library.RefreshNotes(LibraryNote);
        if (_selectedJourney is { } selected)
        {
            if (available.Contains(selected.Journey) || BuiltInLibrary.Instance.Journeys.Contains(selected.Journey))
            {
                selected.Refresh();
            }
            else
            {
                ClearJourneySelection();
            }
        }
    }
}
