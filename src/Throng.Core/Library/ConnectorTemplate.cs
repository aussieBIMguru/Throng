using System.Globalization;
using System.Text.Json.Serialization;

namespace Throng.Core.Library;

/// <summary>Kind of level-changing or moving-floor connector (§7.1).</summary>
public enum ConnectorKind
{
    /// <summary>Fixed stair, bidirectional by default.</summary>
    Stair,

    /// <summary>Inclined moving stair.</summary>
    Escalator,

    /// <summary>Horizontal (or shallow) moving walk.</summary>
    Travelator,

    /// <summary>Sloped walkable connector.</summary>
    Ramp,
}

/// <summary>Allowed travel direction on a connector.</summary>
public enum ConnectorDirection
{
    /// <summary>Both directions.</summary>
    Bidirectional,

    /// <summary>Upward only.</summary>
    Up,

    /// <summary>Downward only.</summary>
    Down,

    /// <summary>One way along its drawn direction (travelators).</summary>
    OneWay,

    /// <summary>One way, reversible by schedule (escalator pairs, peak reversal).</summary>
    Reversible,
}

/// <summary>Plan shape of a stair or ramp (D-057, D-070, D-072).</summary>
public enum ConnectorShape
{
    /// <summary>One straight run, with intermediate landings where needed.</summary>
    Straight,

    /// <summary>Two runs side by side joined by a half landing; the return run comes back alongside the first.</summary>
    Switchback,
}

/// <summary>Side a switchback's return run sits on, looking up the first run (D-058).</summary>
public enum TurnSide
{
    /// <summary>Return run on the left.</summary>
    Left,

    /// <summary>Return run on the right.</summary>
    Right,
}

/// <summary>
/// Stair, escalator, travelator or ramp preset (§7.1, §22.2). Placing one copies its geometry values onto the
/// new <see cref="Throng.Core.Model.Connector"/>, so later edits to the preset never move connectors already placed.
/// </summary>
public sealed class ConnectorTemplate : TemplateBase
{
    /// <summary>Connector kind.</summary>
    public ConnectorKind Kind { get; set; } = ConnectorKind.Stair;

    /// <summary>Clear width in metres.</summary>
    public double WidthM { get; set; } = 1.5;

    /// <summary>Allowed direction.</summary>
    public ConnectorDirection Direction { get; set; } = ConnectorDirection.Bidirectional;

    /// <summary>Belt speed in m/s (escalators, travelators).</summary>
    public double BeltSpeedMps { get; set; }

    /// <summary>Width-based flow limit for stairs and ramps, persons per metre per minute.</summary>
    public double FlowPerMetrePerMin { get; set; }

    /// <summary>Fixed throughput for moving connectors, persons per minute.</summary>
    public double ThroughputPerMin { get; set; }

    /// <summary>Stair riser height in metres.</summary>
    public double RiserM { get; set; } = 0.17;

    /// <summary>Stair going (tread depth) in metres.</summary>
    public double GoingM { get; set; } = 0.28;

    /// <summary>Ramp gradient as rise/run (e.g. 1/14).</summary>
    public double Gradient { get; set; }

    /// <summary>Placed as a linked up + down pair.</summary>
    public bool IsPaired { get; set; }

    /// <summary>Maximum risers in one stair flight before a landing is added (D-069).</summary>
    public int MaxRisersPerFlight { get; set; } = 18;

    /// <summary>Default plan shape for stairs and ramps (D-070).</summary>
    public ConnectorShape Shape { get; set; } = ConnectorShape.Straight;

    /// <summary>Gap between the two runs of a switchback, in metres (D-070).</summary>
    public double SwitchbackGapM { get; set; } = 0.2;

    /// <summary>Minimum landing length in metres; stair landings are also at least the stair width (D-057, D-072).</summary>
    public double LandingM { get; set; } = 1.2;

    /// <summary>Longest ramp run between landings, in metres (D-072).</summary>
    public double MaxRampRunM { get; set; } = 9.0;

    /// <summary>Escalator incline in degrees (D-054, D-071).</summary>
    public double InclineDeg { get; set; } = 30.0;

    /// <summary>Flat run-off at each end of an escalator, in metres (D-071).</summary>
    public double FlatLengthM { get; set; } = 1.2;

    /// <summary>Clear gap between the two units of a pair, in metres (D-071).</summary>
    public double PairGapM { get; set; } = 0.6;

    /// <summary>True for kinds that are placed with CONNECT (they change level). Travelators wait for round 005 (D-074).</summary>
    [JsonIgnore]
    public bool ChangesLevel => Kind is ConnectorKind.Stair or ConnectorKind.Escalator or ConnectorKind.Ramp;

    /// <inheritdoc />
    public override string Summarise()
    {
        CultureInfo c = CultureInfo.InvariantCulture;
        string width = $"{WidthM.ToString("0.0#", c)} m";
        return Kind switch
        {
            ConnectorKind.Stair => $"Stair{(Shape == ConnectorShape.Switchback ? " switchback" : string.Empty)} · {width} · {Direction} · {FlowPerMetrePerMin.ToString("0", c)} p/m/min",
            ConnectorKind.Ramp => $"Ramp 1:{(Gradient > 0 ? (1 / Gradient).ToString("0", c) : "?")} · {width} · {FlowPerMetrePerMin.ToString("0", c)} p/m/min",
            _ => $"{Kind} · {width} · {BeltSpeedMps.ToString("0.0#", c)} m/s · {ThroughputPerMin.ToString("0", c)} p/min · {Direction}{(IsPaired ? " · pair" : string.Empty)}",
        };
    }
}
