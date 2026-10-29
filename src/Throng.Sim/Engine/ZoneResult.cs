namespace Throng.Sim.Engine;

/// <summary>
/// One measurement zone's results after the warm-up (round 012, D-228): how many people were in it and how many
/// walked in.
/// </summary>
/// <param name="Zone">Index into <see cref="SimModel.Zones"/>.</param>
/// <param name="Name">Zone name.</param>
/// <param name="AreaM2">Zone area, m².</param>
/// <param name="WidthM">Width people cross (shorter side of its rotated bounding rectangle), m.</param>
/// <param name="PeakOccupancy">Most people in it at once.</param>
/// <param name="MeanOccupancy">Average number of people in it.</param>
/// <param name="Entries">People who stepped in from outside.</param>
/// <param name="EntriesPerMin">Entries per minute over the measured time.</param>
/// <param name="EntriesPerMinPerM">Entries per minute per metre of <paramref name="WidthM"/>.</param>
public readonly record struct ZoneResult(
    int Zone, string Name, double AreaM2, double WidthM, int PeakOccupancy, double MeanOccupancy, long Entries, double EntriesPerMin,
    double EntriesPerMinPerM)
{
    /// <summary>Peak occupancy over the zone's area, people per m² (0 for a zero area).</summary>
    public double PeakDensity => AreaM2 > 0 ? PeakOccupancy / AreaM2 : 0;
}
