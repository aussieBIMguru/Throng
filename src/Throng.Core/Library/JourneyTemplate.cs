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
/// A journey (D-149, BRIEF §8.2): the ordered steps an agent works through between appearing and leaving, such as
/// ticket machine → gates → exit. Emission rules pick a journey per mix line (D-151). A journey without an
/// <see cref="JourneyStepKind.Exit"/> step still ends with one.
/// </summary>
public sealed class JourneyTemplate : TemplateBase
{
    /// <summary>The steps, in order.</summary>
    public List<JourneyStep> Steps { get; set; } = [];

    /// <inheritdoc />
    public override string Summarise()
    {
        if (Steps.Count == 0)
        {
            return "exit";
        }

        string steps = string.Join(" → ", Steps.Select(s => s.Describe()));
        return Steps.Count == 1 ? steps : string.Create(CultureInfo.InvariantCulture, $"{Steps.Count} steps · {steps}");
    }
}
