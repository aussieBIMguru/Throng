namespace Throng.Sim.Engine;

/// <summary>
/// Journey results for one profile on one journey, or on all its journeys (round 012, D-229, D-230). Counts people
/// spawned after the warm-up; finished, stuck and in progress are kept apart.
/// </summary>
/// <param name="Profile">Profile name.</param>
/// <param name="Journey">Journey name, "(no journey)" for people walking straight to an exit, or "ALL".</param>
/// <param name="IsAll">True for the profile's ALL row (every journey it uses).</param>
/// <param name="Finished">People who reached an exit or boarded (including those who gave up and left).</param>
/// <param name="MeanS">Mean journey time of the finished, seconds (0 when none).</param>
/// <param name="P95S">95th percentile journey time, nearest rank (0 when none).</param>
/// <param name="AcceptableS">The profile's acceptable journey time, seconds.</param>
/// <param name="WithinAcceptablePct">Share of the finished within <paramref name="AcceptableS"/>, percent.</param>
/// <param name="LongWaitPct">Share of the finished whose longest single queue wait passed Long wait, percent (D-231).</param>
/// <param name="GaveUp">Finished people whose patience ran out in a line and who left (D-165).</param>
/// <param name="Stuck">People removed as stuck.</param>
/// <param name="InProgress">People still in the model.</param>
public readonly record struct JourneyResult(
    string Profile, string Journey, bool IsAll, long Finished, double MeanS, double P95S, double AcceptableS, double WithinAcceptablePct,
    double LongWaitPct, long GaveUp, long Stuck, int InProgress);
