namespace Throng.Core.Library;

/// <summary>
/// What a person does when their patience runs out while queueing for a device step (D-165). Patience is the
/// profile's <see cref="AgentProfileTemplate.PatienceS"/>, counted from joining the queue (or starting to wait for a
/// lane) at that device.
/// </summary>
public enum JourneyGiveUp
{
    /// <summary>Stay in the queue; patience only drives stress (the round 009a behaviour, the default).</summary>
    KeepWaiting,

    /// <summary>Leave the queue and the study (walk to an exit), counted as "gave up".</summary>
    Leave,

    /// <summary>
    /// Legacy steps only (before format v11): leave the queue and carry on from another step
    /// (<see cref="JourneyStep.GiveUpStep"/>), with no tag granted. Converted to <see cref="Branch"/> (forward jumps)
    /// or <see cref="Leave"/> (backward jumps, which would make a loop, D-306).
    /// </summary>
    JumpTo,

    /// <summary>
    /// Journey graphs (D-306): leave the queue and carry on along the device node's "gave up" output, with no tag
    /// granted. With nothing wired there, the person leaves as for <see cref="Leave"/>.
    /// </summary>
    Branch,
}
