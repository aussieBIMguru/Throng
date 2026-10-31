using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Throng.Core.Library;

namespace Throng.App.ViewModels;

/// <summary>
/// SELECTION ▸ AGENT PROFILE (round 017, D-286, D-287, D-289): a profile picked in LIBRARY ▸ AGENTS. Built-ins are
/// edited in place for this study (RESET TO BUILT-IN puts them back); the study's own profiles can be deleted when
/// no mix line uses them. Runs read the profile directly, so every edit applies to the next PLAY. Looked up by id on
/// every read (the first edit of a built-in swaps in the study's copy).
/// </summary>
public sealed class ProfileDefinitionViewModel : ObservableObject
{
    private static readonly AgentProfileTemplate Fallback = new() { Name = "(gone)" };
    private readonly MainViewModel _owner;

    /// <summary>Wraps the profile with this id.</summary>
    public ProfileDefinitionViewModel(Guid id, MainViewModel owner)
    {
        Id = id;
        _owner = owner;
        DuplicateCommand = new RelayCommand(() => _owner.DuplicateDefinition(Template));
        ResetCommand = new RelayCommand(() => _owner.ResetDefinition(Template));
        DeleteCommand = new RelayCommand(() => _owner.DeleteDefinition(Template));
        ExportCommand = new RelayCommand(() => _owner.ExportProfileDefinition(Template));
        SaveToMineCommand = new RelayCommand(() => _owner.SaveToMyLibrary(Template));
        UpdateFromMineCommand = new RelayCommand(() => _owner.UpdateFromMyLibrary(Id));
        AddPreferenceCommand = new RelayCommand(AddPreference);
        Speed = new DistributionEditorViewModel("Walking speed", "m/s", () => Template.Speed, d => Edit("Change walking speed", t => t.Speed = d));
        RebuildPreferences();
    }

    /// <summary>Id of the profile.</summary>
    public Guid Id { get; }

    /// <summary>The profile as the study sees it now (its copy, else yours, else the built-in).</summary>
    public AgentProfileTemplate Template => StudyTemplates.FindProfile(_owner.Study, Id, _owner.Mine) ?? Fallback;

    /// <summary>Makes an editable copy with a new name and key.</summary>
    public RelayCommand DuplicateCommand { get; }

    /// <summary>Puts a modified built-in back to the built-in's values.</summary>
    public RelayCommand ResetCommand { get; }

    /// <summary>Deletes one of the study's own profiles that no mix line uses.</summary>
    public RelayCommand DeleteCommand { get; }

    /// <summary>Writes the profile (and its journeys) to a .throngprofile file (D-275).</summary>
    public RelayCommand ExportCommand { get; }

    /// <summary>SAVE TO MY LIBRARY (round 020, D-342): copies this profile (and its default journey, D-350) into your library.</summary>
    public RelayCommand SaveToMineCommand { get; }

    /// <summary>UPDATE FROM MY LIBRARY (D-344): your library's version back onto the study's copy (one undo step).</summary>
    public RelayCommand UpdateFromMineCommand { get; }

    /// <summary>True when the study's copy differs from your library's (D-344, D-351).</summary>
    public bool DiffersFromMine => _owner.MineDiffers(Id);

    /// <summary>The line about your library (MINE, DIFFERS…); empty when it isn't in your library.</summary>
    public string MineText => _owner.MineStatusText(Id);

    /// <summary>True when <see cref="MineText"/> shows.</summary>
    public bool HasMineText => MineText.Length > 0;

    /// <summary>Adds a device preference row.</summary>
    public RelayCommand AddPreferenceCommand { get; }

    /// <summary>The walking speed distribution on the flat.</summary>
    public DistributionEditorViewModel Speed { get; }

    /// <summary>One row per device preference.</summary>
    public ObservableCollection<DevicePreferenceRowViewModel> Preferences { get; } = [];

    /// <summary>True for built-ins and their study copies.</summary>
    public bool IsBuiltIn => StudyDefinitions.IsBuiltIn(Template);

    /// <summary>True when the study's copy of a built-in differs from it.</summary>
    public bool IsModified => StudyDefinitions.IsModified(_owner.Study, Template);

    /// <summary>True for the study's own (duplicated, new or imported) profiles; not yours until the study uses them.</summary>
    public bool IsOwn => !IsBuiltIn && _owner.Study.Library.Find(Id) is not null;

    /// <summary>Where it came from and what editing it does.</summary>
    public string SourceText => _owner.IsMineOnly(Id)
        ? "From your library. Edits change it for this study only (SAVE TO MY LIBRARY puts them back in your library)."
        : IsBuiltIn
        ? IsModified
            ? "Built-in, modified in this study. RESET TO BUILT-IN puts the shipped values back. Edits can be undone."
            : "Built-in. Edits change it for this study only (DUPLICATE makes a separate profile instead)."
        : Template.Source == TemplateSource.Imported
            ? "Imported into this study. Edits can be undone."
            : "Your profile, saved in this study. Edits can be undone.";

    /// <summary>How many mix lines use it.</summary>
    public string UsageText
    {
        get
        {
            int uses = StudyDefinitions.ProfileUseCount(_owner.Study, Id);
            return uses switch
            {
                0 => "Not used by any mix line yet: pick it on a spawn zone's mix line in SELECTION.",
                1 => "Used by 1 mix line.",
                _ => string.Create(CultureInfo.InvariantCulture, $"Used by {uses} mix lines."),
            };
        }
    }

    /// <summary>Display name (blank rejected).</summary>
    public string Name
    {
        get => Template.Name;
        set
        {
            string trimmed = (value ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                _owner.Ticker = "A profile needs a name.";
                OnPropertyChanged(nameof(Name));
                return;
            }

            Edit("Rename profile", t => t.Name = trimmed);
        }
    }

    /// <summary>Description (the LIBRARY tooltip).</summary>
    public string Description
    {
        get => Template.Description;
        set => Edit("Change profile description", t => t.Description = (value ?? string.Empty).Trim());
    }

    /// <summary>Colour as #RRGGBB (PROFILE colouring).</summary>
    public string ColorHex
    {
        get => Template.ColorHex;
        set
        {
            string text = (value ?? string.Empty).Trim();
            if (!DeviceDefinitionViewModel.IsHexColour(text))
            {
                _owner.Ticker = "Colour must be #RRGGBB, e.g. #F472B6.";
                OnPropertyChanged(nameof(ColorHex));
                return;
            }

            Edit("Change profile colour", t => t.ColorHex = text.ToUpperInvariant());
        }
    }

    /// <summary>Body radius (0.1–0.8 m).</summary>
    public double RadiusM { get => Template.RadiusM; set => Edit("Change body radius", t => t.RadiusM = Clamp(value, 0.1, 0.8, t.RadiusM)); }

    /// <summary>Height (0.5–2.5 m).</summary>
    public double HeightM { get => Template.HeightM; set => Edit("Change height", t => t.HeightM = Clamp(value, 0.5, 2.5, t.HeightM)); }

    /// <summary>Speed going up stairs, as a share of the flat speed (0.1–1.5).</summary>
    public double StairUpFactor { get => Template.StairUpFactor; set => Edit("Change stair up speed", t => t.StairUpFactor = Clamp(value, 0.1, 1.5, t.StairUpFactor)); }

    /// <summary>Speed going down stairs, as a share of the flat speed (0.1–1.5).</summary>
    public double StairDownFactor { get => Template.StairDownFactor; set => Edit("Change stair down speed", t => t.StairDownFactor = Clamp(value, 0.1, 1.5, t.StairDownFactor)); }

    /// <summary>How much harder a stair up feels than its walking time (1–10).</summary>
    public double StairUpEffort { get => Template.StairUpEffort; set => Edit("Change stair up effort", t => t.StairUpEffort = Clamp(value, 1, 10, t.StairUpEffort)); }

    /// <summary>How much harder a stair down feels than its walking time (1–10).</summary>
    public double StairDownEffort { get => Template.StairDownEffort; set => Edit("Change stair down effort", t => t.StairDownEffort = Clamp(value, 1, 10, t.StairDownEffort)); }

    /// <summary>Patience in a queue before the give-up rule applies (5–7200 s).</summary>
    public double PatienceS { get => Template.PatienceS; set => Edit("Change patience", t => t.PatienceS = Clamp(value, 5, 7200, t.PatienceS)); }

    /// <summary>0 = very random choices, 1 = always picks the lowest cost option.</summary>
    public double Decisiveness { get => Template.Decisiveness; set => Edit("Change decisiveness", t => t.Decisiveness = Clamp(value, 0, 1, t.Decisiveness)); }

    /// <summary>−1 avoids lifts, 0 neutral, +1 strongly prefers them.</summary>
    public double LiftPreference { get => Template.LiftPreference; set => Edit("Change lift preference", t => t.LiftPreference = Clamp(value, -1, 1, t.LiftPreference)); }

    /// <summary>−1 avoids escalators, travelators and lifts, 0 neutral, +1 strongly prefers them (D-138).</summary>
    public double MechanisedPreference { get => Template.MechanisedPreference; set => Edit("Change escalator preference", t => t.MechanisedPreference = Clamp(value, -1, 1, t.MechanisedPreference)); }

    /// <summary>How far they see crowds and queues (1–200 m).</summary>
    public double SightRadiusM { get => Template.SightRadiusM; set => Edit("Change sight radius", t => t.SightRadiusM = Clamp(value, 1, 200, t.SightRadiusM)); }

    /// <summary>Chance of walking on an escalator rather than standing (0–1).</summary>
    public double EscalatorWalkProbability { get => Template.EscalatorWalkProbability; set => Edit("Change escalator walking", t => t.EscalatorWalkProbability = Clamp(value, 0, 1, t.EscalatorWalkProbability)); }

    /// <summary>Journey time they're content with (JOURNEYS results; 30–14400 s).</summary>
    public double AcceptableJourneyS { get => Template.AcceptableJourneyS; set => Edit("Change acceptable journey time", t => t.AcceptableJourneyS = Clamp(value, 30, 14400, t.AcceptableJourneyS)); }

    /// <summary>0..1 tolerance of crowding before frustration builds.</summary>
    public double DensityTolerance { get => Template.DensityTolerance; set => Edit("Change crowd tolerance", t => t.DensityTolerance = Clamp(value, 0, 1, t.DensityTolerance)); }

    /// <summary>Never uses stairs or escalators (lifts and ramps only).</summary>
    public bool LiftOnly { get => Template.LiftOnly; set => Edit(value ? "Make profile lift / ramp only" : "Let profile use stairs", t => t.LiftOnly = value); }

    /// <summary>Uses only accessible devices of a type where the level has one.</summary>
    public bool NeedsAccessibleDevices { get => Template.NeedsAccessibleDevices; set => Edit(value ? "Profile needs accessible devices" : "Profile uses any device", t => t.NeedsAccessibleDevices = value); }

    /// <summary>Journey choices for the default journey (D-304): "none" (<see cref="Guid.Empty"/>) first.</summary>
    public IReadOnlyList<IdOption> DefaultJourneyOptions =>
        [new IdOption(Guid.Empty, "None: straight to a sink"), .. StudyTemplates.AvailableJourneys(_owner.Study, _owner.Mine).Select(j => new IdOption(j.Id, j.Name))];

    /// <summary>
    /// The journey this profile follows on mix lines set to "(profile default)" (D-304); <see cref="Guid.Empty"/> for
    /// none. A built-in journey is embedded in the study when picked.
    /// </summary>
    public Guid DefaultJourneyId
    {
        get => Template.DefaultJourneyId ?? Guid.Empty;
        set
        {
            Guid? next = value == Guid.Empty ? null : value;
            if (next == Template.DefaultJourneyId)
            {
                return;
            }

            if (next is { } id)
            {
                _owner.EmbedBuiltInJourney(id);
            }

            Edit("Change default journey", t => t.DefaultJourneyId = next);
        }
    }

    /// <summary>Device type tags and preset keys, for the preference target combos.</summary>
    public IReadOnlyList<string> PreferenceTargetOptions =>
        [.. StudyDefinitions.KnownDeviceTypes(_owner.Study)
            .Concat(StudyDefinitions.AvailableDevices(_owner.Study).Where(d => d.Kind == DeviceKind.Point).Select(d => d.Key))
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>Re-reads everything (after an edit or undo); rebuilds the rows only when their number changed.</summary>
    public void Refresh()
    {
        if (Preferences.Count != (Template.DevicePreferences?.Count ?? 0))
        {
            RebuildPreferences();
        }
        else
        {
            foreach (DevicePreferenceRowViewModel row in Preferences)
            {
                row.Refresh();
            }
        }

        OnPropertyChanged(string.Empty);
        Speed.Refresh();
    }

    /// <summary>Replaces one preference (undoable).</summary>
    internal void SetPreference(int index, DevicePreference preference)
    {
        List<DevicePreference> list = [.. Template.DevicePreferences ?? []];
        if (index < 0 || index >= list.Count)
        {
            return;
        }

        list[index] = preference;
        Edit("Change device preference", t => t.DevicePreferences = list);
    }

    /// <summary>Removes one preference (undoable).</summary>
    internal void RemovePreference(int index)
    {
        List<DevicePreference> list = [.. Template.DevicePreferences ?? []];
        if (index < 0 || index >= list.Count)
        {
            return;
        }

        list.RemoveAt(index);
        Edit("Remove device preference", t => t.DevicePreferences = list);
    }

    /// <summary>The preference at a row index, or an empty one.</summary>
    internal DevicePreference PreferenceAt(int index) =>
        Template.DevicePreferences is { } list && index >= 0 && index < list.Count && list[index] is { } p ? p : new DevicePreference();

    private void AddPreference()
    {
        string target = PreferenceTargetOptions.FirstOrDefault() ?? "Gate";
        List<DevicePreference> list = [.. Template.DevicePreferences ?? [], new DevicePreference { Target = target, BonusS = 30 }];
        Edit("Add device preference", t => t.DevicePreferences = list);
    }

    private void RebuildPreferences()
    {
        Preferences.Clear();
        int count = Template.DevicePreferences?.Count ?? 0;
        for (int k = 0; k < count; k++)
        {
            Preferences.Add(new DevicePreferenceRowViewModel(this, k));
        }
    }

    private void Edit(string description, Action<AgentProfileTemplate> change) => _owner.EditProfileDefinition(Id, description, change);

    private static double Clamp(double value, double min, double max, double current) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : current;
}

/// <summary>One device preference of a profile: a type tag or preset key and the seconds of walking it's worth.</summary>
public sealed class DevicePreferenceRowViewModel : ObservableObject
{
    private readonly ProfileDefinitionViewModel _owner;
    private readonly int _index;

    /// <summary>Wraps the preference at <paramref name="index"/>.</summary>
    public DevicePreferenceRowViewModel(ProfileDefinitionViewModel owner, int index)
    {
        _owner = owner;
        _index = index;
        RemoveCommand = new RelayCommand(() => _owner.RemovePreference(_index));
    }

    /// <summary>Removes the row.</summary>
    public RelayCommand RemoveCommand { get; }

    /// <summary>Type tag or preset key the bonus applies to.</summary>
    public string Target
    {
        get => _owner.PreferenceAt(_index).Target;
        set
        {
            string trimmed = (value ?? string.Empty).Trim();
            if (trimmed.Length > 0)
            {
                _owner.SetPreference(_index, _owner.PreferenceAt(_index) with { Target = trimmed });
            }
        }
    }

    /// <summary>Seconds of walking the device is worth (negative avoids it; −600 to 600).</summary>
    public double BonusS
    {
        get => _owner.PreferenceAt(_index).BonusS;
        set
        {
            if (double.IsFinite(value))
            {
                _owner.SetPreference(_index, _owner.PreferenceAt(_index) with { BonusS = Math.Clamp(value, -600, 600) });
            }
        }
    }

    /// <summary>Targets for the combo.</summary>
    public IReadOnlyList<string> TargetOptions => _owner.PreferenceTargetOptions;

    /// <summary>Re-reads the row.</summary>
    public void Refresh() => OnPropertyChanged(string.Empty);
}
