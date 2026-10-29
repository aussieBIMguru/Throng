namespace Throng.Sim.Engine;

/// <summary>What a lift car is doing (round 013).</summary>
public enum LiftCarStatus : byte
{
    /// <summary>Doors shut, waiting for a request.</summary>
    Idle,

    /// <summary>Travelling to its target stop.</summary>
    Moving,

    /// <summary>Doors opening.</summary>
    Opening,

    /// <summary>Doors open: people get off, then on.</summary>
    Open,

    /// <summary>Doors closing.</summary>
    Closing,
}
