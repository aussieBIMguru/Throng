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
    }

    /// <summary>Deletes the service.</summary>
    public RelayCommand DeleteCommand { get; }

    /// <summary>Makes the selected sink zones the service's doors.</summary>
    public RelayCommand SetDoorsCommand { get; }

    /// <summary>The service (an empty one if the list changed under the row).</summary>
    public TrainService Service => _index < _owner.Study.TrainServices.Count ? _owner.Study.TrainServices[_index] : new TrainService();

    /// <summary>"SOUTHBOUND".</summary>
    public string Header => Service.Name.ToUpperInvariant();

    /// <summary>"every 6:00 from 0:30, doors 45 s, 150 off, 300 places".</summary>
    public string Summary => Service.Describe();

    /// <summary>"6 doors: Southbound door 1, …".</summary>
    public string DoorsText
    {
        get
        {
            List<string> names = [.. Service.DoorZoneIds.Select(id => _owner.Study.FindRegion(id)?.Name ?? "(deleted zone)")];
            return names.Count == 0 ? "No doors. Select door zones in the plan, then SET DOORS." : $"{names.Count} door{(names.Count == 1 ? string.Empty : "s")}: {string.Join(", ", names)}";
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
                _owner.RejectTrainEdit("A train service needs a name.");
                OnPropertyChanged();
                return;
            }

            Replace(Service with { Name = trimmed }, "Rename train service");
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
                _owner.RejectTrainEdit("A train service tag is one word, e.g. Southbound.");
                OnPropertyChanged();
                return;
            }

            Replace(Service with { Tag = trimmed }, "Change train service tag");
        }
    }

    /// <summary>Time between trains, seconds.</summary>
    public string HeadwayText
    {
        get => Format(Service.HeadwayS);
        set => SetSeconds(value, 20, v => Service with { HeadwayS = v }, "Change train headway", "The time between trains must be at least 20 s.");
    }

    /// <summary>First arrival, seconds after the run starts.</summary>
    public string OffsetText
    {
        get => Format(Service.OffsetS);
        set => SetSeconds(value, 0, v => Service with { OffsetS = v }, "Change first train time", "The first arrival can't be negative.");
    }

    /// <summary>Doors-open time, seconds.</summary>
    public string DwellText
    {
        get => Format(Service.DwellS);
        set => SetSeconds(value, 10, v => Service with { DwellS = v }, "Change train dwell", "Doors need to stay open at least 10 s.");
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
        set => SetCount(value, v => Service with { Capacity = v }, "Change train places");
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

        Replace(Service with { DoorZoneIds = doors }, "Set train service doors");
    }

    private void Replace(TrainService value, string description) => _owner.ReplaceTrainService(_index, value, description);

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
