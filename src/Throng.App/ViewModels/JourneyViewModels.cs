using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Throng.Core.Library;
using Throng.Core.Model;

namespace Throng.App.ViewModels;

/// <summary>
/// SELECTION ▸ JOURNEY (D-154, D-161, D-162): the journey picked in LIBRARY ▸ JOURNEYS, with its step grid. Built-in
/// journeys (and the copies a study embeds when a mix line uses one) are read-only and offer DUPLICATE; the copies
/// it makes are edited here. Every edit goes through the undo stack: steps are immutable records, so an edit replaces
/// the journey's step list.
/// </summary>
public sealed class JourneyViewModel : ObservableObject
{
    private readonly MainViewModel _owner;

    /// <summary>Wraps a journey.</summary>
    public JourneyViewModel(JourneyTemplate journey, MainViewModel owner)
    {
        Journey = journey;
        _owner = owner;
        DuplicateCommand = new RelayCommand(() => _owner.DuplicateJourney(Journey));
        DeleteCommand = new RelayCommand(() => _owner.DeleteJourney(Journey));
        AddStepCommand = new RelayCommand(AddStep);
        RebuildSteps();
    }

    /// <summary>The wrapped journey.</summary>
    public JourneyTemplate Journey { get; }

    /// <summary>Makes an editable copy in the study library and selects it (D-161).</summary>
    public RelayCommand DuplicateCommand { get; }

    /// <summary>Deletes an editable journey that no mix line uses.</summary>
    public RelayCommand DeleteCommand { get; }

    /// <summary>Adds a device step before the exit.</summary>
    public RelayCommand AddStepCommand { get; }

    /// <summary>True for journeys the user made (duplicates); false for built-ins and their embedded copies.</summary>
    public bool IsEditable => StudyTemplates.IsEditableJourney(_owner.Study, Journey);

    /// <summary>True for built-ins (shows the read-only hint).</summary>
    public bool IsReadOnly => !IsEditable;

    /// <summary>Where it came from.</summary>
    public string SourceText => IsEditable
        ? "Your journey, saved in this study. Edits can be undone."
        : "Built-in · read-only. DUPLICATE makes an editable copy in this study.";

    /// <summary>How many mix lines use it.</summary>
    public string UsageText
    {
        get
        {
            int uses = _owner.JourneyUseCount(Journey.Id);
            return uses switch
            {
                0 => "Not used by any mix line yet: pick it on a spawn zone's mix line in SELECTION.",
                1 => "Used by 1 mix line.",
                _ => string.Create(CultureInfo.InvariantCulture, $"Used by {uses} mix lines."),
            };
        }
    }

    /// <summary>Display name (blank names are rejected).</summary>
    public string Name
    {
        get => Journey.Name;
        set
        {
            string trimmed = (value ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                Reject("A journey needs a name.");
                return;
            }

            if (IsEditable)
            {
                _owner.Apply("Rename journey", () => Journey.Name, v => Journey.Name = v, trimmed);
            }
        }
    }

    /// <summary>Description (the LIBRARY tooltip).</summary>
    public string Description
    {
        get => Journey.Description;
        set
        {
            if (IsEditable)
            {
                _owner.Apply("Change journey description", () => Journey.Description, v => Journey.Description = v, (value ?? string.Empty).Trim());
            }
        }
    }

    /// <summary>The whole journey on one line.</summary>
    public string Summary => Journey.Summarise();

    /// <summary>One row per step.</summary>
    public ObservableCollection<JourneyStepViewModel> Steps { get; } = [];

    /// <summary>Device type tags in the study (and the built-in presets), for the target combo of device steps.</summary>
    public IReadOnlyList<string> DeviceTypeOptions =>
        [.. _owner.Study.Devices.Select(d => d.TypeTag)
            .Concat(BuiltInLibrary.Instance.Devices.Where(d => d.Kind == DeviceKind.Point).Select(d => d.TypeTag))
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)];

    /// <summary>Zone tags in the study, for the target combo of zone steps (empty = any dwell zone).</summary>
    public IReadOnlyList<string> ZoneTagOptions =>
        [string.Empty, .. _owner.Study.Regions.Where(r => r.Kind == RegionKind.Zone).Select(r => r.Tag)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)];

    /// <summary>Train service tags (or names when untagged) for Board train steps, "any" (empty) first (D-186).</summary>
    public IReadOnlyList<string> TrainTagOptions =>
        [string.Empty, .. _owner.Study.TrainServices.Where(t => t is not null)
            .Select(t => string.IsNullOrWhiteSpace(t.Tag) ? t.Name : t.Tag)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)];

    /// <summary>Re-reads everything; rebuilds the rows only when the number of steps changed (keeps focus while editing).</summary>
    public void Refresh()
    {
        if (Steps.Count != Journey.Steps.Count)
        {
            RebuildSteps();
        }
        else
        {
            foreach (JourneyStepViewModel step in Steps)
            {
                step.Refresh();
            }
        }

        OnPropertyChanged(string.Empty);
    }

    /// <summary>Replaces the step list through the undo stack (no-op for read-only journeys).</summary>
    internal void ReplaceSteps(List<JourneyStep> next, string description)
    {
        if (!IsEditable)
        {
            Reject("Built-in journeys are read-only: DUPLICATE it to edit a copy.");
            return;
        }

        _owner.Apply(description, () => Journey.Steps, v => Journey.Steps = v, next);
    }

    /// <summary>Replaces one step.</summary>
    internal void SetStep(int index, JourneyStep step, string description)
    {
        if (index < 0 || index >= Journey.Steps.Count || Equals(Journey.Steps[index], step))
        {
            return;
        }

        List<JourneyStep> next = [.. Journey.Steps];
        next[index] = step;
        ReplaceSteps(next, description);
    }

    /// <summary>Swaps a step with its neighbour, keeping give-up jumps pointing at the same steps.</summary>
    internal void Move(int index, int delta)
    {
        int other = index + delta;
        if (index < 0 || other < 0 || index >= Journey.Steps.Count || other >= Journey.Steps.Count)
        {
            return;
        }

        List<JourneyStep> next = [.. Journey.Steps.Select(s => Remap(s, k => k == index ? other : k == other ? index : k))];
        (next[index], next[other]) = (next[other], next[index]);
        ReplaceSteps(next, delta < 0 ? "Move journey step up" : "Move journey step down");
    }

    /// <summary>Removes a step; jumps to it go to the step that takes its place, jumps past it move up one.</summary>
    internal void Remove(int index)
    {
        if (index < 0 || index >= Journey.Steps.Count)
        {
            return;
        }

        int remaining = Journey.Steps.Count - 1;
        List<JourneyStep> next = [.. Journey.Steps.Select(s => Remap(s, k => k > index ? k - 1 : Math.Min(k, Math.Max(0, remaining - 1))))];
        next.RemoveAt(index);
        ReplaceSteps(next, "Remove journey step");
    }

    /// <summary>Number of steps (for jump ranges and messages).</summary>
    internal int StepCount => Journey.Steps.Count;

    /// <summary>Name of a zone in the study, for pinned zone steps.</summary>
    internal string ZoneName(Guid id) => _owner.Study.Regions.FirstOrDefault(r => r.Id == id)?.Name ?? "(deleted)";

    internal void Reject(string message)
    {
        _owner.Ticker = message;
        OnPropertyChanged(string.Empty);
        foreach (JourneyStepViewModel step in Steps)
        {
            step.Refresh();
        }
    }

    private void AddStep()
    {
        // A device step before the final exit (or at the end when there's no exit), asking for the first device type.
        int at = Journey.Steps.Count > 0 && Journey.Steps[^1] is { EndsJourney: true } ? Journey.Steps.Count - 1 : Journey.Steps.Count;
        string type = DeviceTypeOptions.FirstOrDefault() ?? "Gate";
        List<JourneyStep> next = [.. Journey.Steps.Select(s => Remap(s, k => k >= at ? k + 1 : k))];
        next.Insert(at, JourneyStep.Device(type));
        ReplaceSteps(next, "Add journey step");
    }

    /// <summary>The step with its give-up jump target mapped (only jumps are touched).</summary>
    private static JourneyStep Remap(JourneyStep step, Func<int, int> map) =>
        step is { OnGiveUp: JourneyGiveUp.JumpTo } ? step with { GiveUpStep = map(step.GiveUpStep) } : step;

    private void RebuildSteps()
    {
        Steps.Clear();
        for (int k = 0; k < Journey.Steps.Count; k++)
        {
            Steps.Add(new JourneyStepViewModel(this, k));
        }
    }
}

/// <summary>One row of the journey step grid (D-154, D-165). Reads the step live by index; every setter is undoable.</summary>
public sealed class JourneyStepViewModel : ObservableObject
{
    private static readonly IReadOnlyList<LabelledOption<JourneyStepKind>> Kinds =
    [
        new(JourneyStepKind.GoToDevice, "Use a device"),
        new(JourneyStepKind.PassZone, "Pass through a zone"),
        new(JourneyStepKind.Dwell, "Dwell in a zone"),
        new(JourneyStepKind.Exit, "Exit"),
        new(JourneyStepKind.BoardTrain, "Board a train"),
    ];

    private static readonly IReadOnlyList<LabelledOption<JourneyGiveUp>> GiveUps =
    [
        new(JourneyGiveUp.KeepWaiting, "Keep waiting (stress only)"),
        new(JourneyGiveUp.Leave, "Leave the study"),
        new(JourneyGiveUp.JumpTo, "Jump to step…"),
    ];

    private readonly JourneyViewModel _journey;
    private readonly int _index;

    /// <summary>Creates the row for step <paramref name="index"/>.</summary>
    public JourneyStepViewModel(JourneyViewModel journey, int index)
    {
        _journey = journey;
        _index = index;
        MoveUpCommand = new RelayCommand(() => _journey.Move(_index, -1));
        MoveDownCommand = new RelayCommand(() => _journey.Move(_index, +1));
        RemoveCommand = new RelayCommand(() => _journey.Remove(_index));
    }

    /// <summary>Moves the step up one.</summary>
    public RelayCommand MoveUpCommand { get; }

    /// <summary>Moves the step down one.</summary>
    public RelayCommand MoveDownCommand { get; }

    /// <summary>Removes the step.</summary>
    public RelayCommand RemoveCommand { get; }

    /// <summary>"1", "2"…</summary>
    public string NumberText => (_index + 1).ToString(CultureInfo.InvariantCulture);

    /// <summary>Step kinds for the combo.</summary>
    public IReadOnlyList<LabelledOption<JourneyStepKind>> KindOptions => Kinds;

    /// <summary>Give-up choices for the combo.</summary>
    public IReadOnlyList<LabelledOption<JourneyGiveUp>> GiveUpOptions => GiveUps;

    /// <summary>What the step does.</summary>
    public JourneyStepKind Kind
    {
        get => Step.Kind;
        set
        {
            if (value == Step.Kind)
            {
                return;
            }

            // A new device step asks for the first device type; zone steps start on "any dwell zone".
            string target = value switch
            {
                JourneyStepKind.GoToDevice => _journey.DeviceTypeOptions.FirstOrDefault() ?? string.Empty,
                JourneyStepKind.Exit or JourneyStepKind.BoardTrain => string.Empty,
                _ => Step.Kind is JourneyStepKind.PassZone or JourneyStepKind.Dwell ? Step.TargetTag : string.Empty,
            };
            _journey.SetStep(_index, Step with { Kind = value, TargetTag = target, ZoneId = value is JourneyStepKind.PassZone or JourneyStepKind.Dwell ? Step.ZoneId : null }, "Change journey step kind");
        }
    }

    /// <summary>True when the step has a target (everything but exit).</summary>
    public bool HasTarget => Step.Kind != JourneyStepKind.Exit;

    /// <summary>True for device steps (shows the give-up choice).</summary>
    public bool IsDevice => Step.Kind == JourneyStepKind.GoToDevice;

    /// <summary>True for dwell steps (shows the dwell time).</summary>
    public bool IsDwell => Step.Kind == JourneyStepKind.Dwell;

    /// <summary>Label over the target field.</summary>
    public string TargetLabel => IsDevice ? "Device type" : IsDwell ? "Zone tag (empty = any dwell zone)" : IsTrain ? "Train service tag (empty = any)" : "Zone tag (empty = any zone)";

    /// <summary>True for Board train steps (D-186).</summary>
    public bool IsTrain => Step.Kind == JourneyStepKind.BoardTrain;

    /// <summary>Choices for the target combo (free text allowed).</summary>
    public IReadOnlyList<string> TargetOptions => IsDevice ? _journey.DeviceTypeOptions : IsTrain ? _journey.TrainTagOptions : _journey.ZoneTagOptions;

    /// <summary>Device type tag or zone tag.</summary>
    public string Target
    {
        get => Step.TargetTag;
        set
        {
            string trimmed = (value ?? string.Empty).Trim();
            if (IsDevice && trimmed.Length == 0)
            {
                _journey.Reject("A device step needs a device type, e.g. Gate or TicketMachine.");
                return;
            }

            if (trimmed != Step.TargetTag || (!IsDevice && Step.ZoneId is not null))
            {
                // Typing a tag unpins a pinned zone.
                _journey.SetStep(_index, Step with { TargetTag = trimmed, ZoneId = null }, "Change journey step target");
            }
        }
    }

    /// <summary>"Pinned to zone 'X'" when a specific zone is pinned, else empty.</summary>
    public string PinnedText => Step.ZoneId is { } id && !IsDevice && !IsTrain
        ? $"Pinned to zone '{_journey.ZoneName(id)}'. Type a tag to unpin."
        : string.Empty;

    /// <summary>True when <see cref="PinnedText"/> has something to say.</summary>
    public bool HasPinned => PinnedText.Length > 0;

    /// <summary>The step applies only to people carrying this tag.</summary>
    public string NeedsTag
    {
        get => Step.NeedsTag;
        set => SetTag(value, Step.NeedsTag, t => Step with { NeedsTag = t }, "Change journey step needs tag");
    }

    /// <summary>The step applies only to people without this tag.</summary>
    public string LacksTag
    {
        get => Step.LacksTag;
        set => SetTag(value, Step.LacksTag, t => Step with { LacksTag = t }, "Change journey step lacks tag");
    }

    /// <summary>Tag given on completing the step.</summary>
    public string GrantsTag
    {
        get => Step.GrantsTag;
        set => SetTag(value, Step.GrantsTag, t => Step with { GrantsTag = t }, "Change journey step grants tag");
    }

    /// <summary>Dwell time as "30–180" (uniform range) or "60" (fixed), seconds.</summary>
    public string DwellText
    {
        get => (Step.DwellS ?? Distribution.Fixed(0)).Describe();
        set
        {
            if (!TryParseRange(value, out Distribution? dwell))
            {
                _journey.Reject("Dwell time is seconds, e.g. 60 or a range 30-180.");
                return;
            }

            if (dwell.Describe() != DwellText)
            {
                _journey.SetStep(_index, Step with { DwellS = dwell }, "Change journey dwell time");
            }
        }
    }

    /// <summary>What happens when patience runs out in this step's queue (D-165).</summary>
    public JourneyGiveUp GiveUp
    {
        get => Step.OnGiveUp;
        set
        {
            if (value == Step.OnGiveUp)
            {
                return;
            }

            // Jumping starts at the step after this one (or the last step).
            int target = value == JourneyGiveUp.JumpTo ? Math.Min(_index + 1, Math.Max(0, _journey.StepCount - 1)) : Step.GiveUpStep;
            _journey.SetStep(_index, Step with { OnGiveUp = value, GiveUpStep = target }, "Change journey give-up");
        }
    }

    /// <summary>True when the give-up jumps (shows the step number).</summary>
    public bool IsJump => IsDevice && Step.OnGiveUp == JourneyGiveUp.JumpTo;

    /// <summary>Step to jump to, 1-based.</summary>
    public int GiveUpStepNumber
    {
        get => Step.GiveUpStep + 1;
        set
        {
            if (value < 1 || value > _journey.StepCount)
            {
                _journey.Reject(string.Create(CultureInfo.InvariantCulture, $"Jump to a step from 1 to {_journey.StepCount}."));
                return;
            }

            _journey.SetStep(_index, Step with { GiveUpStep = value - 1 }, "Change journey give-up step");
        }
    }

    /// <summary>The step this row edits.</summary>
    private JourneyStep Step => _index < _journey.Journey.Steps.Count && _journey.Journey.Steps[_index] is { } step ? step : JourneyStep.ExitStep();

    /// <summary>Re-reads every property.</summary>
    public void Refresh() => OnPropertyChanged(string.Empty);

    /// <summary>Parses "60", "30-180" or "30–180" (seconds, non-negative) into a fixed or uniform distribution.</summary>
    public static bool TryParseRange(string? text, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Distribution? range)
    {
        range = null;
        string[] parts = (text ?? string.Empty).Replace('–', '-').Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        CultureInfo c = CultureInfo.InvariantCulture;
        if (parts.Length == 1 && double.TryParse(parts[0], NumberStyles.Float, c, out double value) && value >= 0 && double.IsFinite(value))
        {
            range = Distribution.Fixed(value);
            return true;
        }

        if (parts.Length == 2
            && double.TryParse(parts[0], NumberStyles.Float, c, out double min) && double.TryParse(parts[1], NumberStyles.Float, c, out double max)
            && min >= 0 && max >= min && double.IsFinite(max))
        {
            range = min == max ? Distribution.Fixed(min) : Distribution.Uniform(min, max);
            return true;
        }

        return false;
    }

    private void SetTag(string? value, string current, Func<string, JourneyStep> with, string description)
    {
        string trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Contains(' ', StringComparison.Ordinal) || trimmed.Contains(',', StringComparison.Ordinal))
        {
            _journey.Reject("A tag is one word, e.g. ticket or vip.");
            return;
        }

        if (trimmed != current)
        {
            _journey.SetStep(_index, with(trimmed), description);
        }
    }
}
