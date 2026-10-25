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

/// <summary>
/// One stream of agents emitted by a spawn zone (D-096, D-099): who (an agent profile), how many (a rate and a
/// pattern) and where to (the nearest sink or weighted chosen sinks).
/// </summary>
/// <remarks>
/// Immutable: edits replace the rule (and the region's rule list) so undo restores the exact previous value.
/// </remarks>
public sealed record EmissionRule
{
    /// <summary>Stable id.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Agent profile template id (study library first, then the built-in catalogue).</summary>
    public Guid ProfileId { get; init; }

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
        this with { Id = Guid.NewGuid(), Destinations = [.. Destinations], RateCurve = [.. RateCurve] };
}
