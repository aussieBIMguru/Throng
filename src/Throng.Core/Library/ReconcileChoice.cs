namespace Throng.Core.Library;

/// <summary>The answer for one <see cref="ReconcileItem"/> (D-275).</summary>
/// <param name="Action">Keep, map or drop.</param>
/// <param name="MapTo">The existing value for <see cref="ReconcileAction.Map"/>.</param>
public sealed record ReconcileChoice(ReconcileAction Action, string? MapTo = null)
{
    /// <summary>Keep as is.</summary>
    public static ReconcileChoice Keep { get; } = new(ReconcileAction.Keep);
}
