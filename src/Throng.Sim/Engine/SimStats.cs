namespace Throng.Sim.Engine;

/// <summary>Live run counters for the status bar (D-104).</summary>
/// <param name="TimeS">Simulated seconds since the start.</param>
/// <param name="InSystem">Agents currently walking.</param>
/// <param name="Spawned">Agents emitted so far.</param>
/// <param name="Arrived">Agents that reached their sink.</param>
/// <param name="MeanJourneyS">Mean spawn-to-sink time of arrived agents, seconds (0 when none).</param>
/// <param name="StuckNow">Agents currently flagged stuck (no progress for 30 s, D-106).</param>
/// <param name="StuckRemoved">Agents removed after 120 s without progress.</param>
/// <param name="Waiting">Arrivals waiting for room to appear in their spawn zone.</param>
public readonly record struct SimStats(
    double TimeS, int InSystem, long Spawned, long Arrived, double MeanJourneyS, int StuckNow, long StuckRemoved, int Waiting);
