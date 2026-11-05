using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Throng.Core.Model;

namespace Throng.App.ViewModels;

/// <summary>
/// One train service in STUDY ▸ TRAINS (D-184–D-187). Reads the service live by index; every setter replaces the
/// record through the undo stack, and bad values are rejected with a ticker message.
/// </summary>
public sealed class TrainServiceViewModel : ObservableObject
{
    private readonly MainViewModel _owner;
    private readonly int _index;

    /// <summary>Creates the row for service <paramref name="index"/>.</summary>
    public TrainServiceViewModel(MainViewModel owner, int index)
    {
        _owner = owner;
        _index = index;
        DeleteCommand = new RelayCommand(() => _owner.RemoveTrainService(_index));
        SetDoorsCommand = new RelayCommand(SetDoors);
        DrawShapeCommand = new RelayCommand(() => _owner.StartTrainShape(_index));
        ClearShapeCommand = new RelayCommand(() => _owner.ClearTrainShape(_index));
    }

    /// <summary>Arms the polygon tool to draw the train's outline (D-249).</summary>
    public RelayCommand DrawShapeCommand { get; }

    /// <summary>Back to the automatic box along the platform edge.</summary>
    public RelayCommand ClearShapeCommand { get; }

    /// <summary>"Drawn (6 points)" or "Automatic box along the platform edge".</summary>
    public string ShapeText => Service.Shape.Count >= 3
        ? string.Create(CultureInfo.InvariantCulture, $"Drawn ({Service.Shape.Count} points)")
        : "Automatic box along the edge of the door zones.";

    /// <summary>Deletes the service.</summary>
    public RelayCommand DeleteCommand { get; }

    /// <summary>Makes the selected sink zones the service's doors.</summary>
    public RelayCommand SetDoorsCommand { get; }

    /// <summary>The service (an empty one if the list changed under the row).</summary>
    public TrainService Service => _index < _owner.Study.TrainServices.Count ? _owner.Study.TrainServices[_index] : new TrainService();

    /// <summary>"SOUTHBOUND".</summary>
    public string Header => Service.Name.ToUpperInvariant();

    /// <summary>Kind choices (round 025, D-398).</summary>
    public IReadOnlyList<LabelledOption<ServiceKind>> KindOptions { get; } =
        [.. ServiceKinds.All.Select(k => new LabelledOption<ServiceKind>(k, ServiceKinds.Label(k)))];

    /// <summary>
    /// What the service is (D-398): changing it renames the fields and, for timetable values still at the old kind's
    /// starting values, takes the new kind's (undoable).
    /// </summary>
    public ServiceKind Kind
    {
        get => Service.Kind;
        set
        {
            if (value != Service.Kind)
            {
                Replace(Service.WithKind(value), $"Make '{Service.Name}' a {ServiceKinds.Vehicle(value)} service");
            }
        }
    }

    /// <summary>"train", "plane"… for the field labels.</summary>
    public string Vehicle => ServiceKinds.Vehicle(Service.Kind);

    /// <summary>"First train at (s after the start)".</summary>
    public string FirstLabel => $"First {Vehicle} at (s after the start)";

    /// <summary>"Doors open (s)" / "Boarding gates open (s)".</summary>
    public string OpenLabel => $"{Capital(ServiceKinds.Doors(Service.Kind))} open (s)";

    /// <summary>"People getting off per train".</summary>
    public string AlightLabel => $"People getting off per {Vehicle}";

    /// <summary>"Places per train".</summary>
    public string PlacesLabel => $"Places per {Vehicle}";

    /// <summary>"Doors" / "Boarding gates" (the zones).</summary>
    public string DoorsLabel => Capital(ServiceKinds.Doors(Service.Kind));

    /// <summary>"Train shape".</summary>
    public string ShapeLabel => $"{Capital(Vehicle)} shape";

    /// <summary>"DRAW TRAIN".</summary>
    public string DrawLabel => $"DRAW {ServiceKinds.Badge(Service.Kind)}";

    /// <summary>"every 6:00 from 0:30, doors 45 s, 150 off, 300 places".</summary>
    public string Summary => Service.Describe();

    /// <summary>"6 doors: Southbound door 1, …".</summary>
    public string DoorsText
    {
        get
        {
            List<string> names = [.. Service.DoorZoneIds.Select(id => _owner.Study.FindRegion(id)?.Name ?? "(deleted zone)")];
            return names.Count == 0
                ? $"No {ServiceKinds.Doors(Service.Kind)}. Select their zones in the plan, then SET DOORS."
                : $"{names.Count} zone{(names.Count == 1 ? string.Empty : "s")}: {string.Join(", ", names)}";
        }
    }

    /// <summary>Display name.</summary>
    public string Name
    {
        get => Service.Name;
        set
        {
            string trimmed = (value ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                _owner.RejectTrainEdit("A service needs a name.");
                OnPropertyChanged();
                return;
            }

            Replace(Service with { Name = trimmed }, "Rename service");
        }
    }

    /// <summary>Tag that Board train steps ask for (one word, may be empty).</summary>
    public string Tag
    {
        get => Service.Tag;
        set
        {
            string trimmed = (value ?? string.Empty).Trim();
            if (trimmed.Contains(' ', StringComparison.Ordinal))
            {
                _owner.RejectTrainEdit("A service tag is one word, e.g. Southbound.");
                OnPropertyChanged();
                return;
            }

            Replace(Service with { Tag = trimmed }, "Change service tag");
        }
    }

    /// <summary>Time between trains, seconds.</summary>
    public string HeadwayText
    {
        get => Format(Service.HeadwayS);
        set => SetSeconds(value, 20, v => Service with { HeadwayS = v }, "Change service headway", "The time between arrivals must be at least 20 s.");
    }

    /// <summary>First arrival, seconds after the run starts.</summary>
    public string OffsetText
    {
        get => Format(Service.OffsetS);
        set => SetSeconds(value, 0, v => Service with { OffsetS = v }, "Change first arrival", "The first arrival can't be negative.");
    }

    /// <summary>Doors-open time, seconds.</summary>
    public string DwellText
    {
        get => Format(Service.DwellS);
        set => SetSeconds(value, 10, v => Service with { DwellS = v }, "Change service dwell", "It needs to stay open at least 10 s.");
    }

    /// <summary>How long getting off takes, seconds.</summary>
    public string AlightOverText
    {
        get => Format(Service.AlightOverS);
        set => SetSeconds(value, 1, v => Service with { AlightOverS = v }, "Change getting-off time", "Getting off takes at least 1 s.");
    }

    /// <summary>People getting off each train.</summary>
    public string AlightText
    {
        get => Service.AlightPerTrain.ToString(CultureInfo.InvariantCulture);
        set => SetCount(value, v => Service with { AlightPerTrain = v }, "Change people getting off");
    }

    /// <summary>Boarding places per train.</summary>
    public string CapacityText
    {
        get => Service.Capacity.ToString(CultureInfo.InvariantCulture);
        set => SetCount(value, v => Service with { Capacity = v }, "Change service places");
    }

    /// <summary>Re-reads everything.</summary>
    public void Refresh() => OnPropertyChanged(string.Empty);

    private void SetDoors()
    {
        List<Guid> doors = _owner.SelectedDoorZones();
        if (doors.Count == 0)
        {
            _owner.RejectTrainEdit("Select the door zones (sink or spawn/sink) in the plan first, then SET DOORS.");
            return;
        }

        Replace(Service with { DoorZoneIds = doors }, "Set service doors");
    }

    private void Replace(TrainService value, string description) => _owner.ReplaceTrainService(_index, value, description);

    private static string Capital(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private void SetSeconds(string? text, double min, Func<double, TrainService> with, string description, string error)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value) || value < min)
        {
            _owner.RejectTrainEdit(error);
            OnPropertyChanged(string.Empty);
            return;
        }

        Replace(with(value), description);
    }

    private void SetCount(string? text, Func<int, TrainService> with, string description)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value < 0)
        {
            _owner.RejectTrainEdit("Enter a whole number, 0 or more.");
            OnPropertyChanged(string.Empty);
            return;
        }

        Replace(with(value), description);
    }

    private static string Format(double seconds) => seconds.ToString("0.#", CultureInfo.InvariantCulture);
}
