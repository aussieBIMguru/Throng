using Throng.Core.Geometry;

namespace Throng.Core.Model;

/// <summary>
/// A placed device (node) from a <see cref="Library.DeviceTemplate"/>: ticket machine, gate line, desk… (D-089).
/// It has a rectangular footprint and a service side; queues and behaviour arrive in M3.
/// </summary>
/// <remarks>
/// Plan frame: <see cref="Position"/> is the footprint centre; <see cref="AngleDeg"/> is the direction the service
/// side faces (counter-clockwise from +X). <see cref="DepthM"/> runs along that direction and <see cref="WidthM"/>
/// across it. Pass-through devices (gates, security) keep their lanes walkable and only block the dividers between
/// lanes; other devices block their whole footprint.
/// </remarks>
public sealed class Device
{
    /// <summary>Default visual height in metres.</summary>
    public const double DefaultHeightM = 1.2;

    /// <summary>Stable identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Display name, e.g. "Ticket machine 1".</summary>
    public string Name { get; set; } = "Device";

    /// <summary>Id of the preset it was placed from (embedded in the study library), if any.</summary>
    public Guid? TemplateId { get; set; }

    /// <summary>Preset key, e.g. "device.gate-line".</summary>
    public string PresetKey { get; set; } = string.Empty;

    /// <summary>Type tag agents ask for (M3), copied from the preset.</summary>
    public string TypeTag { get; set; } = string.Empty;

    /// <summary>Level it stands on.</summary>
    public Guid LevelId { get; set; }

    /// <summary>Footprint centre in plan metres.</summary>
    public Point2 Position { get; set; }

    /// <summary>Direction the service side faces, degrees counter-clockwise from +X.</summary>
    public double AngleDeg { get; set; } = 90;

    /// <summary>Footprint width across the service direction, metres.</summary>
    public double WidthM { get; set; } = 1.0;

    /// <summary>Footprint depth along the service direction, metres.</summary>
    public double DepthM { get; set; } = 1.0;

    /// <summary>Lanes or servers side by side (gate lines, counters).</summary>
    public int Lanes { get; set; } = 1;

    /// <summary>True when people pass through (gates, security); false when they turn around after service.</summary>
    public bool PassThrough { get; set; }

    /// <summary>Visual height in metres.</summary>
    public double HeightM { get; set; } = DefaultHeightM;

    /// <summary>Display colour as #RRGGBB, from the preset.</summary>
    public string ColorHex { get; set; } = "#FACC15";

    /// <summary>Unit vector the service side faces.</summary>
    public Point2 Facing()
    {
        double r = AngleDeg * Math.PI / 180.0;
        return new Point2(Math.Cos(r), Math.Sin(r));
    }

    /// <summary>Returns an independent copy, including the id.</summary>
    public Device Clone()
    {
        var copy = new Device();
        copy.CopyFrom(this);
        return copy;
    }

    /// <summary>Copies every value (including the id) from <paramref name="other"/>.</summary>
    public void CopyFrom(Device other)
    {
        Id = other.Id;
        Name = other.Name;
        TemplateId = other.TemplateId;
        PresetKey = other.PresetKey;
        TypeTag = other.TypeTag;
        LevelId = other.LevelId;
        Position = other.Position;
        AngleDeg = other.AngleDeg;
        WidthM = other.WidthM;
        DepthM = other.DepthM;
        Lanes = other.Lanes;
        PassThrough = other.PassThrough;
        HeightM = other.HeightM;
        ColorHex = other.ColorHex;
    }
}
