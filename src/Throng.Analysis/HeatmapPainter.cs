using System.Globalization;
using Throng.Sim.Engine;

namespace Throng.Analysis;

/// <summary>One legend row: a swatch colour (0–1 RGB) and its label.</summary>
/// <param name="Label">Text beside the swatch.</param>
/// <param name="R">Red.</param>
/// <param name="G">Green.</param>
/// <param name="B">Blue.</param>
public readonly record struct HeatLegendEntry(string Label, float R, float G, float B);

/// <summary>The worst place on the map right now, for the ticker (D-204).</summary>
/// <param name="Layer">Index into the density layers.</param>
/// <param name="Cell">Cell index within the layer.</param>
/// <param name="Los">Its grade by the table for that place.</param>
/// <param name="Density">Smoothed people per square metre.</param>
public readonly record struct HeatPeak(int Layer, int Cell, LevelOfService Los, double Density);

/// <summary>
/// Turns the density maps into texture pixels (M5): continuous density or Fruin bands (D-201), each cell graded by
/// the Fruin table for what it is (D-205), in the classic or colour-blind-safe ramp (D-206). Round 011b adds the run
/// maps (D-216): max density on the bands, mean density on the ramp, and time at E or worse and footfall on the ramp
/// scaled to the map's own top value (<see cref="ScaleFor"/>).
/// </summary>
public static class HeatmapPainter
{
    /// <summary>Top of the density ramp, people per square metre.</summary>
    public const double DensityRampMax = 4.0;

    // Six stops, used for the LoS bands A–F and spread evenly along the density ramp.
    private static readonly (float R, float G, float B)[] ClassicStops =
    [
        (0.18f, 0.62f, 0.30f), (0.55f, 0.80f, 0.25f), (0.96f, 0.88f, 0.20f),
        (1.00f, 0.62f, 0.15f), (0.94f, 0.33f, 0.15f), (0.82f, 0.10f, 0.24f),
    ];

    private static readonly (float R, float G, float B)[] SafeStops =
    [
        (0.27f, 0.00f, 0.33f), (0.23f, 0.32f, 0.55f), (0.13f, 0.57f, 0.55f),
        (0.37f, 0.79f, 0.38f), (0.71f, 0.87f, 0.17f), (0.99f, 0.91f, 0.15f),
    ];

    /// <summary>Which map values a mode paints.</summary>
    public static DensityMeasure MeasureOf(HeatmapMode mode) => mode switch
    {
        HeatmapMode.MaxDensity => DensityMeasure.Max,
        HeatmapMode.MeanDensity => DensityMeasure.Mean,
        HeatmapMode.LosEOrWorse => DensityMeasure.LosEOrWorseS,
        HeatmapMode.Footfall => DensityMeasure.Footfall,
        _ => DensityMeasure.Live,
    };

    /// <summary>True for the run maps, which gather after the warm-up and stay until reset (D-216, D-218).</summary>
    public static bool IsCumulative(HeatmapMode mode) => MeasureOf(mode) != DensityMeasure.Live;

    /// <summary>True for modes whose ramp is scaled to the map's own top value (<see cref="ScaleFor"/>).</summary>
    public static bool IsScaled(HeatmapMode mode) => mode is HeatmapMode.LosEOrWorse or HeatmapMode.Footfall;

    /// <summary>
    /// Top of the ramp for a scaled mode: the highest mapped value rounded up to 1, 2 or 5 × 10ⁿ, so the legend
    /// changes in steps rather than every sample (at least 10 s or 10 people); 0 for other modes.
    /// </summary>
    public static double ScaleFor(HeatmapMode mode, ReadOnlySpan<DensityCellKind> kinds, ReadOnlySpan<float> values)
    {
        if (!IsScaled(mode))
        {
            return 0;
        }

        double top = 0;
        int n = Math.Min(kinds.Length, values.Length);
        for (int c = 0; c < n; c++)
        {
            if (kinds[c] != DensityCellKind.None && values[c] > top)
            {
                top = values[c];
            }
        }

        return NiceCeiling(Math.Max(10, top));
    }

    /// <summary>
    /// Paints one layer into <paramref name="bgra"/> (Nx × Ny × 4 bytes, row j = cell row j). Unmapped cells are
    /// transparent. <paramref name="scale"/> is the ramp top for scaled modes (<see cref="ScaleFor"/>).
    /// </summary>
    public static void PaintLayer(
        DensityLayer layer, ReadOnlySpan<DensityCellKind> kinds, ReadOnlySpan<float> density, HeatmapMode mode, HeatmapRamp ramp, byte[] bgra,
        double scale = 0)
    {
        (float R, float G, float B)[] stops = ramp == HeatmapRamp.ColourBlindSafe ? SafeStops : ClassicStops;
        int cells = layer.CellCount;
        for (int c = 0; c < cells; c++)
        {
            int o = c * 4;
            int global = layer.Base + c;
            DensityCellKind kind = kinds[global];
            double d = Math.Max(0, density[global]);
            float r = 0, g = 0, b = 0, a = 0;
            if (kind != DensityCellKind.None && mode != HeatmapMode.Off)
            {
                if (mode is HeatmapMode.Los or HeatmapMode.MaxDensity)
                {
                    int grade = (int)FruinLevelOfService.FromDensity(d, AreaType(kind));
                    (r, g, b) = stops[grade];
                    a = grade == 0 ? 0.22f : 0.72f;
                }
                else if (IsScaled(mode))
                {
                    // Footfall reads on a square-root ramp so light paths still show beside the main routes.
                    double t = scale > 0 ? d / scale : 0;
                    t = mode == HeatmapMode.Footfall ? Math.Sqrt(Math.Clamp(t, 0, 1)) : t;
                    (r, g, b) = Sample(stops, t);
                    a = d <= 0 ? 0 : (float)(0.3 + (0.45 * Math.Clamp(t, 0, 1)));
                }
                else
                {
                    (r, g, b) = Sample(stops, d / DensityRampMax);
                    a = (float)(0.75 * SmoothStep(0.02, 0.3, d));
                }
            }

            bgra[o] = ToByte(b);
            bgra[o + 1] = ToByte(g);
            bgra[o + 2] = ToByte(r);
            bgra[o + 3] = ToByte(a);
        }
    }

    /// <summary>The worst cell over all layers: highest grade, then highest density; null on an empty map.</summary>
    public static HeatPeak? FindPeak(IReadOnlyList<DensityLayer> layers, ReadOnlySpan<DensityCellKind> kinds, ReadOnlySpan<float> density)
    {
        HeatPeak? best = null;
        for (int l = 0; l < layers.Count; l++)
        {
            DensityLayer layer = layers[l];
            for (int c = 0; c < layer.CellCount; c++)
            {
                int global = layer.Base + c;
                double d = density[global];
                if (kinds[global] == DensityCellKind.None || d <= 0)
                {
                    continue;
                }

                LevelOfService los = FruinLevelOfService.FromDensity(d, AreaType(kinds[global]));
                if (best is not { } b || los > b.Los || (los == b.Los && d > b.Density))
                {
                    best = new HeatPeak(l, c, los, d);
                }
            }
        }

        return best;
    }

    /// <summary>
    /// Legend rows for a mode and ramp (empty when off); <paramref name="scale"/> is the ramp top for scaled modes.
    /// </summary>
    public static IReadOnlyList<HeatLegendEntry> Legend(HeatmapMode mode, HeatmapRamp ramp, double scale = 0)
    {
        (float R, float G, float B)[] stops = ramp == HeatmapRamp.ColourBlindSafe ? SafeStops : ClassicStops;
        var rows = new List<HeatLegendEntry>();
        if (IsScaled(mode))
        {
            double top = scale > 0 ? scale : 10;
            foreach (double f in new[] { 0.05, 0.25, 0.5, 0.75, 1.0 })
            {
                double value = f * top;
                double t = mode == HeatmapMode.Footfall ? Math.Sqrt(f) : f;
                (float r, float g, float b) = Sample(stops, t);
                string label = mode == HeatmapMode.Footfall
                    ? string.Create(CultureInfo.InvariantCulture, $"{value:0} PEOPLE")
                    : DurationLabel(value);
                rows.Add(new HeatLegendEntry(f >= 1.0 ? label + "+" : label, r, g, b));
            }
        }
        else if (mode is HeatmapMode.Los or HeatmapMode.MaxDensity)
        {
            string[] labels = ["A  free", "B", "C", "D", "E", "F  critical"];
            for (int k = 0; k < stops.Length; k++)
            {
                rows.Add(new HeatLegendEntry(labels[k], stops[k].R, stops[k].G, stops[k].B));
            }
        }
        else if (mode is HeatmapMode.Density or HeatmapMode.MeanDensity)
        {
            foreach (double d in new[] { 0.5, 1.0, 2.0, 3.0, DensityRampMax })
            {
                (float r, float g, float b) = Sample(stops, d / DensityRampMax);
                string label = d >= DensityRampMax
                    ? string.Create(CultureInfo.InvariantCulture, $"{d:0}+ P/M²")
                    : string.Create(CultureInfo.InvariantCulture, $"{d:0.0} P/M²");
                rows.Add(new HeatLegendEntry(label, r, g, b));
            }
        }

        return rows;
    }

    /// <summary>Which Fruin table a cell kind uses.</summary>
    public static LosAreaType AreaType(DensityCellKind kind) => kind switch
    {
        DensityCellKind.Queue => LosAreaType.Queue,
        DensityCellKind.Stair => LosAreaType.Stair,
        _ => LosAreaType.Walkway,
    };

    private static (float R, float G, float B) Sample((float R, float G, float B)[] stops, double t)
    {
        double x = Math.Clamp(t, 0, 1) * (stops.Length - 1);
        int k = Math.Min(stops.Length - 2, (int)x);
        float f = (float)(x - k);
        (float R, float G, float B) p = stops[k], q = stops[k + 1];
        return (p.R + ((q.R - p.R) * f), p.G + ((q.G - p.G) * f), p.B + ((q.B - p.B) * f));
    }

    /// <summary>Short time label: seconds under 2 min, else minutes.</summary>
    private static string DurationLabel(double seconds) => seconds < 120
        ? string.Create(CultureInfo.InvariantCulture, $"{seconds:0} S")
        : string.Create(CultureInfo.InvariantCulture, $"{seconds / 60:0.#} MIN");

    /// <summary>Rounds up to 1, 2 or 5 × 10ⁿ.</summary>
    private static double NiceCeiling(double value)
    {
        double power = Math.Pow(10, Math.Floor(Math.Log10(value)));
        foreach (double step in new[] { 1.0, 2.0, 5.0, 10.0 })
        {
            if (value <= step * power * (1 + 1e-9))
            {
                return step * power;
            }
        }

        return 10 * power;
    }

    private static double SmoothStep(double edge0, double edge1, double x)
    {
        double t = Math.Clamp((x - edge0) / (edge1 - edge0), 0, 1);
        return t * t * (3 - (2 * t));
    }

    private static byte ToByte(float v) => (byte)Math.Clamp((int)Math.Round(v * 255f), 0, 255);
}
