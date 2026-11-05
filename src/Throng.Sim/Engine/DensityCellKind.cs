namespace Throng.Sim.Engine;

/// <summary>
/// What a density map cell is, which picks the Fruin table its Level of Service is read from (D-205).
/// </summary>
public enum DensityCellKind : byte
{
    /// <summary>Not mapped: not walkable, or on a moving belt.</summary>
    None,

    /// <summary>Floors and stair landings: the walkway table.</summary>
    Walkway,

    /// <summary>Device queue lines, drawn snakes and dwell zones: the queuing table.</summary>
    Queue,

    /// <summary>Stair and ramp flights: the stair table.</summary>
    Stair,
}
