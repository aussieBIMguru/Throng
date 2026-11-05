namespace Throng.Core.Model;

/// <summary>Named starting shapes for a rule's rate curve (D-129). Choosing one fills in editable points.</summary>
public enum RateCurvePreset
{
    /// <summary>No curve: the rule's rate all the way through.</summary>
    Flat,

    /// <summary>A sharp morning peak: building to 1.8× at 30 minutes, back to 0.4× by the hour.</summary>
    AmPeak,

    /// <summary>A broader evening peak: 1.6× from about 25 to 45 minutes.</summary>
    PmPeak,

    /// <summary>An event letting out: quiet, then 3× from 12 to 20 minutes, then tailing off.</summary>
    EventSurge,
}

/// <summary>Rate curve presets and evaluation (D-129), shared by the editor and the simulation.</summary>
public static class RateCurves
{
    /// <summary>Every preset, in menu order.</summary>
    public static IReadOnlyList<RateCurvePreset> All { get; } =
        [RateCurvePreset.Flat, RateCurvePreset.AmPeak, RateCurvePreset.PmPeak, RateCurvePreset.EventSurge];

    /// <summary>Display name of a preset.</summary>
    public static string NameOf(RateCurvePreset preset) => preset switch
    {
        RateCurvePreset.AmPeak => "AM peak",
        RateCurvePreset.PmPeak => "PM peak",
        RateCurvePreset.EventSurge => "Event surge",
        _ => "Flat",
    };

    /// <summary>The points a preset fills in (a new list each call, so it can be edited).</summary>
    public static List<RateCurvePoint> PointsOf(RateCurvePreset preset) => preset switch
    {
        RateCurvePreset.AmPeak =>
        [
            RateCurvePoint.At(0, 0.4), RateCurvePoint.At(15, 1.0), RateCurvePoint.At(30, 1.8),
            RateCurvePoint.At(45, 1.0), RateCurvePoint.At(60, 0.4),
        ],
        RateCurvePreset.PmPeak =>
        [
            RateCurvePoint.At(0, 0.6), RateCurvePoint.At(15, 1.2), RateCurvePoint.At(25, 1.6),
            RateCurvePoint.At(45, 1.6), RateCurvePoint.At(55, 1.0), RateCurvePoint.At(60, 0.6),
        ],
        RateCurvePreset.EventSurge =>
        [
            RateCurvePoint.At(0, 0.2), RateCurvePoint.At(10, 0.2), RateCurvePoint.At(12, 3.0),
            RateCurvePoint.At(20, 3.0), RateCurvePoint.At(25, 0.5), RateCurvePoint.At(60, 0.2),
        ],
        _ => [],
    };

    /// <summary>True when a point can be used: a finite minute of zero or more and a finite multiplier of zero or more.</summary>
    public static bool IsValid(RateCurvePoint point) =>
        double.IsFinite(point.Minute) && point.Minute >= 0 && double.IsFinite(point.Multiplier) && point.Multiplier >= 0;

    /// <summary>
    /// The usable points sorted by minute (invalid points dropped; ties keep their list order), as parallel arrays
    /// for <see cref="MultiplierAt(double[], double[], double)"/>.
    /// </summary>
    public static (double[] Minutes, double[] Multipliers) Compile(IReadOnlyList<RateCurvePoint>? curve)
    {
        if (curve is null || curve.Count == 0)
        {
            return (Array.Empty<double>(), Array.Empty<double>());
        }

        RateCurvePoint[] points = curve.Where(IsValid).OrderBy(p => p.Minute).ToArray();
        return (points.Select(p => p.Minute).ToArray(), points.Select(p => p.Multiplier).ToArray());
    }

    /// <summary>Multiplier at a minute of the run for a curve as given (sorted on the fly; for the editor).</summary>
    public static double MultiplierAt(IReadOnlyList<RateCurvePoint>? curve, double minute)
    {
        (double[] minutes, double[] multipliers) = Compile(curve);
        return MultiplierAt(minutes, multipliers, minute);
    }

    /// <summary>
    /// Multiplier at a minute of the run for a compiled curve: 1 for no points, the first or last value outside the
    /// points, straight lines between them. Allocation-free (used every step).
    /// </summary>
    public static double MultiplierAt(double[] minutes, double[] multipliers, double minute)
    {
        int n = minutes.Length;
        if (n == 0)
        {
            return 1.0;
        }

        if (minute <= minutes[0])
        {
            return multipliers[0];
        }

        for (int k = 1; k < n; k++)
        {
            if (minute < minutes[k])
            {
                double span = minutes[k] - minutes[k - 1];
                double t = span > 1e-9 ? (minute - minutes[k - 1]) / span : 1.0;
                return multipliers[k - 1] + ((multipliers[k] - multipliers[k - 1]) * t);
            }
        }

        return multipliers[n - 1];
    }
}
