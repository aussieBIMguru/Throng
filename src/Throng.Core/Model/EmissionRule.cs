using System.Text.Json.Serialization;

namespace Throng.Core.Model;

/// <summary>How an emission rule spaces its arrivals (D-099). The rate itself can vary over the run (D-129).</summary>
public enum EmissionPattern
{
    /// <summary>Evenly spaced arrivals at the rule's rate.</summary>
    Constant,

    /// <summary>Random (Poisson) arrivals averaging the rule's rate.</summary>
    Poisson,
}

/// <summary>Where the agents a rule emits are heading (D-096).</summary>
public enum DestinationMode
{
    /// <summary>The quickest sink to reach from the spawn point (with a little per-agent noise), never the spawn zone itself.</summary>
    NearestSink,

    /// <summary>One of <see cref="EmissionRule.Destinations"/>, picked at random by weight.</summary>
    ChosenSinks,
}

/// <summary>A sink zone a rule can send agents to, with a relative weight.</summary>
public sealed record SinkWeight
{
    /// <summary>Id of the sink (or spawn + sink) zone.</summary>
    public Guid SinkId { get; init; }

    /// <summary>Relative weight; zero or less means never chosen.</summary>
    public double Weight { get; init; } = 1;
}

/// <summary>
/// One point on a rule's rate curve (D-129): minutes from the start of the run and a multiplier on the rule's
/// <see cref="EmissionRule.RatePerMinute"/>. The rate runs in straight lines between points and holds the first and
/// last values outside them.
/// </summary>
public sealed record RateCurvePoint
{
    /// <summary>Minutes from the start of the run.</summary>
    public double Minute { get; init; }

    /// <summary>Multiplier on the rule's rate (1 = the rate as entered; 0 = no one).</summary>
    public double Multiplier { get; init; } = 1;

    /// <summary>Creates a point.</summary>
    public static RateCurvePoint At(double minute, double multiplier) => new() { Minute = minute, Multiplier = multiplier };
}

/// <summary>A tag given to a share of the agents a mix line spawns (D-150), e.g. 80% start with "ticket".</summary>
public sealed record SpawnTag
{
    /// <summary>Tag text (case-insensitive in the simulation).</summary>
    public string Tag { get; init; } = string.Empty;

    /// <summary>Share of the line's agents that start with it, 0–100.</summary>
    public double Percent { get; init; } = 100;

    /// <summary>Creates a spawn tag.</summary>
    public static SpawnTag Of(string tag, double percent = 100) => new() { Tag = tag, Percent = percent };
}

/// <summary>
/// One line of an emission rule's mix (D-151): a profile, the journey it follows and its share of the rule's
/// arrivals, plus tags some of them start with.
/// </summary>
public sealed record MixLine
{
    /// <summary>Agent profile template id (study library first, then the built-in catalogue).</summary>
    public Guid ProfileId { get; init; }

    /// <summary>Journey template id, or null to walk straight to a sink (as before round 009).</summary>
    public Guid? JourneyId { get; init; }

    /// <summary>Relative share of the rule's arrivals; zero or less means none.</summary>
    public double Share { get; init; } = 1;

    /// <summary>Tags a share of these agents start with (D-150).</summary>
    public List<SpawnTag> SpawnTags { get; init; } = [];

    /// <summary>A line of one profile with a share of 1 by default.</summary>
    public static MixLine Of(Guid profileId, Guid? journeyId = null, double share = 1, params SpawnTag[] tags) =>
        new() { ProfileId = profileId, JourneyId = journeyId, Share = share, SpawnTags = [.. tags] };
}

/// <summary>
/// One stream of agents emitted by a spawn zone (D-096, D-099, D-151): who (a mix of profiles and their journeys),
/// how many (a rate and a pattern) and where they finally leave (the nearest sink or weighted chosen sinks).
/// </summary>
/// <remarks>
/// Immutable: edits replace the rule (and the region's rule list) so undo restores the exact previous value.
/// </remarks>
public sealed record EmissionRule
{
    /// <summary>Stable id.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Who the rule emits (D-151): profile + journey + share per line. Format v5 rules had a single profile, which
    /// the v5 → v6 migration moves into one line (D-157).
    /// </summary>
    public List<MixLine> Mix { get; init; } = [];

    /// <summary>
    /// The single profile of a format v5 rule, read only so the migration can move it into <see cref="Mix"/>;
    /// null (and not written) afterwards.
    /// </summary>
    [JsonPropertyName("profileId")]
    public Guid? LegacyProfileId { get; init; }

    /// <summary>Mean arrivals per minute.</summary>
    public double RatePerMinute { get; init; } = 10;

    /// <summary>Even or random spacing.</summary>
    public EmissionPattern Pattern { get; init; } = EmissionPattern.Poisson;

    /// <summary>Nearest sink or weighted chosen sinks.</summary>
    public DestinationMode DestinationMode { get; init; } = DestinationMode.NearestSink;

    /// <summary>Weighted sinks for <see cref="DestinationMode.ChosenSinks"/>.</summary>
    public List<SinkWeight> Destinations { get; init; } = [];

    /// <summary>
    /// Rate curve (D-129): multipliers on <see cref="RatePerMinute"/> over the run, in any order. Empty means a flat
    /// rate.
    /// </summary>
    public List<RateCurvePoint> RateCurve { get; init; } = [];

    /// <summary>Copy with a new id (for copy/paste).</summary>
    public EmissionRule CopyWithNewId() =>
        this with
        {
            Id = Guid.NewGuid(),
            Destinations = [.. Destinations],
            RateCurve = [.. RateCurve],
            Mix = [.. Mix.Select(m => m with { SpawnTags = [.. m.SpawnTags] })],
        };

    /// <summary>A flat-rate rule of one profile with no journey (tests and simple set-ups).</summary>
    public static EmissionRule ForProfile(Guid profileId) => new() { Mix = [MixLine.Of(profileId)] };
}
