using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Throng.Core.Library;

namespace Throng.App.Views;

/// <summary>
/// A small plot of a distribution's shape (round 017, D-289): the relative density over its range as a filled curve,
/// with the range's ends and the average marked. Fixed values draw as a single bar. Drawn in <see cref="OnRender"/>.
/// </summary>
public sealed class DistributionPlot : FrameworkElement
{
    /// <summary>The distribution to draw.</summary>
    public static readonly DependencyProperty DistributionProperty = DependencyProperty.Register(
        nameof(Distribution), typeof(Distribution), typeof(DistributionPlot), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private const int Samples = 64;
    private const double Pad = 6, LabelHeight = 14, FontSize = 12;

    /// <summary>The distribution to draw.</summary>
    public Distribution? Distribution
    {
        get => (Distribution?)GetValue(DistributionProperty);
        set => SetValue(DistributionProperty, value);
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 20 || h < 20)
        {
            return;
        }

        dc.DrawRectangle(FindBrush("PanelAltBrush", Color.FromRgb(0x16, 0x1B, 0x26)), null, new Rect(0, 0, w, h));
        if (Distribution is not { } d)
        {
            return;
        }

        var plot = new Rect(Pad, Pad, w - (2 * Pad), Math.Max(4, h - (2 * Pad) - LabelHeight));
        (double lo, double hi) = DistributionShape.Range(d);
        double span = hi - lo;
        Brush accent = FindBrush("AccentBrush", Colors.Cyan);
        var fill = new SolidColorBrush(Color.FromArgb(0x50, 0x22, 0xD3, 0xEE));
        fill.Freeze();
        var pen = new Pen(accent, 1.5);
        pen.Freeze();

        double X(double v) => plot.Left + ((v - lo) / span * plot.Width);

        if (d.Kind == DistributionKind.Fixed)
        {
            double x = X(d.Value);
            dc.DrawRectangle(fill, pen, new Rect(x - 2, plot.Top, 4, plot.Height));
        }
        else
        {
            var values = new double[Samples + 1];
            double peak = 0;
            for (int k = 0; k <= Samples; k++)
            {
                values[k] = DistributionShape.Density(d, lo + (span * k / Samples));
                peak = Math.Max(peak, values[k]);
            }

            if (peak > 0)
            {
                var geometry = new StreamGeometry();
                using (StreamGeometryContext g = geometry.Open())
                {
                    g.BeginFigure(new Point(plot.Left, plot.Bottom), isFilled: true, isClosed: true);
                    for (int k = 0; k <= Samples; k++)
                    {
                        double x = plot.Left + (plot.Width * k / Samples);
                        double y = plot.Bottom - (values[k] / peak * plot.Height);
                        g.LineTo(new Point(x, y), isStroked: true, isSmoothJoin: false);
                    }

                    g.LineTo(new Point(plot.Right, plot.Bottom), isStroked: false, isSmoothJoin: false);
                }

                geometry.Freeze();
                dc.DrawGeometry(fill, pen, geometry);
            }
        }

        // The average, dashed.
        double mean = d.ExpectedValue;
        if (mean >= lo && mean <= hi)
        {
            var meanPen = new Pen(FindBrush("YellowBrush", Colors.Gold), 1) { DashStyle = DashStyles.Dash };
            meanPen.Freeze();
            dc.DrawLine(meanPen, new Point(X(mean), plot.Top), new Point(X(mean), plot.Bottom));
        }

        // Range ends under the plot.
        Brush text = FindBrush("TextDimBrush", Colors.Gray);
        Typeface face = new(TryFindResource("MonoFont") as FontFamily ?? new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var left = new FormattedText(Number(lo), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, FontSize, text, dip);
        var right = new FormattedText(Number(hi), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, FontSize, text, dip);
        dc.DrawText(left, new Point(plot.Left, plot.Bottom + 1));
        dc.DrawText(right, new Point(plot.Right - right.Width, plot.Bottom + 1));
    }

    private Brush FindBrush(string key, Color fallback) =>
        TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);

    private static string Number(double v) => Math.Abs(v) >= 100
        ? v.ToString("0", CultureInfo.InvariantCulture)
        : v.ToString("0.##", CultureInfo.InvariantCulture);
}
