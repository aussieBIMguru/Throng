namespace Throng.Core.Library;

/// <summary>
/// What SAVE TO MY LIBRARY would write (round 020, D-342, D-345, D-349, D-350), worked out before anything changes so
/// the app can ask first.
/// </summary>
/// <param name="Items">Copies to write (the definition, and a profile's default journey), marked User.</param>
/// <param name="Replaces">Your existing items those copies would replace (same id, different values).</param>
/// <param name="JourneysWithPicks">Journeys being saved whose nodes pick specific devices or zones (D-349).</param>
/// <param name="UnchangedCount">Items left out because your library already holds them with the same values.</param>
public sealed record LibrarySavePlan(
    IReadOnlyList<TemplateBase> Items,
    IReadOnlyList<TemplateBase> Replaces,
    IReadOnlyList<JourneyTemplate> JourneysWithPicks,
    int UnchangedCount)
{
    /// <summary>True when there's nothing to write (your library already matches).</summary>
    public bool IsUpToDate => Items.Count == 0;
}
