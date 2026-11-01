namespace Throng.Sim.Batch;

/// <summary>One measurement zone across a batch (round 021, D-358), the batch form of <see cref="Engine.ZoneResult"/>.</summary>
public sealed record ZoneBatchResult(
    int Zone, string Name, double AreaM2, double WidthM, BatchStat PeakOccupancy, BatchStat MeanOccupancy, BatchStat PeakDensity, BatchStat Entries,
    BatchStat EntriesPerMin, BatchStat EntriesPerMinPerM);
