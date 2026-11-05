namespace Throng.Sim.Batch;

/// <summary>
/// One lift bank across a batch (round 021, D-358), the batch form of <see cref="Engine.LiftResult"/>. Waits cover the
/// runs where someone boarded; ride times the runs where someone got off.
/// </summary>
public sealed record LiftBatchResult(
    int Lift, string Name, int Cars, BatchStat Boarded, BatchStat MeanWaitS, BatchStat P95WaitS, BatchStat MaxWaitS, BatchStat Rides,
    BatchStat MeanRideS, BatchStat GaveUp);
