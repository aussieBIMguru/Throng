namespace Throng.Sim.Engine;

/// <summary>
/// Journey results for one group template on one journey (round 024b, D-390): groups spawned after the warm-up, timed
/// from spawning until the last member leaves. Member rows stay per profile in <see cref="JourneyResult"/>.
/// </summary>
/// <param name="Group">Group template name.</param>
/// <param name="Journey">Journey name, or "(no journey)".</param>
/// <param name="Finished">Groups whose last member left (none removed as stuck).</param>
/// <param name="MeanS">Mean group time of the finished, seconds (0 when none).</param>
/// <param name="P95S">95th percentile group time, nearest rank (0 when none).</param>
/// <param name="AcceptableS">The lead profile's acceptable journey time, seconds.</param>
/// <param name="WithinAcceptablePct">Share of the finished groups within <paramref name="AcceptableS"/>, percent.</param>
/// <param name="LongWaitPct">Share of the finished groups where any member's single wait passed Long wait, percent.</param>
/// <param name="GaveUp">Finished groups whose patience ran out in a line and who left.</param>
/// <param name="Stuck">Groups with a member removed as stuck.</param>
/// <param name="InProgress">Groups still in the model.</param>
/// <param name="MeanSize">Mean people per finished group.</param>
/// <param name="People">People in the finished groups.</param>
/// <param name="MeanSpreadS">Mean seconds per finished group with two members more than 5 m apart.</param>
/// <param name="SpreadPct">Those seconds as a share of the finished groups' time, percent.</param>
public readonly record struct GroupJourneyResult(
    string Group, string Journey, long Finished, double MeanS, double P95S, double AcceptableS, double WithinAcceptablePct,
    double LongWaitPct, long GaveUp, long Stuck, int InProgress, double MeanSize, long People, double MeanSpreadS, double SpreadPct)
{
    /// <summary>Distance between two members that counts as spread out, metres (D-390).</summary>
    public const double SpreadM = 5.0;

    /// <summary>How the row names the group in tables and CSV ("Family (group)", as the mix line's "who" does).</summary>
    public string Label => LabelOf(Group);

    /// <summary>"Family (group)" for a group template named "Family".</summary>
    public static string LabelOf(string group) => $"{group} (group)";
}
