using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Throng.Core.Editing;
using Throng.Core.Geometry;
using Throng.Core.Model;

namespace Throng.App.Editing;

/// <summary>
/// Immediate-mode WPF overlay drawn over the GL viewport in plan view: hover and selection outlines, vertex grips,
/// rubber bands, shape previews, snap markers and a dimension readout. Redrawn whenever the editor or the plan
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

    private static readonly Pen SelectionPen = FrozenPen(Yellow, 2);
    private static readonly Pen HoverPen = FrozenPen(YellowDim, 1);
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

    /// <summary>True in plan view; the overlay draws nothing in 3D.</summary>
    public bool IsPlanActive { get; set; }

    /// <summary>Creates the overlay.</summary>
    public EditorOverlay()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext dc)
    {
        if (!IsPlanActive || Editor is not { } editor || PlanToScreen is not { } map)
        {
            return;
        }

        // Clip so previews can't spill over the HUD panels outside the viewport.
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight)));

        if (editor.Hover is { } hover && editor.SelectedRegions.All(r => r.Id != hover.Id))
        {
            DrawOutline(dc, map, hover.Outline, RegionPicking.IsClosed(hover), HoverPen);
        }

        DrawSelection(dc, map, editor);
        DrawRubberBand(dc, map, editor);
        DrawShapePreview(dc, map, editor);
        DrawSnapMarker(dc, map, editor);

        dc.Pop();
    }

    private static void DrawSelection(DrawingContext dc, Func<Point2, Point> map, PlanEditor editor)
    {
        Point2? delta = editor.MoveDelta;
        var vertexPreview = editor.VertexPreview;

        foreach (Region region in editor.SelectedRegions)
        {
            bool closed = RegionPicking.IsClosed(region);
            if (delta is { } d)
            {
                // Ghost where it was, solid where it's going.
                DrawOutline(dc, map, region.Outline, closed, GhostPen);
                DrawOutline(dc, map, ShapeFactory.Translate(region.Outline, d), closed, SelectionPen);
            }
            else if (vertexPreview is { } vp && vp.Region.Id == region.Id)
            {
                DrawOutline(dc, map, region.Outline, closed, GhostPen);
                DrawOutline(dc, map, vp.Outline, closed, SelectionPen);
            }
            else
            {
                DrawOutline(dc, map, region.Outline, closed, SelectionPen);
            }
        }

        if (delta is null && editor.GripRegion is { } gripRegion)
        {
            IReadOnlyList<Point2> outline = vertexPreview is { } vp && vp.Region.Id == gripRegion.Id ? vp.Outline : gripRegion.Outline;
            DrawGrips(dc, map, outline, RegionPicking.IsClosed(gripRegion), editor.SelectedVertex, editor.MetresPerDip);
        }
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

        Rect rect = new(map(new Point2(band.Box.MinX, band.Box.MaxY)), map(new Point2(band.Box.MaxX, band.Box.MinY)));
        dc.DrawRectangle(band.Crossing ? GreenFill : AccentFill, band.Crossing ? CrossingPen : WindowPen, rect);
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
            case EditorTool.Rectangle when points.Count == 1:
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

    private void DrawSnapMarker(DrawingContext dc, Func<Point2, Point> map, PlanEditor editor)
    {
        if (!editor.HasCursor)
        {
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

    private static void DrawOutline(DrawingContext dc, Func<Point2, Point> map, IReadOnlyList<Point2> outline, bool closed, Pen pen)
    {
        if (outline.Count < 2)
        {
            return;
        }

        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(map(outline[0]), isFilled: false, isClosed: closed);
            for (int i = 1; i < outline.Count; i++)
            {
                ctx.LineTo(map(outline[i]), isStroked: true, isSmoothJoin: false);
            }
        }

        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
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
