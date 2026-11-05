using System.Globalization;

namespace Throng.Core.Library;

/// <summary>Lift dispatch behaviour (§7.2).</summary>
public enum LiftDispatch
{
    /// <summary>Up/down collective: stop for calls in the direction of travel.</summary>
    Collective,

    /// <summary>Two-stop back-and-forth.</summary>
    Shuttle,

    /// <summary>Return to the lobby when empty.</summary>
    UpPeak,

    /// <summary>Destination dispatch: each person is given a car grouped by destination (round 014, D-255).</summary>
    DestinationDispatch,
}

/// <summary>
/// Lift car / bank preset (§7.2, §22.2).
/// </summary>
public sealed class LiftTemplate : TemplateBase
{
    /// <summary>Rated capacity in persons.</summary>
    public int CapacityPersons { get; set; } = 13;

    /// <summary>Rated speed in m/s.</summary>
    public double SpeedMps { get; set; } = 1.6;

    /// <summary>Acceleration in m/s².</summary>
    public double AccelerationMps2 { get; set; } = 1.0;

    /// <summary>Door opening time in seconds.</summary>
    public double DoorOpenS { get; set; } = 2.0;

    /// <summary>Door closing time in seconds.</summary>
    public double DoorCloseS { get; set; } = 2.5;

    /// <summary>Minimum dwell with doors open per stop, seconds.</summary>
    public double DwellS { get; set; } = 3.0;

    /// <summary>Dispatch behaviour.</summary>
    public LiftDispatch Dispatch { get; set; } = LiftDispatch.Collective;

    /// <summary>Internal car width in metres.</summary>
    public double CarWidthM { get; set; } = 1.6;

    /// <summary>Internal car depth in metres.</summary>
    public double CarDepthM { get; set; } = 1.4;

    /// <summary>Mobility-impaired agents get priority boarding.</summary>
    public bool AccessiblePriority { get; set; }

    /// <inheritdoc />
    public override string Summarise()
    {
        CultureInfo c = CultureInfo.InvariantCulture;
        return $"{CapacityPersons.ToString(c)} persons · {SpeedMps.ToString("0.0#", c)} m/s · {Dispatch}{(AccessiblePriority ? " · accessible priority" : string.Empty)}";
    }
}
