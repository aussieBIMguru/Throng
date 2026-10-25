using System.Text.Json.Serialization;
using Throng.Core.Geometry;
using Throng.Core.Library;

namespace Throng.Core.Model;

/// <summary>
/// A placed stair, escalator or ramp linking a bottom level to a top level (§7.1, D-054). Connectors are
/// parametric: the flights, landings, footprint and floor opening are derived from these values and the two
/// levels' elevations by <see cref="ConnectorLayout"/>, so they always fit the real rise and never drift.
/// </summary>
/// <remarks>
/// Geometry values (riser, going, gradient, landings, incline…) are copied from the preset at placement, so a
/// connector is self-contained and later preset edits never move it. The plan frame is: <see cref="BottomLanding"/>
/// is the centre of the bottom entry edge, <see cref="AngleDeg"/> is the run direction (counter-clockwise from +X).
/// </remarks>
public sealed class Connector
{
    /// <summary>Default auto-opening clearance in metres (D-065).</summary>
    public const double DefaultClearanceM = 0.3;

    /// <summary>Stable identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Display name, e.g. "Stair 1".</summary>
    public string Name { get; set; } = "Connector";

    /// <summary>Id of the preset it was placed from (embedded in the study library), if any.</summary>
    public Guid? TemplateId { get; set; }

    /// <summary>Preset key it was placed from, e.g. "connector.stair-2400" (for display and reset later).</summary>
    public string PresetKey { get; set; } = string.Empty;

    /// <summary>Stair, escalator or ramp.</summary>
    public ConnectorKind Kind { get; set; } = ConnectorKind.Stair;

    /// <summary>Level the connector starts on (lower end).</summary>
    public Guid BottomLevelId { get; set; }

    /// <summary>Level the connector arrives at (upper end).</summary>
    public Guid TopLevelId { get; set; }

    /// <summary>Centre of the bottom entry edge, in plan metres.</summary>
    public Point2 BottomLanding { get; set; }

    /// <summary>Run direction in degrees, counter-clockwise from +X.</summary>
    public double AngleDeg { get; set; }

    /// <summary>Clear width in metres.</summary>
    public double WidthM { get; set; } = 1.5;

    /// <summary>Allowed travel direction (escalators: Up or Down).</summary>
    public ConnectorDirection Direction { get; set; } = ConnectorDirection.Bidirectional;

    /// <summary>Straight or switchback (stairs and ramps; escalators are always straight).</summary>
    public ConnectorShape Shape { get; set; } = ConnectorShape.Straight;

    /// <summary>Side of a switchback's return run, looking up the first run (Tab flips it, D-058).</summary>
    public TurnSide ReturnSide { get; set; } = TurnSide.Left;

    /// <summary>Clearance added around the linked floor opening, in metres (D-065).</summary>
    public double ClearanceM { get; set; } = DefaultClearanceM;

    /// <summary>Shared by the two units of an escalator pair (D-059); null for single connectors.</summary>
    public Guid? PairId { get; set; }

    /// <summary>Target stair riser height in metres (D-069).</summary>
    public double RiserM { get; set; } = 0.17;

    /// <summary>Stair going (tread depth) in metres.</summary>
    public double GoingM { get; set; } = 0.28;

    /// <summary>Maximum risers per stair flight before a landing (D-069).</summary>
    public int MaxRisersPerFlight { get; set; } = 18;

    /// <summary>Minimum landing length in metres (stair landings are also at least the width).</summary>
    public double LandingM { get; set; } = 1.2;

    /// <summary>Gap between switchback runs in metres (D-070).</summary>
    public double SwitchbackGapM { get; set; } = 0.2;

    /// <summary>Ramp gradient as rise/run.</summary>
    public double Gradient { get; set; } = 1.0 / 14.0;

    /// <summary>Longest ramp run between landings in metres (D-072).</summary>
    public double MaxRampRunM { get; set; } = 9.0;

    /// <summary>Escalator incline in degrees (D-071).</summary>
    public double InclineDeg { get; set; } = 30.0;

    /// <summary>Escalator flat run-off at each end in metres (D-071).</summary>
    public double FlatLengthM { get; set; } = 1.2;

    /// <summary>Clear gap to the partner unit of a pair in metres (D-071).</summary>
    public double PairGapM { get; set; } = 0.6;

    /// <summary>
    /// For switchback stairs and ramps spanning 2+ storeys: one circuit per storey stacked at the same start point
    /// (D-091). New connectors default to true; older files load as false so they keep their geometry (D-086).
    /// </summary>
    public bool PerLevelCircuits { get; set; }

    /// <summary>Intermediate levels a per-level stair passes without an exit (D-093, D-094).</summary>
    public List<Guid> ExcludedLevelIds { get; set; } = [];

    /// <summary>Belt length in metres (travelators only, D-090).</summary>
    public double RunLengthM { get; set; } = 20.0;

    /// <summary>
    /// Staff only (file format v4, D-110): public agents never route on it. Staff routing uses it from M3. Older
    /// files load as false.
    /// </summary>
    public bool StaffOnly { get; set; }

    /// <summary>Unit vector of the run direction.</summary>
    public Point2 Axis()
    {
        double radians = AngleDeg * Math.PI / 180.0;
        return new Point2(Math.Cos(radians), Math.Sin(radians));
    }

    /// <summary>True when this connector is one unit of an escalator pair.</summary>
    [JsonIgnore]
    public bool IsPaired => PairId is not null;

    /// <summary>True when the switchback shape applies (stairs and ramps only).</summary>
    [JsonIgnore]
    public bool IsSwitchback => Shape == ConnectorShape.Switchback && Kind is ConnectorKind.Stair or ConnectorKind.Ramp;

    /// <summary>True when stacked per-level circuits apply (a switchback with the option on, D-091).</summary>
    [JsonIgnore]
    public bool UsesCircuits => PerLevelCircuits && IsSwitchback;

    /// <summary>True for travelators, which stay on one level (D-074, D-090).</summary>
    [JsonIgnore]
    public bool IsTravelator => Kind == ConnectorKind.Travelator;

    /// <summary>Returns an independent copy, including the id.</summary>
    public Connector Clone()
    {
        var copy = new Connector();
        copy.CopyFrom(this);
        return copy;
    }

    /// <summary>Copies every value (including the id) from <paramref name="other"/>; used by undo snapshots.</summary>
    public void CopyFrom(Connector other)
    {
        Id = other.Id;
        Name = other.Name;
        TemplateId = other.TemplateId;
        PresetKey = other.PresetKey;
        Kind = other.Kind;
        BottomLevelId = other.BottomLevelId;
        TopLevelId = other.TopLevelId;
        BottomLanding = other.BottomLanding;
        AngleDeg = other.AngleDeg;
        WidthM = other.WidthM;
        Direction = other.Direction;
        Shape = other.Shape;
        ReturnSide = other.ReturnSide;
        ClearanceM = other.ClearanceM;
        PairId = other.PairId;
        RiserM = other.RiserM;
        GoingM = other.GoingM;
        MaxRisersPerFlight = other.MaxRisersPerFlight;
        LandingM = other.LandingM;
        SwitchbackGapM = other.SwitchbackGapM;
        Gradient = other.Gradient;
        MaxRampRunM = other.MaxRampRunM;
        InclineDeg = other.InclineDeg;
        FlatLengthM = other.FlatLengthM;
        PairGapM = other.PairGapM;
        PerLevelCircuits = other.PerLevelCircuits;
        ExcludedLevelIds = [.. other.ExcludedLevelIds];
        RunLengthM = other.RunLengthM;
        StaffOnly = other.StaffOnly;
    }
}
