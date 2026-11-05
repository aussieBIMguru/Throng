namespace Throng.Sim.Engine;

/// <summary>
/// What happened at one journey node during a run (round 019, D-335), counting people spawned after the warm-up.
/// </summary>
/// <param name="NodeId">The model node (<c>JourneyNode.Id</c>).</param>
/// <param name="Label">The compiled step's label (devices, zones, sinks), else the branch's kind ("Chance").</param>
/// <param name="Kind">What the node does: "Device", "Pass zone", "Dwell", "Exit", "Board train", "Chance", "Tag check", "Set tag".</param>
/// <param name="Reached">People who arrived at the node.</param>
/// <param name="Finished">People who completed it: served (devices), inside (zones), dwelt, or left by it (sinks).</param>
/// <param name="Waited">Devices: people whose service started (the waits below cover them).</param>
/// <param name="MeanWaitS">Devices: mean wait from joining to service, seconds.</param>
/// <param name="P95WaitS">Devices: 95th percentile wait, nearest rank, seconds.</param>
/// <param name="GaveUp">Devices: people whose patience ran out in line (D-165).</param>
/// <param name="Balked">Devices: people who switched to another candidate mid-queue (D-152).</param>
/// <param name="Dwelled">Dwell nodes: people who finished dwelling.</param>
/// <param name="MeanDwellS">Dwell nodes: mean time from starting to dwell to moving on, seconds.</param>
/// <param name="Candidates">Per candidate device or zone, parallel to the compiled step's targets (D-336); empty for branches.</param>
public sealed record JourneyNodeStats(
    Guid NodeId, string Label, string Kind, long Reached, long Finished, long Waited, double MeanWaitS, double P95WaitS, long GaveUp, long Balked,
    long Dwelled, double MeanDwellS, IReadOnlyList<JourneyCandidateStats> Candidates);
