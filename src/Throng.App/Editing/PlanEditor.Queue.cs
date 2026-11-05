using System.Globalization;
using Throng.App.Services;
using Throng.Core.Editing;
using Throng.Core.Geometry;
using Throng.Core.Model;
using Throng.Core.World;

namespace Throng.App.Editing;

/// <summary>
/// Round 009b tools of the plan editor: DRAW QUEUE (D-153, D-163) draws a device's queue path as a polyline from the
/// middle of its service side, and the selected device shows its queue slots as faint dots (D-164).
/// </summary>
public sealed partial class PlanEditor
{
    private Guid? _queueDeviceId;
    private int _revision;
    private (Guid Id, int Revision, IReadOnlyList<Point2> Dots)? _queueDots;
    private (Guid LevelId, int Revision, Func<Point2, bool> Free)? _queueFloor;

    /// <summary>Device whose queue path DRAW QUEUE is drawing, or null.</summary>
    public Device? QueuePathDevice =>
        _tool == EditorTool.QueuePath && _queueDeviceId is { } id ? _host.Study.Devices.FirstOrDefault(d => d.Id == id) : null;

    /// <summary>
    /// Queue slots of the single selected device (or the device whose queue is being drawn), for the faint dots
    /// (D-164): automatic lines per lane and side, or the drawn snake plus each lane's service point. Laid out
    /// against the level's walkable area and device footprints, so they match the run closely (the run uses the
    /// floor grid). Empty when no single device is selected.
    /// </summary>
    public IReadOnlyList<Point2> QueueSlotDots
    {
        get
        {
            Device? device = QueuePathDevice ?? (_selection.Count == 1 ? SelectedDevices.FirstOrDefault() : null);
            if (device is null)
            {
                return [];
            }

            // While drawing, the dots follow the points placed so far, so only the floor test is cached then.
            bool drawing = QueuePathDevice is not null;
            if (!drawing && _queueDots is { } cached && cached.Id == device.Id && cached.Revision == _revision)
            {
                return cached.Dots;
            }

            IReadOnlyList<Point2> dots = ComputeQueueDots(device);
            if (!drawing)
            {
                _queueDots = (device.Id, _revision, dots);
            }

            return dots;
        }
    }

    /// <summary>
    /// Starts DRAW QUEUE on a device (from the SELECTION panel): the path starts at the middle of the service side;
    /// click points outwards, Enter or double-click finishes, Esc cancels (D-153).
    /// </summary>
    public void StartQueuePath(Guid deviceId)
    {
        if (!CheckEditable() || _host.Study.Devices.FirstOrDefault(d => d.Id == deviceId) is not { } device)
        {
            return;
        }

        if (_host.ActiveLevelModel?.Id != device.LevelId)
        {
            RaiseFlash("SWITCH TO THE DEVICE'S LEVEL TO DRAW ITS QUEUE");
            return;
        }

        Tool = EditorTool.QueuePath;
        _queueDeviceId = deviceId;
        _points.Add(QueueLines.QueueStart(device));
        SetSelection([deviceId]);
        _host.ShowMessage($"DRAW QUEUE ▸ {device.Name}: click points out from the service side, Enter or double-click to finish, Esc to cancel. One snake feeds every lane.");
        RaiseOverlay();
    }

    /// <summary>Removes a device's drawn queue path, back to the automatic lines (undoable).</summary>
    public void ClearQueuePath(Guid deviceId)
    {
        if (!CheckEditable() || _host.Study.Devices.FirstOrDefault(d => d.Id == deviceId) is not { } device)
        {
            return;
        }

        if (device.QueuePath is not { Count: > 0 })
        {
            _host.ShowMessage($"{device.Name} already uses the automatic queue lines.");
            return;
        }

        Device next = device.Clone();
        next.QueuePath = [];
        _host.Execute(StateActions.Devices($"Clear queue of {device.Name}", [(device, next)], _host.NotifyModelChanged));
    }

    /// <summary>A click while drawing a queue path: adds a point; a double-click finishes.</summary>
    private void QueuePathDown(Point2 raw, bool shift, int clickCount)
    {
        if (QueuePathDevice is not { } device)
        {
            RaiseFlash("SELECT A DEVICE AND PRESS DRAW QUEUE");
            Tool = EditorTool.Select;
            return;
        }

        if (_points.Count == 0)
        {
            // Backspace took the start away: it always begins at the service side.
            _points.Add(QueueLines.QueueStart(device));
        }

        if (clickCount >= 2)
        {
            // The first click of the double-click already placed the point.
            FinishQueuePath();
            return;
        }

        AddDrawPoint(SnapDrawPoint(raw, shift).Point);
    }

    /// <summary>Stores the drawn path on the device (undoable) and returns to Select. Needs at least one point after the start.</summary>
    private bool FinishQueuePath()
    {
        if (QueuePathDevice is not { } device)
        {
            Tool = EditorTool.Select;
            return false;
        }

        if (_points.Count < 2)
        {
            RaiseFlash("CLICK AT LEAST ONE POINT OUT FROM THE DEVICE");
            return false;
        }

        Device next = device.Clone();
        next.QueuePath = [.. _points];
        double length = 0;
        for (int k = 1; k < _points.Count; k++)
        {
            length += Point2.Distance(_points[k - 1], _points[k]);
        }

        string description = string.Create(CultureInfo.InvariantCulture, $"Draw queue for {device.Name} ({length:0.0} m)");
        _host.Execute(StateActions.Devices(description, [(device, next)], _host.NotifyModelChanged));
        _queueDeviceId = null;
        Tool = EditorTool.Select;
        SetSelection([device.Id]);
        return true;
    }

    /// <summary>Queue slots of a device laid out against its level's walkable area minus device footprints.</summary>
    private IReadOnlyList<Point2> ComputeQueueDots(Device device)
    {
        if (FloorTest(device.LevelId) is not { } free)
        {
            return [];
        }

        // While drawing, preview the path drawn so far.
        Device shown = device;
        if (QueuePathDevice is not null && _points.Count >= 2)
        {
            shown = device.Clone();
            shown.QueuePath = [.. _points];
        }

        QueueLineSet set = QueueLines.Build(shown, free);
        var dots = new List<Point2>();
        foreach (Point2[] line in set.Lines)
        {
            dots.AddRange(line);
        }

        if (set.Snake is { } snake)
        {
            dots.AddRange(snake);
        }

        return dots;
    }

    /// <summary>
    /// "Can a person stand here" for a level: inside its net walkable area and outside every device's blocking
    /// footprint (as the run's floor grid sees it). Cached until the next model change; null when the walkable area
    /// can't be computed (logged).
    /// </summary>
    private Func<Point2, bool>? FloorTest(Guid levelId)
    {
        if (_queueFloor is { } cached && cached.LevelId == levelId && cached.Revision == _revision)
        {
            return cached.Free;
        }

        Study study = _host.Study;
        var walkable = WalkableArea.Compute(study, levelId, ClipperPolygonOps.Instance);
        if (!walkable.IsSuccess || walkable.Value is not { } polygons)
        {
            AppLog.Warn(walkable.Error ?? "Walkable area failed for the queue preview.");
            return null;
        }

        List<IReadOnlyList<Point2>> blocking = [.. study.DevicesOn(levelId).SelectMany(d => DeviceGeometry.Blocking(d))];
        bool Free(Point2 p)
        {
            if (!WalkableArea.Contains(polygons, p))
            {
                return false;
            }

            foreach (IReadOnlyList<Point2> block in blocking)
            {
                if (block.Count >= 3 && PolygonMath.Contains(block, p))
                {
                    return false;
                }
            }

            return true;
        }

        Func<Point2, bool> test = Free;
        _queueFloor = (levelId, _revision, test);
        return test;
    }
}
