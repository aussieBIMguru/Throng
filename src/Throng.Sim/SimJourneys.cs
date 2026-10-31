using Throng.Core.Geometry;
using Throng.Core.Library;

namespace Throng.Sim;

/// <summary>
/// One action node of a journey ready to run (D-149, D-302): a device, a zone, or a sink (exit or board train). The
/// branch nodes around it are <see cref="SimNode"/>s.
/// </summary>
public sealed class SimStep
{
    /// <summary>What to do.</summary>
    public required JourneyStepKind Kind { get; init; }

    /// <summary>
    /// Candidates: device indices (<see cref="SimModel.Devices"/>) for device steps, route target indices
    /// (<c>NavWorld.Sinks</c>) for zone steps, zone sinks and train doors; empty for exits by the rule's sinks.
    /// </summary>
    public required int[] Targets { get; init; }

    /// <summary>Tag bits given on completing the step.</summary>
    public required ulong Grants { get; init; }

    /// <summary>Dwell time for dwell steps.</summary>
    public required Distribution Dwell { get; init; }

    /// <summary>Text for messages, diagnostics and per-sink results, e.g. "TicketMachine" or "Northbound".</summary>
    public required string Label { get; init; }

    /// <summary>Device steps: what a queuer does once their patience runs out (D-165, D-306).</summary>
    public JourneyGiveUp OnGiveUp { get; init; } = JourneyGiveUp.KeepWaiting;

    /// <summary>This step's node (index into <see cref="SimJourney.Nodes"/>).</summary>
    public int Node { get; init; } = -1;

    /// <summary>The node to go on to when done (or when the step's targets can't be reached); −1 = leave by the rule's sinks.</summary>
    public int Next { get; init; } = -1;

    /// <summary>Device steps: the node on the "gave up" output (D-306); −1 = leave.</summary>
    public int GaveUpNext { get; init; } = -1;

    /// <summary>How one of <see cref="Targets"/> is picked (D-319).</summary>
    public JourneyChooseBy ChooseBy { get; init; } = JourneyChooseBy.Quickest;

    /// <summary>Weight per target (D-321), parallel to <see cref="Targets"/>; empty = all 1.</summary>
    public double[] Weights { get; init; } = [];

    /// <summary>Device steps: mid-queue switching (D-322).</summary>
    public JourneyBalk Balk { get; init; } = JourneyBalk.Default;

    /// <summary>Device steps: patience in seconds instead of the profile's (D-322); NaN = the profile's.</summary>
    public double PatienceS { get; init; } = double.NaN;

    /// <summary>The weight of target <paramref name="k"/> (index into <see cref="Targets"/>).</summary>
    public double WeightAt(int k) => k >= 0 && k < Weights.Length ? Weights[k] : 1;

    /// <summary>True for exits and board-train steps (sinks, D-303).</summary>
    public bool IsSink => Kind is JourneyStepKind.Exit or JourneyStepKind.BoardTrain;
}

/// <summary>What a compiled journey node does (D-302, D-305, D-314).</summary>
public enum SimNodeKind
{
    /// <summary>An action: <see cref="SimNode.Step"/> says which.</summary>
    Step,

    /// <summary>Pick an output by weight.</summary>
    Chance,

    /// <summary>Output 0 when the person holds <see cref="SimNode.Bit"/>, else output 1.</summary>
    TagCheck,

    /// <summary>Grant (or remove) <see cref="SimNode.Bit"/>, then output 0.</summary>
    SetTag,
}

/// <summary>One node of a compiled journey graph: an action step, or a branch passed through instantly.</summary>
public sealed class SimNode
{
    /// <summary>What it does.</summary>
    public required SimNodeKind Kind { get; init; }

    /// <summary>Step nodes: index into <see cref="SimJourney.Steps"/>; −1 otherwise.</summary>
    public int Step { get; init; } = -1;

    /// <summary>The node on each output (−1 = unwired: leave by the rule's sinks).</summary>
    public required int[] Next { get; init; }

    /// <summary>Chance nodes: running totals of the output shares, ending at 1.</summary>
    public double[] Cumulative { get; init; } = [];

    /// <summary>Tag check and Set tag nodes: the tag bit (0 when the tag couldn't be given a bit).</summary>
    public ulong Bit { get; init; }

    /// <summary>Set tag nodes: true to remove the tag.</summary>
    public bool Remove { get; init; }

    /// <summary>Chance nodes: the output for a uniform draw in [0, 1).</summary>
    public int Pick(double u)
    {
        int port = PickPort(u);
        return port >= 0 ? Next[port] : -1;
    }

    /// <summary>Chance nodes: the output port for a uniform draw in [0, 1), or −1 when the node has no outputs (D-332 counts it).</summary>
    public int PickPort(double u)
    {
        for (int port = 0; port < Cumulative.Length && port < Next.Length; port++)
        {
            if (u < Cumulative[port])
            {
                return port;
            }
        }

        return Next.Length > 0 ? Next.Length - 1 : -1;
    }
}

/// <summary>A journey ready to run (D-302): its nodes from Spawn's output on, and the action steps among them.</summary>
public sealed class SimJourney
{
    /// <summary>Position in <see cref="SimModel.Journeys"/>.</summary>
    public required int Index { get; init; }

    /// <summary>Display name.</summary>
    public required string Name { get; init; }

    /// <summary>The action steps (devices, zones, sinks), in the order they're reached from Spawn.</summary>
    public required SimStep[] Steps { get; init; }

    /// <summary>Every node reachable from Spawn (Spawn itself left out).</summary>
    public SimNode[] Nodes { get; init; } = [];

    /// <summary>The node Spawn is wired to; −1 = straight to the rule's sinks.</summary>
    public int Start { get; init; } = -1;

    /// <summary>The journey template's id (round 019: the run overlay finds its journey by it).</summary>
    public Guid JourneyId { get; init; }

    /// <summary>The model node (<c>JourneyNode.Id</c>) of each compiled node, parallel to <see cref="Nodes"/> (D-332).</summary>
    public Guid[] NodeIds { get; init; } = [];

    /// <summary>The Spawn node's id (its one output is the wire into <see cref="Start"/>).</summary>
    public Guid SpawnId { get; init; }

    /// <summary>The model node id of compiled node <paramref name="node"/>; <see cref="Guid.Empty"/> for −1 (the rule's sinks).</summary>
    public Guid NodeIdAt(int node) => node >= 0 && node < NodeIds.Length ? NodeIds[node] : Guid.Empty;

    /// <summary>Indices of the sink steps (D-312), in order.</summary>
    public int[] SinkSteps => [.. Enumerable.Range(0, Steps.Length).Where(k => Steps[k].IsSink)];
}

/// <summary>One line of an emitter's mix ready to run (D-151).</summary>
public sealed class SimMixLine
{
    /// <summary>Index into <see cref="SimModel.Profiles"/>.</summary>
    public required int Profile { get; init; }

    /// <summary>Index into <see cref="SimModel.Journeys"/>, or −1 to walk straight to a sink.</summary>
    public required int Journey { get; init; }

    /// <summary>Relative share (positive).</summary>
    public required double Share { get; init; }

    /// <summary>Tags given at spawn, each with its probability (0–1).</summary>
    public required (ulong Bit, double Probability)[] SpawnTags { get; init; }
}

/// <summary>
/// A device ready to run (D-152, D-153): its service area as a route target, its servers (lanes) and the queue lines
/// in front of them. Pass-through devices (gates) have lines on both sides and serve one side per lane at a time;
/// other devices have lines on their service side only.
/// </summary>
public sealed class SimDevice
{
    /// <summary>Spacing of queue slots along a line, metres (accepted default).</summary>
    public const double SlotSpacingM = QueueLines.SlotSpacingM;

    /// <summary>Distance of the service point in front of the footprint edge, metres.</summary>
    public const double ServiceOffsetM = QueueLines.ServiceOffsetM;

    /// <summary>Position in <see cref="SimModel.Devices"/>.</summary>
    public required int Index { get; init; }

    /// <summary>Source device id.</summary>
    public required Guid DeviceId { get; init; }

    /// <summary>Display name.</summary>
    public required string Name { get; init; }

    /// <summary>Type tag journeys ask for.</summary>
    public required string TypeTag { get; init; }

    /// <summary>Preset key the device was placed from (profile device preferences match it, D-166).</summary>
    public string PresetKey { get; init; } = string.Empty;

    /// <summary>Footprint width across <see cref="Facing"/>.</summary>
    public double WidthM { get; init; } = 1.0;

    /// <summary>Floor surface it stands on.</summary>
    public required int Floor { get; init; }

    /// <summary>Route target (index into <c>NavWorld.Sinks</c>) leading to its service area or lanes.</summary>
    public required int Target { get; init; }

    /// <summary>Footprint centre.</summary>
    public required Point2 Centre { get; init; }

    /// <summary>Unit direction the service side faces.</summary>
    public required Point2 Facing { get; init; }

    /// <summary>Footprint depth along <see cref="Facing"/>.</summary>
    public required double DepthM { get; init; }

    /// <summary>Lanes or servers side by side.</summary>
    public required int Lanes { get; init; }

    /// <summary>True for gates and security: people walk through after service.</summary>
    public required bool PassThrough { get; init; }

    /// <summary>Service time per person.</summary>
    public required Distribution ServiceTime { get; init; }

    /// <summary>Mean service time, seconds (for expected waits).</summary>
    public required double MeanServiceS { get; init; }

    /// <summary>Seconds added to this device's cost in choices (minus the preset's choice weight).</summary>
    public required double ChoicePenaltyS { get; init; }

    /// <summary>Accessible device (D-213, D-223): the device's override, else its preset's flag.</summary>
    public bool Accessible { get; init; }

    /// <summary>First lane index in the engine's per-lane arrays.</summary>
    public required int LaneBase { get; init; }

    /// <summary>First line index in the engine's per-line arrays.</summary>
    public required int LineBase { get; init; }

    /// <summary>
    /// Queue slot positions per line (line = side × <see cref="Lanes"/> + lane; side 0 is the service side, side 1
    /// the far side of pass-through devices). Slot 0 is the service point. With a drawn queue, the shared snake is
    /// the extra line <see cref="SnakeLine"/> after the lane lines, and each service-side lane line holds only its
    /// service point (D-163).
    /// </summary>
    public required Point2[][] Slots { get; init; }

    /// <summary>Local line index of the shared snake (D-163), or −1 when the device has no drawn queue.</summary>
    public int SnakeLine { get; init; } = -1;

    /// <summary>True when a drawn queue feeds every service-side lane from one snake.</summary>
    public bool HasSnake => SnakeLine >= 0 && Slots[SnakeLine].Length > 0;

    /// <summary>
    /// Where people waiting for a lane stand on each side (D-167): 2 m out from the service points and clear of the
    /// lane mouths, one spot per end of the device that has floor (possibly none).
    /// </summary>
    public Point2[][] HoldPoints { get; init; } = [];

    /// <summary>Queue sides: 2 for pass-through devices, 1 otherwise.</summary>
    public int Sides => PassThrough ? 2 : 1;

    /// <summary>Line index (local) of a lane on a side.</summary>
    public int Line(int side, int lane) => (side * Lanes) + lane;

    /// <summary>
    /// Pass-through devices: the side each lane is fixed to take people from (0 front, 1 back), or −1 for either way
    /// (D-196). Empty for other devices.
    /// </summary>
    public int[] LaneSide { get; init; } = [];

    /// <summary>The side lane <paramref name="lane"/> is fixed to, or −1 (D-196).</summary>
    public int FixedSide(int lane) => lane >= 0 && lane < LaneSide.Length ? LaneSide[lane] : -1;

    /// <summary>True when some lane can take people from <paramref name="side"/> (always for non-pass-through devices).</summary>
    public bool ServesSide(int side)
    {
        if (!PassThrough)
        {
            return true;
        }

        for (int lane = 0; lane < Lanes; lane++)
        {
            int fixedSide = FixedSide(lane);
            if (fixedSide < 0 || fixedSide == side)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The side a plan point is on (0 = service side). Non-pass-through devices only queue on the service side.
    /// </summary>
    public int SideOf(Point2 p) => PassThrough && Point2.Dot(p - Centre, Facing) < 0 ? 1 : 0;

    /// <summary>Where someone served from <paramref name="side"/> in <paramref name="lane"/> walks out on the far side.</summary>
    public Point2 FarPoint(int side, int lane)
    {
        // Straight through from this side's service point, to a little beyond the far side's service point.
        Point2[] line = Slots[Line(side, lane)];
        Point2 start = line.Length > 0 ? line[0] : Centre;
        Point2 through = side == 0 ? Facing * -1 : Facing;
        return start + (through * (DepthM + (2 * ServiceOffsetM) + 0.5));
    }
}
