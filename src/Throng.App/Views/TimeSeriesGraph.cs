using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Throng.App.Views;

/// <summary>
/// Hand-drawn time-series graph (round 011c, D-220): value and time axes, faint bands (Fruin grades), the warm-up
/// shaded (D-218), one polyline per series and a small legend. No chart library; drawn in <see cref="OnRender"/>.
/// </summary>
public sealed class TimeSeriesGraph : FrameworkElement
{
    /// <summary>The data to draw.</summary>
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(GraphData), typeof(TimeSeriesGraph), new FrameworkPropertyMetadata(GraphData.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    private const double LeftM = 38, RightM = 18, TopM = 20, BottomM = 18, FontSize = 13;

    /// <summary>The data to draw.</summary>
    public GraphData Data
    {
        get => (GraphData)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        Brush panel = FindBrush("PanelAltBrush", Color.FromRgb(0x16, 0x1B, 0x26));
        Brush text = FindBrush("TextDimBrush", Colors.Gray);
        Brush grid = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
        grid.Freeze();
        dc.DrawRectangle(panel, null, new Rect(0, 0, w, h));
        var plot = new Rect(LeftM, TopM, Math.Max(10, w - LeftM - RightM), Math.Max(10, h - TopM - BottomM));
        GraphData data = Data ?? GraphData.Empty;
        Typeface face = new(TryFindResource("RetroFont") as FontFamily ?? new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        if (data.Times.Length < 2)
        {
            DrawText(dc, "NO DATA YET ▸ PLAY TO RECORD", face, text, dip, plot.Left, plot.Top + (plot.Height / 2) - 8);
            return;
        }

        double tMax = Math.Max(60, data.Times[^1]);
        double top = data.MinTop;
        foreach (GraphLine line in data.Lines)
        {
            foreach (double v in line.Values)
            {
                if (double.IsFinite(v) && v > top)
                {
                    top = v;
                }
            }
        }

        top = NiceCeiling(top * 1.05);
        double X(double t) => plot.Left + (plot.Width * Math.Clamp(t / tMax, 0, 1));
        double Y(double v) => plot.Bottom - (plot.Height * Math.Clamp(v / top, 0, 1));

        dc.PushClip(new RectangleGeometry(plot));
        foreach (GraphBand band in data.Bands)
        {
            double y0 = Y(band.From), y1 = Y(double.IsFinite(band.To) ? band.To : top);
            if (y0 - y1 < 0.5)
            {
                continue;
            }

            var fill = new SolidColorBrush(Color.FromArgb(0x38, band.Colour.R, band.Colour.G, band.Colour.B));
            fill.Freeze();
            dc.DrawRectangle(fill, null, new Rect(plot.Left, y1, plot.Width, y0 - y1));
        }

        if (data.WarmUpS > 0)
        {
            var shade = new SolidColorBrush(Color.FromArgb(0x40, 0x00, 0x00, 0x00));
            shade.Freeze();
            double xw = X(data.WarmUpS);
            dc.DrawRectangle(shade, null, new Rect(plot.Left, plot.Top, Math.Max(0, xw - plot.Left), plot.Height));
        }

        var gridPen = new Pen(grid, 1);
        gridPen.Freeze();
        for (int k = 1; k <= 4; k++)
        {
            double y = Math.Round(Y(top * k / 4)) + 0.5;
            dc.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
        }

        foreach (GraphLine line in data.Lines)
        {
            DrawLine(dc, data.Times, line, X, Y);
        }

        dc.Pop();

        // Axes, labels, legend.
        var axisPen = new Pen(text, 1);
        axisPen.Freeze();
        dc.DrawLine(axisPen, new Point(plot.Left, plot.Top), new Point(plot.Left, plot.Bottom));
        dc.DrawLine(axisPen, new Point(plot.Left, plot.Bottom), new Point(plot.Right, plot.Bottom));
        for (int k = 0; k <= 4; k++)
        {
            double v = top * k / 4;
            DrawText(dc, Number(v), face, text, dip, 2, Y(v) - 8);
        }

        for (int k = 0; k <= 4; k++)
        {
            double t = tMax * k / 4;
            DrawText(dc, Clock(t), face, text, dip, X(t) - (k == 4 ? 34 : k == 0 ? 0 : 16), plot.Bottom + 1);
        }

        DrawText(dc, data.Unit, face, text, dip, 2, 2);
        if (data.WarmUpS > 0 && X(data.WarmUpS) - plot.Left > 60)
        {
            DrawText(dc, "WARM-UP", face, text, dip, plot.Left + 4, plot.Top + 2);
        }

        foreach (GraphBand band in data.Bands)
        {
            double mid = (Math.Max(0, band.From) + Math.Min(top, double.IsFinite(band.To) ? band.To : top)) / 2;
            if (band.From < top && Y(band.From) - Y(Math.Min(top, double.IsFinite(band.To) ? band.To : top)) > 12)
            {
                DrawText(dc, band.Label, face, text, dip, plot.Right + 4, Y(mid) - 8);
            }
        }

        double lx = plot.Left + (data.WarmUpS > 0 ? Math.Max(70, X(data.WarmUpS) - plot.Left + 6) : 6);
        double ly = plot.Top + 2;
        foreach (GraphLine line in data.Lines.Take(8))
        {
            var swatch = new SolidColorBrush(line.Colour);
            swatch.Freeze();
            dc.DrawRectangle(swatch, null, new Rect(lx, ly + 5, 10, 4));
            DrawText(dc, line.Label, face, FindBrush("TextBrush", Colors.White), dip, lx + 14, ly - 1);
            ly += 14;
            if (ly > plot.Bottom - 14)
            {
                break;
            }
        }
    }

    private static void DrawLine(DrawingContext dc, double[] times, GraphLine line, Func<double, double> x, Func<double, double> y)
    {
        int n = Math.Min(times.Length, line.Values.Length);
        if (n < 2)
        {
            return;
        }

        var geometry = new StreamGeometry();
        using (StreamGeometryContext g = geometry.Open())
        {
            g.BeginFigure(new Point(x(times[0]), y(line.Values[0])), isFilled: false, isClosed: false);
            for (int i = 1; i < n; i++)
            {
                g.LineTo(new Point(x(times[i]), y(line.Values[i])), isStroked: true, isSmoothJoin: false);
            }
        }

        geometry.Freeze();
        var pen = new Pen(new SolidColorBrush(line.Colour), 1.6) { LineJoin = PenLineJoin.Round };
        pen.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }

    private static void DrawText(DrawingContext dc, string value, Typeface face, Brush brush, double dip, double x, double y)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        var formatted = new FormattedText(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, FontSize, brush, dip);
        dc.DrawText(formatted, new Point(x, y));
    }

    private Brush FindBrush(string key, Color fallback) =>
        TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);

    private static string Number(double v) => v >= 100
        ? v.ToString("0", CultureInfo.InvariantCulture)
        : v >= 10 ? v.ToString("0.#", CultureInfo.InvariantCulture) : v.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Clock(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, Math.Round(seconds)));
        return t.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{(int)t.TotalMinutes}:{t.Seconds:00}");
    }

    /// <summary>Rounds up to 1, 2, 2.5 or 5 × 10ⁿ so the four grid lines land on round numbers.</summary>
    private static double NiceCeiling(double value)
    {
        if (!(value > 0))
        {
            return 1;
        }

        double power = Math.Pow(10, Math.Floor(Math.Log10(value)));
        foreach (double step in new[] { 1.0, 2.0, 2.5, 5.0, 10.0 })
        {
            if (value <= step * power * (1 + 1e-9))
            {
                return step * power;
            }
        }

        return 10 * power;
    }
}
