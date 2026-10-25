using System.Globalization;
using System.Windows.Input;
using Throng.Core.Common;
using Throng.Core.Editing;
using Throng.Core.Geometry;
using Throng.Core.Model;

namespace Throng.App.Editing;

/// <summary>
/// Plan-view editing state machine: sketch tools, snapping, selection, move, vertex grips, clipboard and typed
/// dimensions. UI-light: it receives plan-space pointer events from the viewport and commits every model change
/// as an undoable action through <see cref="IEditorHost"/>. The overlay reads its public state to draw previews.
/// Connector and shaft tools, the rotate grip and Copy to Levels live in <c>PlanEditor.Vertical.cs</c>.
/// </summary>
/// <remarks>
/// The selection holds ids of regions, connectors and shafts (ids are unique across all three). Screen tolerances
/// are given in device-independent pixels and converted with the caller's metres-per-DIP, so snapping and picking
/// feel the same at every zoom.
/// </remarks>
public sealed partial class PlanEditor
{
    /// <summary>Snap capture radius in DIPs.</summary>
    public const double SnapTolerancePx = 10;

    /// <summary>Pick tolerance for edges in DIPs.</summary>
    public const double PickTolerancePx = 6;

    /// <summary>Grip capture radius in DIPs.</summary>
    public const double GripTolerancePx = 7;

    /// <summary>Movement before a press becomes a drag, in DIPs.</summary>
    public const double DragThresholdPx = 4;

    /// <summary>Distance of an edge's push/pull arrow handle outside its midpoint, in DIPs (D-047).</summary>
    public const double EdgeHandleOffsetPx = 16;

    private readonly IEditorHost _host;
    private readonly List<Point2> _points = [];
    private readonly HashSet<Guid> _selection = [];
    private readonly HashSet<Guid> _hidden = [];

    private EditorTool _tool = EditorTool.Select;
    private RegionKind _drawKind = RegionKind.Walkable;
    private SnapResult _snap;
    private bool _hasCursor;
    private double _metresPerDip = 0.08;
    private Guid? _hover;
    private int? _selectedVertex;

    // Pointer gesture state (select tool, and drag-to-draw for rectangles/circles/shafts).
    private Gesture _gesture = Gesture.None;
    private Point2 _pressPoint;
    private ModifierKeys _pressModifiers;
    private Guid? _pressId;
    private Point2 _dragPoint;
    private Point2 _moveAnchor;
    private Point2 _moveDelta;
    private OutlineTarget? _vertexTarget;
    private List<Point2>? _vertexPreview;
    private int _vertexIndex;
    private bool _vertexInserted;
    private bool _vertexMoved;
    private int _edgeIndex;
    private Point2 _edgeNormal;
    private Point2 _edgeMid;
    private double _pushDistance;

    /// <summary>Creates the editor for a host.</summary>
    public PlanEditor(IEditorHost host) => _host = host;

    /// <summary>Raised whenever anything the overlay draws has changed.</summary>
    public event EventHandler? OverlayChanged;

    /// <summary>Raised when the selection changes.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>Raised when the active tool or draw kind changes.</summary>
    public event EventHandler? ToolChanged;

    /// <summary>Raised with a short HUD message (rejections such as "EDGES CROSS").</summary>
    public event EventHandler<string>? Flash;

    /// <summary>Raised when temporarily hidden objects change (D-046); the scene must be rebuilt.</summary>
    public event EventHandler? ViewFilterChanged;

    private enum Gesture
    {
        None,
        Pending,
        RubberBand,
        Move,
        Vertex,
        EdgePush,
        DrawDrag,
        Rotate,
    }

    private enum GripKind
    {
        Vertex,
        Midpoint,
        Edge,
    }

    /// <summary>Snap settings (grid step and toggles).</summary>
    public SnapSettings Snap { get; } = new();

    /// <summary>Current tool. Changing it abandons any shape in progress.</summary>
    public EditorTool Tool
    {
        get => _tool;
        set
        {
            if (_tool == value)
            {
                return;
            }

            CancelGesture();
            _points.Clear();
            ClearConnectPreview();
            ClearPlacePreview();
            _tool = value;
            ToolChanged?.Invoke(this, EventArgs.Empty);
            RaiseOverlay();
        }
    }

    /// <summary>Kind given to new rectangles, polygons and circles (walls are always walls).</summary>
    public RegionKind DrawKind
    {
        get => _drawKind;
        set
        {
            RegionKind kind = value == RegionKind.Wall ? RegionKind.Walkable : value;
            if (_drawKind != kind)
            {
                _drawKind = kind;
                ToolChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>
    /// True while a simulation run is active (D-097): every edit is refused with "RUNNING ▸ RESET TO EDIT" until
    /// the run is reset.
    /// </summary>
    public bool IsLocked { get; set; }

    /// <summary>True when the active level exists and isn't locked (D-033), and no run is active (D-097).</summary>
    public bool CanEdit => !IsLocked && _host.ActiveLevelModel is { IsLocked: false };

    /// <summary>True while a drawing tool is active, so typed dimensions are accepted (D-038).</summary>
    public bool AcceptsTypedInput => _tool != EditorTool.Select && CanEdit;

    /// <summary>Points placed so far for the shape being drawn (the bottom landing for CONNECT).</summary>
    public IReadOnlyList<Point2> DrawPoints => _points;

    /// <summary>Snapped cursor position (valid when <see cref="HasCursor"/>).</summary>
    public SnapResult CursorSnap => _snap;

    /// <summary>True while the cursor is over the viewport in plan view.</summary>
    public bool HasCursor => _hasCursor;

    /// <summary>Metres per DIP from the last pointer event (for overlay sizes).</summary>
    public double MetresPerDip => _metresPerDip;

    /// <summary>Outline under the cursor in the select tool, for hover highlight.</summary>
    public (IReadOnlyList<Point2> Outline, bool Closed)? Hover =>
        _gesture == Gesture.None && _tool == EditorTool.Select && _hover is { } id && !_selection.Contains(id) ? OutlineOf(id) : null;

    /// <summary>Selected vertex index on the single grip target, if any.</summary>
    public int? SelectedVertex => _selectedVertex;

    /// <summary>Selected regions in study order.</summary>
    public IReadOnlyList<Region> SelectedRegions => [.. _host.Study.Regions.Where(r => _selection.Contains(r.Id))];

    /// <summary>Selected connectors in study order.</summary>
    public IReadOnlyList<Connector> SelectedConnectors => [.. _host.Study.Connectors.Where(c => _selection.Contains(c.Id))];

    /// <summary>Selected shafts in study order.</summary>
    public IReadOnlyList<Shaft> SelectedShafts => [.. _host.Study.Shafts.Where(s => _selection.Contains(s.Id))];

    /// <summary>Selected devices in study order.</summary>
    public IReadOnlyList<Device> SelectedDevices => [.. _host.Study.Devices.Where(d => _selection.Contains(d.Id))];

    /// <summary>Selected underlays in study order.</summary>
    public IReadOnlyList<Underlay> SelectedUnderlays => [.. _host.Study.Underlays.Where(u => _selection.Contains(u.Id))];

    /// <summary>Number of selected objects.</summary>
    public int SelectionCount => _selection.Count;

    /// <summary>Outlines of everything selected (regions, connector footprints, shafts) for the overlay.</summary>
    public IReadOnlyList<(Guid Id, IReadOnlyList<Point2> Outline, bool Closed)> SelectedOutlines
    {
        get
        {
            var result = new List<(Guid, IReadOnlyList<Point2>, bool)>();
            foreach (Guid id in _selection)
            {
                if (OutlineOf(id) is { } o)
                {
                    result.Add((id, o.Outline, o.Closed));
                }
            }

            return result;
        }
    }

    /// <summary>The single selected region or shaft that shows vertex grips, or null.</summary>
    public OutlineTarget? GripTarget
    {
        get
        {
            if (!CanEdit || _selection.Count != 1)
            {
                return null;
            }

            Guid id = _selection.First();
            if (_host.Study.Regions.FirstOrDefault(r => r.Id == id) is { } region)
            {
                return new OutlineTarget(region.Id, region.Outline, RegionPicking.IsClosed(region), IsShaft: false);
            }

            return _host.Study.FindShaft(id) is { } shaft ? new OutlineTarget(shaft.Id, shaft.Outline, Closed: true, IsShaft: true) : null;
        }
    }

    /// <summary>Box selection in progress: the box and whether it's a crossing (right-to-left) box.</summary>
    public (Bounds2 Box, bool Crossing)? RubberBand =>
        _gesture == Gesture.RubberBand ? (Bounds2.FromCorners(_pressPoint, _dragPoint), _dragPoint.X < _pressPoint.X) : null;

    /// <summary>Offset of the selection being dragged, or null when not moving.</summary>
    public Point2? MoveDelta => _gesture == Gesture.Move ? _moveDelta : null;

    /// <summary>Outline being reshaped by a vertex grip or an edge push/pull, or null.</summary>
    public (Guid Id, IReadOnlyList<Point2> Outline)? VertexPreview =>
        _gesture is Gesture.Vertex or Gesture.EdgePush && _vertexTarget is not null && _vertexPreview is not null
            ? (_vertexTarget.Id, _vertexPreview)
            : null;

    /// <summary>Signed push/pull distance while dragging an edge handle (positive = outward), or null.</summary>
    public double? PushDistance => _gesture == Gesture.EdgePush ? _pushDistance : null;

    /// <summary>Objects temporarily hidden from the view (D-046). Not saved; no effect on the model.</summary>
    public IReadOnlySet<Guid> HiddenIds => _hidden;

    /// <summary>Number of temporarily hidden objects.</summary>
    public int HiddenCount => _hidden.Count;

    /// <summary>Plan position of an edge's push/pull arrow handle at the given zoom.</summary>
    public static Point2 EdgeHandlePosition(IReadOnlyList<Point2> outline, bool closed, int edgeIndex, double metresPerDip)
    {
        Point2 mid = (outline[edgeIndex] + outline[(edgeIndex + 1) % outline.Count]) * 0.5;
        return mid + (EdgePushPull.Normal(outline, closed, edgeIndex) * (EdgeHandleOffsetPx * metresPerDip));
    }

    // ------------------------------------------------------------------ pointer input

    /// <summary>Cursor moved (with or without the left button held).</summary>
    /// <param name="raw">Cursor in plan metres.</param>
    /// <param name="metresPerDip">Current zoom.</param>
    /// <param name="shift">Shift held (forces ortho while drawing; free angle while aiming or rotating).</param>
    public void PointerMove(Point2 raw, double metresPerDip, bool shift)
    {
        _hasCursor = true;
        _metresPerDip = metresPerDip;

        switch (_gesture)
        {
            case Gesture.Pending:
                if (Point2.Distance(raw, _pressPoint) / metresPerDip > DragThresholdPx)
                {
                    BeginDrag();
                }

                break;
            case Gesture.RubberBand:
                _dragPoint = raw;
                break;
            case Gesture.Move:
                UpdateMove(raw, shift);
                break;
            case Gesture.Vertex:
                UpdateVertex(raw, shift);
                break;
            case Gesture.EdgePush:
                UpdateEdgePush(raw);
                break;
            case Gesture.Rotate:
                UpdateRotate(raw, shift);
                break;
        }

        if (_tool == EditorTool.Select)
        {
            _snap = new SnapResult(raw, SnapKind.None);
            _hover = _gesture == Gesture.None && CanEdit ? PickAt(raw) : null;
        }
        else if (_tool == EditorTool.Connect && _points.Count == 1)
        {
            _snap = new SnapResult(raw, SnapKind.None);
            UpdateConnectAim(raw, shift);
        }
        else if (_tool == EditorTool.Calibrate)
        {
            // Calibration points go exactly where you click on the image (no snapping).
            _snap = new SnapResult(raw, SnapKind.None);
        }
        else if (_tool == EditorTool.Place)
        {
            // Devices aim freely after the first click; travelators snap their far end like a drawn segment.
            _snap = _points.Count == 1 && !IsPlacingTravelator ? new SnapResult(raw, SnapKind.None) : SnapDrawPoint(raw, shift);
            UpdatePlaceAim(_snap.Point, shift);
        }
        else
        {
            _snap = SnapDrawPoint(raw, shift);
            if (_tool == EditorTool.Connect)
            {
                UpdateConnectAim(_snap.Point, shift);
            }
        }

        RaiseOverlay();
    }

    /// <summary>Cursor left the viewport.</summary>
    public void PointerLeave()
    {
        _hasCursor = false;
        _hover = null;
        RaiseOverlay();
    }

    /// <summary>Left button pressed.</summary>
    public void PointerDown(Point2 raw, double metresPerDip, ModifierKeys modifiers, int clickCount)
    {
        _metresPerDip = metresPerDip;
        if (!CheckEditable())
        {
            return;
        }

        if (_tool == EditorTool.Select)
        {
            SelectDown(raw, modifiers);
        }
        else
        {
            DrawDown(raw, modifiers, clickCount);
        }

        RaiseOverlay();
    }

    /// <summary>Left button released.</summary>
    public void PointerUp(Point2 raw, double metresPerDip, ModifierKeys modifiers)
    {
        _metresPerDip = metresPerDip;
        Gesture gesture = _gesture;
        _gesture = Gesture.None;

        switch (gesture)
        {
            case Gesture.Pending:
                ApplyClick(_pressId, _pressModifiers);
                break;
            case Gesture.RubberBand:
                ApplyBox(Bounds2.FromCorners(_pressPoint, raw), raw.X < _pressPoint.X, _pressModifiers);
                break;
            case Gesture.Move:
                CommitMove();
                break;
            case Gesture.Vertex:
                CommitVertex();
                break;
            case Gesture.EdgePush:
                CommitEdgePush();
                break;
            case Gesture.Rotate:
                CommitRotate();
                break;
            case Gesture.DrawDrag:
                // Press-drag-release draws a rectangle, circle or shaft in one stroke.
                if (_points.Count == 1 && Point2.Distance(raw, _pressPoint) / metresPerDip > DragThresholdPx)
                {
                    FinishTwoPointShape(SnapDrawPoint(raw, (modifiers & ModifierKeys.Shift) != 0).Point);
                }

                break;
        }

        _pressId = null;
        _vertexTarget = null;
        _vertexPreview = null;
        RaiseOverlay();
    }

    // ------------------------------------------------------------------ keyboard and commands

    /// <summary>Handles a key pressed while the viewport has focus. Returns true when used.</summary>
    public bool HandleKey(Key key, ModifierKeys modifiers)
    {
        bool ctrl = (modifiers & ModifierKeys.Control) != 0;
        bool shift = (modifiers & ModifierKeys.Shift) != 0;
        if (ctrl && shift)
        {
            if (key == Key.V)
            {
                _host.RequestCopyToLevels();
                return true;
            }

            return false;
        }

        if (ctrl)
        {
            switch (key)
            {
                case Key.A: SelectAll(); return true;
                case Key.C: Copy(); return true;
                case Key.X: Cut(); return true;
                case Key.V: Paste(); return true;
                case Key.D: Duplicate(); return true;
                default: return false;
            }
        }

        if ((modifiers & ModifierKeys.Alt) != 0)
        {
            return false;
        }

        switch (key)
        {
            case Key.Escape:
                Escape();
                return true;
            case Key.Enter:
                if (_tool is EditorTool.Polygon or EditorTool.Wall)
                {
                    FinishPolyShape();
                    return true;
                }

                return false;
            case Key.Back:
                if (_points.Count > 0)
                {
                    _points.RemoveAt(_points.Count - 1);
                    RaiseOverlay();
                    return true;
                }

                return false;
            case Key.Tab:
                return FlipSide();
            case Key.Delete:
                Delete();
                return true;
            case Key.V or Key.S:
                Tool = EditorTool.Select;
                return true;
            case Key.R:
                Tool = EditorTool.Rectangle;
                return true;
            case Key.P:
                Tool = EditorTool.Polygon;
                return true;
            case Key.C:
                Tool = EditorTool.Circle;
                return true;
            case Key.W:
                Tool = EditorTool.Wall;
                return true;
            case Key.T:
                Tool = EditorTool.Connect;
                return true;
            case Key.O:
                Tool = EditorTool.Shaft;
                return true;
            case Key.D:
                Tool = EditorTool.Place;
                return true;
            case Key.H:
                HideSelection();
                return true;
            case Key.I:
                IsolateSelection();
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Esc: abandon the current drag, then the shape in progress, then the drawing tool, then the selection, then
    /// any temporary hide/isolate.
    /// </summary>
    public void Escape()
    {
        if (_gesture != Gesture.None)
        {
            CancelGesture();
        }
        else if (_points.Count > 0)
        {
            _points.Clear();
        }
        else if (_tool != EditorTool.Select)
        {
            Tool = EditorTool.Select;
        }
        else if (_selection.Count > 0)
        {
            SetSelection([]);
        }
        else
        {
            ResetView();
        }

        RaiseOverlay();
    }

    /// <summary>Temporarily hides the selection (H, D-046).</summary>
    public void HideSelection()
    {
        if (_selection.Count == 0)
        {
            RaiseFlash("SELECT SOMETHING TO HIDE");
            return;
        }

        int count = _selection.Count;
        _hidden.UnionWith(_selection);
        SetSelection([]);
        RaiseViewFilter($"{Plural(count, "object")} hidden ▸ Esc or RESET VIEW shows everything again.");
    }

    /// <summary>Temporarily hides everything except the selection (I, D-046).</summary>
    public void IsolateSelection()
    {
        if (_selection.Count == 0)
        {
            RaiseFlash("SELECT SOMETHING TO ISOLATE");
            return;
        }

        Study study = _host.Study;
        _hidden.Clear();
        _hidden.UnionWith(study.Regions.Select(r => r.Id)
            .Concat(study.Connectors.Select(c => c.Id))
            .Concat(study.Shafts.Select(s => s.Id))
            .Concat(study.Devices.Select(d => d.Id))
            .Concat(study.Underlays.Select(u => u.Id))
            .Where(id => !_selection.Contains(id)));
        RaiseViewFilter($"Isolated {Plural(_selection.Count, "object")} ▸ Esc or RESET VIEW shows everything again.");
    }

    /// <summary>Shows every temporarily hidden object again.</summary>
    /// <param name="quiet">True to skip the ticker message (level switch, new/open).</param>
    public void ResetView(bool quiet = false)
    {
        if (_hidden.Count == 0)
        {
            return;
        }

        _hidden.Clear();
        RaiseViewFilter(quiet ? null : "View reset ▸ everything visible.");
    }

    /// <summary>Abandons the current drag only (e.g. the viewport lost mouse capture); shapes in progress stay.</summary>
    public void CancelDrag()
    {
        if (_gesture != Gesture.None)
        {
            CancelGesture();
            RaiseOverlay();
        }
    }

    /// <summary>Abandons any drag and any shape in progress (view switch, level switch, new study).</summary>
    public void Reset()
    {
        CancelGesture();
        _points.Clear();
        _hover = null;
        ClearConnectPreview();
        ClearPlacePreview();
        RaiseOverlay();
    }

    /// <summary>
    /// Applies the typed-dimension field (D-038). Returns true when accepted; false keeps the field open after a
    /// HUD message.
    /// </summary>
    /// <remarks>
    /// First point of any shape: <c>x,y</c> absolute. Rectangle and shaft: <c>w,h</c> (or one value for a square)
    /// towards the cursor. Circle: radius. Polygon/wall: a length along the cursor direction, or <c>dx,dy</c>
    /// relative. Connect: an angle in degrees, or <c>dx,dy</c> as a direction.
    /// </remarks>
    public bool ApplyTypedInput(string text)
    {
        if (!CheckEditable())
        {
            return false;
        }

        Result<double[]> parsed = TypedInput.Parse(text);
        if (!parsed.IsSuccess || parsed.Value is not { } v)
        {
            RaiseFlash(parsed.Error ?? "TYPE A NUMBER");
            return false;
        }

        if (_points.Count == 0)
        {
            if (v.Length != 2)
            {
                RaiseFlash("TYPE X,Y FOR THE FIRST POINT");
                return false;
            }

            if ((_tool == EditorTool.Connect && !CheckConnectReady()) || (_tool == EditorTool.Place && !CheckPlaceReady()))
            {
                return false;
            }

            _points.Add(new Point2(v[0], v[1]));
            if (_tool == EditorTool.Connect)
            {
                UpdateConnectAim(_hasCursor ? _snap.Point : _points[0] + new Point2(1, 0), shift: false);
            }
            else if (_tool == EditorTool.Place)
            {
                UpdatePlaceAim(_hasCursor ? _snap.Point : _points[0] + new Point2(1, 0), shift: false);
            }

            RaiseOverlay();
            return true;
        }

        Point2 last = _points[^1];
        Point2 toward = _hasCursor ? _snap.Point - last : Point2.Zero;
        switch (_tool)
        {
            case EditorTool.Rectangle or EditorTool.Shaft:
            {
                double w = Math.Abs(v[0]);
                double h = Math.Abs(v.Length == 2 ? v[1] : v[0]);
                double sx = toward.X < 0 ? -1 : 1;
                double sy = toward.Y < 0 ? -1 : 1;
                return FinishTwoPointShape(last + new Point2(sx * w, sy * h));
            }

            case EditorTool.Circle:
                if (v.Length != 1 || v[0] <= 0)
                {
                    RaiseFlash("TYPE A RADIUS");
                    return false;
                }

                return FinishTwoPointShape(last + new Point2(v[0], 0));

            case EditorTool.Polygon or EditorTool.Wall:
            {
                Point2 next;
                if (v.Length == 2)
                {
                    next = last + new Point2(v[0], v[1]);
                }
                else
                {
                    Point2 direction = toward.Normalised();
                    next = last + ((direction == Point2.Zero ? new Point2(1, 0) : direction) * v[0]);
                }

                AddDrawPoint(next);
                RaiseOverlay();
                return true;
            }

            case EditorTool.Connect:
            {
                double angle = v.Length == 2 ? ConnectorFactory.AngleOf(new Point2(v[0], v[1])) : v[0];
                if (v.Length == 2 && Math.Abs(v[0]) + Math.Abs(v[1]) < 1e-9)
                {
                    RaiseFlash("TYPE AN ANGLE OR A DIRECTION");
                    return false;
                }

                return CommitConnector(ConnectorFactory.NormaliseAngle(angle));
            }

            case EditorTool.Place:
                return ApplyPlaceTyped(v);

            case EditorTool.Calibrate:
                return ApplyCalibrationDistance(v);

            default:
                return false;
        }
    }

    /// <summary>Selects everything on the active level: regions, connectors that touch it, shafts through it.</summary>
    public void SelectAll()
    {
        if (CheckEditable())
        {
            SetSelection(ActiveRegions().Select(r => r.Id)
                .Concat(ActiveConnectors().Select(c => c.Connector.Id))
                .Concat(ActiveShafts().Select(s => s.Id))
                .Concat(ActiveDevices().Select(d => d.Id)));
        }
    }

    /// <summary>Deletes the selected vertex, or the selection when no vertex is selected.</summary>
    public void Delete()
    {
        if (!CheckEditable())
        {
            return;
        }

        if (_selectedVertex is int index && GripTarget is { } target)
        {
            DeleteVertex(target, index);
            return;
        }

        if (_selection.Count > 0)
        {
            RemoveSelection("Delete");
        }
    }

    /// <summary>
    /// Re-checks the selection against the model (after undo/redo, level switch, lock). Drops objects that no
    /// longer exist or aren't on the editable active level.
    /// </summary>
    public void Revalidate()
    {
        Level? level = _host.ActiveLevelModel;
        int before = _selection.Count;
        if (level is null || level.IsLocked)
        {
            _selection.Clear();
        }
        else
        {
            Study study = _host.Study;
            var valid = study.RegionsPresentOn(level.Id).Select(r => r.Id)
                .Concat(study.ConnectorsTouching(level.Id).Select(c => c.Id))
                .Concat(study.ShaftsSpanning(level.Id).Select(s => s.Id))
                .Concat(study.DevicesOn(level.Id).Select(d => d.Id))
                .Concat(study.UnderlaysOn(level.Id).Where(u => !u.IsLocked).Select(u => u.Id))
                .ToHashSet();
            _selection.RemoveWhere(id => !valid.Contains(id));
        }

        if (_selectedVertex is int vertex && (GripTarget is not { } target || vertex >= target.Outline.Count))
        {
            _selectedVertex = null;
        }

        if (level is null || level.IsLocked)
        {
            _points.Clear();
            CancelGesture();
        }

        if (_selection.Count != before)
        {
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        RaiseOverlay();
    }

    // ------------------------------------------------------------------ select tool

    private void SelectDown(Point2 raw, ModifierKeys modifiers)
    {
        _pressPoint = raw;
        _dragPoint = raw;
        _pressModifiers = modifiers;

        if (modifiers == ModifierKeys.None && TryStartRotate(raw))
        {
            return;
        }

        if (modifiers == ModifierKeys.None && GripTarget is { } target && TryHitGrip(target, raw, out int index, out GripKind kind))
        {
            if (kind == GripKind.Edge)
            {
                StartEdgePush(target, index);
            }
            else
            {
                StartVertexDrag(target, index, kind == GripKind.Midpoint);
            }

            return;
        }

        _pressId = PickAt(raw);
        _gesture = Gesture.Pending;
    }

    private void BeginDrag()
    {
        bool plain = _pressModifiers == ModifierKeys.None;
        if (plain && _pressId is { } pressed)
        {
            if (!_selection.Contains(pressed))
            {
                SetSelection([pressed]);
            }

            // Grab the nearest vertex when close to one, so moves snap vertex-to-vertex.
            double tolerance = SnapTolerancePx * _metresPerDip;
            Point2 anchor = _pressPoint;
            double best = tolerance;
            foreach (Point2 p in SnapPointsOf(pressed))
            {
                double d = Point2.Distance(p, _pressPoint);
                if (d <= best)
                {
                    best = d;
                    anchor = p;
                }
            }

            _moveAnchor = anchor;
            _moveDelta = Point2.Zero;
            _gesture = Gesture.Move;
        }
        else
        {
            _gesture = Gesture.RubberBand;
        }
    }

    private void UpdateMove(Point2 raw, bool shift)
    {
        SnapGeometry geometry = BuildSnapGeometry(MovingIds());
        Point2 target = raw + (_moveAnchor - _pressPoint);
        SnapResult snap = Snapper.Snap(target, geometry, Snap, SnapTolerancePx * _metresPerDip, _moveAnchor, shift);
        _moveDelta = snap.Point - _moveAnchor;
    }

    /// <summary>The selection plus the partners of selected paired escalators (they move together, D-059).</summary>
    private HashSet<Guid> MovingIds()
    {
        var ids = new HashSet<Guid>(_selection);
        foreach (Connector connector in SelectedConnectors)
        {
            if (_host.Study.FindPartner(connector) is { } partner)
            {
                ids.Add(partner.Id);
            }
        }

        return ids;
    }

    private void CommitMove()
    {
        if (_moveDelta.Length < ShapeValidator.PointToleranceM)
        {
            return;
        }

        Point2 delta = _moveDelta;
        IReadOnlyList<Region> regions = SelectedRegions;
        List<Connector> connectors = WithPartners(SelectedConnectors);
        IReadOnlyList<Shaft> shafts = SelectedShafts;
        IReadOnlyList<Device> devices = SelectedDevices;
        IReadOnlyList<Underlay> underlays = SelectedUnderlays;

        var actions = new List<IUndoableAction>();
        if (regions.Count > 0)
        {
            actions.Add(new ReplaceOutlinesAction("Move", regions.Select(r => (r, (IReadOnlyList<Point2>)ShapeFactory.Translate(r.Outline, delta))), null));
        }

        if (devices.Count > 0)
        {
            actions.Add(StateActions.Devices("Move", devices.Select(d => (d, DeviceFactory.Translated(d, delta))), null));
        }

        if (underlays.Count > 0)
        {
            actions.Add(StateActions.Underlays("Move", underlays.Select(u => (u, Moved(u, delta))), null));
        }

        if (connectors.Count > 0)
        {
            actions.Add(StateActions.Connectors("Move", connectors.Select(c => (c, ConnectorFactory.Translated(c, delta))), null));
        }

        if (shafts.Count > 0)
        {
            actions.Add(StateActions.Shafts("Move", shafts.Select(s => (s, Reshaped(s, ShapeFactory.Translate(s.Outline, delta)))), null));
        }

        _host.Execute(new CompositeAction($"Move {Plural(_selection.Count, "object")}", actions, _host.NotifyModelChanged));
    }

    private void ApplyClick(Guid? hit, ModifierKeys modifiers)
    {
        _selectedVertex = null;
        if (hit is not { } id)
        {
            if (modifiers == ModifierKeys.None)
            {
                SetSelection([]);
            }

            return;
        }

        if ((modifiers & ModifierKeys.Control) != 0)
        {
            var next = new HashSet<Guid>(_selection);
            if (!next.Remove(id))
            {
                next.Add(id);
            }

            SetSelection(next);
        }
        else if ((modifiers & ModifierKeys.Shift) != 0)
        {
            SetSelection(_selection.Append(id));
        }
        else
        {
            SetSelection([id]);
        }
    }

    private void ApplyBox(Bounds2 box, bool crossing, ModifierKeys modifiers)
    {
        var hits = new List<Guid>();
        hits.AddRange(ActiveRegions()
            .Where(r => crossing ? RegionPicking.Touches(r, box) : RegionPicking.IsInside(r, box))
            .Select(r => r.Id));
        hits.AddRange(ActiveConnectors()
            .Where(c => crossing ? RegionPicking.Touches(c.Layout.Footprint, closed: true, box) : RegionPicking.IsInside(c.Layout.Footprint, box))
            .Select(c => c.Connector.Id));
        hits.AddRange(ActiveShafts()
            .Where(s => crossing ? RegionPicking.Touches(s.Outline, closed: true, box) : RegionPicking.IsInside(s.Outline, box))
            .Select(s => s.Id));
        hits.AddRange(ActiveDevices()
            .Where(d => crossing ? RegionPicking.Touches(DeviceGeometry.Footprint(d), closed: true, box) : RegionPicking.IsInside(DeviceGeometry.Footprint(d), box))
            .Select(d => d.Id));

        _selectedVertex = null;
        if ((modifiers & ModifierKeys.Control) != 0)
        {
            var next = new HashSet<Guid>(_selection);
            foreach (Guid id in hits)
            {
                if (!next.Remove(id))
                {
                    next.Add(id);
                }
            }

            SetSelection(next);
        }
        else if ((modifiers & ModifierKeys.Shift) != 0)
        {
            SetSelection(_selection.Concat(hits));
        }
        else
        {
            SetSelection(hits);
        }
    }

    // ------------------------------------------------------------------ vertex grips (D-037)

    /// <summary>Hit-tests the grips of the grip target: vertices, then edge arrows, then midpoints.</summary>
    private bool TryHitGrip(OutlineTarget target, Point2 raw, out int index, out GripKind kind)
    {
        double tolerance = GripTolerancePx * _metresPerDip;
        IReadOnlyList<Point2> outline = target.Outline;
        for (int i = 0; i < outline.Count; i++)
        {
            if (Point2.Distance(outline[i], raw) <= tolerance)
            {
                index = i;
                kind = GripKind.Vertex;
                return true;
            }
        }

        int edges = target.Closed ? outline.Count : outline.Count - 1;
        for (int i = 0; i < edges; i++)
        {
            if (Point2.Distance(EdgeHandlePosition(outline, target.Closed, i, _metresPerDip), raw) <= tolerance)
            {
                index = i;
                kind = GripKind.Edge;
                return true;
            }
        }

        for (int i = 0; i < edges; i++)
        {
            Point2 mid = (outline[i] + outline[(i + 1) % outline.Count]) * 0.5;
            if (Point2.Distance(mid, raw) <= tolerance)
            {
                index = i;
                kind = GripKind.Midpoint;
                return true;
            }
        }

        index = -1;
        kind = GripKind.Vertex;
        return false;
    }

    // ------------------------------------------------------------------ edge push/pull (D-047, D-048)

    private void StartEdgePush(OutlineTarget target, int edgeIndex)
    {
        _vertexTarget = target;
        _vertexPreview = [.. target.Outline];
        _edgeIndex = edgeIndex;
        _edgeNormal = EdgePushPull.Normal(target.Outline, target.Closed, edgeIndex);
        _edgeMid = (target.Outline[edgeIndex] + target.Outline[(edgeIndex + 1) % target.Outline.Count]) * 0.5;
        _pushDistance = 0;
        _vertexMoved = false;
        _vertexInserted = false;
        _selectedVertex = null;
        _gesture = Gesture.EdgePush;
    }

    /// <summary>
    /// Offset = cursor travel along the edge normal. A nearby endpoint or midpoint makes the edge line pass through
    /// it; otherwise the offset rounds to whole grid steps (when grid snap is on).
    /// </summary>
    private void UpdateEdgePush(Point2 raw)
    {
        if (_vertexTarget is not { } target)
        {
            return;
        }

        var pointSnaps = new SnapSettings { Grid = false, Ortho = false, Endpoint = Snap.Endpoint, Midpoint = Snap.Midpoint };
        SnapResult snap = Snapper.Snap(raw, BuildSnapGeometry([target.Id]), pointSnaps, SnapTolerancePx * _metresPerDip);

        double distance;
        if (snap.Kind is SnapKind.Endpoint or SnapKind.Midpoint)
        {
            distance = Point2.Dot(snap.Point - _edgeMid, _edgeNormal);
            _snap = snap;
        }
        else
        {
            distance = Point2.Dot(raw - _pressPoint, _edgeNormal);
            if (Snap.Grid && Snap.GridStepM > 0)
            {
                distance = Snapper.RoundToStep(distance, Snap.GridStepM);
            }

            _snap = new SnapResult(raw, SnapKind.None);
        }

        Result<List<Point2>> moved = EdgePushPull.Apply(target.Outline, target.Closed, _edgeIndex, distance);
        if (moved.IsSuccess && moved.Value is not null)
        {
            _vertexPreview = moved.Value;
            _pushDistance = distance;
            _vertexMoved = Math.Abs(distance) > ShapeValidator.PointToleranceM;
        }
    }

    private void CommitEdgePush()
    {
        if (_vertexTarget is not { } target || _vertexPreview is not { } outline || !_vertexMoved)
        {
            return;
        }

        Result<List<Point2>> valid = ShapeValidator.Validate(outline, target.Closed);
        if (!valid.IsSuccess || valid.Value is null)
        {
            RaiseFlash(valid.Error ?? ShapeValidator.NoArea);
            return;
        }

        CommitOutline(target, valid.Value, string.Create(CultureInfo.InvariantCulture, $"Push/pull edge {_pushDistance:+0.###;−0.###} m"));
    }

    private void StartVertexDrag(OutlineTarget target, int index, bool isMidpoint)
    {
        _vertexTarget = target;
        _vertexPreview = [.. target.Outline];
        if (isMidpoint)
        {
            Point2 a = target.Outline[index];
            Point2 b = target.Outline[(index + 1) % target.Outline.Count];
            _vertexIndex = index + 1;
            _vertexPreview.Insert(_vertexIndex, (a + b) * 0.5);
            _vertexInserted = true;
        }
        else
        {
            _vertexIndex = index;
            _vertexInserted = false;
        }

        _vertexMoved = false;
        _selectedVertex = _vertexIndex;
        _gesture = Gesture.Vertex;
    }

    private void UpdateVertex(Point2 raw, bool shift)
    {
        if (_vertexTarget is not { } target || _vertexPreview is null)
        {
            return;
        }

        // Snap to everything except the outline being edited, plus that outline's other vertices.
        SnapGeometry geometry = BuildSnapGeometry([target.Id]);
        for (int i = 0; i < _vertexPreview.Count; i++)
        {
            if (i != _vertexIndex)
            {
                geometry.AddPoint(_vertexPreview[i]);
            }
        }

        // Ortho reference: the previous vertex (or the next one for the first point of a wall).
        int count = _vertexPreview.Count;
        Point2? neighbour = null;
        if (target.Closed || _vertexIndex > 0)
        {
            neighbour = _vertexPreview[(_vertexIndex - 1 + count) % count];
        }
        else if (count > 1)
        {
            neighbour = _vertexPreview[1];
        }

        SnapResult snap = Snapper.Snap(raw, geometry, Snap, SnapTolerancePx * _metresPerDip, neighbour, shift);
        _vertexPreview[_vertexIndex] = snap.Point;
        _vertexMoved = true;
    }

    private void CommitVertex()
    {
        if (_vertexTarget is not { } target || _vertexPreview is not { } outline || (!_vertexMoved && !_vertexInserted))
        {
            return;
        }

        Result<List<Point2>> valid = ShapeValidator.Validate(outline, target.Closed);
        if (!valid.IsSuccess || valid.Value is null)
        {
            RaiseFlash(valid.Error ?? ShapeValidator.NoArea);
            _selectedVertex = null;
            return;
        }

        // Cleaning can drop vertices (e.g. a vertex dragged onto its neighbour), which shifts indices.
        if (valid.Value.Count != outline.Count)
        {
            _selectedVertex = null;
        }

        string description = _vertexInserted && !_vertexMoved ? "Insert vertex" : _vertexInserted ? "Insert and move vertex" : "Move vertex";
        CommitOutline(target, valid.Value, description);
    }

    private void DeleteVertex(OutlineTarget target, int index)
    {
        int minimum = target.Closed ? 3 : 2;
        if (target.Outline.Count <= minimum)
        {
            RaiseFlash(target.Closed ? ShapeValidator.NeedThreePoints : ShapeValidator.NeedTwoPoints);
            return;
        }

        List<Point2> outline = [.. target.Outline];
        outline.RemoveAt(index);
        Result<List<Point2>> valid = ShapeValidator.Validate(outline, target.Closed);
        if (!valid.IsSuccess || valid.Value is null)
        {
            RaiseFlash(valid.Error ?? ShapeValidator.NoArea);
            return;
        }

        _selectedVertex = null;
        CommitOutline(target, valid.Value, "Delete vertex");
    }

    /// <summary>Commits a new outline for a region or a shaft as one undoable step.</summary>
    private void CommitOutline(OutlineTarget target, IReadOnlyList<Point2> outline, string description)
    {
        if (target.IsShaft)
        {
            if (_host.Study.FindShaft(target.Id) is { } shaft)
            {
                _host.Execute(StateActions.Shafts(description, [(shaft, Reshaped(shaft, outline))], _host.NotifyModelChanged));
            }

            return;
        }

        if (_host.Study.Regions.FirstOrDefault(r => r.Id == target.Id) is { } region)
        {
            _host.Execute(new ReplaceOutlinesAction(description, [(region, outline)], _host.NotifyModelChanged));
        }
    }

    // ------------------------------------------------------------------ drawing tools

    private void DrawDown(Point2 raw, ModifierKeys modifiers, int clickCount)
    {
        bool shift = (modifiers & ModifierKeys.Shift) != 0;
        if (_tool == EditorTool.Connect)
        {
            ConnectDown(raw, shift);
            return;
        }

        if (_tool == EditorTool.Place)
        {
            PlaceDown(raw, shift);
            return;
        }

        if (_tool == EditorTool.Calibrate)
        {
            CalibrateDown(raw, shift);
            return;
        }

        Point2 p = SnapDrawPoint(raw, shift).Point;

        if (clickCount >= 2)
        {
            // The first click of the double-click already placed the point.
            if (_tool is EditorTool.Polygon or EditorTool.Wall)
            {
                FinishPolyShape();
            }

            return;
        }

        switch (_tool)
        {
            case EditorTool.Rectangle or EditorTool.Circle or EditorTool.Shaft:
                if (_points.Count == 0)
                {
                    _points.Add(p);
                    _pressPoint = raw;
                    _gesture = Gesture.DrawDrag;
                }
                else
                {
                    FinishTwoPointShape(p);
                }

                break;

            case EditorTool.Polygon:
                if (_points.Count >= 3 && Point2.Distance(p, _points[0]) <= SnapTolerancePx * _metresPerDip)
                {
                    FinishPolyShape();
                }
                else
                {
                    AddDrawPoint(p);
                }

                break;

            case EditorTool.Wall:
                AddDrawPoint(p);
                break;
        }
    }

    private void AddDrawPoint(Point2 p)
    {
        if (_points.Count == 0 || Point2.Distance(_points[^1], p) > ShapeValidator.PointToleranceM)
        {
            _points.Add(p);
        }
    }

    /// <summary>Completes a rectangle, circle or shaft whose first point is placed.</summary>
    private bool FinishTwoPointShape(Point2 second)
    {
        if (_points.Count != 1)
        {
            return false;
        }

        Point2 first = _points[0];
        if (_tool == EditorTool.Shaft)
        {
            return CommitShaft(ShapeFactory.Rectangle(first, second));
        }

        List<Point2> outline = _tool == EditorTool.Circle
            ? ShapeFactory.Circle(first, Point2.Distance(first, second))
            : ShapeFactory.Rectangle(first, second);
        return CommitShape(outline, closed: true, _drawKind);
    }

    /// <summary>Completes a polygon (closed) or wall (open).</summary>
    private void FinishPolyShape()
    {
        if (_tool == EditorTool.Wall)
        {
            CommitShape([.. _points], closed: false, RegionKind.Wall);
        }
        else if (_tool == EditorTool.Polygon)
        {
            CommitShape([.. _points], closed: true, _drawKind);
        }
    }

    /// <summary>
    /// Validates and adds a sketched shape (D-040). On rejection the points stay so drawing can continue.
    /// </summary>
    private bool CommitShape(IReadOnlyList<Point2> outline, bool closed, RegionKind kind)
    {
        if (_host.ActiveLevelModel is not { } level)
        {
            return false;
        }

        Result<List<Point2>> valid = ShapeValidator.Validate(outline, closed);
        if (!valid.IsSuccess || valid.Value is null)
        {
            RaiseFlash(valid.Error ?? ShapeValidator.NoArea);
            return false;
        }

        Region region = RegionFactory.Create(_host.Study, level.Id, kind, valid.Value);
        _host.Execute(new AddRegionsAction($"Draw {region.Name}", _host.Study, [region], _host.NotifyModelChanged));
        _points.Clear();
        _gesture = Gesture.None;
        SetSelection([region.Id]);
        RaiseOverlay();
        return true;
    }

    private SnapResult SnapDrawPoint(Point2 raw, bool shift)
    {
        SnapGeometry geometry = BuildSnapGeometry([]);
        foreach (Point2 p in _points)
        {
            geometry.AddPoint(p);
        }

        Point2? reference = _tool switch
        {
            EditorTool.Polygon or EditorTool.Wall when _points.Count > 0 => _points[^1],
            EditorTool.Circle when _points.Count > 0 => _points[0],
            _ => null,
        };

        return Snapper.Snap(raw, geometry, Snap, SnapTolerancePx * _metresPerDip, reference, shift);
    }

    // ------------------------------------------------------------------ picking and outlines

    /// <summary>Regions on (or spanning onto) the active level that aren't temporarily hidden (D-081).</summary>
    private IEnumerable<Region> ActiveRegions() =>
        _host.ActiveLevelModel is { } level ? _host.Study.RegionsPresentOn(level.Id).Where(r => !_hidden.Contains(r.Id)) : [];

    /// <summary>Devices on the active level that aren't temporarily hidden.</summary>
    private IEnumerable<Device> ActiveDevices() =>
        _host.ActiveLevelModel is { } level ? _host.Study.DevicesOn(level.Id).Where(d => !_hidden.Contains(d.Id)) : [];

    /// <summary>Visible, unlocked underlays on the active level (locked ones can't be picked, D-085).</summary>
    private IEnumerable<Underlay> ActiveUnderlays() =>
        _host.ActiveLevelModel is { } level
            ? _host.Study.UnderlaysOn(level.Id).Where(u => u.IsVisible && !u.IsLocked && u.PixelWidth > 0 && !_hidden.Contains(u.Id))
            : [];

    /// <summary>Connectors that start or end on the active level, with their layouts (failed layouts are skipped).</summary>
    private List<(Connector Connector, ConnectorLayout Layout)> ActiveConnectors()
    {
        var result = new List<(Connector, ConnectorLayout)>();
        if (_host.ActiveLevelModel is not { } level)
        {
            return result;
        }

        foreach (Connector connector in _host.Study.ConnectorsTouching(level.Id))
        {
            if (!_hidden.Contains(connector.Id) && ConnectorLayout.Compute(connector, _host.Study) is { IsSuccess: true, Value: { } layout })
            {
                result.Add((connector, layout));
            }
        }

        return result;
    }

    /// <summary>Shafts through the active level that aren't temporarily hidden.</summary>
    private IEnumerable<Shaft> ActiveShafts() =>
        _host.ActiveLevelModel is { } level ? _host.Study.ShaftsSpanning(level.Id).Where(s => !_hidden.Contains(s.Id)) : [];

    /// <summary>
    /// Picks the object under the cursor. Edges within tolerance beat interiors, and smaller shapes beat larger
    /// ones, so a column or a stair inside a floor is picked before the floor.
    /// </summary>
    private Guid? PickAt(Point2 raw)
    {
        double tolerance = PickTolerancePx * _metresPerDip;
        Guid? best = null;
        (int Rank, double Area) bestScore = (int.MaxValue, double.PositiveInfinity);

        void Consider(Guid id, (int Rank, double Area)? score)
        {
            if (score is { } s && (s.Rank < bestScore.Rank || (s.Rank == bestScore.Rank && s.Area < bestScore.Area)))
            {
                best = id;
                bestScore = s;
            }
        }

        if (RegionPicking.Pick(ActiveRegions(), raw, tolerance) is { } region)
        {
            bool closed = RegionPicking.IsClosed(region);
            Consider(region.Id, closed ? RegionPicking.Score(region.Outline, raw, tolerance) ?? (0, 0) : (0, 0));
        }

        foreach ((Connector connector, ConnectorLayout layout) in ActiveConnectors())
        {
            Consider(connector.Id, RegionPicking.Score(layout.Footprint, raw, tolerance));
        }

        foreach (Shaft shaft in ActiveShafts())
        {
            Consider(shaft.Id, RegionPicking.Score(shaft.Outline, raw, tolerance));
        }

        foreach (Device device in ActiveDevices())
        {
            Consider(device.Id, RegionPicking.Score(DeviceGeometry.Footprint(device), raw, tolerance));
        }

        // Underlays only win when nothing else is under the cursor.
        if (best is null)
        {
            foreach (Underlay underlay in ActiveUnderlays())
            {
                if (RegionPicking.Score(UnderlayGeometry.Corners(underlay), raw, tolerance) is { } s)
                {
                    Consider(underlay.Id, (s.Rank + 2, s.Area));
                }
            }
        }

        return best;
    }

    /// <summary>Outline drawn for a selectable object: region outline, connector footprint or shaft outline.</summary>
    private (IReadOnlyList<Point2> Outline, bool Closed)? OutlineOf(Guid id)
    {
        Study study = _host.Study;
        if (study.Regions.FirstOrDefault(r => r.Id == id) is { } region)
        {
            return (region.Outline, RegionPicking.IsClosed(region));
        }

        if (study.FindConnector(id) is { } connector)
        {
            return ConnectorLayout.Compute(connector, study) is { IsSuccess: true, Value: { } layout } ? (layout.Footprint, true) : null;
        }

        if (study.FindShaft(id) is { } shaft)
        {
            return (shaft.Outline, true);
        }

        if (study.FindDevice(id) is { } device)
        {
            return (DeviceGeometry.Footprint(device), true);
        }

        return study.FindUnderlay(id) is { } underlay ? (UnderlayGeometry.Corners(underlay), true) : null;
    }

    /// <summary>Points a move can be anchored to and snapped from.</summary>
    private IEnumerable<Point2> SnapPointsOf(Guid id)
    {
        if (OutlineOf(id) is { } o)
        {
            foreach (Point2 p in o.Outline)
            {
                yield return p;
            }
        }

        if (_host.Study.FindConnector(id) is { } connector)
        {
            yield return connector.BottomLanding;
        }

        if (_host.Study.FindDevice(id) is { } device)
        {
            yield return device.Position;
            yield return DeviceGeometry.ServicePoint(device);
        }
    }

    /// <summary>
    /// Snap candidates: everything on the active level and on the nearest visible level below (traced through the
    /// plan ghost), locked or not (D-033), minus the excluded objects. Connectors add their footprint corners and
    /// landing centres; shafts their outlines.
    /// </summary>
    private SnapGeometry BuildSnapGeometry(IEnumerable<Guid> exclude)
    {
        var geometry = new SnapGeometry();
        if (_host.ActiveLevelModel is not { } level)
        {
            return geometry;
        }

        Study study = _host.Study;
        var excluded = exclude as ISet<Guid> ?? exclude.ToHashSet();
        Level? below = study.Levels
            .Where(l => l.IsVisible && l.ElevationM < level.ElevationM)
            .OrderByDescending(l => l.ElevationM)
            .FirstOrDefault();

        foreach (Region region in study.Regions)
        {
            bool onLevel = study.RegionPresentOn(region, level.Id) || (below is not null && study.RegionPresentOn(region, below.Id));
            if (onLevel && !excluded.Contains(region.Id) && !_hidden.Contains(region.Id))
            {
                geometry.AddOutline(region.Outline, RegionPicking.IsClosed(region));
            }
        }

        foreach (Connector connector in study.Connectors)
        {
            bool touches = connector.BottomLevelId == level.Id || connector.TopLevelId == level.Id
                || (below is not null && (connector.BottomLevelId == below.Id || connector.TopLevelId == below.Id));
            if (touches && !excluded.Contains(connector.Id) && !_hidden.Contains(connector.Id)
                && ConnectorLayout.Compute(connector, study) is { IsSuccess: true, Value: { } layout })
            {
                geometry.AddOutline(layout.Footprint, closed: true);
                geometry.AddPoint(layout.BottomLanding);
                geometry.AddPoint(layout.TopLanding);
            }
        }

        foreach (Shaft shaft in study.ShaftsSpanning(level.Id))
        {
            if (!excluded.Contains(shaft.Id) && !_hidden.Contains(shaft.Id))
            {
                geometry.AddOutline(shaft.Outline, closed: true);
            }
        }

        foreach (Device device in study.DevicesOn(level.Id))
        {
            if (!excluded.Contains(device.Id) && !_hidden.Contains(device.Id))
            {
                geometry.AddOutline(DeviceGeometry.Footprint(device), closed: true);
                geometry.AddPoint(device.Position);
            }
        }

        return geometry;
    }

    // ------------------------------------------------------------------ helpers

    private bool CheckEditable()
    {
        if (IsLocked)
        {
            RaiseFlash("RUNNING ▸ RESET TO EDIT");
            return false;
        }

        if (_host.ActiveLevelModel is null)
        {
            RaiseFlash("NO ACTIVE LEVEL");
            return false;
        }

        if (_host.ActiveLevelModel.IsLocked)
        {
            RaiseFlash("LEVEL LOCKED ▸ UNLOCK IT TO EDIT");
            return false;
        }

        return true;
    }

    /// <summary>Connectors plus the partners of any paired units among them, in study order.</summary>
    private List<Connector> WithPartners(IEnumerable<Connector> connectors)
    {
        var ids = new HashSet<Guid>();
        foreach (Connector connector in connectors)
        {
            ids.Add(connector.Id);
            if (_host.Study.FindPartner(connector) is { } partner)
            {
                ids.Add(partner.Id);
            }
        }

        return [.. _host.Study.Connectors.Where(c => ids.Contains(c.Id))];
    }

    /// <summary>An underlay snapshot moved by <paramref name="delta"/> (same id).</summary>
    private static Underlay Moved(Underlay source, Point2 delta)
    {
        Underlay next = source.Clone();
        next.Centre = source.Centre + delta;
        return next;
    }

    /// <summary>A shaft snapshot with a new outline (same id).</summary>
    private static Shaft Reshaped(Shaft source, IReadOnlyList<Point2> outline)
    {
        Shaft next = source.Clone();
        next.Outline = [.. outline];
        return next;
    }

    /// <summary>Removes the selection (regions, connectors with their partners, shafts) as one undo step.</summary>
    private void RemoveSelection(string verb)
    {
        IReadOnlyList<Region> regions = SelectedRegions;
        List<Connector> connectors = WithPartners(SelectedConnectors);
        IReadOnlyList<Shaft> shafts = SelectedShafts;
        IReadOnlyList<Device> devices = SelectedDevices;
        IReadOnlyList<Underlay> underlays = SelectedUnderlays;
        int count = regions.Count + connectors.Count + shafts.Count + devices.Count + underlays.Count;
        if (count == 0)
        {
            return;
        }

        Study study = _host.Study;
        var actions = new List<IUndoableAction>();
        if (regions.Count > 0)
        {
            actions.Add(new RemoveRegionsAction(verb, study, regions, null));
        }

        if (connectors.Count > 0)
        {
            actions.Add(new RemoveItemsAction<Connector>(verb, study.Connectors, connectors, null));
        }

        if (shafts.Count > 0)
        {
            actions.Add(new RemoveItemsAction<Shaft>(verb, study.Shafts, shafts, null));
        }

        if (devices.Count > 0)
        {
            actions.Add(new RemoveItemsAction<Device>(verb, study.Devices, devices, null));
        }

        // Underlay images stay in memory, so undoing the delete brings the picture back.
        if (underlays.Count > 0)
        {
            actions.Add(new RemoveItemsAction<Underlay>(verb, study.Underlays, underlays, null));
        }

        _selectedVertex = null;
        _host.Execute(new CompositeAction($"{verb} {Plural(count, "object")}", actions, _host.NotifyModelChanged));
        SetSelection([]);
    }

    private void SetSelection(IEnumerable<Guid> ids)
    {
        var next = ids.ToHashSet();
        if (next.SetEquals(_selection))
        {
            return;
        }

        _selection.Clear();
        _selection.UnionWith(next);
        if (_selection.Count != 1)
        {
            _selectedVertex = null;
        }

        SelectionChanged?.Invoke(this, EventArgs.Empty);
        RaiseOverlay();
    }

    private void CancelGesture()
    {
        _gesture = Gesture.None;
        _pressId = null;
        _vertexTarget = null;
        _vertexPreview = null;
        _moveDelta = Point2.Zero;
        _rotatePreview = null;
    }

    private void RaiseFlash(string message)
    {
        Flash?.Invoke(this, message);
        _host.ShowMessage(message);
    }

    private void RaiseOverlay() => OverlayChanged?.Invoke(this, EventArgs.Empty);

    private void RaiseViewFilter(string? message)
    {
        if (message is not null)
        {
            _host.ShowMessage(message);
        }

        _hover = null;
        ViewFilterChanged?.Invoke(this, EventArgs.Empty);
        RaiseOverlay();
    }

    private static string Plural(int count, string noun) =>
        string.Create(CultureInfo.InvariantCulture, $"{count} {noun}{(count == 1 ? string.Empty : "s")}");
}
