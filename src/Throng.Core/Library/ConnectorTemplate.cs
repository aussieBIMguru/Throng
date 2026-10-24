using System.Globalization;

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

/// <summary>
/// Stair, escalator, travelator or ramp preset (§7.1, §22.2).
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

    /// <inheritdoc />
    public override string Summarise()
    {
        CultureInfo c = CultureInfo.InvariantCulture;
        string width = $"{WidthM.ToString("0.0#", c)} m";
        return Kind switch
        {
            ConnectorKind.Stair => $"Stair · {width} · {Direction} · {FlowPerMetrePerMin.ToString("0", c)} p/m/min",
            ConnectorKind.Ramp => $"Ramp 1:{(Gradient > 0 ? (1 / Gradient).ToString("0", c) : "?")} · {width} · {FlowPerMetrePerMin.ToString("0", c)} p/m/min",
            _ => $"{Kind} · {width} · {BeltSpeedMps.ToString("0.0#", c)} m/s · {ThroughputPerMin.ToString("0", c)} p/min · {Direction}{(IsPaired ? " · pair" : string.Empty)}",
        };
    }
}
