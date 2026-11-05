namespace Throng.Sim.Engine;

/// <summary>
/// One device's results after the warm-up (round 011c, D-218, D-219): who it served, how long they waited and how
/// busy it was.
/// </summary>
/// <param name="Device">Index into <see cref="SimModel.Devices"/>.</param>
/// <param name="Name">Device name.</param>
/// <param name="TypeTag">Type agents ask for.</param>
/// <param name="Served">People whose service started after the warm-up.</param>
/// <param name="MeanWaitS">Mean seconds from joining a line to service starting (0 when none served).</param>
/// <param name="P95WaitS">95th percentile wait, nearest rank (0 when none served).</param>
/// <param name="MaxWaitS">Longest wait (0 when none served).</param>
/// <param name="MaxQueue">Most people in its lines at once (including whoever is being served).</param>
/// <param name="Utilisation">Share of lane time spent serving, 0–1.</param>
/// <param name="Balked">People who left its line for another device of the same type.</param>
/// <param name="GaveUp">People whose patience ran out in its line (left the study or skipped the step).</param>
public readonly record struct DeviceResult(
    int Device, string Name, string TypeTag, long Served, double MeanWaitS, double P95WaitS, double MaxWaitS, int MaxQueue, double Utilisation,
    long Balked, long GaveUp);
