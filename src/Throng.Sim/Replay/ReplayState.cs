using Throng.Sim.Engine;

namespace Throng.Sim.Replay;

/// <summary>
/// The non-agent state of a run at a replayed time (round 023, D-374): counters, lift cars (heights and doors
/// interpolated) and trains, as the viewport and status bar read them.
/// </summary>
/// <param name="TimeS">The replayed time, seconds.</param>
/// <param name="Stats">The counters at the frame at or before that time (with <see cref="SimStats.TimeS"/> set to it).</param>
/// <param name="Cars">Lift cars, <see cref="ReplayRecording.CarStride"/> values each.</param>
/// <param name="Trains">Per train service: 0 none, 1 in, 2 in and boarding.</param>
public sealed record ReplayState(double TimeS, SimStats Stats, double[] Cars, byte[] Trains);
