using System.Globalization;

namespace Throng.Core.Library;

/// <summary>Whether a device is placed as a point object or drawn as a zone.</summary>
public enum DeviceKind
{
    /// <summary>A placed object with a footprint and service position(s).</summary>
    Point,

    /// <summary>A drawn area (dwell, waiting, entrance, vehicle door).</summary>
    Zone,
}

/// <summary>How agents wait for a node (§5).</summary>
public enum QueueMode
{
    /// <summary>Agents cluster freely near the service point.</summary>
    None,

    /// <summary>No drawn queue; agents hold in a loose crowd and are served FIFO.</summary>
    AutoCrowd,

    /// <summary>Drawn polyline queue with slots.</summary>
    Linear,

    /// <summary>One queue feeding several servers (bank style).</summary>
    Shared,

    /// <summary>Releases N agents every T seconds or when N are waiting.</summary>
    BatchRelease,
}

/// <summary>
/// A device (node) preset: ticket machines, gates, security, desks, dwell and spawn/sink zones (§4, §22.1).
/// </summary>
public sealed class DeviceTemplate : TemplateBase
{
    /// <summary>Point object or drawn zone.</summary>
    public DeviceKind Kind { get; set; } = DeviceKind.Point;

    /// <summary>Type tag agents request, e.g. "TicketMachine". Agents ask for types, not instances.</summary>
    public string TypeTag { get; set; } = string.Empty;

    /// <summary>Agents served simultaneously (lanes / windows).</summary>
    public int Servers { get; set; } = 1;

    /// <summary>Service time per agent in seconds.</summary>
    public Distribution ServiceTime { get; set; } = Distribution.Fixed(0);

    /// <summary>Dwell time inside zone devices, seconds (null when not a dwell device).</summary>
    public Distribution? Dwell { get; set; }

    /// <summary>Queue behaviour.</summary>
    public QueueMode Queue { get; set; } = QueueMode.None;

    /// <summary>True when agents exit on the far side (gates, security); false when they turn around.</summary>
    public bool PassThrough { get; set; }

    /// <summary>Max agents committed (queue + service); 0 = unlimited.</summary>
    public int Capacity { get; set; }

    /// <summary>Bias added to attractiveness when agents choose between same-type nodes.</summary>
    public double ChoiceWeight { get; set; }

    /// <summary>Footprint width in metres (point devices).</summary>
    public double WidthM { get; set; } = 1.0;

    /// <summary>Footprint depth in metres (point devices).</summary>
    public double DepthM { get; set; } = 1.0;

    /// <summary>Agents can enter the study here.</summary>
    public bool IsSpawn { get; set; }

    /// <summary>Agents can leave the study here.</summary>
    public bool IsSink { get; set; }

    /// <summary>
    /// Accessible device (D-213, D-223): people whose profile needs accessible devices use only these where the floor
    /// has one of the step's type; others soft-avoid them (D-224). The default for placed devices, which can override
    /// it. Presets saved before format v8 are not accessible (the built-in accessible gate is upgraded on load).
    /// </summary>
    public bool Accessible { get; set; }

    /// <inheritdoc />
    public override string Summarise()
    {
        if (Kind == DeviceKind.Zone)
        {
            string role = (IsSpawn, IsSink) switch
            {
                (true, true) => "spawn + sink",
                (true, false) => "spawn",
                (false, true) => "sink",
                _ => Dwell is not null ? $"dwell {Dwell.Describe()} s" : "zone",
            };
            string cap = Capacity > 0 ? $" · cap {Capacity}" : string.Empty;
            return $"{TypeTag} · {role}{cap}";
        }

        string servers = Servers == 1 ? "1 server" : $"{Servers.ToString(CultureInfo.InvariantCulture)} lanes";
        string flow = PassThrough ? " · pass-through" : string.Empty;
        string access = Accessible ? " · accessible" : string.Empty;
        return $"{TypeTag} · {servers} · {ServiceTime.Describe()} s · {Queue}{flow}{access}";
    }
}
