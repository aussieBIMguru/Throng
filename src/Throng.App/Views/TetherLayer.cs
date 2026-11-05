using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Throng.App.Views;

/// <summary>
/// Hover and selection tethers (round 024b, D-391): thin lines from a person to each of their group mates, drawn over
/// the viewport in plan, 3D and ISO while a run or replay shows people. Never hit-testable. The viewport sets the lines
/// in screen DIPs each frame (<see cref="Set"/>) and only redraws when they change.
/// </summary>
public sealed class TetherLayer : FrameworkElement
{
    private static readonly Brush LineBrush = Frozen(Color.FromArgb(0xD0, 0xFA, 0xCC, 0x15));
    private static readonly Pen LinePen = FrozenPen(LineBrush, 1.25);
    private static readonly Pen PinnedPen = FrozenPen(Frozen(Color.FromArgb(0xF0, 0x22, 0xD3, 0xEE)), 1.5);
    private static readonly Brush LabelBack = Frozen(Color.FromArgb(0xD0, 0x0B, 0x0F, 0x17));
    private static readonly Typeface LabelFace = new(new FontFamily("Cascadia Mono, Consolas"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    private readonly List<Tether> _tethers = [];

    /// <summary>Creates the layer.</summary>
    public TetherLayer()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
    }

    /// <summary>One person's tethers: where they are, where each mate is, the group size, and whether it was clicked.</summary>
    /// <param name="From">The person, in DIPs.</param>
    /// <param name="Mates">Their mates in view, in DIPs.</param>
    /// <param name="People">People in the group in view (including the person).</param>
    /// <param name="Pinned">True for the group picked with a click (kept until another click), false for the hover.</param>
    public readonly record struct Tether(Point From, IReadOnlyList<Point> Mates, int People, bool Pinned);

    /// <summary>True while any tether is drawn.</summary>
    public bool HasTethers => _tethers.Count > 0;

    /// <summary>Replaces the tethers; redraws only when they changed (or were cleared).</summary>
    public void Set(IReadOnlyList<Tether> tethers)
    {
        if (tethers.Count == 0 && _tethers.Count == 0)
        {
            return;
        }

        _tethers.Clear();
        _tethers.AddRange(tethers);
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext dc)
    {
        if (_tethers.Count == 0)
        {
            return;
        }

        dc.PushClip(new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight)));
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        foreach (Tether tether in _tethers)
        {
            Pen pen = tether.Pinned ? PinnedPen : LinePen;
            foreach (Point mate in tether.Mates)
            {
                dc.DrawLine(pen, tether.From, mate);
                dc.DrawEllipse(null, pen, mate, 4, 4);
            }

            dc.DrawEllipse(null, pen, tether.From, 6, 6);
            string text = string.Create(CultureInfo.InvariantCulture, $"GROUP OF {tether.People}");
            var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, LabelFace, 11, pen.Brush, pixelsPerDip);
            var at = new Point(tether.From.X + 10, tether.From.Y - 22);
            dc.DrawRectangle(LabelBack, null, new Rect(at.X - 3, at.Y - 1, formatted.Width + 6, formatted.Height + 2));
            dc.DrawText(formatted, at);
        }

        dc.Pop();
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness);
        pen.Freeze();
        return pen;
    }
}
