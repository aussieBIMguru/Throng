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

    /// <summary>Leave the queue and carry on from another step (<see cref="JourneyStep.GiveUpStep"/>), with no tag granted.</summary>
    JumpTo,
}
