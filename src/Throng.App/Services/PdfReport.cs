using System.Globalization;
using System.IO;
using PdfSharp.Drawing;
using PdfSharp.Drawing.Layout;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using Throng.Core.Common;

namespace Throng.App.Services;

/// <summary>
/// Writes the one-page PDF report (round 025, D-400–D-403) with PDFsharp (MIT, D-401): A3 landscape; down the left the
/// title block, headline numbers, map legend, the people-in-model graph and the CHECK findings; on the right a plan of
/// every level with the chosen map, tiled to fit, six at most per page, more pages of plans after that. Never throws.
/// </summary>
public static class PdfReport
{
    /// <summary>Most level plans on a page.</summary>
    public const int PlansPerPage = 6;

    private const double PageWidthMm = 420, PageHeightMm = 297;
    private const double Margin = 28, ColumnWidth = 330, Gap = 14;
    private const string Face = ReportFontResolver.Text;
    private const string MonoFace = ReportFontResolver.Mono;

    private static readonly XColor Ink = XColor.FromArgb(0x11, 0x18, 0x27);
    private static readonly XColor Dim = XColor.FromArgb(0x5B, 0x64, 0x74);
    private static readonly XColor Rule = XColor.FromArgb(0xCB, 0xD5, 0xE1);
    private static readonly XColor Accent = XColor.FromArgb(0x08, 0x91, 0xB2);
    private static readonly XColor Queue = XColor.FromArgb(0xDB, 0x27, 0x77);
    private static readonly XColor WarmUp = XColor.FromArgb(0xF1, 0xF5, 0xF9);
    private static readonly object FontGate = new();
    private static bool _fontsReady;

    /// <summary>Lays out <paramref name="data"/> and writes it to <paramref name="path"/>.</summary>
    public static Result Write(ReportData data, string path)
    {
        string tempPath = path + ".tmp";
        var streams = new List<MemoryStream>();
        try
        {
            EnsureFonts();
            using var document = new PdfDocument();
            document.Info.Title = $"Throng report · {data.Study}";
            document.Info.Creator = "Throng";

            int pages = Math.Max(1, (int)Math.Ceiling(data.Levels.Count / (double)PlansPerPage));
            for (int p = 0; p < pages; p++)
            {
                PdfPage page = document.AddPage();
                page.Width = XUnit.FromMillimeter(PageWidthMm);
                page.Height = XUnit.FromMillimeter(PageHeightMm);
                using XGraphics gfx = XGraphics.FromPdfPage(page);
                double width = page.Width.Point, height = page.Height.Point;
                IReadOnlyList<ReportData.LevelPlan> plans = [.. data.Levels.Skip(p * PlansPerPage).Take(PlansPerPage)];
                double left;
                if (p == 0)
                {
                    DrawSideColumn(gfx, data, height);
                    left = Margin + ColumnWidth + Gap;
                }
                else
                {
                    DrawText(gfx, string.Create(CultureInfo.InvariantCulture, $"{data.Study} · level plans, page {p + 1} of {pages}"), Font(11, bold: true), Ink, Margin, Margin - 6);
                    left = Margin;
                }

                DrawPlans(gfx, plans, streams, left, Margin + (p == 0 ? 0 : 14), width - Margin - left, height - (2 * Margin) - (p == 0 ? 0 : 14));
                DrawFooter(gfx, data, width, height, p + 1, pages);
            }

            document.Save(tempPath);
            File.Move(tempPath, path, overwrite: true);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            // Boundary: PDFsharp, fonts and the file system can all fail; the app shows the message.
            TryDelete(tempPath);
            return Result.Fail($"The PDF couldn't be written: {ex.Message}");
        }
        finally
        {
            foreach (MemoryStream stream in streams)
            {
                stream.Dispose();
            }
        }
    }

    /// <summary>Title, headline numbers, legend, graph and findings, top to bottom in the left column.</summary>
    private static void DrawSideColumn(XGraphics gfx, ReportData data, double pageHeight)
    {
        CultureInfo c = CultureInfo.InvariantCulture;
        double x = Margin, y = Margin - 6, w = ColumnWidth;
        DrawText(gfx, "THRONG", Font(10, bold: true), Accent, x, y);
        y += 14;
        y = DrawWrapped(gfx, data.Study, Font(20, bold: true), Ink, x, y, w, 52);
        y = DrawWrapped(gfx, data.Source, Font(9), Dim, x, y + 2, w, 26);
        y = DrawWrapped(gfx, "Indicative pedestrian study for design testing; not a compliance or safety assessment.", Font(8, italic: true), Dim, x, y + 2, w, 22);
        y += 6;

        // Headline numbers.
        y = Heading(gfx, "HEADLINES", x, y, w);
        foreach ((string label, string value) in data.Headlines)
        {
            DrawText(gfx, label, Font(9), Dim, x, y);
            gfx.DrawString(value, Font(9, bold: true, mono: true), new XSolidBrush(Ink), new XRect(x, y, w, 12), XStringFormats.TopRight);
            y += 13;
        }

        // Map legend.
        y = Heading(gfx, "MAP · " + data.MapTitle, x, y + 6, w);
        foreach (PngLegendRow row in data.Legend)
        {
            gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(row.Colour.R, row.Colour.G, row.Colour.B)), x, y + 1, 12, 9);
            DrawText(gfx, row.Label, Font(8.5), Ink, x + 18, y);
            y += 12;
        }

        if (data.LegendNote.Length > 0)
        {
            y = DrawWrapped(gfx, data.LegendNote, Font(8), Dim, x, y + 1, w, 24);
        }

        // Graph.
        if (data.Graph is { TimesS.Length: >= 2 } graph)
        {
            y = Heading(gfx, "PEOPLE IN THE MODEL", x, y + 6, w);
            DrawGraph(gfx, graph, x, y, w, 130);
            y += 130 + 4;
            gfx.DrawLine(new XPen(Accent, 1.5), x, y + 5, x + 14, y + 5);
            DrawText(gfx, "in the model", Font(8), Dim, x + 18, y);
            gfx.DrawLine(new XPen(Queue, 1.2), x + 90, y + 5, x + 104, y + 5);
            DrawText(gfx, "queuing at devices", Font(8), Dim, x + 108, y);
            gfx.DrawRectangle(new XSolidBrush(WarmUp), x + 200, y + 1, 12, 9);
            DrawText(gfx, "warm-up", Font(8), Dim, x + 216, y);
            y += 14;
        }

        // CHECK findings, as many as fit.
        y = Heading(gfx, string.Create(c, $"CHECK · {data.Findings.Count} finding{(data.Findings.Count == 1 ? string.Empty : "s")}"), x, y + 6, w);
        if (data.Findings.Count == 0)
        {
            DrawText(gfx, "No errors or warnings.", Font(8.5), Dim, x, y);
            return;
        }

        double bottom = pageHeight - Margin - 18;
        int shown = 0;
        foreach (ReportData.Finding finding in data.Findings)
        {
            if (y + 22 > bottom)
            {
                break;
            }

            DrawText(gfx, finding.Severity, Font(7.5, bold: true), XColor.FromArgb(finding.Colour.R, finding.Colour.G, finding.Colour.B), x, y + 0.5);
            y = DrawWrapped(gfx, finding.Message, Font(8), Ink, x + 52, y, w - 52, Math.Min(36, bottom - y)) + 2;
            shown++;
        }

        if (shown < data.Findings.Count)
        {
            DrawText(gfx, string.Create(c, $"… and {data.Findings.Count - shown} more (CHECK tab)."), Font(8, italic: true), Dim, x, Math.Min(y, bottom));
        }
    }

    /// <summary>Level plans tiled in a grid over the area, each with its name; images keep their shape.</summary>
    private static void DrawPlans(XGraphics gfx, IReadOnlyList<ReportData.LevelPlan> plans, List<MemoryStream> streams, double x, double y, double w, double h)
    {
        if (plans.Count == 0)
        {
            DrawWrapped(gfx, "No levels to show.", Font(10), Dim, x, y, w, 20);
            return;
        }

        int cols = plans.Count switch { 1 => 1, 2 => 2, 3 or 4 => 2, _ => 3 };
        int rows = (int)Math.Ceiling(plans.Count / (double)cols);
        double cellW = (w - ((cols - 1) * Gap)) / cols;
        double cellH = (h - ((rows - 1) * Gap)) / rows;
        for (int k = 0; k < plans.Count; k++)
        {
            ReportData.LevelPlan plan = plans[k];
            double cx = x + ((k % cols) * (cellW + Gap));
            double cy = y + ((k / cols) * (cellH + Gap));
            DrawText(gfx, plan.Name, Font(10, bold: true), Ink, cx, cy - 2);
            double top = cy + 14, avail = cellH - 14;
            var stream = new MemoryStream(plan.Png, writable: false);
            streams.Add(stream);
            XImage image = XImage.FromStream(stream);
            double scale = Math.Min(cellW / Math.Max(1, plan.PixelWidth), avail / Math.Max(1, plan.PixelHeight));
            double iw = plan.PixelWidth * scale, ih = plan.PixelHeight * scale;
            gfx.DrawImage(image, cx + ((cellW - iw) / 2), top, iw, ih);
            gfx.DrawRectangle(new XPen(Rule, 0.5), cx + ((cellW - iw) / 2), top, iw, ih);
        }
    }

    /// <summary>People in the model and queuing over time, the warm-up shaded, hour:minute ticks.</summary>
    private static void DrawGraph(XGraphics gfx, ReportData.Series graph, double x, double y, double w, double h)
    {
        CultureInfo c = CultureInfo.InvariantCulture;
        double t0 = graph.TimesS[0], t1 = graph.TimesS[^1];
        double top = Math.Max(1, Math.Max(graph.InModel.DefaultIfEmpty(0).Max(), graph.Queuing.DefaultIfEmpty(0).Max()));
        double plotX = x + 34, plotW = w - 38, plotY = y + 4, plotH = h - 20;
        double X(double t) => plotX + ((t - t0) / Math.Max(1e-9, t1 - t0) * plotW);
        double Y(double v) => plotY + plotH - (v / top * plotH);

        if (graph.WarmUpS > t0)
        {
            gfx.DrawRectangle(new XSolidBrush(WarmUp), plotX, plotY, Math.Min(plotW, X(graph.WarmUpS) - plotX), plotH);
        }

        var axis = new XPen(Rule, 0.6);
        gfx.DrawRectangle(axis, plotX, plotY, plotW, plotH);
        gfx.DrawString(top.ToString("N0", c), Font(7, mono: true), new XSolidBrush(Dim), new XRect(x, plotY - 4, 30, 10), XStringFormats.TopRight);
        gfx.DrawString("0", Font(7, mono: true), new XSolidBrush(Dim), new XRect(x, plotY + plotH - 6, 30, 10), XStringFormats.TopRight);
        for (int k = 0; k <= 4; k++)
        {
            double t = t0 + ((t1 - t0) * k / 4);
            TimeSpan span = TimeSpan.FromSeconds(Math.Round(t));
            string label = string.Create(c, $"{(int)span.TotalHours}:{span.Minutes:00}");
            gfx.DrawString(label, Font(7, mono: true), new XSolidBrush(Dim), new XRect(X(t) - 20, plotY + plotH + 3, 40, 10), XStringFormats.TopCenter);
        }

        Polyline(gfx, new XPen(Queue, 1.0), graph.TimesS, graph.Queuing, X, Y);
        Polyline(gfx, new XPen(Accent, 1.4), graph.TimesS, graph.InModel, X, Y);
    }

    private static void Polyline(XGraphics gfx, XPen pen, double[] times, double[] values, Func<double, double> x, Func<double, double> y)
    {
        int n = Math.Min(times.Length, values.Length);
        if (n < 2)
        {
            return;
        }

        var points = new XPoint[n];
        for (int k = 0; k < n; k++)
        {
            points[k] = new XPoint(x(times[k]), y(values[k]));
        }

        gfx.DrawLines(pen, points);
    }

    private static void DrawFooter(XGraphics gfx, ReportData data, double width, double height, int page, int pages)
    {
        string left = $"Throng · {data.Made.ToString("d MMM yyyy HH:mm", CultureInfo.InvariantCulture)} · map: {data.MapTitle}";
        string right = string.Create(CultureInfo.InvariantCulture, $"page {page} of {pages}");
        DrawText(gfx, left, Font(7.5), Dim, Margin, height - Margin + 6);
        gfx.DrawString(right, Font(7.5), new XSolidBrush(Dim), new XRect(Margin, height - Margin + 6, width - (2 * Margin), 10), XStringFormats.TopRight);
    }

    private static double Heading(XGraphics gfx, string text, double x, double y, double w)
    {
        DrawText(gfx, text, Font(9, bold: true), Accent, x, y);
        gfx.DrawLine(new XPen(Rule, 0.6), x, y + 13, x + w, y + 13);
        return y + 17;
    }

    private static void DrawText(XGraphics gfx, string text, XFont font, XColor colour, double x, double y) =>
        gfx.DrawString(text, font, new XSolidBrush(colour), new XRect(x, y, 2000, font.Size * 1.4), XStringFormats.TopLeft);

    /// <summary>Wrapped text in a box at most <paramref name="maxHeight"/> tall; returns the y under it.</summary>
    private static double DrawWrapped(XGraphics gfx, string text, XFont font, XColor colour, double x, double y, double w, double maxHeight)
    {
        double lineHeight = font.Size * 1.25;
        double width = gfx.MeasureString(text, font).Width;
        int lines = Math.Max(1, (int)Math.Ceiling(width / Math.Max(1, w * 0.92)));
        double h = Math.Min(maxHeight, lines * lineHeight + 1);
        var formatter = new XTextFormatter(gfx);
        formatter.DrawString(text, font, new XSolidBrush(colour), new XRect(x, y, w, h), XStringFormats.TopLeft);
        return y + h;
    }

    private static XFont Font(double size, bool bold = false, bool italic = false, bool mono = false)
    {
        XFontStyleEx style = bold && italic ? XFontStyleEx.BoldItalic : bold ? XFontStyleEx.Bold : italic ? XFontStyleEx.Italic : XFontStyleEx.Regular;
        return new XFont(mono ? MonoFace : Face, size, style);
    }

    /// <summary>
    /// PDFsharp's Core build finds no fonts on its own: the report's resolver reads Arial and Consolas from the Windows
    /// fonts folder (the built-in Windows resolver doesn't know Consolas). It can be set only once per process.
    /// </summary>
    private static void EnsureFonts()
    {
        lock (FontGate)
        {
            if (_fontsReady)
            {
                return;
            }

            try
            {
                GlobalFontSettings.FontResolver ??= new ReportFontResolver();
            }
            catch (InvalidOperationException)
            {
                // A resolver is already in place (set once per process): fonts are available either way.
            }

            _fontsReady = true;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temp file is harmless.
        }
    }
}
