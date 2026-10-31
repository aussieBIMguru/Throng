using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Throng.Core.Editing;
using Throng.Core.Geometry;
using Throng.Render.Scene;

namespace Throng.App.Editing;

/// <summary>
/// Immediate-mode WPF overlay drawn over the GL viewport in plan view: connector UP/DN labels, hover and selection
/// outlines, vertex and rotate grips, rubber bands, shape and connector previews, snap markers and readouts. Redrawn whenever the editor or the plan
/// camera changes. Never hit-testable, so mouse input goes to the viewport's input layer underneath.
/// </summary>
public sealed class EditorOverlay : FrameworkElement
{
    private const double GripSize = 7;
    private const double MidGripSize = 5;
    private const double SnapMarkerSize = 9;

    private static readonly Brush Accent = Frozen(Color.FromRgb(0x22, 0xD3, 0xEE));
    private static readonly Brush AccentFill = Frozen(Color.FromArgb(0x18, 0x22, 0xD3, 0xEE));
    private static readonly Brush Yellow = Frozen(Color.FromRgb(0xFA, 0xCC, 0x15));
    private static readonly Brush YellowDim = Frozen(Color.FromArgb(0x80, 0xFA, 0xCC, 0x15));
    private static readonly Brush Green = Frozen(Color.FromRgb(0x4A, 0xDE, 0x80));
    private static readonly Brush GreenFill = Frozen(Color.FromArgb(0x18, 0x4A, 0xDE, 0x80));
    private static readonly Brush Magenta = Frozen(Color.FromRgb(0xE8, 0x79, 0xF9));
    private static readonly Brush Panel = Frozen(Color.FromRgb(0x0B, 0x0F, 0x17));
    private static readonly Brush LabelBack = Frozen(Color.FromArgb(0xD0, 0x0B, 0x0F, 0x17));
    private static readonly Brush TextBrush = Frozen(Color.FromRgb(0xD8, 0xDE, 0xE9));
    private static readonly Brush QueueDot = Frozen(Color.FromArgb(0x80, 0xFA, 0xCC, 0x15));
    private static readonly Brush TrainFill = Frozen(Color.FromArgb(0x60, 0x94, 0xA3, 0xB8));
    private static readonly Pen TrainPen = FrozenPen(Frozen(Color.FromRgb(0x94, 0xA3, 0xB8)), 1.5);
    private static readonly Pen TrainShapePen = FrozenPen(Frozen(Color.FromArgb(0xB0, 0x94, 0xA3, 0xB8)), 1.5, dashed: true);
    private static readonly Brush DoorOpenFill = Frozen(Color.FromArgb(0x50, 0xFA, 0xCC, 0x15));
    private static readonly Brush DoorBoardFill = Frozen(Color.FromArgb(0x60, 0x4A, 0xDE, 0x80));
    private static readonly Pen LiftCarPen = FrozenPen(Frozen(Color.FromRgb(0x38, 0xBD, 0xF8)), 1);
    private static readonly Pen LiftDoorPen = FrozenPen(Frozen(Color.FromRgb(0x38, 0xBD, 0xF8)), 3);
    private static readonly Pen LiftLandingPen = FrozenPen(Frozen(Color.FromArgb(0x90, 0x38, 0xBD, 0xF8)), 1, dashed: true);
    private static readonly Brush LiftHereFill = Frozen(Color.FromArgb(0x70, 0x38, 0xBD, 0xF8));
    private static readonly Brush LiftOpenFill = Frozen(Color.FromArgb(0xA0, 0x4A, 0xDE, 0x80));

    private static readonly Pen SelectionPen = FrozenPen(Yellow, 2);
    // Hover (D-144): what a Select click would pick is cyan with a light fill and a name tag; the object being
    // snapped to while drawing is a dashed magenta outline.
    private static readonly Pen HoverPen = FrozenPen(Accent, 2);
    private static readonly Pen SnapTargetPen = FrozenPen(Frozen(Color.FromArgb(0xB0, 0xE8, 0x79, 0xF9)), 1.5, dashed: true);
    private static readonly Pen DragGhostPen = FrozenPen(Accent, 1.5, dashed: true);
    private static readonly Pen GhostPen = FrozenPen(YellowDim, 1, dashed: true);
    private static readonly Pen GripPen = FrozenPen(Yellow, 1);
    private static readonly Pen MidGripPen = FrozenPen(YellowDim, 1);
    private static readonly Pen PreviewPen = FrozenPen(Accent, 1.5);
    private static readonly Pen PreviewClosePen = FrozenPen(Accent, 1, dashed: true);
    private static readonly Pen WindowPen = FrozenPen(Accent, 1);
    private static readonly Pen CrossingPen = FrozenPen(Green, 1, dashed: true);
    private static readonly Pen SnapPen = FrozenPen(Magenta, 1.5);
    private static readonly Pen OrthoPen = FrozenPen(Magenta, 1, dashed: true);
    private static readonly Pen CrossPen = FrozenPen(Accent, 1);
    private static readonly Pen EdgeStemPen = FrozenPen(Frozen(Color.FromArgb(0x90, 0x22, 0xD3, 0xEE)), 1);

    private static readonly Typeface LabelFace = new(new FontFamily("Cascadia Mono, Consolas"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    /// <summary>Editor whose state is drawn.</summary>
    public PlanEditor? Editor { get; set; }

    /// <summary>Maps plan metres to overlay DIPs (supplied by the viewport's plan camera).</summary>
    public Func<Point2, Point>? PlanToScreen { get; set; }

    /// <summary>True in plan view.</summary>
    public bool IsPlanActive { get; set; }

    /// <summary>
    /// True in 3D and ISO with SELECT (round 017, D-291, D-294): hover, selection, the rotate grip, the rubber band,
    /// snaps and the drag ghost are drawn on the active level's floor plane (through <see cref="PlanToScreen"/>);
    /// labels, trains, lifts and shape previews stay plan-only.
    /// </summary>
    public bool Is3D { get; set; }

    /// <summary>Text labels for the active level from the scene (connector UP / DN, D-062).</summary>
    public IReadOnlyList<PlanLabel> Labels { get; set; } = [];

    /// <summary>Creates the overlay.</summary>
    public EditorOverlay()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext dc)
    {
        if (!(IsPlanActive || Is3D) || Editor is not { } editor || PlanToScreen is not { } map)
        {
            return;
        }

        // Clip so previews can't spill over the HUD panels outside the viewport.
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight)));

        if (IsPlanActive)
        {
            DrawSceneLabels(dc, map);
            DrawTrains(dc, map, editor);
            DrawLifts(dc, map, editor);
        }

        DrawHover(dc, map, editor);

        DrawQueueDots(dc, map, editor);
        DrawSelection(dc, map, editor);
        DrawRotateGrip(dc, map, editor);
        DrawRubberBand(dc, map, editor);
        if (IsPlanActive)
        {
            DrawShapePreview(dc, map, editor);
        }

        DrawSnapMarker(dc, map, editor);
        DrawDragGhost(dc, map, editor);

        dc.Pop();
    }

    /// <summary>Faint dots on the queue slots of the selected device (D-164), automatic or drawn.</summary>
    /// <summary>
    /// Trains in while running (D-190): a plain box along the platform edge, doors lit yellow while people get off and
    /// green once boarding.
    /// </summary>
    private static void DrawTrains(DrawingContext dc, Func<Point2, Point> map, PlanEditor editor)
    {
        // Drawn train shapes (D-249) read as a dashed outline while editing.
        foreach (IReadOnlyList<Point2> shape in editor.TrainShapes)
        {
            DrawOutline(dc, map, shape, closed: true, TrainShapePen, null);
        }

        foreach (RunTrainView train in editor.RunTrains)
        {
            DrawOutline(dc, map, train.Box, closed: true, TrainPen, TrainFill);
            Brush doorFill = train.Boarding ? DoorBoardFill : DoorOpenFill;
            foreach (IReadOnlyList<Point2> door in train.Doors)
            {
                DrawOutline(dc, map, door, closed: true, null, doorFill);
            }
        }
    }

    /// <summary>
    /// Lifts (round 013, D-240): landings dashed, car outlines, doors in use at this level as thick ticks; while
    /// running, a car at this level is filled (green with its doors open) and every car shows its direction and riders.
    /// </summary>
    private void DrawLifts(DrawingContext dc, Func<Point2, Point> map, PlanEditor editor)
    {
        foreach (LiftSymbolView lift in editor.LiftSymbols)
        {
            foreach (IReadOnlyList<Point2> landing in lift.Landings)
            {
                DrawOutline(dc, map, landing, closed: true, LiftLandingPen);
            }

            foreach (IReadOnlyList<Point2> car in lift.Cars)
            {
                DrawOutline(dc, map, car, closed: true, LiftCarPen);
            }

            foreach ((Point2 a, Point2 b) in lift.Doors)
            {
                dc.DrawLine(LiftDoorPen, map(a), map(b));
            }
        }

        foreach (RunLiftCarView car in editor.RunLiftCars)
        {
            if (car.Here)
            {
                DrawOutline(dc, map, car.Footprint, closed: true, null, car.DoorsOpen ? LiftOpenFill : LiftHereFill);
            }

            if (car.Label.Length > 0)
            {
                double x = 0, y = 0;
                foreach (Point2 p in car.Footprint)
                {
                    x += p.X;
                    y += p.Y;
                }

                Point at = map(new Point2(x / car.Footprint.Count, y / car.Footprint.Count));
                DrawLabel(dc, car.Label, new Point(at.X - 8, at.Y - 8), car.Here ? TextBrush : Accent);
            }
        }
    }

    private static void DrawQueueDots(DrawingContext dc, Func<Point2, Point> map, PlanEditor editor)
    {
        IReadOnlyList<Point2> dots = editor.QueueSlotDots;
        foreach (Point2 dot in dots)
        {
            dc.DrawEllipse(QueueDot, null, map(dot), 2, 2);
        }
    }

    /// <summary>Hover highlight (D-144): pick target in Select, snap target's owner while drawing.</summary>
    private void DrawHover(DrawingContext dc, Func<Point2, Point> map, PlanEditor editor)
    {
        if (editor.Hover is not { } hover)
        {
            return;
        }

        if (editor.HoverIsSnapTarget)
        {
            DrawOutline(dc, map, hover.Outline, hover.Closed, SnapTargetPen);
            return;
        }

        if (hover.Closed)
        {
            DrawOutline(dc, map, hover.Outline, closed: true, null, AccentFill);
        }

        DrawOutline(dc, map, hover.Outline, hover.Closed, HoverPen);
        if (editor.HoverLabel is { } label && editor.HasCursor)
        {
            Point at = map(editor.CursorSnap.Point);
            DrawLabel(dc, label, new Point(at.X + 14, at.Y + 12), Accent);
        }
    }

    /// <summary>Ghost of a LIBRARY item dragged over the plan (D-144).</summary>
    private void DrawDragGhost(DrawingContext dc, Func<Point2, Point> map, PlanEditor editor)
    {
        if (editor.DragGhost is not { } ghost)
        {
            return;
        }

        Point at = map(ghost.At);
        if (ghost.Footprint is { } device)
        {
            DrawOutline(dc, map, DeviceGeometry.Footprint(device), closed: true, null, AccentFill);
            DrawOutline(dc, map, DeviceGeometry.Footprint(device), closed: true, DragGhostPen);
        }

        dc.DrawLine(CrossPen, new Point(at.X - 8, at.Y), new Point(at.X + 8, at.Y));
        dc.DrawLine(CrossPen, new Point(at.X, at.Y - 8), new Point(at.X, at.Y + 8));
        dc.DrawEllipse(null, DragGhostPen, at, 6, 6);
        DrawLabel(dc, ghost.Label, new Point(at.X + 14, at.Y + 12), Accent);
    }

    private static void DrawSelection(DrawingContext dc, Func<Point2, Point> map, PlanEditor editor)
    {
        Point2? delta = editor.MoveDelta;
        var vertexPreview = editor.VertexPreview;
        IReadOnlyDictionary<Guid, IReadOnlyList<Point2>>? rotatePreview = editor.RotatePreview;

        foreach ((Guid id, IReadOnlyList<Point2> outline, bool closed) in editor.SelectedOutlines)
        {
            if (delta is { } d)
            {
                // Ghost where it was, solid where it's going.
                DrawOutline(dc, map, outline, closed, GhostPen);
                DrawOutline(dc, map, ShapeFactory.Translate(outline, d), closed, SelectionPen);
            }
            else if (vertexPreview is { } vp && vp.Id == id)
            {
                DrawOutline(dc, map, outline, closed, GhostPen);
                DrawOutline(dc, map, vp.Outline, closed, SelectionPen);
            }
            else if (rotatePreview is not null && rotatePreview.TryGetValue(id, out IReadOnlyList<Point2>? rotated))
            {
                DrawOutline(dc, map, outline, closed, GhostPen);
                DrawOutline(dc, map, rotated, closed, SelectionPen);
            }
            else
            {
                DrawOutline(dc, map, outline, closed, SelectionPen);
            }
        }

        // A paired escalator's partner moves and rotates with it (D-059): show it ghosted.
        if (rotatePreview is not null)
        {
            foreach ((Guid id, IReadOnlyList<Point2> rotated) in rotatePreview)
            {
                if (editor.SelectedOutlines.All(o => o.Id != id))
                {
                    DrawOutline(dc, map, rotated, closed: true, SelectionPen);
                }
            }
        }

        if (delta is null && !editor.Is3D && editor.GripTarget is { } target)
        {
            IReadOnlyList<Point2> outline = vertexPreview is { } vp && vp.Id == target.Id ? vp.Outline : target.Outline;
            DrawGrips(dc, map, outline, target.Closed, editor.SelectedVertex, editor.MetresPerDip);
        }
    }

    /// <summary>The rotate grip of a single selected connector: a stem from its far end and a round handle (D-063).</summary>
    private static void DrawRotateGrip(DrawingContext dc, Func<Point2, Point> map, PlanEditor editor)
    {
        if (editor.Is3D || editor.MoveDelta is not null || editor.RotateGrip is not { } grip)
        {
            return;
        }

        Point anchor = map(grip.Anchor);
        Point handle = map(grip.Grip);
        dc.DrawLine(EdgeStemPen, anchor, handle);
        dc.DrawEllipse(Panel, GripPen, handle, GripSize * 0.6, GripSize * 0.6);
        dc.DrawEllipse(Yellow, null, handle, 1.6, 1.6);
    }

    private void DrawSceneLabels(DrawingContext dc, Func<Point2, Point> map)
    {
        foreach (PlanLabel label in Labels)
        {
            Point at = map(label.Position);
            Brush brush = BrushFor(label.Color);
            double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var formatted = new FormattedText(label.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, LabelFace, 10, brush, pixelsPerDip);
            var origin = new Point(at.X - (formatted.Width * 0.5), at.Y - (formatted.Height * 0.5));
            dc.DrawRectangle(LabelBack, null, new Rect(origin.X - 2, origin.Y, formatted.Width + 4, formatted.Height));
            dc.DrawText(formatted, origin);
        }
    }

    private static Brush BrushFor(ColorF color)
    {
        static byte Channel(float v) => (byte)Math.Clamp((int)Math.Round(v * 255), 0, 255);
        return Frozen(Color.FromArgb(Channel(color.A), Channel(color.R), Channel(color.G), Channel(color.B)));
    }

    private static void DrawGrips(DrawingContext dc, Func<Point2, Point> map, IReadOnlyList<Point2> outline, bool closed, int? selectedVertex, double metresPerDip)
    {
        int n = outline.Count;
        int edges = closed ? n : n - 1;

        // Push/pull arrows just outside each edge midpoint (D-047).
        for (int i = 0; i < edges; i++)
        {
            Point2 a = outline[i];
            Point2 b = outline[(i + 1) % n];
            if (Point2.Distance(a, b) < 1e-9)
            {
                continue;
            }

            Point2 handle = PlanEditor.EdgeHandlePosition(outline, closed, i, metresPerDip);
            Point2 normal = EdgePushPull.Normal(outline, closed, i);
            Point sMid = map((a + b) * 0.5);
            Point sHandle = map(handle);
            Vector dir = map(handle + (normal * metresPerDip)) - sHandle;
            if (dir.Length < 1e-9)
            {
                continue;
            }

            dir.Normalize();
            var side = new Vector(-dir.Y, dir.X);
            dc.DrawLine(EdgeStemPen, sMid, sHandle);

            var arrow = new StreamGeometry();
            using (StreamGeometryContext ctx = arrow.Open())
            {
                ctx.BeginFigure(sHandle + (dir * 5), isFilled: true, isClosed: true);
                ctx.LineTo(sHandle - (dir * 3) + (side * 4.5), isStroked: true, isSmoothJoin: false);
                ctx.LineTo(sHandle - (dir * 3) - (side * 4.5), isStroked: true, isSmoothJoin: false);
            }

            arrow.Freeze();
            dc.DrawGeometry(Accent, null, arrow);
        }

        for (int i = 0; i < edges; i++)
        {
            Point mid = map((outline[i] + outline[(i + 1) % n]) * 0.5);
            var diamond = new StreamGeometry();
            using (StreamGeometryContext ctx = diamond.Open())
            {
                double h = MidGripSize * 0.5;
                ctx.BeginFigure(new Point(mid.X, mid.Y - h), isFilled: true, isClosed: true);
                ctx.LineTo(new Point(mid.X + h, mid.Y), isStroked: true, isSmoothJoin: false);
                ctx.LineTo(new Point(mid.X, mid.Y + h), isStroked: true, isSmoothJoin: false);
                ctx.LineTo(new Point(mid.X - h, mid.Y), isStroked: true, isSmoothJoin: false);
            }

            diamond.Freeze();
            dc.DrawGeometry(Panel, MidGripPen, diamond);
        }

        for (int i = 0; i < n; i++)
        {
            Point p = map(outline[i]);
            double h = GripSize * 0.5;
            dc.DrawRectangle(i == selectedVertex ? Yellow : Panel, GripPen, new Rect(p.X - h, p.Y - h, GripSize, GripSize));
        }
    }

    private static void DrawRubberBand(DrawingContext dc, Func<Point2, Point> map, PlanEditor editor)
    {
        if (editor.RubberBand is not { } band)
        {
            return;
        }

        // Four mapped corners, so the box lies on the floor plane in 3D too (round 017).
        var geometry = new StreamGeometry();
        using (StreamGeometryContext g = geometry.Open())
        {
            g.BeginFigure(map(new Point2(band.Box.MinX, band.Box.MinY)), isFilled: true, isClosed: true);
            g.LineTo(map(new Point2(band.Box.MaxX, band.Box.MinY)), isStroked: true, isSmoothJoin: false);
            g.LineTo(map(new Point2(band.Box.MaxX, band.Box.MaxY)), isStroked: true, isSmoothJoin: false);
            g.LineTo(map(new Point2(band.Box.MinX, band.Box.MaxY)), isStroked: true, isSmoothJoin: false);
        }

        geometry.Freeze();
        dc.DrawGeometry(band.Crossing ? GreenFill : AccentFill, band.Crossing ? CrossingPen : WindowPen, geometry);
    }

    private void DrawShapePreview(DrawingContext dc, Func<Point2, Point> map, PlanEditor editor)
    {
        if (editor.Tool == EditorTool.Select || !editor.HasCursor)
        {
            return;
        }

        IReadOnlyList<Point2> points = editor.DrawPoints;
        Point2 cursor = editor.CursorSnap.Point;
        string? label = null;

        switch (editor.Tool)
        {
            case EditorTool.Connect:
                DrawConnectPreview(dc, map, editor);
                label = editor.ConnectPreviewText;
                break;

            case EditorTool.Place:
                DrawPlacePreview(dc, map, editor);
                label = editor.PlacePreviewText;
                break;

            case EditorTool.Calibrate when editor.CalibrationLine is { } line:
            {
                Point a = map(line.A);
                Point b = map(line.B);
                dc.DrawLine(SnapPen, a, b);
                dc.DrawEllipse(null, SnapPen, a, 4, 4);
                dc.DrawEllipse(null, SnapPen, b, 4, 4);
                label = string.Create(CultureInfo.InvariantCulture, $"{Point2.Distance(line.A, line.B):0.###} m on the image");
                break;
            }

            case EditorTool.Rectangle or EditorTool.Shaft when points.Count == 1:
            {
                DrawOutline(dc, map, ShapeFactory.Rectangle(points[0], cursor), closed: true, PreviewPen);
                Point2 size = cursor - points[0];
                label = string.Create(CultureInfo.InvariantCulture, $"{Math.Abs(size.X):0.00} × {Math.Abs(size.Y):0.00} m");
                break;
            }

            case EditorTool.Circle when points.Count == 1:
            {
                double radius = Point2.Distance(points[0], cursor);
                if (radius > 0)
                {
                    DrawOutline(dc, map, ShapeFactory.Circle(points[0], radius), closed: true, PreviewPen);
                }

                dc.DrawLine(PreviewClosePen, map(points[0]), map(cursor));
                label = string.Create(CultureInfo.InvariantCulture, $"r {radius:0.00} m");
                break;
            }

            case EditorTool.QueuePath when points.Count > 0:
            {
                var path = new List<Point2>(points) { cursor };
                DrawOutline(dc, map, path, closed: false, PreviewPen);
                double length = 0;
                for (int k = 1; k < path.Count; k++)
                {
                    length += Point2.Distance(path[k - 1], path[k]);
                }

                label = string.Create(CultureInfo.InvariantCulture, $"queue {length:0.0} m · ~{(int)(length / QueueLines.SlotSpacingM) + 1} people · Enter finishes");
                break;
            }

            case EditorTool.Polygon or EditorTool.Wall when points.Count > 0:
            {
                var path = new List<Point2>(points) { cursor };
                DrawOutline(dc, map, path, closed: false, PreviewPen);
                if (editor.Tool == EditorTool.Polygon && points.Count >= 2)
                {
                    dc.DrawLine(PreviewClosePen, map(cursor), map(points[0]));
                }

                Point2 segment = cursor - points[^1];
                double angle = Math.Atan2(segment.Y, segment.X) * 180.0 / Math.PI;
                label = string.Create(CultureInfo.InvariantCulture, $"{segment.Length:0.00} m  {(angle + 360) % 360:0.0}°");
                break;
            }
        }

        foreach (Point2 p in points)
        {
            Point s = map(p);
            dc.DrawRectangle(Accent, null, new Rect(s.X - 2.5, s.Y - 2.5, 5, 5));
        }

        // Small crosshair at the snapped cursor.
        Point c = map(cursor);
        dc.DrawLine(CrossPen, new Point(c.X - 6, c.Y), new Point(c.X + 6, c.Y));
        dc.DrawLine(CrossPen, new Point(c.X, c.Y - 6), new Point(c.X, c.Y + 6));

        if (label is not null)
        {
            DrawLabel(dc, label, new Point(c.X + 14, c.Y + 12));
        }
    }

    /// <summary>Footprint and service arrow of the device being placed, or the travelator belt (D-090).</summary>
    private static void DrawPlacePreview(DrawingContext dc, Func<Point2, Point> map, PlanEditor editor)
    {
        if (editor.PlaceDevicePreview is { } device)
        {
            DrawOutline(dc, map, DeviceGeometry.Footprint(device), closed: true, PreviewPen);
            foreach ((Point2 a, Point2 b) in DeviceGeometry.LaneLines(device))
            {
                dc.DrawLine(PreviewClosePen, map(a), map(b));
            }

            Point2 front = DeviceGeometry.ServicePoint(device);
            Point2 tip = front + (device.Facing() * Math.Max(0.3, device.DepthM * 0.5));
            dc.DrawLine(PreviewPen, map(front), map(tip));
            dc.DrawEllipse(Accent, null, map(tip), 3, 3);
        }

        if (editor.PlaceTravelatorPreview is { } belt)
        {
            DrawOutline(dc, map, belt.Layout.Footprint, closed: true, PreviewPen);
            DrawOutline(dc, map, [.. belt.Layout.Centreline.Select(p => p.Point)], closed: false, PreviewClosePen);
            Point top = map(belt.Layout.TopLanding);
            dc.DrawRectangle(Accent, null, new Rect(top.X - 3, top.Y - 3, 6, 6));
        }
    }

    /// <summary>Footprint, pieces and centreline of the connector(s) being placed (D-054).</summary>
    private static void DrawConnectPreview(DrawingContext dc, Func<Point2, Point> map, PlanEditor editor)
    {
        foreach (ConnectorPreview preview in editor.ConnectPreviews)
        {
            foreach (ConnectorPiece piece in preview.Layout.Pieces)
            {
                DrawOutline(dc, map, piece.Outline(), closed: true, PreviewClosePen);
            }

            DrawOutline(dc, map, preview.Layout.Footprint, closed: true, PreviewPen);
            var path = preview.Layout.Centreline.Select(p => p.Point).ToList();
            DrawOutline(dc, map, path, closed: false, PreviewClosePen);

            Point bottom = map(preview.Layout.BottomLanding);
            dc.DrawEllipse(null, PreviewPen, bottom, 4, 4);
            Point top = map(preview.Layout.TopLanding);
            dc.DrawRectangle(Accent, null, new Rect(top.X - 3, top.Y - 3, 6, 6));
        }
    }

    private void DrawSnapMarker(DrawingContext dc, Func<Point2, Point> map, PlanEditor editor)
    {
        if (!editor.HasCursor)
        {
            return;
        }

        if (editor.RotateAngle is { } angle)
        {
            Point at = map(editor.CursorSnap.Point);
            DrawLabel(dc, string.Create(CultureInfo.InvariantCulture, $"↻ {angle:0.#}°  (Shift: free)"), new Point(at.X + 14, at.Y + 12));
            return;
        }

        if (editor.PushDistance is { } push)
        {
            Point at = map(editor.CursorSnap.Point);
            DrawLabel(dc, string.Create(CultureInfo.InvariantCulture, $"⇕ {push:+0.00;−0.00;0.00} m"), new Point(at.X + 14, at.Y + 12));
            return;
        }

        if (editor.MoveDelta is { } delta)
        {
            Point anchor = map(editor.CursorSnap.Point);
            DrawLabel(dc, string.Create(CultureInfo.InvariantCulture, $"Δ {delta.X:0.00}, {delta.Y:0.00} m"), new Point(anchor.X + 14, anchor.Y + 12));
            return;
        }

        if (editor.Tool == EditorTool.Select)
        {
            return;
        }

        SnapResult snap = editor.CursorSnap;
        Point p = map(snap.Point);
        double h = SnapMarkerSize * 0.5;
        switch (snap.Kind)
        {
            case SnapKind.Endpoint:
                dc.DrawRectangle(null, SnapPen, new Rect(p.X - h, p.Y - h, SnapMarkerSize, SnapMarkerSize));
                break;
            case SnapKind.Grid:
                dc.DrawEllipse(Magenta, null, p, 2.5, 2.5);
                break;
            case SnapKind.Midpoint:
            {
                var triangle = new StreamGeometry();
                using (StreamGeometryContext ctx = triangle.Open())
                {
                    ctx.BeginFigure(new Point(p.X, p.Y - h), isFilled: false, isClosed: true);
                    ctx.LineTo(new Point(p.X + h, p.Y + h), isStroked: true, isSmoothJoin: false);
                    ctx.LineTo(new Point(p.X - h, p.Y + h), isStroked: true, isSmoothJoin: false);
                }

                triangle.Freeze();
                dc.DrawGeometry(null, SnapPen, triangle);
                break;
            }

            case SnapKind.Ortho when snap.OrthoFrom is { } origin:
                dc.DrawLine(OrthoPen, map(origin), p);
                break;
        }

        if (snap.Kind != SnapKind.None)
        {
            DrawLabel(dc, snap.Kind.ToString().ToUpperInvariant(), new Point(p.X + 14, p.Y - 22), Magenta);
        }
    }

    private void DrawLabel(DrawingContext dc, string text, Point at, Brush? foreground = null)
    {
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, LabelFace, 11, foreground ?? TextBrush, pixelsPerDip);
        dc.DrawRectangle(LabelBack, null, new Rect(at.X - 3, at.Y - 1, formatted.Width + 6, formatted.Height + 2));
        dc.DrawText(formatted, at);
    }

    private static void DrawOutline(DrawingContext dc, Func<Point2, Point> map, IReadOnlyList<Point2> outline, bool closed, Pen pen) =>
        DrawOutline(dc, map, outline, closed, pen, null);

    private static void DrawOutline(DrawingContext dc, Func<Point2, Point> map, IReadOnlyList<Point2> outline, bool closed, Pen? pen, Brush? fill)
    {
        if (outline.Count < 2)
        {
            return;
        }

        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(map(outline[0]), isFilled: fill is not null, isClosed: closed);
            for (int i = 1; i < outline.Count; i++)
            {
                ctx.LineTo(map(outline[i]), isStroked: true, isSmoothJoin: false);
            }
        }

        geometry.Freeze();
        dc.DrawGeometry(fill, pen, geometry);
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(Brush brush, double thickness, bool dashed = false)
    {
        var pen = new Pen(brush, thickness);
        if (dashed)
        {
            pen.DashStyle = new DashStyle([4, 3], 0);
        }

        pen.Freeze();
        return pen;
    }
}
