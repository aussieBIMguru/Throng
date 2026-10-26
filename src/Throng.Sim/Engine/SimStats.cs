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
/// <param name="Stressed">People with stress at or above 0.5 (D-156).</param>
/// <param name="Queuing">People in device queues, being served or waiting for a lane (D-153).</param>
/// <param name="GaveUp">People who left because their patience ran out in a queue (D-165, D-168).</param>
/// <param name="LeftBehind">People still waiting on the platform when their train left (D-187).</param>
public readonly record struct SimStats(
    double TimeS, int InSystem, long Spawned, long Arrived, double MeanJourneyS, int StuckNow, long StuckRemoved, int Waiting,
    int Stressed = 0, int Queuing = 0, long GaveUp = 0, long LeftBehind = 0);
