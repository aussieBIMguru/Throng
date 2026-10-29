using Throng.Core.Library;

namespace Throng.Core.Model;

/// <summary>
/// One row of a lift's dispatch schedule (D-238): from this time of day on, the bank runs this way, until the next
/// row. Before the first row the lift's own dispatch applies.
/// </summary>
public sealed class LiftScheduleEntry
{
    /// <summary>Time of day the row starts.</summary>
    public TimeSpan From { get; set; }

    /// <summary>Dispatch from then on (Collective or Up-peak; Shuttle is a lift's fixed mode).</summary>
    public LiftDispatch Dispatch { get; set; } = LiftDispatch.Collective;

    /// <summary>An independent copy.</summary>
    public LiftScheduleEntry Clone() => new() { From = From, Dispatch = Dispatch };
}
