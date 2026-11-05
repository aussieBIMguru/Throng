namespace Throng.IO;

/// <summary>What a stadium demo runs (round 025, D-381): fans arriving, leaving, or both.</summary>
public enum StadiumScenario
{
    /// <summary>Ingress over the first hour (event surge), then everyone leaves from minute 70 (the demo).</summary>
    InAndOut,

    /// <summary>Ingress only.</summary>
    Ingress,

    /// <summary>
    /// Egress only, from the start: every seat block releases its people over 10 minutes (the bench's crowd test: the
    /// most people in the model at once).
    /// </summary>
    Egress,
}
