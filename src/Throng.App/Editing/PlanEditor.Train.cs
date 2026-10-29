using Throng.Core.Common;
using Throng.Core.Geometry;

namespace Throng.App.Editing;

/// <summary>
/// DRAW TRAIN (round 013b, D-249): the polygon tool armed for one train service. The finished polygon becomes the
/// service's train shape instead of a new region; Esc or another tool cancels.
/// </summary>
public sealed partial class PlanEditor
{
    private Guid? _trainShapeServiceId;

    /// <summary>The train service the polygon tool is drawing a shape for, or null for ordinary drawing.</summary>
    public Guid? TrainShapeServiceId => _tool == EditorTool.Polygon ? _trainShapeServiceId : null;

    /// <summary>
    /// Arms the polygon tool for a service's train shape on <paramref name="levelId"/> (the doors' level), which must
    /// be the active level.
    /// </summary>
    public void StartTrainShape(Guid serviceId, Guid levelId, string serviceName)
    {
        if (!CheckEditable())
        {
            return;
        }

        if (_host.ActiveLevelModel?.Id != levelId)
        {
            RaiseFlash("SWITCH TO THE DOORS' LEVEL TO DRAW THE TRAIN");
            return;
        }

        Tool = EditorTool.Polygon;
        _points.Clear();
        _trainShapeServiceId = serviceId;
        _host.ShowMessage($"DRAW TRAIN ▸ {serviceName}: click the train's outline (beside the platform edge), close it or double-click to finish, Esc to cancel.");
        RaiseOverlay();
    }

    /// <summary>Validates the polygon (D-040) and hands it to the service; back to Select.</summary>
    private void CommitTrainShape(Guid serviceId)
    {
        Result<List<Point2>> valid = ShapeValidator.Validate([.. _points], closed: true);
        if (!valid.IsSuccess || valid.Value is null)
        {
            RaiseFlash(valid.Error ?? ShapeValidator.NoArea);
            return;
        }

        _host.SetTrainShape(serviceId, valid.Value);
        _points.Clear();
        _gesture = Gesture.None;
        _trainShapeServiceId = null;
        Tool = EditorTool.Select;
    }
}
