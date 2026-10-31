using System.Globalization;
using System.Text.Json.Serialization;

namespace Throng.Core.Library;

/// <summary>What a node of a journey graph is (D-302–D-305, D-314).</summary>
public enum JourneyNodeKind
{
    /// <summary>Where every journey starts (D-303): one per journey, one output, no input.</summary>
    Spawn,

    /// <summary>Where a journey ends (D-303): leave by the rule's sinks, at a sink zone, or on a train. No outputs.</summary>
    Sink,

    /// <summary>Use a device of a type: queue, be served, move on. Outputs: done, gave up (D-306).</summary>
    Device,

    /// <summary>Walk into a zone; done as soon as the person is inside.</summary>
    PassZone,

    /// <summary>Walk into a zone and linger there for <see cref="JourneyNode.DwellS"/> seconds.</summary>
    Dwell,

    /// <summary>Branch by chance (D-305): 2–6 outputs with weights, rolled per person on arrival.</summary>
    Chance,

    /// <summary>Branch by tag (D-305): outputs "has the tag" and "hasn't".</summary>
    TagCheck,

    /// <summary>Grant (or remove) a tag and carry on (D-314).</summary>
    SetTag,
}

/// <summary>How a Sink node lets people leave (D-303).</summary>
public enum JourneySinkKind
{
    /// <summary>By the emission rule's destinations (nearest sink or chosen sinks), as before round 018.</summary>
    Destinations,

    /// <summary>At a sink zone: the pinned <see cref="JourneyNode.ZoneId"/>, else sink zones tagged <see cref="JourneyNode.TargetTag"/>, else any sink.</summary>
    Zone,

    /// <summary>On a train of the services tagged <see cref="JourneyNode.TargetTag"/> (empty = any), D-186.</summary>
    BoardTrain,
}

/// <summary>How a device or zone node picks one of its candidates (D-319).</summary>
public enum JourneyChooseBy
{
    /// <summary>Lowest walk + expected queue (devices) or walk (zones), as before round 018b. The default.</summary>
    Quickest,

    /// <summary>Least extra time compared with walking straight to the next node's targets (D-320).</summary>
    AlongTheWay,

    /// <summary>Shortest walk only, queues ignored.</summary>
    Nearest,

    /// <summary>The shortest queue (devices) or the least crowded zone; walk breaks ties.</summary>
    FirstFree,

    /// <summary>At random by each candidate's weight (D-321).</summary>
    RandomWeighted,
}

/// <summary>Whether people at a device node switch to a quicker device mid-queue (D-322).</summary>
public enum JourneyBalk
{
    /// <summary>As the study's run setting says (on unless switched off for diagnostics).</summary>
    Default,

    /// <summary>Always may switch.</summary>
    On,

    /// <summary>Never switch: stay in the queue they joined.</summary>
    Off,
}

/// <summary>One specific device or zone a node may use (D-318), with its weight for <see cref="JourneyChooseBy.RandomWeighted"/>.</summary>
public sealed record JourneyPick
{
    /// <summary>The device's or zone's id.</summary>
    public Guid Id { get; init; }

    /// <summary>Relative weight (D-326): zero or more.</summary>
    public double Weight { get; init; } = 1;

    /// <summary>Creates a pick.</summary>
    public static JourneyPick Of(Guid id, double weight = 1) => new() { Id = id, Weight = weight };
}

/// <summary>
/// One node of a <see cref="JourneyTemplate"/> graph (D-302). Targets are device types and zone tags rather than
/// instances, so a journey works in any study; a specific zone can still be pinned with <see cref="ZoneId"/>.
/// </summary>
/// <remarks>Immutable: edits replace the node (and the journey's node list) so undo restores the exact value.</remarks>
public sealed record JourneyNode
{
    /// <summary>Most outputs a Chance node may have (D-305).</summary>
    public const int MaxChanceOutputs = 6;

    /// <summary>Stable id within the journey; wires refer to it.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>What the node is.</summary>
    public JourneyNodeKind Kind { get; init; } = JourneyNodeKind.Sink;

    /// <summary>Canvas position of the node's top-left corner, device-independent pixels.</summary>
    public double X { get; init; }

    /// <summary>Canvas position of the node's top-left corner, device-independent pixels.</summary>
    public double Y { get; init; }

    /// <summary>Optional display name (e.g. "Northbound"); empty = described from its settings.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>
    /// Device nodes: the device type tag (e.g. "Gate"). Zone nodes and zone sinks: the zone tag (empty = any dwell
    /// zone, any zone, or any sink). Board-train sinks: the service tag (empty = any). Tag check and Set tag: the tag.
    /// </summary>
    public string TargetTag { get; init; } = string.Empty;

    /// <summary>A specific zone for zone nodes and zone sinks; overrides <see cref="TargetTag"/> when set.</summary>
    public Guid? ZoneId { get; init; }

    /// <summary>Device and zone nodes: tag given to the person on completing the node (empty = none, D-314).</summary>
    public string GrantsTag { get; init; } = string.Empty;

    /// <summary>Dwell nodes: linger time, seconds (null on other nodes).</summary>
    public Distribution? DwellS { get; init; }

    /// <summary>Device nodes: what happens when patience runs out in the queue (D-165, D-306).</summary>
    public JourneyGiveUp OnGiveUp { get; init; } = JourneyGiveUp.KeepWaiting;

    /// <summary>Sink nodes: how people leave (D-303).</summary>
    public JourneySinkKind SinkKind { get; init; } = JourneySinkKind.Destinations;

    /// <summary>Chance nodes: one relative weight per output (2–6, D-305); null on other nodes.</summary>
    public List<double>? Weights { get; init; }

    /// <summary>Set tag nodes: true to remove the tag instead of granting it.</summary>
    public bool RemoveTag { get; init; }

    /// <summary>
    /// Device and zone nodes, zone sinks (D-318): specific devices or zones to use instead of <see cref="TargetTag"/>;
    /// null or empty = by type / tag.
    /// </summary>
    public List<JourneyPick>? Picks { get; init; }

    /// <summary>Device and zone nodes, zone sinks: how one candidate is picked (D-319).</summary>
    public JourneyChooseBy ChooseBy { get; init; } = JourneyChooseBy.Quickest;

    /// <summary>Device nodes: switching to a quicker device mid-queue (D-322).</summary>
    public JourneyBalk Balk { get; init; } = JourneyBalk.Default;

    /// <summary>Device nodes: patience in seconds instead of the profile's (D-322); null = the profile's.</summary>
    public double? PatienceS { get; init; }

    /// <summary>True when the node uses a pick list (D-318).</summary>
    [JsonIgnore]
    public bool HasPicks => Picks is { Count: > 0 };

    /// <summary>True for nodes that pick among candidates: devices, zones and zone sinks.</summary>
    [JsonIgnore]
    public bool HasCandidates => Kind is JourneyNodeKind.Device or JourneyNodeKind.PassZone or JourneyNodeKind.Dwell
        || (Kind == JourneyNodeKind.Sink && SinkKind == JourneySinkKind.Zone);

    /// <summary>The step kind the simulation runs for this node, or null for Spawn and the branch nodes.</summary>
    [JsonIgnore]
    public JourneyStepKind? StepKind => Kind switch
    {
        JourneyNodeKind.Device => JourneyStepKind.GoToDevice,
        JourneyNodeKind.PassZone => JourneyStepKind.PassZone,
        JourneyNodeKind.Dwell => JourneyStepKind.Dwell,
        JourneyNodeKind.Sink => SinkKind == JourneySinkKind.BoardTrain ? JourneyStepKind.BoardTrain : JourneyStepKind.Exit,
        _ => null,
    };

    /// <summary>The heading shown on the node: its label, else <see cref="Describe"/>.</summary>
    [JsonIgnore]
    public string Title => string.IsNullOrWhiteSpace(Label) ? Describe() : Label.Trim();

    /// <summary>A short description from the node's settings, e.g. "TicketMachine → ticket" or "Has ticket?".</summary>
    public string Describe()
    {
        CultureInfo c = CultureInfo.InvariantCulture;
        string tag = TargetTag?.Trim() ?? string.Empty;
        string grants = string.IsNullOrWhiteSpace(GrantsTag) ? string.Empty : $" → {GrantsTag.Trim()}";
        string zone = HasPicks ? $"{Picks!.Count.ToString(c)} picked" : ZoneId is not null ? "a pinned zone" : tag.Length > 0 ? tag : string.Empty;
        return Kind switch
        {
            JourneyNodeKind.Spawn => "Spawn",
            JourneyNodeKind.Sink => SinkKind switch
            {
                JourneySinkKind.Zone => $"Exit at {(zone.Length > 0 ? zone : "any sink")}",
                JourneySinkKind.BoardTrain => $"Board {(tag.Length > 0 ? tag : "any")} train",
                _ => "Exit",
            },
            JourneyNodeKind.Device => (HasPicks ? $"{Picks!.Count.ToString(c)} picked device{(Picks.Count == 1 ? string.Empty : "s")}" : tag.Length > 0 ? tag : "device") + grants,
            JourneyNodeKind.PassZone => $"Through {(zone.Length > 0 ? zone : "zone")}{grants}",
            JourneyNodeKind.Dwell => $"Dwell {(zone.Length > 0 ? zone : "zone")} {(DwellS ?? JourneyGraph.DefaultDwell).Describe()} s{grants}",
            JourneyNodeKind.Chance => "Chance " + string.Join(" / ", JourneyGraph.Percentages(this).Select(p => p.ToString("0", c) + "%")),
            JourneyNodeKind.TagCheck => $"Has {(tag.Length > 0 ? tag : "tag")}?",
            JourneyNodeKind.SetTag => $"{(RemoveTag ? "−" : "+")}{(tag.Length > 0 ? tag : "tag")}",
            _ => Kind.ToString(),
        };
    }
}

/// <summary>A wire of a journey graph: output <see cref="Port"/> of node <see cref="From"/> into node <see cref="To"/>.</summary>
public sealed record JourneyWire
{
    /// <summary>The node the wire leaves.</summary>
    public Guid From { get; init; }

    /// <summary>The output port it leaves from (0 = the first, see <see cref="JourneyGraph.PortLabel"/>).</summary>
    public int Port { get; init; }

    /// <summary>The node it enters (every node but Spawn has one input, which takes any number of wires).</summary>
    public Guid To { get; init; }

    /// <summary>Creates a wire.</summary>
    public static JourneyWire Of(Guid from, int port, Guid to) => new() { From = from, Port = port, To = to };
}

/// <summary>A problem with a journey graph's structure (D-302–D-306), for CHECK.</summary>
/// <param name="IsError">True when it blocks a run (a loop, a broken wire); false for a warning.</param>
/// <param name="Message">What's wrong, naming the node.</param>
/// <param name="NodeId">The node concerned, when there is one.</param>
public readonly record struct JourneyGraphProblem(bool IsError, string Message, Guid? NodeId);

/// <summary>
/// Reading, checking and converting journey graphs (D-302–D-306): ports, wires, reachability, loops, a one-line
/// summary, and the conversion of the pre-v11 step list into a chain of nodes laid out left to right.
/// </summary>
public static class JourneyGraph
{
    /// <summary>Canvas column spacing of converted journeys, DIPs.</summary>
    public const double ColumnStep = 230;

    /// <summary>Canvas row spacing of converted journeys (a node around a tag check sits one row down), DIPs.</summary>
    public const double RowStep = 120;

    /// <summary>The dwell time of a dwell node that has none.</summary>
    public static Distribution DefaultDwell { get; } = Distribution.Uniform(30, 120);

    // ------------------------------------------------------------------ ports

    /// <summary>Output ports of a node: Spawn 1, Sink 0, Device 2 (done, gave up), Chance one per weight, Tag check 2, others 1.</summary>
    public static int OutputCount(JourneyNode node) => node.Kind switch
    {
        JourneyNodeKind.Sink => 0,
        JourneyNodeKind.Device => 2,
        JourneyNodeKind.TagCheck => 2,
        JourneyNodeKind.Chance => Math.Clamp(node.Weights?.Count ?? 0, 0, JourneyNode.MaxChanceOutputs),
        _ => 1,
    };

    /// <summary>True for nodes with an input (every node but Spawn).</summary>
    public static bool HasInput(JourneyNode node) => node.Kind != JourneyNodeKind.Spawn;

    /// <summary>A port's label: "done" / "gave up", "has" / "hasn't", a Chance output's share ("40%"), else empty.</summary>
    public static string PortLabel(JourneyNode node, int port)
    {
        switch (node.Kind)
        {
            case JourneyNodeKind.Device:
                return port == 0 ? "done" : "gave up";
            case JourneyNodeKind.TagCheck:
                return port == 0 ? "has" : "hasn't";
            case JourneyNodeKind.Chance:
                IReadOnlyList<double> shares = Percentages(node);
                return port >= 0 && port < shares.Count ? shares[port].ToString("0", CultureInfo.InvariantCulture) + "%" : string.Empty;
            default:
                return string.Empty;
        }
    }

    /// <summary>A Chance node's weights as percentages of their total (zeros when the total isn't positive).</summary>
    public static IReadOnlyList<double> Percentages(JourneyNode node)
    {
        List<double> weights = node.Weights ?? [];
        double total = weights.Where(w => double.IsFinite(w) && w > 0).Sum();
        return [.. weights.Select(w => total > 0 && double.IsFinite(w) && w > 0 ? 100.0 * w / total : 0)];
    }

    /// <summary>The node with an id, or null.</summary>
    public static JourneyNode? Find(JourneyTemplate journey, Guid id) => journey.Nodes.FirstOrDefault(n => n is not null && n.Id == id);

    /// <summary>The journey's Spawn node (the first, if there are several), or null.</summary>
    public static JourneyNode? Spawn(JourneyTemplate journey) => journey.Nodes.FirstOrDefault(n => n is { Kind: JourneyNodeKind.Spawn });

    /// <summary>The node wired to an output port, or null when the port is unwired (or the wire is broken).</summary>
    public static JourneyNode? Next(JourneyTemplate journey, Guid from, int port)
    {
        foreach (JourneyWire wire in journey.Wires)
        {
            if (wire is not null && wire.From == from && wire.Port == port && Find(journey, wire.To) is { } to && to.Kind != JourneyNodeKind.Spawn)
            {
                return to;
            }
        }

        return null;
    }

    /// <summary>The nodes reachable from Spawn along valid wires, Spawn first, in breadth-first order.</summary>
    public static IReadOnlyList<JourneyNode> Reachable(JourneyTemplate journey)
    {
        var order = new List<JourneyNode>();
        if (Spawn(journey) is not { } spawn)
        {
            return order;
        }

        var seen = new HashSet<Guid> { spawn.Id };
        var queue = new Queue<JourneyNode>();
        queue.Enqueue(spawn);
        while (queue.Count > 0)
        {
            JourneyNode node = queue.Dequeue();
            order.Add(node);
            for (int port = 0; port < OutputCount(node); port++)
            {
                if (Next(journey, node.Id, port) is { } next && seen.Add(next.Id))
                {
                    queue.Enqueue(next);
                }
            }
        }

        return order;
    }

    /// <summary>True when following the wires can come back to a node (D-306 forbids it).</summary>
    public static bool HasLoop(JourneyTemplate journey) => FindLoopNode(journey) is not null;

    /// <summary>A node on a loop (following every wire, even a second one from the same output), or null when there's none.</summary>
    public static JourneyNode? FindLoopNode(JourneyTemplate journey)
    {
        var byId = new Dictionary<Guid, JourneyNode>();
        foreach (JourneyNode node in journey.Nodes.Where(n => n is not null))
        {
            byId.TryAdd(node.Id, node);
        }

        var successors = new Dictionary<Guid, List<Guid>>();
        foreach (JourneyWire wire in journey.Wires.Where(w => w is not null))
        {
            if (byId.ContainsKey(wire.From) && byId.TryGetValue(wire.To, out JourneyNode? to) && to.Kind != JourneyNodeKind.Spawn)
            {
                if (!successors.TryGetValue(wire.From, out List<Guid>? list))
                {
                    successors[wire.From] = list = [];
                }

                list.Add(wire.To);
            }
        }

        // Iterative depth-first search with white (0) / grey (1) / black (2) marks.
        var state = new Dictionary<Guid, int>();
        foreach (Guid root in byId.Keys)
        {
            if (state.ContainsKey(root))
            {
                continue;
            }

            var stack = new Stack<(Guid Node, int Next)>();
            stack.Push((root, 0));
            state[root] = 1;
            while (stack.Count > 0)
            {
                (Guid node, int next) = stack.Pop();
                List<Guid> outs = successors.GetValueOrDefault(node) ?? [];
                if (next >= outs.Count)
                {
                    state[node] = 2;
                    continue;
                }

                stack.Push((node, next + 1));
                Guid child = outs[next];
                int mark = state.GetValueOrDefault(child);
                if (mark == 1)
                {
                    return byId[child];
                }

                if (mark == 0)
                {
                    state[child] = 1;
                    stack.Push((child, 0));
                }
            }
        }

        return null;
    }

    /// <summary>The journey's Sink nodes reachable from Spawn, in breadth-first order.</summary>
    public static IReadOnlyList<JourneyNode> Sinks(JourneyTemplate journey) => [.. Reachable(journey).Where(n => n.Kind == JourneyNodeKind.Sink)];

    /// <summary>
    /// Structure problems (D-302–D-306): Spawn missing or repeated, broken wires (unknown nodes, into Spawn, from a
    /// port the node doesn't have), an output with two wires, loops, bad Chance weights and empty tags are errors;
    /// unwired outputs and nodes no one reaches from Spawn are warnings.
    /// </summary>
    public static IReadOnlyList<JourneyGraphProblem> Problems(JourneyTemplate journey)
    {
        var problems = new List<JourneyGraphProblem>();
        List<JourneyNode> nodes = [.. journey.Nodes.Where(n => n is not null)];
        int spawns = nodes.Count(n => n.Kind == JourneyNodeKind.Spawn);
        if (spawns == 0)
        {
            problems.Add(new JourneyGraphProblem(true, "has no Spawn node", null));
        }
        else if (spawns > 1)
        {
            problems.Add(new JourneyGraphProblem(true, "has more than one Spawn node", nodes.Where(n => n.Kind == JourneyNodeKind.Spawn).Skip(1).First().Id));
        }

        var ids = nodes.Select(n => n.Id).ToHashSet();
        var used = new HashSet<(Guid, int)>();
        foreach (JourneyWire wire in journey.Wires.Where(w => w is not null))
        {
            JourneyNode? from = Find(journey, wire.From);
            JourneyNode? to = Find(journey, wire.To);
            if (from is null || to is null || to.Kind == JourneyNodeKind.Spawn || wire.Port < 0 || wire.Port >= OutputCount(from))
            {
                problems.Add(new JourneyGraphProblem(true, "has a broken wire" + (from is not null ? $" from '{from.Title}'" : string.Empty), from?.Id ?? (ids.Contains(wire.To) ? (Guid?)wire.To : null)));
                continue;
            }

            if (!used.Add((wire.From, wire.Port)))
            {
                string port = PortLabel(from, wire.Port);
                problems.Add(new JourneyGraphProblem(true, $"node '{from.Title}' has two wires from one output{(port.Length > 0 ? $" ({port})" : string.Empty)}", from.Id));
            }
        }

        if (FindLoopNode(journey) is { } loop)
        {
            problems.Add(new JourneyGraphProblem(true, $"loops back to node '{loop.Title}'; journeys must run forward from Spawn to a Sink", loop.Id));
        }

        var reachable = Reachable(journey).Select(n => n.Id).ToHashSet();
        foreach (JourneyNode node in nodes)
        {
            switch (node.Kind)
            {
                case JourneyNodeKind.Chance:
                {
                    int count = node.Weights?.Count ?? 0;
                    if (count < 2 || count > JourneyNode.MaxChanceOutputs)
                    {
                        problems.Add(new JourneyGraphProblem(true, string.Create(CultureInfo.InvariantCulture, $"Chance node '{node.Title}' needs 2 to {JourneyNode.MaxChanceOutputs} outputs"), node.Id));
                    }
                    else if (node.Weights!.Any(w => !double.IsFinite(w) || w < 0) || !node.Weights!.Any(w => w > 0))
                    {
                        problems.Add(new JourneyGraphProblem(true, $"Chance node '{node.Title}' needs weights of zero or more, with at least one above zero", node.Id));
                    }

                    break;
                }

                case JourneyNodeKind.TagCheck or JourneyNodeKind.SetTag when string.IsNullOrWhiteSpace(node.TargetTag):
                    problems.Add(new JourneyGraphProblem(true, $"node '{node.Title}' doesn't say which tag", node.Id));
                    break;
            }

            if (!reachable.Contains(node.Id))
            {
                if (node.Kind != JourneyNodeKind.Spawn)
                {
                    problems.Add(new JourneyGraphProblem(false, $"node '{node.Title}' isn't wired from Spawn, so no one reaches it", node.Id));
                }

                continue;
            }

            for (int port = 0; port < OutputCount(node); port++)
            {
                if (Next(journey, node.Id, port) is not null)
                {
                    continue;
                }

                if (node.Kind == JourneyNodeKind.Device && port == 1)
                {
                    if (node.OnGiveUp is JourneyGiveUp.Branch or JourneyGiveUp.JumpTo)
                    {
                        problems.Add(new JourneyGraphProblem(false, $"node '{node.Title}' has nothing wired to 'gave up', so people who give up leave by the rule's sinks", node.Id));
                    }

                    continue;
                }

                string label = PortLabel(node, port);
                string what = node.Kind == JourneyNodeKind.Spawn ? "Spawn isn't wired to anything, so people walk straight to a sink" : $"node '{node.Title}' has nothing wired to its {(label.Length > 0 ? $"'{label}' " : string.Empty)}output, so people there leave by the rule's sinks";
                problems.Add(new JourneyGraphProblem(false, what, node.Id));
            }
        }

        return problems;
    }

    // ------------------------------------------------------------------ summary

    /// <summary>
    /// One line for lists and tooltips: a straight chain reads "TicketMachine → ticket → Gate → Exit"; a graph with
    /// branches reads "6 nodes · 2 branches · 2 sinks: …" with its steps in order.
    /// </summary>
    public static string Summarise(JourneyTemplate journey)
    {
        if (journey.Nodes is not { Count: > 0 } && journey.Steps is { Count: > 0 } steps)
        {
            return string.Join(" → ", steps.Where(s => s is not null).Select(s => s.Describe()));
        }

        IReadOnlyList<JourneyNode> reachable = Reachable(journey);
        List<JourneyNode> shown = [.. reachable.Where(n => n.Kind != JourneyNodeKind.Spawn)];
        if (shown.Count == 0)
        {
            return "exit";
        }

        int branches = reachable.Count(n => n.Kind is JourneyNodeKind.Chance or JourneyNodeKind.TagCheck
            || (n.Kind == JourneyNodeKind.Device && n.OnGiveUp is JourneyGiveUp.Branch or JourneyGiveUp.JumpTo && Next(journey, n.Id, 1) is not null));
        string chain = string.Join(" → ", shown.Select(n => n.Title));
        if (branches == 0)
        {
            return chain;
        }

        int sinks = shown.Count(n => n.Kind == JourneyNodeKind.Sink);
        CultureInfo c = CultureInfo.InvariantCulture;
        return string.Create(c, $"{shown.Count} nodes · {branches} branch{(branches == 1 ? string.Empty : "es")} · {sinks} sink{(sinks == 1 ? string.Empty : "s")}: {string.Join(", ", shown.Where(n => n.StepKind is not null).Select(n => n.Title).Distinct())}");
    }

    // ------------------------------------------------------------------ conversion (D-302)

    /// <summary>
    /// Makes sure a journey is a graph (D-302): a legacy step list is converted (and cleared); an empty journey
    /// becomes Spawn → Exit. Null nodes and wires are dropped. Safe to call more than once.
    /// </summary>
    /// <returns>Notes about the conversion (e.g. a backward give-up jump that became Leave); empty when nothing was converted.</returns>
    public static IReadOnlyList<string> Ensure(JourneyTemplate journey)
    {
        journey.Nodes ??= [];
        journey.Wires ??= [];
        journey.Nodes.RemoveAll(n => n is null);
        journey.Wires.RemoveAll(w => w is null);
        var notes = new List<string>();
        if (journey.Nodes.Count == 0)
        {
            (List<JourneyNode> nodes, List<JourneyWire> wires) = FromSteps(journey.Id, journey.Steps ?? [], notes);
            journey.Nodes = nodes;
            journey.Wires = wires;
        }

        journey.Steps = null;
        return notes;
    }

    /// <summary>A Spawn wired straight to one Exit, laid out on one row.</summary>
    public static (List<JourneyNode> Nodes, List<JourneyWire> Wires) StraightToExit(Guid journeyId) => FromSteps(journeyId, [], []);

    /// <summary>
    /// Converts a pre-v11 step list (D-302, D-305): Spawn, then each step as a node; a step's needs / lacks
    /// conditions become Tag check nodes that go around it and merge back; a forward give-up jump becomes the gave-up
    /// output; steps after the first exit or board-train step are dropped (they could never run); anyone left over
    /// at the end leaves by an Exit sink. Node ids are derived from <paramref name="journeyId"/>, so a conversion is
    /// repeatable.
    /// </summary>
    /// <param name="journeyId">The journey's id (seeds the node ids).</param>
    /// <param name="steps">The legacy steps.</param>
    /// <param name="notes">Receives notes about anything that couldn't be carried over as it was.</param>
    public static (List<JourneyNode> Nodes, List<JourneyWire> Wires) FromSteps(Guid journeyId, IReadOnlyList<JourneyStep> steps, List<string> notes)
    {
        var nodes = new List<JourneyNode>();
        var wires = new List<JourneyWire>();
        int serial = 0;
        int column = 0;
        Guid NewId() => TemplateIds.FromKey(string.Create(CultureInfo.InvariantCulture, $"{journeyId:N}/node/{serial++}"));
        JourneyNode Add(JourneyNode node)
        {
            nodes.Add(node);
            return node;
        }

        // Steps that can run: none after the first step that ends the journey (the old compiler dropped them).
        var kept = new List<JourneyStep>();
        foreach (JourneyStep step in steps)
        {
            if (step is null)
            {
                continue;
            }

            kept.Add(step);
            if (step.EndsJourney)
            {
                break;
            }
        }

        JourneyNode spawn = Add(new JourneyNode { Id = NewId(), Kind = JourneyNodeKind.Spawn, X = 0, Y = 0 });
        column++;

        // Loose ends: outputs still waiting for whatever comes next.
        var loose = new List<(Guid From, int Port)> { (spawn.Id, 0) };
        var jumps = new Dictionary<int, List<(Guid From, int Port)>>();
        void Connect(List<(Guid From, int Port)> ends, Guid to)
        {
            foreach ((Guid from, int port) in ends)
            {
                wires.Add(JourneyWire.Of(from, port, to));
            }

            ends.Clear();
        }

        for (int k = 0; k < kept.Count; k++)
        {
            JourneyStep step = kept[k];
            if (jumps.TryGetValue(k, out List<(Guid From, int Port)>? landing))
            {
                loose.AddRange(landing);
            }

            // Conditions (D-305): needs → "has" carries on, "hasn't" goes around; lacks → the other way round.
            var around = new List<(Guid From, int Port)>();
            bool guarded = false;
            foreach ((string tag, bool needs) in new[] { (step.NeedsTag, true), (step.LacksTag, false) })
            {
                if (string.IsNullOrWhiteSpace(tag))
                {
                    continue;
                }

                JourneyNode check = Add(new JourneyNode { Id = NewId(), Kind = JourneyNodeKind.TagCheck, TargetTag = tag.Trim(), X = column * ColumnStep, Y = 0 });
                column++;
                Connect(loose, check.Id);
                loose.Add((check.Id, needs ? 0 : 1));
                around.Add((check.Id, needs ? 1 : 0));
                guarded = true;
            }

            JourneyNode action = Add(NodeFromStep(step, NewId(), column * ColumnStep, guarded ? RowStep : 0));
            column++;
            Connect(loose, action.Id);

            if (step.Kind == JourneyStepKind.GoToDevice && step.OnGiveUp == JourneyGiveUp.JumpTo)
            {
                if (step.GiveUpStep > k && step.GiveUpStep < kept.Count)
                {
                    if (!jumps.TryGetValue(step.GiveUpStep, out List<(Guid From, int Port)>? list))
                    {
                        jumps[step.GiveUpStep] = list = [];
                    }

                    list.Add((action.Id, 1));
                    nodes[^1] = action = action with { OnGiveUp = JourneyGiveUp.Branch };
                }
                else
                {
                    nodes[^1] = action = action with { OnGiveUp = JourneyGiveUp.Leave };
                    notes.Add(string.Create(CultureInfo.InvariantCulture, $"step {k + 1} ({step.Describe()}) jumped to step {step.GiveUpStep + 1} when patience ran out; that would loop (or the step is gone), so people who give up there now leave"));
                }
            }

            if (!step.EndsJourney)
            {
                loose.Add((action.Id, 0));
            }

            loose.AddRange(around);
        }

        // Jumps past the last step that ran, and everyone still walking, leave by the rule's sinks.
        foreach (List<(Guid From, int Port)> late in jumps.Where(j => j.Key >= kept.Count).Select(j => j.Value))
        {
            loose.AddRange(late);
        }

        if (loose.Count > 0 || kept.Count == 0)
        {
            JourneyNode exit = Add(new JourneyNode { Id = NewId(), Kind = JourneyNodeKind.Sink, SinkKind = JourneySinkKind.Destinations, X = column * ColumnStep, Y = 0 });
            Connect(loose, exit.Id);
        }

        return (nodes, wires);
    }

    /// <summary>The node for one legacy step (its conditions are handled by the caller).</summary>
    private static JourneyNode NodeFromStep(JourneyStep step, Guid id, double x, double y)
    {
        string tag = step.TargetTag?.Trim() ?? string.Empty;
        string grants = step.GrantsTag?.Trim() ?? string.Empty;
        return step.Kind switch
        {
            JourneyStepKind.GoToDevice => new JourneyNode { Id = id, Kind = JourneyNodeKind.Device, TargetTag = tag, GrantsTag = grants, OnGiveUp = step.OnGiveUp, X = x, Y = y },
            JourneyStepKind.PassZone => new JourneyNode { Id = id, Kind = JourneyNodeKind.PassZone, TargetTag = tag, ZoneId = step.ZoneId, GrantsTag = grants, X = x, Y = y },
            JourneyStepKind.Dwell => new JourneyNode { Id = id, Kind = JourneyNodeKind.Dwell, TargetTag = tag, ZoneId = step.ZoneId, GrantsTag = grants, DwellS = step.DwellS ?? DefaultDwell, X = x, Y = y },
            JourneyStepKind.BoardTrain => new JourneyNode { Id = id, Kind = JourneyNodeKind.Sink, SinkKind = JourneySinkKind.BoardTrain, TargetTag = tag, X = x, Y = y },
            _ => new JourneyNode { Id = id, Kind = JourneyNodeKind.Sink, SinkKind = JourneySinkKind.Destinations, X = x, Y = y },
        };
    }

    // ------------------------------------------------------------------ building (built-ins, tests)

    /// <summary>Builds a journey graph in code: add nodes, wire them, then <see cref="Apply"/> to a template.</summary>
    public sealed class Builder
    {
        private readonly Guid _journeyId;
        private readonly List<JourneyNode> _nodes = [];
        private readonly List<JourneyWire> _wires = [];
        private int _serial;

        /// <summary>Starts a graph for the journey with this id (seeds the node ids); adds the Spawn at (0, 0).</summary>
        public Builder(Guid journeyId)
        {
            _journeyId = journeyId;
            Start = Add(new JourneyNode { Kind = JourneyNodeKind.Spawn });
        }

        /// <summary>The Spawn node's id.</summary>
        public Guid Start { get; }

        /// <summary>Adds a node at a column and row (ids are derived from the journey id) and returns its id.</summary>
        public Guid Add(JourneyNode node, int column = 0, int row = 0)
        {
            Guid id = TemplateIds.FromKey(string.Create(CultureInfo.InvariantCulture, $"{_journeyId:N}/node/{_serial++}"));
            _nodes.Add(node with { Id = id, X = column * ColumnStep, Y = row * RowStep });
            return id;
        }

        /// <summary>Wires output <paramref name="port"/> of <paramref name="from"/> into <paramref name="to"/>.</summary>
        public Builder Wire(Guid from, int port, Guid to)
        {
            _wires.Add(JourneyWire.Of(from, port, to));
            return this;
        }

        /// <summary>Wires output 0 of <paramref name="from"/> into <paramref name="to"/>.</summary>
        public Builder Wire(Guid from, Guid to) => Wire(from, 0, to);

        /// <summary>Puts the nodes and wires on <paramref name="journey"/> (replacing any) and returns it.</summary>
        public JourneyTemplate Apply(JourneyTemplate journey)
        {
            journey.Steps = null;
            journey.Nodes = [.. _nodes];
            journey.Wires = [.. _wires];
            return journey;
        }
    }

    /// <summary>A device node (for <see cref="Builder.Add"/>).</summary>
    public static JourneyNode DeviceNode(string typeTag, string grants = "", JourneyGiveUp onGiveUp = JourneyGiveUp.KeepWaiting) =>
        new() { Kind = JourneyNodeKind.Device, TargetTag = typeTag, GrantsTag = grants, OnGiveUp = onGiveUp };

    /// <summary>A dwell node on zones with a tag.</summary>
    public static JourneyNode DwellNode(string zoneTag, Distribution dwell) => new() { Kind = JourneyNodeKind.Dwell, TargetTag = zoneTag, DwellS = dwell };

    /// <summary>A pass-zone node on zones with a tag.</summary>
    public static JourneyNode PassNode(string zoneTag) => new() { Kind = JourneyNodeKind.PassZone, TargetTag = zoneTag };

    /// <summary>A Chance node with these weights (one output each).</summary>
    public static JourneyNode ChanceNode(params double[] weights) => new() { Kind = JourneyNodeKind.Chance, Weights = [.. weights] };

    /// <summary>A Tag check node (outputs: has, hasn't).</summary>
    public static JourneyNode TagCheckNode(string tag) => new() { Kind = JourneyNodeKind.TagCheck, TargetTag = tag };

    /// <summary>A Set tag node.</summary>
    public static JourneyNode SetTagNode(string tag, bool remove = false) => new() { Kind = JourneyNodeKind.SetTag, TargetTag = tag, RemoveTag = remove };

    /// <summary>An Exit sink by the rule's destinations.</summary>
    public static JourneyNode ExitNode(string label = "") => new() { Kind = JourneyNodeKind.Sink, SinkKind = JourneySinkKind.Destinations, Label = label };

    /// <summary>An Exit sink at sink zones tagged <paramref name="zoneTag"/> (empty = any sink).</summary>
    public static JourneyNode ZoneSinkNode(string zoneTag, string label = "") => new() { Kind = JourneyNodeKind.Sink, SinkKind = JourneySinkKind.Zone, TargetTag = zoneTag, Label = label };

    /// <summary>A Board train sink for services tagged <paramref name="serviceTag"/> (empty = any).</summary>
    public static JourneyNode TrainSinkNode(string serviceTag = "", string label = "") => new() { Kind = JourneyNodeKind.Sink, SinkKind = JourneySinkKind.BoardTrain, TargetTag = serviceTag, Label = label };
}
