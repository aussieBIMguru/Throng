namespace Throng.Core.Validation;

/// <summary>A named CHECK rule (round 016, D-272): what the CHECK tab shows beside each finding.</summary>
/// <param name="Id">Stable id, stored in a study's ignore list.</param>
/// <param name="Category">Group shown in CHECK (Model, Vertical, Demand, Capacity…).</param>
/// <param name="Name">Short display name.</param>
public sealed record ValidationRule(string Id, string Category, string Name);
