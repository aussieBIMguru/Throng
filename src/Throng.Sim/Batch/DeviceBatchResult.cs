namespace Throng.Sim.Batch;

/// <summary>
/// One device across a batch (round 021, D-358), the batch form of <see cref="Engine.DeviceResult"/>. Waits cover the
/// runs where it served someone.
/// </summary>
public sealed record DeviceBatchResult(
    int Device, string Name, string TypeTag, BatchStat Served, BatchStat MeanWaitS, BatchStat P95WaitS, BatchStat MaxWaitS, BatchStat MaxQueue,
    BatchStat Utilisation, BatchStat Balked, BatchStat GaveUp);
