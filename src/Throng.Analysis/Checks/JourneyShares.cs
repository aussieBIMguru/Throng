namespace Throng.Analysis.Checks;

/// <summary>
/// Predicted shares of a journey's people (round 019, D-334), from the capacity model's walk of the graph: the share
/// reaching each compiled node and the share leaving each of its output ports (0–1 of everyone starting the journey).
/// </summary>
/// <param name="Nodes">Share reaching each compiled node, parallel to <c>SimJourney.Nodes</c>.</param>
/// <param name="Wires">Share leaving each output port, per compiled node.</param>
public sealed record JourneyShares(double[] Nodes, double[][] Wires)
{
    /// <summary>The share out of compiled node <paramref name="node"/>'s <paramref name="port"/>, or 0.</summary>
    public double WireAt(int node, int port) =>
        node >= 0 && node < Wires.Length && port >= 0 && port < Wires[node].Length ? Wires[node][port] : 0;
}
