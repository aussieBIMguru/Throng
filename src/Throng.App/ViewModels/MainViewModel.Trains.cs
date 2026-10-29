using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Throng.Core.Geometry;
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

    /// <summary>Drawn train shapes on a level (D-249): services whose first door zone is on it.</summary>
    public IReadOnlyList<IReadOnlyList<Point2>> TrainShapes(Guid levelId)
    {
        List<IReadOnlyList<Point2>> shapes = [];
        foreach (TrainService service in _study.TrainServices)
        {
            if (service.Shape is { Count: >= 3 } shape && TrainLevelId(service) == levelId)
            {
                shapes.Add(shape);
            }
        }

        return shapes;
    }

    /// <summary>The level of a service's doors (its first door zone), or null with no doors.</summary>
    internal Guid? TrainLevelId(TrainService service) =>
        service.DoorZoneIds.Select(id => _study.FindRegion(id)).FirstOrDefault(r => r is not null)?.LevelId;

    /// <summary>Starts DRAW TRAIN for service <paramref name="index"/> on its doors' level (D-249).</summary>
    internal void StartTrainShape(int index)
    {
        if (!CanEditTrains() || index < 0 || index >= _study.TrainServices.Count)
        {
            return;
        }

        TrainService service = _study.TrainServices[index];
        if (TrainLevelId(service) is not { } levelId)
        {
            Ticker = "Set the service's doors first: the train is drawn on their level.";
            return;
        }

        Editor.StartTrainShape(service.Id, levelId, service.Name);
    }

    /// <summary>Stores a finished DRAW TRAIN polygon (D-249).</summary>
    public void SetTrainShape(Guid serviceId, IReadOnlyList<Point2> outline)
    {
        int index = _study.TrainServices.FindIndex(t => t.Id == serviceId);
        if (index < 0)
        {
            return;
        }

        TrainService service = _study.TrainServices[index];
        ReplaceTrainService(index, service with { Shape = [.. outline] }, $"Draw train shape for '{service.Name}'");
    }

    /// <summary>Removes service <paramref name="index"/>'s drawn shape, back to the automatic box (undoable).</summary>
    internal void ClearTrainShape(int index)
    {
        if (index < 0 || index >= _study.TrainServices.Count)
        {
            return;
        }

        TrainService service = _study.TrainServices[index];
        if (service.Shape.Count == 0)
        {
            Ticker = $"'{service.Name}' already uses the automatic train box.";
            return;
        }

        ReplaceTrainService(index, service with { Shape = [] }, $"Clear train shape for '{service.Name}'");
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
