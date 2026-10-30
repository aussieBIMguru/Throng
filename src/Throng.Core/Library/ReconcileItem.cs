namespace Throng.Core.Library;

/// <summary>One reference a profile import makes that the study lacks (D-275).</summary>
/// <param name="Kind">What it is.</param>
/// <param name="Value">The tag or key as written.</param>
/// <param name="UsedBy">Where it's used, for the dialog ("journey 'Ticket then gates', step 1").</param>
/// <param name="MapOptions">Existing values in the study it can be mapped to.</param>
public sealed record ReconcileItem(ReconcileKind Kind, string Value, string UsedBy, IReadOnlyList<string> MapOptions);
