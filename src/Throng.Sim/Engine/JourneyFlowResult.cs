namespace Throng.Sim.Engine;

/// <summary>
/// The run overlay of one journey for one profile, or for everyone on it (round 019, D-331–D-336): people per wire
/// and what happened at each node. Built by <c>SimEngine.CopyResults</c>; immutable.
/// </summary>
public sealed class JourneyFlowResult
{
    /// <summary>Position of the journey in <see cref="SimModel.Journeys"/>.</summary>
    public required int JourneyIndex { get; init; }

    /// <summary>The journey template's id.</summary>
    public required Guid JourneyId { get; init; }

    /// <summary>The journey's name.</summary>
    public required string JourneyName { get; init; }

    /// <summary>Index into <see cref="SimModel.Profiles"/>, or −1 for everyone on the journey (D-333).</summary>
    public required int ProfileIndex { get; init; }

    /// <summary>The profile's name, or <c>SimEngine.AllJourneysName</c> ("ALL") for everyone.</summary>
    public required string Profile { get; init; }

    /// <summary>True for the row over every profile on the journey.</summary>
    public bool IsAllProfiles => ProfileIndex < 0;

    /// <summary>Every output of the Spawn node and the compiled nodes, taken or not, in compiled order.</summary>
    public required IReadOnlyList<JourneyFlowWire> Wires { get; init; }

    /// <summary>One row per compiled node, in compiled order.</summary>
    public required IReadOnlyList<JourneyNodeStats> Nodes { get; init; }

    /// <summary>People who left the Spawn node (everyone counted on the journey).</summary>
    public long Started { get; init; }

    /// <summary>People counted out of node <paramref name="from"/>'s <paramref name="port"/>, or 0.</summary>
    public long CountOf(Guid from, int port)
    {
        foreach (JourneyFlowWire wire in Wires)
        {
            if (wire.From == from && wire.Port == port)
            {
                return wire.Count;
            }
        }

        return 0;
    }

    /// <summary>The stats of a node, or null.</summary>
    public JourneyNodeStats? NodeOf(Guid id)
    {
        foreach (JourneyNodeStats node in Nodes)
        {
            if (node.NodeId == id)
            {
                return node;
            }
        }

        return null;
    }
}
