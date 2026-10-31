namespace Throng.Sim.Engine;

/// <summary>
/// People who left one output of a journey node during a run (round 019, D-332): output <paramref name="Port"/> of
/// node <paramref name="From"/> into <paramref name="To"/>.
/// </summary>
/// <param name="From">The model node the wire leaves (<c>JourneyNode.Id</c>; the Spawn node for the first wire).</param>
/// <param name="Port">The output port (0 = the first).</param>
/// <param name="To">The model node it leads to, or <see cref="Guid.Empty"/> for an unwired output (the rule's sinks).</param>
/// <param name="Count">People who took it (spawned after the warm-up, D-338).</param>
public readonly record struct JourneyFlowWire(Guid From, int Port, Guid To, long Count);
