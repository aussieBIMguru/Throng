namespace Throng.Sim.Batch;

/// <summary>
/// One journey row across a batch (round 021, D-358), the batch form of <see cref="Engine.JourneyResult"/>. A row a run
/// didn't have counts as zero people there; times and shares cover the runs where someone finished.
/// </summary>
public sealed record JourneyBatchResult(
    string Profile, string Journey, bool IsAll, string Sink, double AcceptableS, BatchStat Finished, BatchStat MeanS, BatchStat P95S,
    BatchStat WithinAcceptablePct, BatchStat LongWaitPct, BatchStat GaveUp, BatchStat Stuck, BatchStat InProgress)
{
    /// <summary>True for a per-sink row (D-312).</summary>
    public bool IsSink => !string.IsNullOrEmpty(Sink);
}
