using Throng.Core.Editing;
using Throng.Core.Geometry;
using Throng.Core.Model;

namespace Throng.App.Editing;

/// <summary>Plan editing tools (M1, plan view only per D-026).</summary>
public enum EditorTool
{
    /// <summary>Pick, box-select, move and reshape regions.</summary>
    Select,

    /// <summary>Two-corner axis-aligned rectangle.</summary>
    Rectangle,

    /// <summary>Click-by-click closed polygon.</summary>
    Polygon,

    /// <summary>Centre + radius circle, stored as a polygon (D-036).</summary>
    Circle,

    /// <summary>Open polyline with thickness.</summary>
    Wall,

    /// <summary>Place a stair, escalator or ramp: click the bottom landing, aim, click (D-054).</summary>
    Connect,

    /// <summary>Draw a shaft spanning levels as a rectangle (D-056).</summary>
    Shaft,

    /// <summary>Place a device or a travelator from the library: click, aim, click (D-090).</summary>
    Place,

    /// <summary>Set an underlay's scale: click two points on the image, type the real distance (D-084).</summary>
    Calibrate,

    /// <summary>Draw a device's queue path from its service side, click by click (DRAW QUEUE, D-153, D-163).</summary>
    QueuePath,
}

/// <summary>
/// A closed or open outline that vertex grips and edge push/pull can reshape: a region or a shaft.
/// </summary>
/// <param name="Id">Region or shaft id.</param>
/// <param name="Outline">Current outline.</param>
/// <param name="Closed">False for walls.</param>
/// <param name="IsShaft">True for shafts.</param>
public sealed record OutlineTarget(Guid Id, IReadOnlyList<Point2> Outline, bool Closed, bool IsShaft);

/// <summary>A connector being previewed while placing (D-054).</summary>
/// <param name="Connector">The unplaced connector.</param>
/// <param name="Layout">Its layout.</param>
public sealed record ConnectorPreview(Connector Connector, ConnectorLayout Layout);

/// <summary>What the editor needs from its owner. Implemented by the main view model.</summary>
public interface IEditorHost
{
    /// <summary>The open study.</summary>
    Study Study { get; }

    /// <summary>Level being edited, if any.</summary>
    Level? ActiveLevelModel { get; }

    /// <summary>Runs an action through the undo stack.</summary>
    void Execute(IUndoableAction action);

    /// <summary>Callback actions invoke after every do/undo so views refresh.</summary>
    void NotifyModelChanged();

    /// <summary>Shows a short status message in the ticker.</summary>
    void ShowMessage(string message);

    /// <summary>Opens the Copy to Levels… dialog for the current selection (Ctrl+Shift+V, D-060).</summary>
    void RequestCopyToLevels();

    /// <summary>
    /// Extra hover text while a run is going (D-183), e.g. "~14 S" for a stair's current crowd wait; null when the
    /// object has none or nothing is running.
    /// </summary>
    string? RunHoverInfo(Guid id) => null;

    /// <summary>Trains in on the given level while running (D-190): their boxes and open doors; empty otherwise.</summary>
    IReadOnlyList<RunTrainView> RunTrains(Guid levelId) => [];

    /// <summary>Lift cars while running (round 013), as seen on the given level; empty otherwise.</summary>
    IReadOnlyList<RunLiftCarView> RunLiftCars(Guid levelId) => [];

    /// <summary>Drawn train shapes on the given level (D-249), shown dashed while not running; empty for none.</summary>
    IReadOnlyList<IReadOnlyList<Point2>> TrainShapes(Guid levelId) => [];

    /// <summary>Stores a finished DRAW TRAIN polygon on its train service (D-249, undoable).</summary>
    void SetTrainShape(Guid serviceId, IReadOnlyList<Point2> outline)
    {
    }

    /// <summary>
    /// Makes another level the active one (round 017, D-293: a click on its floor in 3D). True when it's now active.
    /// The editor is reset by the switch, so callers pick after it.
    /// </summary>
    bool ActivateLevel(Guid levelId) => false;
}

/// <summary>A train drawn in the plan while it's in (D-190).</summary>
/// <param name="Box">The train's outline along the platform edge.</param>
/// <param name="Doors">Door zone outlines (lit while the doors are open).</param>
/// <param name="Boarding">True once people are boarding.</param>
public sealed record RunTrainView(IReadOnlyList<Point2> Box, IReadOnlyList<IReadOnlyList<Point2>> Doors, bool Boarding);
