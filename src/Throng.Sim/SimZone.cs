namespace Throng.Sim;

/// <summary>
/// A measurement zone ready to count (round 012, D-228): its floor cells are flagged in
/// <see cref="SimModel.ZoneCellBits"/> with bit <see cref="Index"/>.
/// </summary>
public sealed class SimZone
{
    /// <summary>At most this many zones are counted per run (one bit each).</summary>
    public const int MaxZones = 64;

    /// <summary>Position in <see cref="SimModel.Zones"/> and its bit in the cell masks.</summary>
    public required int Index { get; init; }

    /// <summary>The study region.</summary>
    public required Guid RegionId { get; init; }

    /// <summary>Zone name.</summary>
    public required string Name { get; init; }

    /// <summary>Floor surface the zone is on.</summary>
    public required int Floor { get; init; }

    /// <summary>Plan area of the zone outline, m².</summary>
    public required double AreaM2 { get; init; }

    /// <summary>
    /// Width people cross: the shorter side of the zone's smallest rotated bounding rectangle, metres (at least 0.1).
    /// </summary>
    public required double WidthM { get; init; }

    /// <summary>The zone's bit.</summary>
    public ulong Bit => 1UL << Index;
}
