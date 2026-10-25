namespace Throng.Core.Model;

/// <summary>How a study run decides it is finished.</summary>
public enum StudyEndCondition
{
    /// <summary>Run for <see cref="StudySettings.DurationS"/> seconds.</summary>
    FixedDuration,

    /// <summary>Run until <see cref="StudySettings.TargetCompletedJourneys"/> agents have finished.</summary>
    CompletedJourneys,

    /// <summary>Run until stopped.</summary>
    Indefinite,
}

/// <summary>Which side people drift to when they meet oncoming flows (D-105).</summary>
public enum KeepSide
{
    /// <summary>Keep left (the Australian convention, default). On escalators: stand left, walk right.</summary>
    Left,

    /// <summary>Keep right. On escalators: stand right, walk left.</summary>
    Right,

    /// <summary>No bias; lanes form from the movement model alone.</summary>
    None,
}

/// <summary>
/// Run-level settings for a study: clock, duration, randomness, keep side and navigation resolution.
/// </summary>
public sealed class StudySettings
{
    /// <summary>RNG seed; same file + same seed = same result.</summary>
    public int Seed { get; set; } = 1;

    /// <summary>Time of day the study clock starts at.</summary>
    public TimeSpan StartTimeOfDay { get; set; } = new(7, 0, 0);

    /// <summary>End condition.</summary>
    public StudyEndCondition EndCondition { get; set; } = StudyEndCondition.FixedDuration;

    /// <summary>Study length in seconds for <see cref="StudyEndCondition.FixedDuration"/>.</summary>
    public double DurationS { get; set; } = 1800;

    /// <summary>Completed journeys target for <see cref="StudyEndCondition.CompletedJourneys"/>.</summary>
    public int TargetCompletedJourneys { get; set; }

    /// <summary>Initial period excluded from statistics, in seconds.</summary>
    public double WarmUpS { get; set; }

    /// <summary>Navigation grid cell size in metres.</summary>
    public double NavCellSizeM { get; set; } = 0.25;

    /// <summary>Fixed simulation timestep in seconds.</summary>
    public double TimeStepS { get; set; } = 0.1;

    /// <summary>Keep-side bias for bidirectional flows (file format v4, D-105).</summary>
    public KeepSide KeepSide { get; set; } = KeepSide.Left;
}
