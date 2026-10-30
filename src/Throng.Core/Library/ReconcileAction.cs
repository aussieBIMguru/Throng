namespace Throng.Core.Library;

/// <summary>What to do with a reference the study lacks (D-275).</summary>
public enum ReconcileAction
{
    /// <summary>Keep it as it is (it works once the study gets one).</summary>
    Keep,

    /// <summary>Use an existing one instead (<see cref="ReconcileChoice.MapTo"/>).</summary>
    Map,

    /// <summary>Drop the step or preference (tags: clear the tag).</summary>
    Drop,
}
