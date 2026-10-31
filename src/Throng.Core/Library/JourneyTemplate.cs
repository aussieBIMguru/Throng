using System.Globalization;
using System.Text.Json.Serialization;

namespace Throng.Core.Library;

/// <summary>What one step of a journey asks an agent to do (D-149).</summary>
public enum JourneyStepKind
{
    /// <summary>Use a device of a type (<see cref="JourneyStep.TargetTag"/>): queue, be served, move on (D-152, D-153).</summary>
    GoToDevice,

    /// <summary>Walk into a zone; the step is done as soon as the agent is inside.</summary>
    PassZone,

    /// <summary>Walk into a zone and linger there for <see cref="JourneyStep.DwellS"/> seconds.</summary>
    Dwell,

    /// <summary>Leave the study at one of the emission rule's sinks (the rule's destinations decide which).</summary>
    Exit,

    /// <summary>
    /// Leave on a train (D-186): go to a door of a train service whose tag is <see cref="JourneyStep.TargetTag"/>
    /// (empty = any service), wait beside it, and board when the doors open, if there's room. Ends the journey.
    /// </summary>
    BoardTrain,
}

/// <summary>
/// One step of a <see cref="JourneyTemplate"/> (D-149, D-150). Steps target device types and zone tags rather than
/// instances, so a journey works in any study; a specific zone can still be pinned with <see cref="ZoneId"/>.
/// </summary>
/// <remarks>Immutable: edits replace the step (and the journey's step list) so undo restores the exact value.</remarks>
public sealed record JourneyStep
{
    /// <summary>What to do.</summary>
    public JourneyStepKind Kind { get; init; } = JourneyStepKind.Exit;

    /// <summary>
    /// Device type tag for <see cref="JourneyStepKind.GoToDevice"/> (e.g. "Gate"); for zone steps, the zone tag
    /// (<c>Region.Tag</c>, e.g. "Retail"). Empty on a zone step means any dwell zone (Dwell) or any zone (PassZone).
    /// </summary>
    public string TargetTag { get; init; } = string.Empty;

    /// <summary>A specific zone for zone steps; overrides <see cref="TargetTag"/> when set.</summary>
    public Guid? ZoneId { get; init; }

    /// <summary>The step only applies to agents carrying this tag (empty = no condition).</summary>
    public string NeedsTag { get; init; } = string.Empty;

    /// <summary>The step only applies to agents without this tag (empty = no condition), e.g. "ticket" at a machine.</summary>
    public string LacksTag { get; init; } = string.Empty;

    /// <summary>Tag given to the agent when it completes the step (empty = none).</summary>
    public string GrantsTag { get; init; } = string.Empty;

    /// <summary>Linger time for <see cref="JourneyStepKind.Dwell"/>, seconds.</summary>
    public Distribution DwellS { get; init; } = Distribution.Uniform(30, 120);

    /// <summary>
    /// Device steps: what happens when the person's patience runs out in the queue (D-165). Keep waiting (stress
    /// only) by default.
    /// </summary>
    public JourneyGiveUp OnGiveUp { get; init; } = JourneyGiveUp.KeepWaiting;

    /// <summary>
    /// The step to carry on from when <see cref="OnGiveUp"/> is <see cref="JourneyGiveUp.JumpTo"/>: an index into
    /// the journey's steps (0 = the first step). Shown 1-based in the editor.
    /// </summary>
    public int GiveUpStep { get; init; }

    /// <summary>Creates a device step.</summary>
    public static JourneyStep Device(string typeTag, string needs = "", string lacks = "", string grants = "") =>
        new() { Kind = JourneyStepKind.GoToDevice, TargetTag = typeTag, NeedsTag = needs, LacksTag = lacks, GrantsTag = grants };

    /// <summary>Creates a dwell step on zones with a tag.</summary>
    public static JourneyStep DwellIn(string zoneTag, Distribution dwell, string needs = "", string lacks = "") =>
        new() { Kind = JourneyStepKind.Dwell, TargetTag = zoneTag, DwellS = dwell, NeedsTag = needs, LacksTag = lacks };

    /// <summary>The exit step.</summary>
    public static JourneyStep ExitStep() => new() { Kind = JourneyStepKind.Exit };

    /// <summary>Creates a Board train step for services with a tag (empty = any service).</summary>
    public static JourneyStep BoardTrainStep(string serviceTag = "") => new() { Kind = JourneyStepKind.BoardTrain, TargetTag = serviceTag };

    /// <summary>True for the steps that end a journey (exit and board train).</summary>
    [JsonIgnore]
    public bool EndsJourney => Kind is JourneyStepKind.Exit or JourneyStepKind.BoardTrain;

    /// <summary>Short description for lists, e.g. "TicketMachine (no ticket → ticket)".</summary>
    public string Describe()
    {
        string what = Kind switch
        {
            JourneyStepKind.GoToDevice => TargetTag,
            JourneyStepKind.PassZone => $"through {(string.IsNullOrEmpty(TargetTag) ? "zone" : TargetTag)}",
            JourneyStepKind.Dwell => $"dwell {(string.IsNullOrEmpty(TargetTag) ? "zone" : TargetTag)} {DwellS.Describe()} s",
            JourneyStepKind.BoardTrain => $"board {(string.IsNullOrEmpty(TargetTag) ? "any" : TargetTag)} train",
            _ => "exit",
        };

        var conditions = new List<string>(4);
        if (!string.IsNullOrEmpty(NeedsTag))
        {
            conditions.Add($"needs {NeedsTag}");
        }

        if (!string.IsNullOrEmpty(LacksTag))
        {
            conditions.Add($"no {LacksTag}");
        }

        if (!string.IsNullOrEmpty(GrantsTag))
        {
            conditions.Add($"→ {GrantsTag}");
        }

        if (Kind == JourneyStepKind.GoToDevice && OnGiveUp != JourneyGiveUp.KeepWaiting)
        {
            conditions.Add(OnGiveUp == JourneyGiveUp.Leave
                ? "gives up: leave"
                : string.Create(CultureInfo.InvariantCulture, $"gives up: step {GiveUpStep + 1}"));
        }

        return conditions.Count == 0 ? what : $"{what} ({string.Join(", ", conditions)})";
    }
}

/// <summary>
/// A journey (D-149, D-302, BRIEF §8.2): a graph of nodes wired from one Spawn to one or more Sinks, such as
/// spawn → has ticket? → ticket machine → gate → chance → north / south train. Emission rules pick a journey per mix
/// line (D-151), or the profile's default journey (D-304). Graphs have no loops (D-306).
/// </summary>
/// <remarks>
/// Files before format v11 (and .throngprofile bundles before format 2) hold an ordered <see cref="Steps"/> list
/// instead; <see cref="JourneyGraph.Ensure"/> converts it into <see cref="Nodes"/> and <see cref="Wires"/> once, on
/// load, and clears it.
/// </remarks>
public sealed class JourneyTemplate : TemplateBase
{
    /// <summary>
    /// Legacy ordered steps (round 009 to 017). Only read when loading older files; null once converted, and never
    /// written by this version.
    /// </summary>
    public List<JourneyStep>? Steps { get; set; }

    /// <summary>The nodes (D-302): one Spawn, one or more Sinks, and the steps and branches between.</summary>
    public List<JourneyNode> Nodes { get; set; } = [];

    /// <summary>The wires: each joins one output port of a node to another node's input.</summary>
    public List<JourneyWire> Wires { get; set; } = [];

    /// <inheritdoc />
    public override string Summarise() => JourneyGraph.Summarise(this);
}
