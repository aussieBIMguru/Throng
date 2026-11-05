namespace Throng.Sim.Batch;

/// <summary>
/// One group × journey row across a batch (round 024b, D-390), the batch form of <see cref="Engine.GroupJourneyResult"/>.
/// A row a run didn't have counts as zero groups there; times, sizes, spread and shares cover the runs where a group
/// finished.
/// </summary>
public sealed record GroupJourneyBatchResult(
    string Group, string Journey, double AcceptableS, BatchStat Finished, BatchStat MeanS, BatchStat P95S, BatchStat WithinAcceptablePct,
    BatchStat LongWaitPct, BatchStat GaveUp, BatchStat Stuck, BatchStat InProgress, BatchStat MeanSize, BatchStat People, BatchStat MeanSpreadS,
    BatchStat SpreadPct)
{
    /// <summary>"Family (group)", as in the single-run row.</summary>
    public string Label => Engine.GroupJourneyResult.LabelOf(Group);
}
