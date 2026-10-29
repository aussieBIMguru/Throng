using System.Globalization;

namespace Throng.Core.Library;

/// <summary>
/// An agent profile: body, movement and behaviour defaults for a type of person (§8.1).
/// </summary>
public sealed class AgentProfileTemplate : TemplateBase
{
    /// <summary>Body radius in metres (collision/obstruction).</summary>
    public double RadiusM { get; set; } = 0.25;

    /// <summary>Visual height in metres.</summary>
    public double HeightM { get; set; } = 1.75;

    /// <summary>Desired free walking speed in m/s.</summary>
    public Distribution Speed { get; set; } = Distribution.Normal(1.34, 0.26, 0.6, 2.0);

    /// <summary>Speed multiplier walking up stairs.</summary>
    public double StairUpFactor { get; set; } = 0.6;

    /// <summary>Speed multiplier walking down stairs.</summary>
    public double StairDownFactor { get; set; } = 0.7;

    /// <summary>Seconds an agent will wait in a queue before giving up (balking/reneging, D-010).</summary>
    public double PatienceS { get; set; } = 300;

    /// <summary>0 = very random choices, 1 = always picks the lowest cost option.</summary>
    public double Decisiveness { get; set; } = 0.7;

    /// <summary>-1 avoids lifts, 0 neutral, +1 strongly prefers lifts.</summary>
    public double LiftPreference { get; set; }

    /// <summary>
    /// -1 avoids escalators, travelators and lifts, 0 neutral, +1 strongly prefers them where they're available
    /// (D-138). Scales how long the ride and its queue feel in route choice: +1 = 0.6×, −1 = 1.4×.
    /// </summary>
    public double MechanisedPreference { get; set; }

    /// <summary>Default <see cref="StairUpEffort"/> (D-174).</summary>
    public const double DefaultStairUpEffort = 2.0;

    /// <summary>Default <see cref="StairDownEffort"/> (D-174).</summary>
    public const double DefaultStairDownEffort = 1.4;

    /// <summary>
    /// How much longer climbing a stair feels than it takes, in route choice (D-174): 2 = a 10 s climb weighs like
    /// 20 s, so an escalator nearby wins unless it's queued. 1 = time only (before round 009c).
    /// </summary>
    public double StairUpEffort { get; set; } = DefaultStairUpEffort;

    /// <summary>The same for walking down a stair (D-174).</summary>
    public double StairDownEffort { get; set; } = DefaultStairDownEffort;

    /// <summary>Default <see cref="SightRadiusM"/> (D-179).</summary>
    public const double DefaultSightRadiusM = 30.0;

    /// <summary>
    /// How far this person can see crowding, in metres (D-179): stairs, escalators and exits on their floor within
    /// this straight-line distance are judged on their live crowd; further ones on their uncrowded time. Profiles
    /// saved before round 010 load the default.
    /// </summary>
    public double SightRadiusM { get; set; } = DefaultSightRadiusM;

    /// <summary>When true the agent can only use lifts and ramps for level changes.</summary>
    public bool LiftOnly { get; set; }

    /// <summary>
    /// Needs accessible devices (D-213): where a journey step's device type has an accessible device on a floor, these
    /// people use only the accessible ones there; with none they use what's there and CHECK warns (D-225). Profiles
    /// saved before format v8 don't (the built-in wheelchair profile is upgraded on load).
    /// </summary>
    public bool NeedsAccessibleDevices { get; set; }

    /// <summary>Probability of walking (not standing) on an escalator.</summary>
    public double EscalatorWalkProbability { get; set; } = 0.3;

    /// <summary>Journey time considered acceptable, seconds.</summary>
    public double AcceptableJourneyS { get; set; } = 600;

    /// <summary>0..1 tolerance of crowding before frustration builds.</summary>
    public double DensityTolerance { get; set; } = 0.5;

    /// <summary>
    /// Device presets or types this profile favours (or avoids) when choosing which device to use (D-166). Profiles
    /// saved before round 009b have none.
    /// </summary>
    public List<DevicePreference> DevicePreferences { get; set; } = [];

    /// <summary>Total seconds off the choice cost of a device with this preset key and type tag (D-166).</summary>
    public double DeviceBonusS(string? presetKey, string? typeTag)
    {
        double bonus = 0;
        foreach (DevicePreference preference in DevicePreferences ?? [])
        {
            if (preference is not null && preference.Matches(presetKey, typeTag) && double.IsFinite(preference.BonusS))
            {
                bonus += preference.BonusS;
            }
        }

        return bonus;
    }

    /// <inheritdoc />
    public override string Summarise()
    {
        CultureInfo c = CultureInfo.InvariantCulture;
        string access = (LiftOnly ? " · lift/ramp only" : string.Empty) + (NeedsAccessibleDevices ? " · accessible devices only" : string.Empty);
        string prefers = string.Empty;
        if (DevicePreferences is { Count: > 0 } preferences)
        {
            IEnumerable<string> names = preferences
                .Where(p => p is not null && !string.IsNullOrWhiteSpace(p.Target))
                .Select(p => (BuiltInLibrary.Instance.FindByKey(p.Target.Trim())?.Name ?? p.Target.Trim()).ToLowerInvariant());
            prefers = $" · prefers {string.Join(", ", names)}";
        }

        return $"{Speed.Describe()} m/s · r {RadiusM.ToString("0.00", c)} m · patience {PatienceS.ToString("0", c)} s · sees {SightRadiusM.ToString("0", c)} m{access}{prefers}";
    }
}
