using Throng.Core.Editing;
using Throng.Core.Library;
using Throng.Core.Model;

namespace Throng.App.ViewModels;

/// <summary>
/// Journey editing (round 009b, D-161, D-162): selecting a journey in LIBRARY ▸ JOURNEYS shows it in SELECTION,
/// DUPLICATE makes an editable copy in the study library, and the study's own journeys are listed in the JOURNEYS
/// tab. Selecting something in the viewport clears the journey selection and the other way round.
/// </summary>
public sealed partial class MainViewModel
{
    private JourneyViewModel? _selectedJourney;

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
            SelectLibraryProfile(profile);
        }
    }

    /// <summary>Number of mix lines (on every spawn zone's rules) that use a journey.</summary>
    public int JourneyUseCount(Guid journeyId)
    {
        int uses = 0;
        foreach (Region region in _study.Regions)
        {
            foreach (EmissionRule rule in region.EmissionRules)
            {
                foreach (MixLine line in rule.Mix ?? [])
                {
                    if (line?.JourneyId == journeyId)
                    {
                        uses++;
                    }
                }
            }
        }

        return uses;
    }

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
        Ticker = $"{copy.Name} ▸ edit its steps here, then pick it on a spawn zone's mix line.";
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

        ClearJourneySelection();
        Execute(new RemoveItemsAction<JourneyTemplate>($"Delete journey {journey.Name}", _study.Library.Journeys, [journey], OnModelChanged));
    }

    private void SelectJourney(JourneyTemplate journey)
    {
        // One thing in SELECTION at a time: the viewport selection goes.
        Editor.ClearSelection();
        if (!ReferenceEquals(_selectedJourney?.Journey, journey))
        {
            _selectedJourney = new JourneyViewModel(journey, this);
            OnPropertyChanged(nameof(SelectedJourney));
            OnPropertyChanged(nameof(HasJourneySelection));
        }

        Library.MarkSelected(journey.Id);
        PropertiesTabIndex = PropertiesTabSelection;
        OnPropertyChanged(nameof(SelectionText));
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
    }

    /// <summary>
    /// After a model change: refreshes the JOURNEYS tab from the study, and the journey panel (dropping it when its
    /// journey left the study, e.g. an undone DUPLICATE).
    /// </summary>
    private void RefreshJourneys()
    {
        // Imported profiles (D-275) show with the built-ins.
        Library.SetProfiles(StudyTemplates.AvailableProfiles(_study));
        if (_libraryProfile is { } picked && !StudyTemplates.AvailableProfiles(_study).Contains(picked))
        {
            _libraryProfile = null;
        }

        IReadOnlyList<JourneyTemplate> available = StudyTemplates.AvailableJourneys(_study);
        Library.SetJourneys([.. available.Where(j => StudyTemplates.IsEditableJourney(_study, j)), .. available.Where(j => !StudyTemplates.IsEditableJourney(_study, j))]);
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
