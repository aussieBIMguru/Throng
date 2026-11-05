namespace Throng.Sim.Engine;

/// <summary>
/// One candidate device or zone of a journey node during a run (round 019, D-336): how many headed for it and, for
/// devices, how long they waited to be served.
/// </summary>
/// <param name="Name">The device's or zone's name.</param>
/// <param name="Count">People who headed for it (a balk moves the person to the device they switched to).</param>
/// <param name="Served">Devices: people whose service started there; 0 for zones.</param>
/// <param name="MeanWaitS">Devices: mean wait from joining to service, seconds (0 when none served).</param>
public readonly record struct JourneyCandidateStats(string Name, long Count, long Served, double MeanWaitS);
