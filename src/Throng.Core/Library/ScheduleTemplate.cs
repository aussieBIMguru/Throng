using System.Globalization;

namespace Throng.Core.Library;

/// <summary>What a schedule controls (§22.4).</summary>
public enum ScheduleKind
{
    /// <summary>Open/closed windows for a node, connector or spawn.</summary>
    Availability,

    /// <summary>Emission rate over time for a spawn.</summary>
    Emission,

    /// <summary>Arrive → unload → board → depart cycle for a vehicle door zone.</summary>
    VehicleTimetable,

    /// <summary>Switches a connector's direction at a time of day.</summary>
    DirectionReversal,

    /// <summary>Switches a lift bank's dispatch profile at a time of day.</summary>
    DispatchSwitch,

    /// <summary>Closes something for a window (maintenance, incident tests).</summary>
    Closure,
}

/// <summary>A time-of-day window. <see cref="End"/> may be 24:00 (one day).</summary>
public sealed class TimeWindow
{
    /// <summary>Window start (time of day).</summary>
    public TimeSpan Start { get; set; }

    /// <summary>Window end (time of day, up to 1.00:00:00).</summary>
    public TimeSpan End { get; set; }

    /// <summary>Creates a window.</summary>
    public static TimeWindow Of(int startHour, int startMinute, int endHour, int endMinute) =>
        new() { Start = new TimeSpan(startHour, startMinute, 0), End = new TimeSpan(endHour, endMinute, 0) };
}

/// <summary>A point on an emission rate curve; rates interpolate linearly between points.</summary>
public sealed class RatePoint
{
    /// <summary>Time of day.</summary>
    public TimeSpan Time { get; set; }

    /// <summary>Agents per minute at this time.</summary>
    public double PerMinute { get; set; }

    /// <summary>Creates a rate point.</summary>
    public static RatePoint At(int hour, int minute, double perMinute) =>
        new() { Time = new TimeSpan(hour, minute, 0), PerMinute = perMinute };
}

/// <summary>
/// Availability, emission, timetable and switching schedules (§6, §22.4).
/// Fields that do not apply to a <see cref="Kind"/> are simply left at their defaults.
/// </summary>
public sealed class ScheduleTemplate : TemplateBase
{
    /// <summary>What this schedule controls.</summary>
    public ScheduleKind Kind { get; set; } = ScheduleKind.Availability;

    /// <summary>Open (availability) or closed (closure) windows.</summary>
    public List<TimeWindow> Windows { get; set; } = [];

    /// <summary>Emission rate curve; when empty, <see cref="RatePerMinute"/> applies throughout.</summary>
    public List<RatePoint> RateCurve { get; set; } = [];

    /// <summary>Constant emission rate, agents per minute.</summary>
    public double RatePerMinute { get; set; }

    /// <summary>When true, arrivals are random (Poisson) around the rate rather than evenly spaced.</summary>
    public bool Poisson { get; set; }

    /// <summary>Vehicle headway in seconds.</summary>
    public double IntervalS { get; set; }

    /// <summary>Agents unloaded per vehicle arrival.</summary>
    public int BatchSize { get; set; }

    /// <summary>Seconds over which a vehicle unloads.</summary>
    public double UnloadDurationS { get; set; }

    /// <summary>Seconds the vehicle accepts boarding after unloading.</summary>
    public double BoardWindowS { get; set; }

    /// <summary>Alternate arrivals between paired door zones (e.g. two platform faces).</summary>
    public bool AlternateDirections { get; set; }

    /// <summary>Time of day a switch or reversal happens.</summary>
    public TimeSpan? SwitchTime { get; set; }

    /// <summary>State before <see cref="SwitchTime"/>, e.g. "Up" or "UpPeak".</summary>
    public string? StateBefore { get; set; }

    /// <summary>State after <see cref="SwitchTime"/>, e.g. "Down" or "Collective".</summary>
    public string? StateAfter { get; set; }

    /// <summary>Reference event start for event ingress/egress curves.</summary>
    public TimeSpan? EventStart { get; set; }

    /// <inheritdoc />
    public override string Summarise()
    {
        CultureInfo c = CultureInfo.InvariantCulture;
        return Kind switch
        {
            ScheduleKind.Availability or ScheduleKind.Closure =>
                $"{Kind} · {string.Join(", ", Windows.Select(w => $"{Hm(w.Start)}–{Hm(w.End)}"))}",
            ScheduleKind.Emission when RateCurve.Count > 0 =>
                $"Emission curve · peak {RateCurve.Max(p => p.PerMinute).ToString("0", c)}/min · {Hm(RateCurve[0].Time)}–{Hm(RateCurve[^1].Time)}",
            ScheduleKind.Emission =>
                $"Emission · {RatePerMinute.ToString("0.#", c)}/min{(Poisson ? " · random arrivals" : " · evenly spaced")}",
            ScheduleKind.VehicleTimetable =>
                $"Every {(IntervalS / 60).ToString("0.#", c)} min · {BatchSize.ToString(c)} off over {UnloadDurationS.ToString("0", c)} s · boarding {BoardWindowS.ToString("0", c)} s",
            _ => $"{StateBefore} → {StateAfter} at {(SwitchTime is { } t ? Hm(t) : "?")}",
        };
    }

    private static string Hm(TimeSpan t) => t >= TimeSpan.FromDays(1) ? "24:00" : t.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
}
