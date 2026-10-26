using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Throng.Core.Model;

namespace Throng.App.ViewModels;

/// <summary>
/// STUDY ▸ TRAINS (round 010b, D-184, D-185): add a train service from the door zones selected in the plan, edit its
/// timetable, set its doors again from a new selection, or delete it. Services are immutable records, so every edit
/// replaces the record (or the list) in one undo step.
/// </summary>
public sealed partial class MainViewModel
{
    private RelayCommand? _addTrainServiceCommand;

    /// <summary>One row per train service, in study order.</summary>
    public ObservableCollection<TrainServiceViewModel> TrainServiceRows { get; } = [];

    /// <summary>True when the study has no train services (shows the hint).</summary>
    public bool HasNoTrainServices => _study.TrainServices.Count == 0;

    /// <summary>Adds a service whose doors are the selected sink zones.</summary>
    public RelayCommand AddTrainServiceCommand => _addTrainServiceCommand ??= new RelayCommand(AddTrainServiceFromSelection);

    /// <summary>The sink zones selected in the plan, in study order (candidate doors).</summary>
    internal List<Guid> SelectedDoorZones() => [.. Editor.SelectedRegions.Where(r => r.IsSink).Select(r => r.Id)];

    /// <summary>Replaces service <paramref name="index"/> (undoable).</summary>
    internal void ReplaceTrainService(int index, TrainService value, string description)
    {
        if (!CanEditTrains() || index < 0 || index >= _study.TrainServices.Count)
        {
            return;
        }

        Apply(description, () => _study.TrainServices[index], v => _study.TrainServices[index] = v, value);
    }

    /// <summary>Deletes service <paramref name="index"/> (undoable).</summary>
    internal void RemoveTrainService(int index)
    {
        if (!CanEditTrains() || index < 0 || index >= _study.TrainServices.Count)
        {
            return;
        }

        List<TrainService> next = [.. _study.TrainServices];
        string name = next[index].Name;
        next.RemoveAt(index);
        Apply($"Delete train service '{name}'", () => _study.TrainServices, v => _study.TrainServices = v, next);
    }

    /// <summary>Rejects an edit with a ticker message.</summary>
    internal void RejectTrainEdit(string message) => Ticker = message;

    private void AddTrainServiceFromSelection()
    {
        if (!CanEditTrains())
        {
            return;
        }

        List<Guid> doors = SelectedDoorZones();
        if (doors.Count == 0)
        {
            Ticker = "Select the door zones (sink or spawn/sink) in the plan first, then ADD SERVICE.";
            return;
        }

        int number = _study.TrainServices.Count + 1;
        var service = new TrainService { Name = $"Service {number}", DoorZoneIds = doors };
        List<TrainService> next = [.. _study.TrainServices, service];
        Apply($"Add train service '{service.Name}'", () => _study.TrainServices, v => _study.TrainServices = v, next);
    }

    private bool CanEditTrains()
    {
        if (_isRunActive)
        {
            Ticker = "RUNNING ▸ RESET TO EDIT";
            return false;
        }

        return true;
    }

    /// <summary>Syncs the rows with the study: rebuilt when the count changes, refreshed in place otherwise (keeps focus).</summary>
    private void RefreshTrains()
    {
        if (TrainServiceRows.Count != _study.TrainServices.Count)
        {
            TrainServiceRows.Clear();
            for (int k = 0; k < _study.TrainServices.Count; k++)
            {
                TrainServiceRows.Add(new TrainServiceViewModel(this, k));
            }
        }
        else
        {
            foreach (TrainServiceViewModel row in TrainServiceRows)
            {
                row.Refresh();
            }
        }

        OnPropertyChanged(nameof(HasNoTrainServices));
    }
}
