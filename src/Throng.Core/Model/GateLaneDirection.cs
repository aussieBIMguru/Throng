namespace Throng.Core.Model;

/// <summary>
/// Which way a lane of a pass-through device (a gate line) runs (D-196). Front is the side the device faces (its
/// service side); back is the other side.
/// </summary>
public enum GateLaneDirection
{
    /// <summary>Either way, one direction at a time: the lane turns round once it's empty (D-153).</summary>
    Both,

    /// <summary>Entered from the front only (people walk front → back).</summary>
    FromFront,

    /// <summary>Entered from the back only (people walk back → front).</summary>
    FromBack,
}
