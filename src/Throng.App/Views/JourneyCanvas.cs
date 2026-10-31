using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Throng.Core.Library;

namespace Throng.App.Views;

/// <summary>
/// The JOURNEY view (D-308, D-309, D-323–D-327): a journey graph drawn as nodes and bezier wires, edited in place.
/// Our own control, no dependency: everything is drawn in <see cref="OnRender"/> and hit-tested here; every edit goes
/// to the <see cref="Host"/> (the selected journey's view model), which puts it through the undo stack.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Left-drag a node to move it; left-drag from an output port to a node to wire it (dropped on empty canvas: a
/// menu adds a node there, wired); left-drag from an input port takes its last wire off to move or remove it.</item>
/// <item>Click a node or a wire to select it; Del deletes it. Click a chip on a node for its dropdown (D-323).</item>
/// <item>The palette strip on the left: drag a kind onto the canvas, or click it to add one in the middle (D-325).
/// Right-click empty canvas for the add menu, a node for its menu.</item>
/// <item>Left-drag on empty canvas, middle-drag or right-drag pans; the wheel zooms about the cursor; Home / F
/// fits.</item>
/// </list>
/// Node positions are the model's <see cref="JourneyNode.X"/> / <see cref="JourneyNode.Y"/> in canvas units (DIPs at
/// zoom 1).
/// </remarks>
public sealed class JourneyCanvas : FrameworkElement
{
    /// <summary>Node width, canvas units.</summary>
    public const double NodeWidth = 190;

    /// <summary>Header height (kind and title), canvas units.</summary>
    public const double HeaderHeight = 38;

    /// <summary>Height of one chip row, canvas units.</summary>
    public const double ChipRow = 20;

    /// <summary>Height of one output port row, canvas units.</summary>
    public const double PortRow = 18;

    /// <summary>Height of the run overlay's stats line under the header (round 019), canvas units.</summary>
    public const double StatsRow = 16;

    private const double StripChipWidth = 86, StripProfileWidth = 150, StripHeight = 22, StripTop = 8, StripRight = 12, StripGap = 6;

    private const double PortRadius = 4.5, PortHitRadius = 9, WireHitPx = 6, MinScale = 0.25, MaxScale = 3.0, FitMargin = 40, ClickSlop = 4;
    private const double PaletteWidth = 112, PaletteTop = 40, PaletteItem = 26;

    /// <summary>The kinds offered by the palette and the add menu (Spawn is never added: every journey has one).</summary>
    public static readonly IReadOnlyList<JourneyNodeKind> AddableKinds =
    [
        JourneyNodeKind.Device, JourneyNodeKind.PassZone, JourneyNodeKind.Dwell, JourneyNodeKind.Chance,
        JourneyNodeKind.TagCheck, JourneyNodeKind.SetTag, JourneyNodeKind.Sink,
    ];

    /// <summary>The journey drawn (null = an empty canvas with a hint).</summary>
    public static readonly DependencyProperty JourneyProperty = DependencyProperty.Register(
        nameof(Journey), typeof(JourneyTemplate), typeof(JourneyCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((JourneyCanvas)d).OnJourneyChanged()));

    /// <summary>Who applies the edits and supplies the chips (the selected journey's view model).</summary>
    public static readonly DependencyProperty HostProperty = DependencyProperty.Register(
        nameof(Host), typeof(IJourneyCanvasHost), typeof(JourneyCanvas), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The selected node's id (two-way).</summary>
    public static readonly DependencyProperty SelectedNodeIdProperty = DependencyProperty.Register(
        nameof(SelectedNodeId), typeof(Guid?), typeof(JourneyCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    /// <summary>Bumped by the view model whenever the journey changed in place, to redraw.</summary>
    public static readonly DependencyProperty RevisionProperty = DependencyProperty.Register(
        nameof(Revision), typeof(int), typeof(JourneyCanvas), new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Brush CanvasFill = Frozen(new SolidColorBrush(Color.FromRgb(0x0B, 0x0F, 0x17)));
    private static readonly Brush NodeFill = Frozen(new SolidColorBrush(Color.FromRgb(0x11, 0x17, 0x25)));
    private static readonly Brush HeaderFill = Frozen(new SolidColorBrush(Color.FromRgb(0x17, 0x20, 0x33)));
    private static readonly Brush ChipFill = Frozen(new SolidColorBrush(Color.FromRgb(0x0D, 0x13, 0x20)));
    private static readonly Brush PaletteFill = Frozen(new SolidColorBrush(Color.FromArgb(0xE6, 0x11, 0x17, 0x25)));
    private static readonly Brush TextBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xD8, 0xDE, 0xE9)));
    private static readonly Brush DimBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x8A, 0x96, 0xAD)));
    private static readonly Pen GridPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0x15, 0x1C, 0x2B))), 1));
    private static readonly Pen BorderPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0x24, 0x30, 0x4A))), 1));
    private static readonly Pen SelectedPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0x22, 0xD3, 0xEE))), 2.5));
    private static readonly Pen WirePen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0x8A, 0x96, 0xAD))), 2));
    private static readonly Pen SelectedWirePen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0x22, 0xD3, 0xEE))), 3));
    private static readonly Pen DragWirePen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0xFA, 0xCC, 0x15))), 2) { DashStyle = DashStyles.Dash });
    private static readonly Pen BrokenWirePen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71))), 2) { DashStyle = DashStyles.Dash });
    private static readonly Brush FlowBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x5E, 0xC8, 0xE0)));
    private static readonly Brush LabelFill = Frozen(new SolidColorBrush(Color.FromArgb(0xE0, 0x07, 0x0A, 0x11)));
    private static readonly Brush StatsBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xA7, 0xF3, 0xD0)));
    private static readonly Brush LitFill = Frozen(new SolidColorBrush(Color.FromRgb(0x0E, 0x4A, 0x57)));

    // Overlay pens by thickness (2–8 px in steps of 0.5), made once.
    private static readonly Pen[] FlowPens = [.. Enumerable.Range(0, 13).Select(k => Frozen(new Pen(FlowBrush, 2 + (k * 0.5))))];

    private enum DragMode
    {
        None,
        Pan,
        Node,
        Wire,
        Palette,
    }

    private Point _offset = new(140, 40);
    private double _scale = 1;
    private bool _fitPending = true;
    private Guid? _fittedId;

    // Pointer state.
    private DragMode _drag;
    private Point _pressScreen;
    private Point _lastScreen;
    private bool _moved;
    private Guid _dragNode;
    private Vector _dragOffset;
    private Point _dragPos;
    private Guid _wireFrom;
    private int _wirePort;
    private bool _rewire;
    private Point _wireEnd;
    private JourneyNodeKind _paletteKind;
    private (Guid From, int Port)? _selectedWire;
    private Point? _rightPress;

    /// <summary>Creates the canvas.</summary>
    public JourneyCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
        SizeChanged += (_, _) =>
        {
            if (_fitPending)
            {
                Fit();
            }
        };
    }

    /// <summary>The journey drawn.</summary>
    public JourneyTemplate? Journey
    {
        get => (JourneyTemplate?)GetValue(JourneyProperty);
        set => SetValue(JourneyProperty, value);
    }

    /// <summary>The editing host.</summary>
    public IJourneyCanvasHost? Host
    {
        get => (IJourneyCanvasHost?)GetValue(HostProperty);
        set => SetValue(HostProperty, value);
    }

    /// <summary>The selected node's id.</summary>
    public Guid? SelectedNodeId
    {
        get => (Guid?)GetValue(SelectedNodeIdProperty);
        set => SetValue(SelectedNodeIdProperty, value);
    }

    /// <summary>Redraw trigger.</summary>
    public int Revision
    {
        get => (int)GetValue(RevisionProperty);
        set => SetValue(RevisionProperty, value);
    }

    private bool CanEdit => Host?.CanEdit == true && Journey is not null;

    /// <summary>Accent colour per node kind.</summary>
    public static Color KindColor(JourneyNodeKind kind) => kind switch
    {
        JourneyNodeKind.Spawn => Color.FromRgb(0x4A, 0xDE, 0x80),
        JourneyNodeKind.Sink => Color.FromRgb(0xF8, 0x71, 0x71),
        JourneyNodeKind.Device => Color.FromRgb(0x22, 0xD3, 0xEE),
        JourneyNodeKind.PassZone or JourneyNodeKind.Dwell => Color.FromRgb(0xFA, 0xCC, 0x15),
        JourneyNodeKind.Chance => Color.FromRgb(0xE8, 0x79, 0xF9),
        JourneyNodeKind.TagCheck => Color.FromRgb(0xFB, 0x92, 0x3C),
        _ => Color.FromRgb(0x94, 0xA3, 0xB8),
    };

    /// <summary>Upper-case kind name for the node header.</summary>
    public static string KindName(JourneyNode node) => node.Kind switch
    {
        JourneyNodeKind.Spawn => "SPAWN",
        JourneyNodeKind.Sink => node.SinkKind == JourneySinkKind.BoardTrain ? "SINK · TRAIN" : node.SinkKind == JourneySinkKind.Zone ? "SINK · ZONE" : "SINK",
        _ => KindName(node.Kind),
    };

    /// <summary>Upper-case name of a kind (palette, menus).</summary>
    public static string KindName(JourneyNodeKind kind) => kind switch
    {
        JourneyNodeKind.Spawn => "SPAWN",
        JourneyNodeKind.Sink => "SINK",
        JourneyNodeKind.Device => "DEVICE",
        JourneyNodeKind.PassZone => "PASS ZONE",
        JourneyNodeKind.Dwell => "DWELL",
        JourneyNodeKind.Chance => "CHANCE",
        JourneyNodeKind.TagCheck => "TAG CHECK",
        JourneyNodeKind.SetTag => "SET TAG",
        _ => kind.ToString().ToUpperInvariant(),
    };

    /// <summary>Frames the whole graph.</summary>
    public void Fit()
    {
        if (ActualWidth <= 1 || ActualHeight <= 1)
        {
            _fitPending = true;
            return;
        }

        _fitPending = false;
        if (Journey is not { Nodes.Count: > 0 } journey)
        {
            _scale = 1;
            _offset = new Point(PaletteWidth + FitMargin, FitMargin);
            InvalidateVisual();
            return;
        }

        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (JourneyNode node in journey.Nodes.Where(n => n is not null))
        {
            minX = Math.Min(minX, node.X);
            minY = Math.Min(minY, node.Y);
            maxX = Math.Max(maxX, node.X + NodeWidth);
            maxY = Math.Max(maxY, node.Y + NodeHeight(node));
        }

        double left = PaletteWidth + 8;
        double width = Math.Max(1, maxX - minX), height = Math.Max(1, maxY - minY);
        double availableWidth = ActualWidth - left - (2 * FitMargin), availableHeight = ActualHeight - (2 * FitMargin) - 24;
        _scale = Math.Clamp(Math.Min(availableWidth / width, availableHeight / height), MinScale, 1.25);
        _offset = new Point(
            left + ((ActualWidth - left - (width * _scale)) / 2) - (minX * _scale),
            ((ActualHeight - 24 - (height * _scale)) / 2) - (minY * _scale));
        InvalidateVisual();
    }

    // ------------------------------------------------------------------ geometry

    private int ChipCount(JourneyNode node) => Host?.ChipTexts(node).Count ?? 0;

    /// <summary>The overlay now (empty without a host).</summary>
    private JourneyOverlay Overlay => Host?.Overlay ?? JourneyOverlay.Empty;

    /// <summary>The stats line's height: one row on every node while the overlay has node lines, else none.</summary>
    private double StatsHeight => Overlay.NodeLines.Count > 0 ? StatsRow : 0;

    /// <summary>Top of the chips, below the header and the overlay's stats line.</summary>
    private double BodyTop => HeaderHeight + StatsHeight;

    /// <summary>A node's height: header, stats line, chips, then one row per output port (at least one).</summary>
    private double NodeHeight(JourneyNode node) => BodyTop + (ChipCount(node) * ChipRow) + (Math.Max(1, JourneyGraph.OutputCount(node)) * PortRow) + 6;

    /// <summary>Where a node is drawn (the drag position while it's being moved).</summary>
    private Point NodeAtPos(JourneyNode node) => _drag == DragMode.Node && _moved && node.Id == _dragNode ? _dragPos : new Point(node.X, node.Y);

    private Point InputPort(JourneyNode node)
    {
        Point at = NodeAtPos(node);
        return new Point(at.X, at.Y + (HeaderHeight / 2));
    }

    private Point OutputPort(JourneyNode node, int port)
    {
        Point at = NodeAtPos(node);
        return node.Kind == JourneyNodeKind.Spawn
            ? new Point(at.X + NodeWidth, at.Y + (HeaderHeight / 2))
            : new Point(at.X + NodeWidth, at.Y + BodyTop + (ChipCount(node) * ChipRow) + (PortRow * (port + 0.5)) + 2);
    }

    private Rect ChipRect(JourneyNode node, int chip)
    {
        Point at = NodeAtPos(node);
        return new Rect(at.X + 8, at.Y + BodyTop + (chip * ChipRow) + 1, NodeWidth - 16, ChipRow - 3);
    }

    private Rect NodeRect(JourneyNode node)
    {
        Point at = NodeAtPos(node);
        return new Rect(at.X, at.Y, NodeWidth, NodeHeight(node));
    }

    private Point ToCanvas(Point screen) => new((screen.X - _offset.X) / _scale, (screen.Y - _offset.Y) / _scale);

    // ------------------------------------------------------------------ drawing

    /// <inheritdoc />
    protected override void OnRender(DrawingContext dc)
    {
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        dc.DrawRectangle(CanvasFill, null, bounds);
        DrawGrid(dc, bounds);

        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        FontFamily retro = TryFindResource("RetroFont") as FontFamily ?? new FontFamily("Consolas");
        var titleFace = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        var bodyFace = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var retroFace = new Typeface(retro, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        if (Journey is not { } journey)
        {
            DrawText(dc, "Pick a journey in LIBRARY ▸ JOURNEYS to see its graph.", retroFace, 18, DimBrush, new Point(16, 16), dpi);
            return;
        }

        dc.PushTransform(new MatrixTransform(_scale, 0, 0, _scale, _offset.X, _offset.Y));
        JourneyOverlay overlay = Overlay;
        foreach (JourneyWire wire in journey.Wires.Where(w => w is not null))
        {
            DrawWire(dc, journey, wire, overlay);
        }

        if (_drag == DragMode.Wire && _moved && JourneyGraph.Find(journey, _wireFrom) is { } source)
        {
            dc.DrawGeometry(null, DragWirePen, Bezier(OutputPort(source, _wirePort), _wireEnd));
        }

        Guid? selected = SelectedNodeId;
        foreach (JourneyNode node in journey.Nodes.Where(n => n is not null))
        {
            DrawNode(dc, journey, node, node.Id == selected, titleFace, bodyFace, retroFace, dpi);
        }

        // The overlay's wire labels go on top of the nodes, so a label is never hidden (D-332).
        if (overlay.Wires.Count > 0)
        {
            DrawWireLabels(dc, journey, overlay, bodyFace, dpi);
        }

        dc.Pop();

        DrawText(dc, "JOURNEY ▸ " + journey.Name.ToUpperInvariant(), retroFace, 20, TextBrush, new Point(CanEdit ? PaletteWidth + 14 : 14, 8), dpi);
        string hint = CanEdit
            ? "Drag a node to move it · drag from an output to a node to wire it · click a chip for its choices · Del deletes · right-click to add · Esc or PLAN / 3D / ISO to go back"
            : "Read-only while running · drag to pan · wheel to zoom · Home to fit · Esc or PLAN / 3D / ISO to go back";
        DrawText(dc, hint, bodyFace, 12, DimBrush, new Point(CanEdit ? PaletteWidth + 14 : 14, ActualHeight - 22), dpi);

        if (CanEdit)
        {
            DrawPalette(dc, retroFace, dpi);
        }

        if (Host is not null)
        {
            DrawStrip(dc, overlay, retroFace, bodyFace, dpi);
        }

        if (_drag == DragMode.Palette && _moved)
        {
            var ghost = new SolidColorBrush(Color.FromArgb(0x60, 0x22, 0xD3, 0xEE));
            ghost.Freeze();
            dc.DrawRoundedRectangle(ghost, SelectedPen, new Rect(_lastScreen.X - (NodeWidth * _scale / 2), _lastScreen.Y - (15 * _scale), NodeWidth * _scale, HeaderHeight * _scale), 4, 4);
        }
    }

    private void DrawGrid(DrawingContext dc, Rect bounds)
    {
        double step = 40 * _scale;
        if (step < 12)
        {
            return;
        }

        for (double x = _offset.X % step; x < bounds.Width; x += step)
        {
            dc.DrawLine(GridPen, new Point(x, 0), new Point(x, bounds.Height));
        }

        for (double y = _offset.Y % step; y < bounds.Height; y += step)
        {
            dc.DrawLine(GridPen, new Point(0, y), new Point(bounds.Width, y));
        }
    }

    private void DrawWire(DrawingContext dc, JourneyTemplate journey, JourneyWire wire, JourneyOverlay overlay)
    {
        JourneyNode? from = JourneyGraph.Find(journey, wire.From);
        JourneyNode? to = JourneyGraph.Find(journey, wire.To);
        if (from is null || to is null)
        {
            return;
        }

        bool broken = wire.Port < 0 || wire.Port >= JourneyGraph.OutputCount(from) || to.Kind == JourneyNodeKind.Spawn;
        bool selected = _selectedWire is { } s && s.From == wire.From && s.Port == wire.Port;
        Point a = OutputPort(from, Math.Clamp(wire.Port, 0, Math.Max(0, JourneyGraph.OutputCount(from) - 1)));
        Pen pen = broken ? BrokenWirePen : selected ? SelectedWirePen : WirePen;
        if (!broken && !selected && overlay.Wires.TryGetValue((wire.From, wire.Port), out JourneyWireLabel flow))
        {
            // Thickness by share (D-332): 2 px for none, 8 px for everyone.
            pen = FlowPens[(int)Math.Round(Math.Clamp(flow.Share, 0, 1) * (FlowPens.Length - 1))];
        }

        dc.DrawGeometry(null, pen, Bezier(a, InputPort(to)));
    }

    /// <summary>
    /// The overlay's labels (D-332): at each wire's midpoint on a dark backing; for an unwired output (people leaving
    /// by the rule's sinks) just right of its port.
    /// </summary>
    private void DrawWireLabels(DrawingContext dc, JourneyTemplate journey, JourneyOverlay overlay, Typeface body, double dpi)
    {
        foreach (((Guid fromId, int port), JourneyWireLabel label) in overlay.Wires)
        {
            if (label.Text.Length == 0 || JourneyGraph.Find(journey, fromId) is not { } from || port < 0 || port >= JourneyGraph.OutputCount(from))
            {
                continue;
            }

            Point a = OutputPort(from, port);
            FormattedText text = Text(label.Text, body, 11, TextBrush, dpi);
            Point at;
            if (JourneyGraph.Next(journey, fromId, port) is { } to)
            {
                Point mid = BezierMid(a, InputPort(to));
                at = new Point(mid.X - (text.Width / 2), mid.Y - (text.Height / 2));
            }
            else
            {
                text = Text("→ " + label.Text, body, 11, DimBrush, dpi);
                at = new Point(a.X + PortRadius + 6, a.Y - (text.Height / 2));
            }

            dc.DrawRoundedRectangle(LabelFill, BorderPen, new Rect(at.X - 4, at.Y - 1, text.Width + 8, text.Height + 2), 3, 3);
            dc.DrawText(text, at);
        }
    }

    private static (Point C1, Point C2) BezierControls(Point a, Point b)
    {
        double reach = Math.Max(40, Math.Abs(b.X - a.X) * 0.5);
        return (new Point(a.X + reach, a.Y), new Point(b.X - reach, b.Y));
    }

    private static PathGeometry Bezier(Point a, Point b)
    {
        (Point c1, Point c2) = BezierControls(a, b);
        var figure = new PathFigure { StartPoint = a };
        figure.Segments.Add(new BezierSegment(c1, c2, b, true));
        var geometry = new PathGeometry([figure]);
        geometry.Freeze();
        return geometry;
    }

    /// <summary>The point halfway along the wire's bezier (t = 0.5).</summary>
    private static Point BezierMid(Point a, Point b)
    {
        (Point c1, Point c2) = BezierControls(a, b);
        return new Point((a.X + (3 * c1.X) + (3 * c2.X) + b.X) / 8, (a.Y + (3 * c1.Y) + (3 * c2.Y) + b.Y) / 8);
    }

    // ------------------------------------------------------------------ overlay strip (D-333, D-334)

    private Rect ActualChipRect => new(ActualWidth - StripRight - StripProfileWidth - StripGap - (2 * StripChipWidth) - StripGap, StripTop, StripChipWidth, StripHeight);

    private Rect PredictedChipRect => new(ActualWidth - StripRight - StripProfileWidth - StripGap - StripChipWidth, StripTop, StripChipWidth, StripHeight);

    private Rect ProfileChipRect => new(ActualWidth - StripRight - StripProfileWidth, StripTop, StripProfileWidth, StripHeight);

    /// <summary>Top right: the state text, ACTUAL / PREDICTED and the profile filter.</summary>
    private void DrawStrip(DrawingContext dc, JourneyOverlay overlay, Typeface retro, Typeface body, double dpi)
    {
        Rect actual = ActualChipRect, predicted = PredictedChipRect, profile = ProfileChipRect;
        if (actual.Left < PaletteWidth + 220)
        {
            // Too narrow for the strip next to the title.
            return;
        }

        DrawStripChip(dc, actual, "ACTUAL", !overlay.IsPredicted, enabled: true, retro, dpi);
        DrawStripChip(dc, predicted, "PREDICTED", overlay.IsPredicted, overlay.HasPrediction, retro, dpi);
        dc.DrawRoundedRectangle(ChipFill, BorderPen, profile, 3, 3);
        DrawText(dc, overlay.ProfileText, body, 11, TextBrush, new Point(profile.X + 6, profile.Y + 3), dpi, profile.Width - 10);
        if (overlay.StateText.Length > 0)
        {
            FormattedText state = Text(overlay.StateText, retro, 14, DimBrush, dpi);
            dc.DrawText(state, new Point(actual.Left - StripGap - 4 - state.Width, actual.Top + ((StripHeight - state.Height) / 2)));
        }
    }

    private static void DrawStripChip(DrawingContext dc, Rect rect, string text, bool lit, bool enabled, Typeface retro, double dpi)
    {
        dc.DrawRoundedRectangle(lit ? LitFill : ChipFill, lit ? SelectedPen : BorderPen, rect, 3, 3);
        FormattedText label = Text(text, retro, 14, enabled ? TextBrush : DimBrush, dpi);
        dc.DrawText(label, new Point(rect.X + ((rect.Width - label.Width) / 2), rect.Y + ((rect.Height - label.Height) / 2)));
    }

    /// <summary>Handles a click on the strip; true when it was on the strip.</summary>
    private bool ClickStrip(Point screen)
    {
        if (Host is not { } host || Journey is null || ActualChipRect.Left < PaletteWidth + 220)
        {
            return false;
        }

        if (ActualChipRect.Contains(screen))
        {
            host.SetOverlayPredicted(false);
        }
        else if (PredictedChipRect.Contains(screen))
        {
            host.SetOverlayPredicted(true);
        }
        else if (ProfileChipRect.Contains(screen))
        {
            IReadOnlyList<JourneyChipOption> options = host.OverlayProfileOptions();
            if (options.Count > 0)
            {
                var menu = new ContextMenu();
                foreach (JourneyChipOption option in options)
                {
                    var item = new MenuItem { Header = option.Label, IsChecked = option.IsCurrent };
                    item.Click += (_, _) => host.ChooseOverlayProfile(option);
                    menu.Items.Add(item);
                }

                Show(menu);
            }
        }
        else
        {
            return false;
        }

        InvalidateVisual();
        return true;
    }

    private void DrawNode(DrawingContext dc, JourneyTemplate journey, JourneyNode node, bool isSelected, Typeface title, Typeface body, Typeface retro, double dpi)
    {
        Rect rect = NodeRect(node);
        var accent = new SolidColorBrush(KindColor(node.Kind));
        accent.Freeze();
        dc.DrawRoundedRectangle(NodeFill, isSelected ? SelectedPen : BorderPen, rect, 4, 4);
        dc.DrawRoundedRectangle(HeaderFill, null, new Rect(rect.X + 1, rect.Y + 1, NodeWidth - 2, HeaderHeight - 2), 3, 3);
        dc.DrawRectangle(accent, null, new Rect(rect.X + 1, rect.Y + 1, 4, HeaderHeight - 2));

        DrawText(dc, KindName(node), retro, 13, accent, new Point(rect.X + 10, rect.Y + 2), dpi, NodeWidth - 16);
        DrawText(dc, node.Title, title, 12, TextBrush, new Point(rect.X + 10, rect.Y + 18), dpi, NodeWidth - 16);

        // The run overlay's stats line (D-335).
        if (StatsHeight > 0 && Overlay.NodeLines.TryGetValue(node.Id, out string? stats))
        {
            DrawText(dc, stats, body, 10.5, StatsBrush, new Point(rect.X + 10, rect.Y + HeaderHeight), dpi, NodeWidth - 16);
        }

        // Dropdown chips (D-323).
        IReadOnlyList<string> chips = Host?.ChipTexts(node) ?? [];
        for (int k = 0; k < chips.Count; k++)
        {
            Rect chip = ChipRect(node, k);
            dc.DrawRoundedRectangle(ChipFill, BorderPen, chip, 3, 3);
            DrawText(dc, chips[k], body, 11, TextBrush, new Point(chip.X + 6, chip.Y + 1), dpi, chip.Width - 10);
        }

        if (JourneyGraph.HasInput(node))
        {
            dc.DrawEllipse(NodeFill, new Pen(accent, 1.5), InputPort(node), PortRadius, PortRadius);
        }

        int outputs = JourneyGraph.OutputCount(node);
        for (int port = 0; port < outputs; port++)
        {
            Point p = OutputPort(node, port);
            bool wired = JourneyGraph.Next(journey, node.Id, port) is not null;
            dc.DrawEllipse(wired ? accent : NodeFill, new Pen(accent, 1.5), p, PortRadius, PortRadius);
            string label = JourneyGraph.PortLabel(node, port);
            if (label.Length > 0)
            {
                FormattedText text = Text(label, body, 11, DimBrush, dpi);
                dc.DrawText(text, new Point(p.X - PortRadius - 4 - text.Width, p.Y - (text.Height / 2)));
            }
        }

        if (outputs == 0)
        {
            DrawText(dc, "leaves the study", body, 11, DimBrush, new Point(rect.X + 10, rect.Y + BodyTop + (chips.Count * ChipRow) + 1), dpi, NodeWidth - 16);
        }
    }

    private void DrawPalette(DrawingContext dc, Typeface retro, double dpi)
    {
        dc.DrawRectangle(PaletteFill, BorderPen, new Rect(0, 0, PaletteWidth, ActualHeight));
        DrawText(dc, "ADD", retro, 15, DimBrush, new Point(10, 12), dpi);
        for (int k = 0; k < AddableKinds.Count; k++)
        {
            JourneyNodeKind kind = AddableKinds[k];
            var rect = new Rect(8, PaletteTop + (k * PaletteItem), PaletteWidth - 16, PaletteItem - 4);
            var accent = new SolidColorBrush(KindColor(kind));
            accent.Freeze();
            dc.DrawRoundedRectangle(HeaderFill, BorderPen, rect, 3, 3);
            dc.DrawRectangle(accent, null, new Rect(rect.X + 1, rect.Y + 1, 4, rect.Height - 2));
            DrawText(dc, KindName(kind), retro, 13, TextBrush, new Point(rect.X + 10, rect.Y + 2), dpi, rect.Width - 12);
        }
    }

    private static FormattedText Text(string text, Typeface face, double size, Brush brush, double dpi) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, size, brush, dpi);

    private static void DrawText(DrawingContext dc, string text, Typeface face, double size, Brush brush, Point at, double dpi, double maxWidth = 0)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        FormattedText formatted = Text(text, face, size, brush, dpi);
        if (maxWidth > 0)
        {
            formatted.MaxTextWidth = maxWidth;
            formatted.MaxLineCount = 1;
            formatted.Trimming = TextTrimming.CharacterEllipsis;
        }

        dc.DrawText(formatted, at);
    }

    private void OnJourneyChanged()
    {
        // Re-frame only when a different journey is shown, not when the same one changed (an edit).
        Guid? id = Journey?.Id;
        _selectedWire = null;
        if (id != _fittedId)
        {
            _fittedId = id;
            _fitPending = true;
            Fit();
        }
    }

    // ------------------------------------------------------------------ hit tests

    private JourneyNode? NodeAt(Point canvas)
    {
        if (Journey is not { } journey)
        {
            return null;
        }

        for (int k = journey.Nodes.Count - 1; k >= 0; k--)
        {
            JourneyNode node = journey.Nodes[k];
            if (node is not null && NodeRect(node).Contains(canvas))
            {
                return node;
            }
        }

        return null;
    }

    private (JourneyNode Node, int Port)? OutputPortAt(Point canvas)
    {
        if (Journey is not { } journey)
        {
            return null;
        }

        foreach (JourneyNode node in journey.Nodes.Where(n => n is not null))
        {
            for (int port = 0; port < JourneyGraph.OutputCount(node); port++)
            {
                if ((OutputPort(node, port) - canvas).Length <= PortHitRadius)
                {
                    return (node, port);
                }
            }
        }

        return null;
    }

    private JourneyNode? InputPortAt(Point canvas) =>
        Journey?.Nodes.FirstOrDefault(n => n is not null && JourneyGraph.HasInput(n) && (InputPort(n) - canvas).Length <= PortHitRadius);

    private int ChipAt(JourneyNode node, Point canvas)
    {
        int count = ChipCount(node);
        for (int k = 0; k < count; k++)
        {
            if (ChipRect(node, k).Contains(canvas))
            {
                return k;
            }
        }

        return -1;
    }

    private JourneyWire? WireAt(Point canvas)
    {
        if (Journey is not { } journey)
        {
            return null;
        }

        double tolerance = WireHitPx / _scale;
        foreach (JourneyWire wire in journey.Wires.Where(w => w is not null))
        {
            if (JourneyGraph.Find(journey, wire.From) is not { } from || JourneyGraph.Find(journey, wire.To) is not { } to)
            {
                continue;
            }

            PathGeometry geometry = Bezier(OutputPort(from, Math.Clamp(wire.Port, 0, Math.Max(0, JourneyGraph.OutputCount(from) - 1))), InputPort(to));
            if (geometry.StrokeContains(new Pen(Brushes.Black, tolerance * 2), canvas))
            {
                return wire;
            }
        }

        return null;
    }

    private JourneyNodeKind? PaletteItemAt(Point screen)
    {
        if (!CanEdit || screen.X < 0 || screen.X > PaletteWidth)
        {
            return null;
        }

        int k = (int)Math.Floor((screen.Y - PaletteTop) / PaletteItem);
        return screen.Y >= PaletteTop && k >= 0 && k < AddableKinds.Count ? AddableKinds[k] : null;
    }

    // ------------------------------------------------------------------ pointer

    /// <inheritdoc />
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        Point p = e.GetPosition(this);
        Point c = ToCanvas(p);
        _pressScreen = p;
        _lastScreen = p;
        _moved = false;
        _drag = DragMode.None;
        e.Handled = true;

        if (e.ChangedButton == MouseButton.Right)
        {
            _rightPress = p;
            _drag = DragMode.Pan;
            CaptureMouse();
            return;
        }

        if (e.ChangedButton == MouseButton.Middle)
        {
            _drag = DragMode.Pan;
            CaptureMouse();
            return;
        }

        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (ClickStrip(p))
        {
            return;
        }

        if (PaletteItemAt(p) is { } kind)
        {
            _drag = DragMode.Palette;
            _paletteKind = kind;
            CaptureMouse();
            return;
        }

        if (CanEdit && OutputPortAt(c) is { } output)
        {
            BeginWire(output.Node.Id, output.Port, rewire: false, c);
            return;
        }

        if (CanEdit && InputPortAt(c) is { } input && Journey!.Wires.LastOrDefault(w => w.To == input.Id) is { } incoming)
        {
            // Take the input's last wire off to move or remove it (D-327).
            BeginWire(incoming.From, incoming.Port, rewire: true, c);
            return;
        }

        if (NodeAt(c) is { } node)
        {
            _selectedWire = null;
            SelectedNodeId = node.Id;
            int chip = CanEdit ? ChipAt(node, c) : -1;
            if (chip >= 0)
            {
                OpenChipMenu(node.Id, chip);
                InvalidateVisual();
                return;
            }

            if (CanEdit)
            {
                _drag = DragMode.Node;
                _dragNode = node.Id;
                _dragOffset = c - new Point(node.X, node.Y);
                _dragPos = new Point(node.X, node.Y);
                CaptureMouse();
            }

            InvalidateVisual();
            return;
        }

        if (WireAt(c) is { } wire)
        {
            _selectedWire = (wire.From, wire.Port);
            SelectedNodeId = null;
            InvalidateVisual();
            return;
        }

        // Empty canvas: clear the selection and pan.
        _selectedWire = null;
        SelectedNodeId = null;
        _drag = DragMode.Pan;
        CaptureMouse();
        InvalidateVisual();
    }

    private void BeginWire(Guid from, int port, bool rewire, Point canvas)
    {
        _drag = DragMode.Wire;
        _wireFrom = from;
        _wirePort = port;
        _rewire = rewire;
        _wireEnd = canvas;
        CaptureMouse();
    }

    /// <inheritdoc />
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_drag == DragMode.None)
        {
            return;
        }

        Point p = e.GetPosition(this);
        if ((p - _pressScreen).Length > ClickSlop)
        {
            _moved = true;
        }

        switch (_drag)
        {
            case DragMode.Pan when _moved:
                _offset = new Point(_offset.X + (p.X - _lastScreen.X), _offset.Y + (p.Y - _lastScreen.Y));
                break;
            case DragMode.Node:
                _dragPos = ToCanvas(p) - _dragOffset;
                break;
            case DragMode.Wire:
                _wireEnd = ToCanvas(p);
                break;
        }

        _lastScreen = p;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        Point p = e.GetPosition(this);
        Point c = ToCanvas(p);
        DragMode drag = _drag;
        bool moved = _moved;
        _drag = DragMode.None;
        _moved = false;
        ReleaseMouseCapture();
        e.Handled = true;

        if (e.ChangedButton == MouseButton.Right && _rightPress is not null)
        {
            _rightPress = null;
            if (!moved)
            {
                OpenContextMenu(c);
            }

            InvalidateVisual();
            return;
        }

        IJourneyCanvasHost? host = Host;
        switch (drag)
        {
            case DragMode.Node when moved && host is not null:
                host.MoveNode(_dragNode, _dragPos.X, _dragPos.Y);
                break;

            case DragMode.Wire when host is not null:
            {
                JourneyNode? target = NodeAt(c);
                if (target is not null && target.Id != _wireFrom)
                {
                    host.Connect(_wireFrom, _wirePort, target.Id);
                }
                else if (moved && target is null && _rewire)
                {
                    host.Disconnect(_wireFrom, _wirePort);
                }
                else if (moved && target is null)
                {
                    OpenAddMenu(c, _wireFrom, _wirePort);
                }

                break;
            }

            case DragMode.Palette when host is not null:
                if (!moved)
                {
                    // A click: add it in the middle of the view.
                    Point middle = ToCanvas(new Point((PaletteWidth + ActualWidth) / 2, ActualHeight / 2));
                    SelectedNodeId = host.AddNode(_paletteKind, middle.X - (NodeWidth / 2), middle.Y - 20, null, 0) ?? SelectedNodeId;
                }
                else if (p.X > PaletteWidth)
                {
                    SelectedNodeId = host.AddNode(_paletteKind, c.X - (NodeWidth / 2), c.Y - 15, null, 0) ?? SelectedNodeId;
                }

                break;
        }

        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        Point p = e.GetPosition(this);
        Point before = ToCanvas(p);
        _scale = Math.Clamp(_scale * (e.Delta > 0 ? 1.15 : 1 / 1.15), MinScale, MaxScale);
        _offset = new Point(p.X - (before.X * _scale), p.Y - (before.Y * _scale));
        InvalidateVisual();
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.Home or Key.F)
        {
            Fit();
            e.Handled = true;
        }
        else if (e.Key is Key.Delete or Key.Back && CanEdit && Host is { } host)
        {
            if (_selectedWire is { } wire)
            {
                host.Disconnect(wire.From, wire.Port);
                _selectedWire = null;
            }
            else if (SelectedNodeId is { } id)
            {
                host.RemoveNode(id);
            }

            InvalidateVisual();
            e.Handled = true;
        }
    }

    // ------------------------------------------------------------------ menus

    private void OpenChipMenu(Guid nodeId, int chip)
    {
        if (Host is not { } host)
        {
            return;
        }

        IReadOnlyList<JourneyChipOption> options = host.ChipOptions(nodeId, chip);
        if (options.Count == 0)
        {
            return;
        }

        var menu = new ContextMenu();
        foreach (JourneyChipOption option in options)
        {
            var item = new MenuItem { Header = option.Label, IsChecked = option.IsCurrent };
            item.Click += (_, _) => host.ChooseChip(nodeId, chip, option);
            menu.Items.Add(item);
        }

        Show(menu);
    }

    private void OpenContextMenu(Point canvas)
    {
        if (!CanEdit || Host is not { } host)
        {
            return;
        }

        if (NodeAt(canvas) is { } node)
        {
            SelectedNodeId = node.Id;
            var menu = new ContextMenu();
            if (node.Kind != JourneyNodeKind.Spawn)
            {
                var delete = new MenuItem { Header = "DELETE NODE" };
                delete.Click += (_, _) => host.RemoveNode(node.Id);
                menu.Items.Add(delete);
            }

            for (int port = 0; port < JourneyGraph.OutputCount(node); port++)
            {
                if (Journey is { } journey && JourneyGraph.Next(journey, node.Id, port) is not null)
                {
                    int p = port;
                    string label = JourneyGraph.PortLabel(node, port);
                    var unwire = new MenuItem { Header = label.Length > 0 ? $"UNWIRE '{label}'" : "UNWIRE OUTPUT" };
                    unwire.Click += (_, _) => host.Disconnect(node.Id, p);
                    menu.Items.Add(unwire);
                }
            }

            if (menu.Items.Count > 0)
            {
                Show(menu);
            }

            return;
        }

        OpenAddMenu(canvas, null, 0);
    }

    private void OpenAddMenu(Point canvas, Guid? wireFrom, int wirePort)
    {
        if (!CanEdit || Host is not { } host)
        {
            return;
        }

        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = wireFrom is null ? "ADD NODE" : "ADD AND WIRE", IsEnabled = false });
        foreach (JourneyNodeKind kind in AddableKinds)
        {
            var item = new MenuItem { Header = KindName(kind) };
            item.Click += (_, _) =>
            {
                Guid? id = host.AddNode(kind, canvas.X, canvas.Y - 15, wireFrom, wirePort);
                if (id is not null)
                {
                    SelectedNodeId = id;
                }
            };
            menu.Items.Add(item);
        }

        Show(menu);
    }

    private void Show(ContextMenu menu)
    {
        menu.PlacementTarget = this;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    private static T Frozen<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
