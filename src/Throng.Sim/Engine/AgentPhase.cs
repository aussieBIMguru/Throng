namespace Throng.Sim.Engine;

/// <summary>What an agent is doing about its current journey step (round 009, D-149–D-153).</summary>
public enum AgentPhase : byte
{
    /// <summary>Walking to its current target (a sink, a device's service area or a zone).</summary>
    Walk,

    /// <summary>At a device with no lane free for its side; standing until one is (D-153).</summary>
    WaitJoin,

    /// <summary>In a queue line, walking to and holding its slot.</summary>
    Queue,

    /// <summary>At the head of its line, being served.</summary>
    Service,

    /// <summary>Walking through a pass-through device's lane after service.</summary>
    Through,

    /// <summary>Stepping aside from a device after service, before heading on.</summary>
    Aside,

    /// <summary>Lingering in a zone for a dwell time.</summary>
    Dwell,
}
