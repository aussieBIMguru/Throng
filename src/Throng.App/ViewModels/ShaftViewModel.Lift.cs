using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Throng.Core.Editing;
using Throng.Core.Geometry;
using Throng.Core.Library;
using Throng.Core.Model;

namespace Throng.App.ViewModels;

/// <summary>
/// The LIFT part of a selected shaft's SELECTION panel (round 013, D-236–D-244): pick a lift preset (or none), then
/// cars, front edge, landing depth, lobby, dispatch with its time-of-day schedule, and the doors per served level.
/// Every edit replaces the shaft's lift with an edited copy through the undo stack.
/// </summary>
public sealed partial class ShaftViewModel
{
    /// <summary>"No lift" in the preset combo.</summary>
    public const string NoLiftKey = "";

    private static readonly IReadOnlyList<LabelledOption<int>> CarChoices =
        [new(1, "1 car"), new(2, "2 cars"), new(3, "3 cars"), new(4, "4 cars")];

    private static readonly IReadOnlyList<LabelledOption<LiftDispatch>> DispatchChoices =
        [new(LiftDispatch.Collective, "Collective"), new(LiftDispatch.UpPeak, "Up-peak"), new(LiftDispatch.Shuttle, "Shuttle (lowest ↔ highest)")];

    private RelayCommand? _addScheduleRowCommand;

    /// <summary>Lift presets, with "No lift" first.</summary>
    public IReadOnlyList<LabelledOption<string>> LiftPresetOptions =>
        [new(NoLiftKey, "No lift"), .. StudyTemplates.AvailableLifts(_owner.Study).Select(t => new LabelledOption<string>(t.Key, t.Name))];

    /// <summary>The lift's preset key, or <see cref="NoLiftKey"/>. Choosing one adds or swaps the lift; "No lift" removes it.</summary>
    public string LiftPresetKey
    {
        get => Model.Lift?.PresetKey ?? NoLiftKey;
        set
        {
            string key = value ?? NoLiftKey;
            if (key == LiftPresetKey)
            {
                return;
            }

            if (key.Length == 0)
            {
                _owner.Apply($"Remove lift from {Model.Name}", () => Model.Lift, v => Model.Lift = v, null);
                return;
            }

            if (StudyTemplates.AvailableLifts(_owner.Study).FirstOrDefault(t => t.Key == key) is not { } preset)
            {
                return;
            }

            TemplateBase embedded = _owner.Study.Library.AddCopy(preset, TemplateSource.Embedded);
            if (Model.Lift is { } lift)
            {
                Lift copy = lift.Clone();
                copy.PresetKey = preset.Key;
                copy.TemplateId = embedded.Id;
                copy.Dispatch = preset.Dispatch == LiftDispatch.DestinationDispatch ? LiftDispatch.Collective : preset.Dispatch;
                _owner.Apply($"Change {lift.Name} to {preset.Name}", () => Model.Lift, v => Model.Lift = v, copy);
            }
            else
            {
                _owner.Apply($"Add {preset.Name} to {Model.Name}", () => Model.Lift, v => Model.Lift = v, LiftFactory.FromTemplate(Model, preset, embedded.Id));
            }
        }
    }

    /// <summary>True when the shaft has a lift (shows the lift fields).</summary>
    public bool HasLift => Model.Lift is not null;

    /// <summary>The preset's values, e.g. "13 persons · 1.6 m/s · Collective".</summary>
    public string LiftPresetText => Model.Lift is { } lift && StudyTemplates.FindLiftTemplate(_owner.Study, lift) is { } t ? t.Summarise() : string.Empty;

    /// <summary>Lift name (blank names are rejected).</summary>
    public string LiftName
    {
        get => Model.Lift?.Name ?? string.Empty;
        set
        {
            string trimmed = (value ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                _owner.Ticker = "A lift needs a name.";
                OnPropertyChanged();
                return;
            }

            EditLift("Rename lift", l => l.Name = trimmed);
        }
    }

    /// <summary>Cars choices.</summary>
    public IReadOnlyList<LabelledOption<int>> LiftCarOptions => CarChoices;

    /// <summary>Cars in the bank, 1–4 (D-237).</summary>
    public int LiftCars
    {
        get => Model.Lift?.Cars ?? 1;
        set => EditLift("Change lift cars", l => l.Cars = Math.Clamp(value, 1, Lift.MaxCars));
    }

    /// <summary>The shaft's edges, for the front edge combo (D-242).</summary>
    public IReadOnlyList<LabelledOption<int>> LiftEdgeOptions
    {
        get
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            int n = Model.Outline.Count;
            return [.. Enumerable.Range(0, n).Select(e => new LabelledOption<int>(e, string.Create(c, $"Edge {e + 1} · {(Model.Outline[(e + 1) % n] - Model.Outline[e]).Length:0.0} m")))];
        }
    }

    /// <summary>The shaft edge the front doors are on; the rear is the edge most nearly opposite (D-242).</summary>
    public int LiftFrontEdge
    {
        get => Model.Lift?.FrontEdge ?? 0;
        set => EditLift("Change lift front edge", l => l.FrontEdge = value);
    }

    /// <summary>Landing depth in front of each door, metres (D-241).</summary>
    public double LiftLandingDepthM
    {
        get => Model.Lift?.LandingDepthM ?? Lift.DefaultLandingDepthM;
        set
        {
            if (!(value >= 0.5 && value <= 20))
            {
                _owner.Ticker = "Landing depth must be 0.5–20 m.";
                OnPropertyChanged();
                return;
            }

            EditLift("Change lift landing depth", l => l.LandingDepthM = Math.Round(value, 2));
        }
    }

    /// <summary>Served levels for the lobby combo, lowest first.</summary>
    public IReadOnlyList<LevelOption> LiftStopOptions => [.. _owner.Study.LiftStops(Model).Select(l => new LevelOption(l.Id, l.Name))];

    /// <summary>Lobby level for Up-peak (D-244); the lowest served level when unset.</summary>
    public Guid LiftLobbyLevelId
    {
        get => Model.Lift?.LobbyLevelId ?? _owner.Study.LiftStops(Model).FirstOrDefault()?.Id ?? Guid.Empty;
        set => EditLift("Change lift lobby", l => l.LobbyLevelId = value);
    }

    /// <summary>Dispatch choices.</summary>
    public IReadOnlyList<LabelledOption<LiftDispatch>> LiftDispatchOptions => DispatchChoices;

    /// <summary>Dispatch mode (D-238).</summary>
    public LiftDispatch LiftDispatchMode
    {
        get => Model.Lift?.Dispatch ?? LiftDispatch.Collective;
        set => EditLift("Change lift dispatch", l => l.Dispatch = value);
    }

    /// <summary>Doors per served level, highest first (D-237).</summary>
    public IReadOnlyList<LiftDoorRowViewModel> LiftDoorRows
    {
        get
        {
            if (Model.Lift is not { } lift)
            {
                return [];
            }

            bool hasRear = LiftLayout.Compute(Model.Outline, lift.FrontEdge, lift.Cars, lift.LandingDepthM) is { IsSuccess: true, Value.Rear: not null };
            return [.. Enumerable.Reverse(_owner.Study.LiftStops(Model)).Select(level => new LiftDoorRowViewModel(
                level.Name,
                hasRear,
                () => Model.Lift?.DoorsAt(level.Id) ?? new LiftLevelDoors { LevelId = level.Id },
                (front, rear) => EditLift($"Change lift doors at {level.Name}", l =>
                {
                    l.Doors.RemoveAll(d => d.LevelId == level.Id);
                    l.Doors.Add(new LiftLevelDoors { LevelId = level.Id, Front = front, Rear = rear });
                })))];
        }
    }

    /// <summary>Schedule rows in time order (D-238).</summary>
    public IReadOnlyList<LiftScheduleRowViewModel> LiftScheduleRows
    {
        get
        {
            if (Model.Lift is not { } lift)
            {
                return [];
            }

            var rows = new List<LiftScheduleRowViewModel>();
            for (int r = 0; r < lift.Schedule.Count; r++)
            {
                int row = r;
                rows.Add(new LiftScheduleRowViewModel(
                    lift.Schedule[r],
                    (from, dispatch) => EditLift("Change lift schedule", l => l.Schedule[row] = new LiftScheduleEntry { From = from, Dispatch = dispatch }),
                    () => EditLift("Remove lift schedule row", l => l.Schedule.RemoveAt(row)),
                    _owner));
            }

            return [.. rows.OrderBy(r => r.From)];
        }
    }

    /// <summary>ADD ROW: a schedule row an hour after the last (or at the study start), Up-peak first then Collective.</summary>
    public RelayCommand AddScheduleRowCommand => _addScheduleRowCommand ??= new RelayCommand(() =>
    {
        if (Model.Lift is not { } lift)
        {
            return;
        }

        TimeSpan from = lift.Schedule.Count == 0 ? _owner.Study.Settings.StartTimeOfDay : lift.Schedule.Max(s => s.From) + TimeSpan.FromHours(1);
        from = TimeSpan.FromMinutes(Math.Floor(from.TotalMinutes % 1440));
        LiftDispatch dispatch = lift.Schedule.Count % 2 == 0 ? LiftDispatch.UpPeak : LiftDispatch.Collective;
        EditLift("Add lift schedule row", l => l.Schedule.Add(new LiftScheduleEntry { From = from, Dispatch = dispatch }));
    });

    /// <summary>Replaces the lift with an edited copy (undoable). No-op without a lift.</summary>
    private void EditLift(string description, Action<Lift> change)
    {
        if (Model.Lift is not { } lift)
        {
            return;
        }

        Lift copy = lift.Clone();
        change(copy);
        _owner.Apply(description, () => Model.Lift, v => Model.Lift = v, copy);
    }
}
