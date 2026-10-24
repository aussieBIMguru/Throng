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
/// </summary>
/// <remarks>
/// Screen tolerances are given in device-independent pixels and converted with the caller's metres-per-DIP, so
/// snapping and picking feel the same at every zoom.
/// </remarks>
public sealed class PlanEditor
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
    private readonly List<Region> _clipboard = [];
    private readonly HashSet<Guid> _hidden = [];

    private EditorTool _tool = EditorTool.Select;
    private RegionKind _drawKind = RegionKind.Walkable;
    private SnapResult _snap;
    private bool _hasCursor;
    private double _metresPerDip = 0.08;
    private Region? _hover;
    private int? _selectedVertex;

    // Pointer gesture state (select tool, and drag-to-draw for rectangles/circles).
    private Gesture _gesture = Gesture.None;
    private Point2 _pressPoint;
    private ModifierKeys _pressModifiers;
    private Region? _pressRegion;
    private Point2 _dragPoint;
    private Point2 _moveAnchor;
    private Point2 _moveDelta;
    private Region? _vertexRegion;
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

    /// <summary>Raised when the selected regions change.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>Raised when the active tool or draw kind changes.</summary>
    public event EventHandler? ToolChanged;

    /// <summary>Raised with a short HUD message (rejections such as "EDGES CROSS").</summary>
    public event EventHandler<string>? Flash;

    /// <summary>Raised when temporarily hidden regions change (D-046); the scene must be rebuilt.</summary>
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

    /// <summary>True when the active level exists and isn't locked (D-033).</summary>
    public bool CanEdit => _host.ActiveLevelModel is { IsLocked: false };

    /// <summary>True while a drawing tool is active, so typed dimensions are accepted (D-038).</summary>
    public bool AcceptsTypedInput => _tool != EditorTool.Select && CanEdit;

    /// <summary>Points placed so far for the shape being drawn.</summary>
    public IReadOnlyList<Point2> DrawPoints => _points;

    /// <summary>Snapped cursor position (valid when <see cref="HasCursor"/>).</summary>
    public SnapResult CursorSnap => _snap;

    /// <summary>True while the cursor is over the viewport in plan view.</summary>
    public bool HasCursor => _hasCursor;

    /// <summary>Metres per DIP from the last pointer event (for overlay sizes).</summary>
    public double MetresPerDip => _metresPerDip;

    /// <summary>Region under the cursor in the select tool, for hover highlight.</summary>
    public Region? Hover => _gesture == Gesture.None && _tool == EditorTool.Select ? _hover : null;

    /// <summary>Selected vertex index on the single selected region, if any.</summary>
    public int? SelectedVertex => _selectedVertex;

    /// <summary>Selected regions in study order.</summary>
    public IReadOnlyList<Region> SelectedRegions => [.. _host.Study.Regions.Where(r => _selection.Contains(r.Id))];

    /// <summary>Number of selected regions.</summary>
    public int SelectionCount => _selection.Count;

    /// <summary>True when the clipboard holds regions.</summary>
    public bool HasClipboard => _clipboard.Count > 0;

    /// <summary>The single selected region that shows grips, or null.</summary>
    public Region? GripRegion => CanEdit && _selection.Count == 1 ? SelectedRegions.FirstOrDefault() : null;

    /// <summary>Box selection in progress: the box and whether it's a crossing (right-to-left) box.</summary>
    public (Bounds2 Box, bool Crossing)? RubberBand =>
        _gesture == Gesture.RubberBand ? (Bounds2.FromCorners(_pressPoint, _dragPoint), _dragPoint.X < _pressPoint.X) : null;

    /// <summary>Offset of the selection being dragged, or null when not moving.</summary>
    public Point2? MoveDelta => _gesture == Gesture.Move ? _moveDelta : null;

    /// <summary>Outline being reshaped by a vertex grip or an edge push/pull, or null.</summary>
    public (Region Region, IReadOnlyList<Point2> Outline)? VertexPreview =>
        _gesture is Gesture.Vertex or Gesture.EdgePush && _vertexRegion is not null && _vertexPreview is not null
            ? (_vertexRegion, _vertexPreview)
            : null;

    /// <summary>Signed push/pull distance while dragging an edge handle (positive = outward), or null.</summary>
    public double? PushDistance => _gesture == Gesture.EdgePush ? _pushDistance : null;

    /// <summary>Regions temporarily hidden from the view (D-046). Not saved; no effect on the model.</summary>
    public IReadOnlySet<Guid> HiddenRegionIds => _hidden;

    /// <summary>Number of temporarily hidden regions.</summary>
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
    /// <param name="shift">Shift held (forces ortho while drawing).</param>
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
        }

        if (_tool == EditorTool.Select)
        {
            _snap = new SnapResult(raw, SnapKind.None);
            _hover = _gesture == Gesture.None && CanEdit ? RegionPicking.Pick(ActiveRegions(), raw, PickTolerancePx * metresPerDip) : null;
        }
        else
        {
            _snap = SnapDrawPoint(raw, shift);
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
                ApplyClick(_pressRegion, _pressModifiers);
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
            case Gesture.DrawDrag:
                // Press-drag-release draws a rectangle or circle in one stroke.
                if (_points.Count == 1 && Point2.Distance(raw, _pressPoint) / metresPerDip > DragThresholdPx)
                {
                    FinishTwoPointShape(SnapDrawPoint(raw, (modifiers & ModifierKeys.Shift) != 0).Point);
                }

                break;
        }

        _pressRegion = null;
        _vertexRegion = null;
        _vertexPreview = null;
        RaiseOverlay();
    }

    // ------------------------------------------------------------------ keyboard and commands

    /// <summary>Handles a key pressed while the viewport has focus. Returns true when used.</summary>
    public bool HandleKey(Key key, ModifierKeys modifiers)
    {
        bool ctrl = (modifiers & ModifierKeys.Control) != 0;
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

    /// <summary>Temporarily hides the selected regions (H, D-046).</summary>
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
        RaiseViewFilter($"{Plural(count, "region")} hidden ▸ Esc or RESET VIEW shows everything again.");
    }

    /// <summary>Temporarily hides everything except the selected regions (I, D-046).</summary>
    public void IsolateSelection()
    {
        if (_selection.Count == 0)
        {
            RaiseFlash("SELECT SOMETHING TO ISOLATE");
            return;
        }

        _hidden.Clear();
        _hidden.UnionWith(_host.Study.Regions.Where(r => !_selection.Contains(r.Id)).Select(r => r.Id));
        RaiseViewFilter($"Isolated {Plural(_selection.Count, "region")} ▸ Esc or RESET VIEW shows everything again.");
    }

    /// <summary>Shows every temporarily hidden region again.</summary>
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
        RaiseOverlay();
    }

    /// <summary>
    /// Applies the typed-dimension field (D-038). Returns true when accepted; false keeps the field open after a
    /// HUD message.
    /// </summary>
    /// <remarks>
    /// First point of any shape: <c>x,y</c> absolute. Rectangle: <c>w,h</c> (or one value for a square) towards the
    /// cursor. Circle: radius. Polygon/wall: a length along the cursor direction, or <c>dx,dy</c> relative.
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

            _points.Add(new Point2(v[0], v[1]));
            RaiseOverlay();
            return true;
        }

        Point2 last = _points[^1];
        Point2 toward = _hasCursor ? _snap.Point - last : Point2.Zero;
        switch (_tool)
        {
            case EditorTool.Rectangle:
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

            default:
                return false;
        }
    }

    /// <summary>Selects every region on the active level.</summary>
    public void SelectAll()
    {
        if (CheckEditable())
        {
            SetSelection(ActiveRegions().Select(r => r.Id));
        }
    }

    /// <summary>Copies the selection to the editor clipboard.</summary>
    public void Copy()
    {
        IReadOnlyList<Region> selected = SelectedRegions;
        if (selected.Count == 0)
        {
            _host.ShowMessage("Nothing selected to copy.");
            return;
        }

        _clipboard.Clear();
        _clipboard.AddRange(selected.Select(r => RegionFactory.Copy(r, r.LevelId, Point2.Zero)));
        _host.ShowMessage(Plural(selected.Count, "region") + " copied. Ctrl+V pastes in place on the active level.");
    }

    /// <summary>Copies then deletes the selection.</summary>
    public void Cut()
    {
        if (_selection.Count == 0 || !CheckEditable())
        {
            return;
        }

        Copy();
        RemoveRegions("Cut");
    }

    /// <summary>Pastes the clipboard at the same plan position on the active level (D-041).</summary>
    public void Paste()
    {
        if (_clipboard.Count == 0 || !CheckEditable() || _host.ActiveLevelModel is not { } level)
        {
            return;
        }

        AddRegions("Paste", _clipboard.Select(r => RegionFactory.Copy(r, level.Id, Point2.Zero)));
    }

    /// <summary>Duplicates the selection on the same level, offset by one grid step (D-041).</summary>
    public void Duplicate()
    {
        IReadOnlyList<Region> selected = SelectedRegions;
        if (selected.Count == 0 || !CheckEditable())
        {
            return;
        }

        double step = Snap.GridStepM > 0 ? Snap.GridStepM : 0.5;
        var offset = new Point2(step, -step);
        AddRegions("Duplicate", selected.Select(r => RegionFactory.Copy(r, r.LevelId, offset)));
    }

    /// <summary>Deletes the selected vertex, or the selected regions when no vertex is selected.</summary>
    public void Delete()
    {
        if (!CheckEditable())
        {
            return;
        }

        if (_selectedVertex is int index && GripRegion is { } region)
        {
            DeleteVertex(region, index);
            return;
        }

        if (_selection.Count > 0)
        {
            RemoveRegions("Delete");
        }
    }

    /// <summary>
    /// Re-checks the selection against the model (after undo/redo, level switch, lock). Drops regions that no
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
            var valid = _host.Study.Regions.Where(r => r.LevelId == level.Id).Select(r => r.Id).ToHashSet();
            _selection.RemoveWhere(id => !valid.Contains(id));
        }

        if (_selectedVertex is int vertex && (GripRegion is not { } region || vertex >= region.Outline.Count))
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

        if (modifiers == ModifierKeys.None && GripRegion is { } region && TryHitGrip(region, raw, out int index, out GripKind kind))
        {
            if (kind == GripKind.Edge)
            {
                StartEdgePush(region, index);
            }
            else
            {
                StartVertexDrag(region, index, kind == GripKind.Midpoint);
            }

            return;
        }

        _pressRegion = RegionPicking.Pick(ActiveRegions(), raw, PickTolerancePx * _metresPerDip);
        _gesture = Gesture.Pending;
    }

    private void BeginDrag()
    {
        bool plain = _pressModifiers == ModifierKeys.None;
        if (plain && _pressRegion is not null)
        {
            if (!_selection.Contains(_pressRegion.Id))
            {
                SetSelection([_pressRegion.Id]);
            }

            // Grab the nearest vertex when close to one, so moves snap vertex-to-vertex.
            double tolerance = SnapTolerancePx * _metresPerDip;
            Point2 anchor = _pressPoint;
            double best = tolerance;
            foreach (Point2 p in _pressRegion.Outline)
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
        SnapGeometry geometry = BuildSnapGeometry(_selection);
        Point2 target = raw + (_moveAnchor - _pressPoint);
        SnapResult snap = Snapper.Snap(target, geometry, Snap, SnapTolerancePx * _metresPerDip, _moveAnchor, shift);
        _moveDelta = snap.Point - _moveAnchor;
    }

    private void CommitMove()
    {
        if (_moveDelta.Length < ShapeValidator.PointToleranceM)
        {
            return;
        }

        IReadOnlyList<Region> selected = SelectedRegions;
        Point2 delta = _moveDelta;
        var action = new ReplaceOutlinesAction(
            $"Move {Plural(selected.Count, "region")}",
            selected.Select(r => (r, (IReadOnlyList<Point2>)ShapeFactory.Translate(r.Outline, delta))),
            _host.NotifyModelChanged);
        _host.Execute(action);
    }

    private void ApplyClick(Region? hit, ModifierKeys modifiers)
    {
        _selectedVertex = null;
        if (hit is null)
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
            if (!next.Remove(hit.Id))
            {
                next.Add(hit.Id);
            }

            SetSelection(next);
        }
        else if ((modifiers & ModifierKeys.Shift) != 0)
        {
            SetSelection(_selection.Append(hit.Id));
        }
        else
        {
            SetSelection([hit.Id]);
        }
    }

    private void ApplyBox(Bounds2 box, bool crossing, ModifierKeys modifiers)
    {
        Guid[] hits = [.. ActiveRegions()
            .Where(r => crossing ? RegionPicking.Touches(r, box) : RegionPicking.IsInside(r, box))
            .Select(r => r.Id)];

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

    /// <summary>Hit-tests the grips of the single selected region: vertices, then edge arrows, then midpoints.</summary>
    private bool TryHitGrip(Region region, Point2 raw, out int index, out GripKind kind)
    {
        double tolerance = GripTolerancePx * _metresPerDip;
        List<Point2> outline = region.Outline;
        for (int i = 0; i < outline.Count; i++)
        {
            if (Point2.Distance(outline[i], raw) <= tolerance)
            {
                index = i;
                kind = GripKind.Vertex;
                return true;
            }
        }

        bool closed = RegionPicking.IsClosed(region);
        int edges = closed ? outline.Count : outline.Count - 1;
        for (int i = 0; i < edges; i++)
        {
            if (Point2.Distance(EdgeHandlePosition(outline, closed, i, _metresPerDip), raw) <= tolerance)
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

    private void StartEdgePush(Region region, int edgeIndex)
    {
        bool closed = RegionPicking.IsClosed(region);
        _vertexRegion = region;
        _vertexPreview = [.. region.Outline];
        _edgeIndex = edgeIndex;
        _edgeNormal = EdgePushPull.Normal(region.Outline, closed, edgeIndex);
        _edgeMid = (region.Outline[edgeIndex] + region.Outline[(edgeIndex + 1) % region.Outline.Count]) * 0.5;
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
        if (_vertexRegion is not { } region)
        {
            return;
        }

        var pointSnaps = new SnapSettings { Grid = false, Ortho = false, Endpoint = Snap.Endpoint, Midpoint = Snap.Midpoint };
        SnapResult snap = Snapper.Snap(raw, BuildSnapGeometry([region.Id]), pointSnaps, SnapTolerancePx * _metresPerDip);

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

        Result<List<Point2>> moved = EdgePushPull.Apply(region.Outline, RegionPicking.IsClosed(region), _edgeIndex, distance);
        if (moved.IsSuccess && moved.Value is not null)
        {
            _vertexPreview = moved.Value;
            _pushDistance = distance;
            _vertexMoved = Math.Abs(distance) > ShapeValidator.PointToleranceM;
        }
    }

    private void CommitEdgePush()
    {
        if (_vertexRegion is not { } region || _vertexPreview is not { } outline || !_vertexMoved)
        {
            return;
        }

        Result<List<Point2>> valid = ShapeValidator.Validate(outline, RegionPicking.IsClosed(region));
        if (!valid.IsSuccess || valid.Value is null)
        {
            RaiseFlash(valid.Error ?? ShapeValidator.NoArea);
            return;
        }

        string description = string.Create(CultureInfo.InvariantCulture, $"Push/pull edge {_pushDistance:+0.###;−0.###} m");
        _host.Execute(new ReplaceOutlinesAction(description, [(region, valid.Value)], _host.NotifyModelChanged));
    }

    private void StartVertexDrag(Region region, int index, bool isMidpoint)
    {
        _vertexRegion = region;
        _vertexPreview = [.. region.Outline];
        if (isMidpoint)
        {
            Point2 a = region.Outline[index];
            Point2 b = region.Outline[(index + 1) % region.Outline.Count];
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
        if (_vertexRegion is null || _vertexPreview is null)
        {
            return;
        }

        // Snap to everything except the region being edited, plus that region's other vertices.
        SnapGeometry geometry = BuildSnapGeometry([_vertexRegion.Id]);
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
        if (RegionPicking.IsClosed(_vertexRegion) || _vertexIndex > 0)
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
        if (_vertexRegion is not { } region || _vertexPreview is not { } outline || (!_vertexMoved && !_vertexInserted))
        {
            return;
        }

        bool closed = RegionPicking.IsClosed(region);
        Result<List<Point2>> valid = ShapeValidator.Validate(outline, closed);
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
        _host.Execute(new ReplaceOutlinesAction(description, [(region, valid.Value)], _host.NotifyModelChanged));
    }

    private void DeleteVertex(Region region, int index)
    {
        bool closed = RegionPicking.IsClosed(region);
        int minimum = closed ? 3 : 2;
        if (region.Outline.Count <= minimum)
        {
            RaiseFlash(closed ? ShapeValidator.NeedThreePoints : ShapeValidator.NeedTwoPoints);
            return;
        }

        List<Point2> outline = [.. region.Outline];
        outline.RemoveAt(index);
        Result<List<Point2>> valid = ShapeValidator.Validate(outline, closed);
        if (!valid.IsSuccess || valid.Value is null)
        {
            RaiseFlash(valid.Error ?? ShapeValidator.NoArea);
            return;
        }

        _selectedVertex = null;
        _host.Execute(new ReplaceOutlinesAction("Delete vertex", [(region, valid.Value)], _host.NotifyModelChanged));
    }

    // ------------------------------------------------------------------ drawing tools

    private void DrawDown(Point2 raw, ModifierKeys modifiers, int clickCount)
    {
        bool shift = (modifiers & ModifierKeys.Shift) != 0;
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
            case EditorTool.Rectangle or EditorTool.Circle:
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

    /// <summary>Completes a rectangle or circle whose first point is placed.</summary>
    private bool FinishTwoPointShape(Point2 second)
    {
        if (_points.Count != 1)
        {
            return false;
        }

        Point2 first = _points[0];
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

    // ------------------------------------------------------------------ helpers

    private bool CheckEditable()
    {
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

    /// <summary>Regions on the active level that aren't temporarily hidden.</summary>
    private IEnumerable<Region> ActiveRegions() =>
        _host.ActiveLevelModel is { } level ? _host.Study.RegionsOn(level.Id).Where(r => !_hidden.Contains(r.Id)) : [];

    /// <summary>
    /// Snap candidates: regions on the active level and on the nearest visible level below (traced through the
    /// plan ghost), locked or not (D-033), minus the excluded regions.
    /// </summary>
    private SnapGeometry BuildSnapGeometry(IEnumerable<Guid> exclude)
    {
        var geometry = new SnapGeometry();
        if (_host.ActiveLevelModel is not { } level)
        {
            return geometry;
        }

        var excluded = exclude as ISet<Guid> ?? exclude.ToHashSet();
        Level? below = _host.Study.Levels
            .Where(l => l.IsVisible && l.ElevationM < level.ElevationM)
            .OrderByDescending(l => l.ElevationM)
            .FirstOrDefault();

        foreach (Region region in _host.Study.Regions)
        {
            if ((region.LevelId == level.Id || region.LevelId == below?.Id) && !excluded.Contains(region.Id) && !_hidden.Contains(region.Id))
            {
                geometry.AddOutline(region.Outline, RegionPicking.IsClosed(region));
            }
        }

        return geometry;
    }

    private void AddRegions(string verb, IEnumerable<Region> regions)
    {
        Region[] added = [.. regions];
        if (added.Length == 0)
        {
            return;
        }

        _host.Execute(new AddRegionsAction($"{verb} {Plural(added.Length, "region")}", _host.Study, added, _host.NotifyModelChanged));
        SetSelection(added.Select(r => r.Id));
    }

    private void RemoveRegions(string verb)
    {
        IReadOnlyList<Region> selected = SelectedRegions;
        if (selected.Count == 0)
        {
            return;
        }

        _selectedVertex = null;
        _host.Execute(new RemoveRegionsAction($"{verb} {Plural(selected.Count, "region")}", _host.Study, selected, _host.NotifyModelChanged));
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
        _pressRegion = null;
        _vertexRegion = null;
        _vertexPreview = null;
        _moveDelta = Point2.Zero;
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
