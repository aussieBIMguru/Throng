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

    /// <summary>When true the agent can only use lifts and ramps for level changes.</summary>
    public bool LiftOnly { get; set; }

    /// <summary>Probability of walking (not standing) on an escalator.</summary>
    public double EscalatorWalkProbability { get; set; } = 0.3;

    /// <summary>Journey time considered acceptable, seconds.</summary>
    public double AcceptableJourneyS { get; set; } = 600;

    /// <summary>0..1 tolerance of crowding before frustration builds.</summary>
    public double DensityTolerance { get; set; } = 0.5;

    /// <inheritdoc />
    public override string Summarise()
    {
        CultureInfo c = CultureInfo.InvariantCulture;
        string access = LiftOnly ? " · lift/ramp only" : string.Empty;
        return $"{Speed.Describe()} m/s · r {RadiusM.ToString("0.00", c)} m · patience {PatienceS.ToString("0", c)} s{access}";
    }
}
