using System.Globalization;

namespace Throng.Sim;

/// <summary>
/// Fixed-step simulation clock with a time-of-day display (§9).
/// Elapsed time is derived from an integer tick count so long runs don't accumulate floating-point drift.
/// </summary>
public sealed class SimClock
{
    /// <summary>Creates a clock.</summary>
    /// <param name="startTimeOfDay">Time of day at tick 0.</param>
    /// <param name="timeStepS">Seconds of simulated time per tick (must be positive).</param>
    public SimClock(TimeSpan startTimeOfDay, double timeStepS = 0.1)
    {
        StartTimeOfDay = startTimeOfDay;
        TimeStepS = timeStepS > 0 ? timeStepS : 0.1;
    }

    /// <summary>Time of day at tick 0.</summary>
    public TimeSpan StartTimeOfDay { get; }

    /// <summary>Seconds per tick.</summary>
    public double TimeStepS { get; }

    /// <summary>Ticks advanced since reset.</summary>
    public long Tick { get; private set; }

    /// <summary>Simulated seconds since start.</summary>
    public double ElapsedSeconds => Tick * TimeStepS;

    /// <summary>Absolute simulated time since midnight of day 1 (can exceed 24 h).</summary>
    public TimeSpan AbsoluteTime => StartTimeOfDay + TimeSpan.FromSeconds(ElapsedSeconds);

    /// <summary>Zero-based day index (0 on the start day).</summary>
    public int DayIndex => (int)Math.Floor(AbsoluteTime.TotalDays);

    /// <summary>Current time of day, wrapped to 0..24 h.</summary>
    public TimeSpan TimeOfDay => AbsoluteTime - TimeSpan.FromDays(DayIndex);

    /// <summary>Advances one tick.</summary>
    public void Advance() => Tick++;

    /// <summary>Advances several ticks.</summary>
    public void Advance(long ticks) => Tick += Math.Max(0, ticks);

    /// <summary>Returns to tick 0.</summary>
    public void Reset() => Tick = 0;

    /// <summary>Formats the current time, e.g. "07:42:15 AM" or "Day 2 · 01:05:00 AM".</summary>
    public string Format() => FormatTimeOfDay(TimeOfDay, DayIndex);

    /// <summary>Formats a time of day in 12-hour AM/PM form, prefixing the day when past the first day.</summary>
    public static string FormatTimeOfDay(TimeSpan timeOfDay, int dayIndex = 0)
    {
        string clock = DateTime.MinValue.Add(timeOfDay).ToString("hh:mm:ss tt", CultureInfo.InvariantCulture);
        return dayIndex > 0 ? $"Day {(dayIndex + 1).ToString(CultureInfo.InvariantCulture)} · {clock}" : clock;
    }
}
