using Throng.Sim.Engine;

namespace Throng.Sim.Replay;

/// <summary>One recorded moment of a run (round 023, D-373): everyone, the lift cars, the trains and the counters.</summary>
/// <param name="TimeS">Simulated seconds from the start.</param>
/// <param name="Stats">The status counters then.</param>
/// <param name="Agents">Everyone in the model, packed.</param>
/// <param name="Cars">Lift cars, <see cref="ReplayRecording.CarStride"/> values each (height, state, riders, direction, door share).</param>
/// <param name="Trains">Per train service: 0 none, 1 in, 2 in and boarding.</param>
internal sealed record ReplayFrame(double TimeS, SimStats Stats, PackedAgent[] Agents, double[] Cars, byte[] Trains)
{
    /// <summary>Approximate memory held, bytes.</summary>
    public long Bytes => 64 + (Agents.Length * 23L) + (Cars.Length * 8L) + Trains.Length;
}
