namespace Throng.Core.Library;

/// <summary>
/// What a <c>.throngprofile</c> file holds (BRIEF §8.6, round 016, D-275): one agent profile and the study journeys
/// its emission mix lines used, so the profile arrives in another study ready to use.
/// </summary>
public sealed class ProfileBundle
{
    /// <summary>Current bundle format version.</summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>Format version of this file.</summary>
    public int FormatVersion { get; set; } = CurrentFormatVersion;

    /// <summary>The profile.</summary>
    public AgentProfileTemplate Profile { get; set; } = new();

    /// <summary>Journeys it was used with.</summary>
    public List<JourneyTemplate> Journeys { get; set; } = [];
}
