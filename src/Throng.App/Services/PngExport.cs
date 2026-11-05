using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Throng.Core.Common;

namespace Throng.App.Services;

/// <summary>
/// PNG export of the viewport (round 012, D-234): the captured view (already at 2×) with a legend and a title block
/// (study, level, sim time, map, "Conceptual, not a compliance tool") drawn over it, encoded with
/// <see cref="PngBitmapEncoder"/>. Problems come back as a <see cref="Result"/>, never thrown.
/// </summary>
public static class PngExport
{
    /// <summary>The disclaimer on every exported image (BRIEF §1).</summary>
    public const string Disclaimer = "Conceptual, not a compliance tool";

    private const double Pad = 10;
    private const double Margin = 14;

    /// <summary>Composes the image and writes it to <paramref name="path"/>.</summary>
    public static Result Write(BitmapSource view, PngExportInfo info, string path)
    {
        try
        {
            BitmapSource image = Compose(view, info);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            string? folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            using FileStream stream = File.Create(path);
            encoder.Save(stream);
            return Result.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException
                                       or InvalidOperationException or OutOfMemoryException or System.Runtime.InteropServices.COMException)
        {
            return Result.Fail($"The image couldn't be written to '{path}': {ex.Message}");
        }
    }

    /// <summary>The view with the legend (top right) and title block (bottom left) drawn over it.</summary>
    public static BitmapSource Compose(BitmapSource view, PngExportInfo info)
    {
        double width = view.Width, height = view.Height;
        double pixelsPerDip = view.DpiX / 96.0;
        FontFamily retro = Application.Current?.TryFindResource(RetroFont.ResourceKey) as FontFamily ?? new FontFamily("Consolas");
        var typeface = new Typeface(retro, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        Brush panel = Frozen(Color.FromArgb(0xE6, 0x10, 0x14, 0x1C));
        Brush border = BrushOr("AccentBrush", Color.FromRgb(0x22, 0xD3, 0xEE));
        Brush text = BrushOr("TextBrush", Color.FromRgb(0xE5, 0xE7, 0xEB));
        Brush dim = BrushOr("TextDimBrush", Color.FromRgb(0x94, 0xA3, 0xB8));
        Brush warn = BrushOr("OrangeBrush", Color.FromRgb(0xFB, 0x92, 0x3C));
        var pen = new Pen(border, 1);
        pen.Freeze();

        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawImage(view, new Rect(0, 0, width, height));

            // Title block, bottom left.
            string exported = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            FormattedText[] lines =
            [
                Text("THRONG ▸ " + info.Study.ToUpperInvariant(), typeface, 22, border, pixelsPerDip),
                Text("LEVEL ▸ " + info.Level, typeface, 16, text, pixelsPerDip),
                Text("SIM TIME ▸ " + info.SimTime, typeface, 16, text, pixelsPerDip),
                Text("MAP ▸ " + info.Map, typeface, 16, text, pixelsPerDip),
                Text(Disclaimer.ToUpperInvariant(), typeface, 16, warn, pixelsPerDip),
                Text("EXPORTED " + exported, typeface, 13, dim, pixelsPerDip),
            ];
            double blockWidth = lines.Max(l => l.WidthIncludingTrailingWhitespace) + (2 * Pad);
            double blockHeight = lines.Sum(l => l.Height) + (2 * Pad);
            var block = new Rect(Margin, Math.Max(Margin, height - Margin - blockHeight), blockWidth, blockHeight);
            dc.DrawRectangle(panel, pen, block);
            double y = block.Top + Pad;
            foreach (FormattedText line in lines)
            {
                dc.DrawText(line, new Point(block.Left + Pad, y));
                y += line.Height;
            }

            // Legend, top right.
            if (info.LegendTitle.Length > 0 || info.Legend.Count > 0)
            {
                FormattedText title = Text(info.LegendTitle, typeface, 16, border, pixelsPerDip);
                FormattedText[] labels = [.. info.Legend.Select(r => Text(r.Label, typeface, 15, text, pixelsPerDip))];
                FormattedText? note = info.LegendNote.Length > 0 ? Text(info.LegendNote, typeface, 13, dim, pixelsPerDip, 260) : null;
                const double swatch = 14, gap = 6;
                double rowHeight = Math.Max(swatch, labels.Length > 0 ? labels.Max(l => l.Height) : 0) + 2;
                double innerWidth = Math.Max(title.WidthIncludingTrailingWhitespace, labels.Length > 0 ? labels.Max(l => l.WidthIncludingTrailingWhitespace) + swatch + gap : 0);
                innerWidth = Math.Max(innerWidth, note is null ? 0 : Math.Min(260, note.WidthIncludingTrailingWhitespace));
                double legendHeight = title.Height + (labels.Length * rowHeight) + (note is null ? 0 : note.Height + 4) + (2 * Pad);
                var box = new Rect(Math.Max(Margin, width - Margin - innerWidth - (2 * Pad)), Margin, innerWidth + (2 * Pad), legendHeight);
                dc.DrawRectangle(panel, pen, box);
                double ly = box.Top + Pad;
                dc.DrawText(title, new Point(box.Left + Pad, ly));
                ly += title.Height;
                for (int r = 0; r < labels.Length; r++)
                {
                    dc.DrawRectangle(Frozen(info.Legend[r].Colour), null, new Rect(box.Left + Pad, ly + ((rowHeight - swatch) / 2), swatch, swatch));
                    dc.DrawText(labels[r], new Point(box.Left + Pad + swatch + gap, ly + ((rowHeight - labels[r].Height) / 2)));
                    ly += rowHeight;
                }

                if (note is not null)
                {
                    dc.DrawText(note, new Point(box.Left + Pad, ly + 4));
                }
            }
        }

        var image = new RenderTargetBitmap(view.PixelWidth, view.PixelHeight, view.DpiX, view.DpiY, PixelFormats.Pbgra32);
        image.Render(visual);
        image.Freeze();
        return image;
    }

    private static FormattedText Text(string value, Typeface typeface, double size, Brush brush, double pixelsPerDip, double maxWidth = 0)
    {
        var text = new FormattedText(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, size, brush, pixelsPerDip);
        if (maxWidth > 0)
        {
            text.MaxTextWidth = maxWidth;
        }

        return text;
    }

    private static Brush BrushOr(string key, Color fallback) =>
        Application.Current?.TryFindResource(key) as Brush ?? Frozen(fallback);

    private static SolidColorBrush Frozen(Color colour)
    {
        var brush = new SolidColorBrush(colour);
        brush.Freeze();
        return brush;
    }
}
