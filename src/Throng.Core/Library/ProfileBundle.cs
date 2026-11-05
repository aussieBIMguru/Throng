namespace Throng.Core.Library;

/// <summary>
/// What a <c>.throngprofile</c> file holds (BRIEF §8.6, round 016, D-275): one agent profile and the study journeys
/// its emission mix lines used, so the profile arrives in another study ready to use.
/// </summary>
public sealed class ProfileBundle
{
    /// <summary>
    /// Current bundle format version. 1: journeys as step lists (round 016). 2: journeys as graphs and the profile's
    /// default journey (round 018, D-302, D-304); format 1 journeys convert on load.
    /// </summary>
    public const int CurrentFormatVersion = 2;

    /// <summary>Format version of this file.</summary>
    public int FormatVersion { get; set; } = CurrentFormatVersion;

    /// <summary>The profile.</summary>
    public AgentProfileTemplate Profile { get; set; } = new();

    /// <summary>Journeys it was used with, its default journey first (D-304).</summary>
    public List<JourneyTemplate> Journeys { get; set; } = [];
}
