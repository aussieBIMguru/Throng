using System.Globalization;
using Throng.Core.Geometry;
using Throng.Core.Library;
using Throng.Core.Model;
using Throng.Core.Validation;
using Throng.Sim;
using Throng.Sim.Nav;

namespace Throng.Analysis.Checks;

/// <summary>
/// The static capacity check (BRIEF §10, round 016, D-273) and the demand-based rules (D-274), worked out from a
/// compiled model without running it. Every emission rule's people are walked once along their nominal route
/// (spawn → each journey step's nearest target → sink) with the route planner, and their demand minute by minute
/// (the rule's rate × its rate curve; train doors at their mean rate × the service's curve) is added to every device
/// group, lift and connector on the way. A journey step only gets the share of people it applies to: its needs / lacks
/// tags are followed from the mix line's spawn tags and the grants of earlier steps (r016 fix: not everyone needs a
/// ticket). Then:
/// <list type="bullet">
/// <item>a device group or connector is run as a simple queue minute by minute (arrivals in, capacity out); a queue
/// over a minute's service warns with its peak, when, and whether it clears by the end;</item>
/// <item>a train burst that takes longer to clear a device or connector than the time between trains warns, and one that
/// takes over a minute is noted;</item>
/// <item>stairs, ramps and travelators with demand get an Info line with the expected Fruin Level of Service at the
/// peak;</item>
/// <item>escalators no one uses while everyone between their levels goes the other way warn;</item>
/// <item>devices no journey uses warn.</item>
/// </list>
/// Rough by design ("roughly right"): everyone takes the single best route, and waits don't feed back into choices.
/// </summary>
public static class CapacityCheck
{
    /// <summary>Minutes assumed when the study runs until stopped.</summary>
    private const int IndefiniteMinutes = 60;

    /// <summary>Longest horizon worked out, minutes.</summary>
    private const int MaxMinutes = 24 * 60;

    /// <summary>Resources busier than this share of capacity at the peak, without a real queue, get an Info line.</summary>
    private const double BusyShare = 0.8;

    /// <summary>Capacity Info lines kept per level, the busiest first (round 017, D-296); warnings are all kept.</summary>
    private const int InfoPerLevel = 5;

    /// <summary>Most legs a route walk follows (connectors chained floor to floor).</summary>
    private const int MaxLegs = 16;

    /// <summary>Horizontal walking speed on stairs for the Level of Service estimate, metres per minute.</summary>
    private const double StairSpeedMpm = 30;

    /// <summary>Walking speed on ramps and travelators for the Level of Service estimate, metres per minute.</summary>
    private const double WalkSpeedMpm = 72;

    private static readonly CultureInfo C = CultureInfo.InvariantCulture;

    /// <summary>Capacity, escalator direction and unused device findings for a compiled model.</summary>
    public static IReadOnlyList<ValidationMessage> Run(Study study, SimModel model)
    {
        int minutes = model.DurationS is { } d && d > 0 ? Math.Clamp((int)Math.Ceiling(d / 60), 1, MaxMinutes) : IndefiniteMinutes;
        var load = new Load(model, minutes);
        var trainEmitters = new HashSet<int>();
        foreach (SimTrain train in model.Trains)
        {
            foreach ((int emitter, _) in train.DoorEmitters.SelectMany(door => door))
            {
                trainEmitters.Add(emitter);
            }
        }

        // Steady emitters: their rate through the rate curve.
        foreach (SimEmitter emitter in model.Emitters)
        {
            if (!trainEmitters.Contains(emitter.Index))
            {
                WalkEmitter(model, load, emitter, RateSeries(emitter.Rule, minutes), burst: null);
            }
        }

        // Train doors: the mean rate of people getting off through the service's curve, and each train as a burst.
        for (int t = 0; t < model.Trains.Count; t++)
        {
            SimTrain train = model.Trains[t];
            double total = train.DoorEmitters.SelectMany(door => door).Sum(e => Math.Max(0, e.Weight));
            if (total <= 0 || train.AlightPerTrain <= 0 || train.HeadwayS <= 0)
            {
                continue;
            }

            double[] series = TrainSeries(train, minutes);
            foreach ((int e, double weight) in train.DoorEmitters.SelectMany(door => door))
            {
                if (e >= 0 && e < model.Emitters.Count && weight > 0)
                {
                    double share = weight / total;
                    WalkEmitter(model, load, model.Emitters[e], Scale(series, share), burst: (t, train.AlightPerTrain * share));
                }
            }
        }

        var findings = new CapacityFindings();
        DeviceFindings(model, load, findings);
        ConnectorFindings(study, model, load, findings);
        TrainFindings(model, load, findings);
        EscalatorFindings(study, model, load, findings);
        UnusedDeviceFindings(study, model, load, findings);
        return findings.Trimmed(study, InfoPerLevel);
    }

    /// <summary>People per minute for each minute of the study: the rule's rate × its rate curve (flat without one).</summary>
    public static double[] RateSeries(EmissionRule rule, int minutes)
    {
        (double[] curveMinutes, double[] multipliers) = RateCurves.Compile(rule.RateCurve);
        double rate = Math.Max(0, rule.RatePerMinute);
        var series = new double[Math.Max(1, minutes)];
        for (int m = 0; m < series.Length; m++)
        {
            double multiplier = multipliers.Length > 0 ? RateCurves.MultiplierAt(curveMinutes, multipliers, m + 0.5) : 1;
            series[m] = rate * Math.Max(0, multiplier);
        }

        return series;
    }

    /// <summary>
    /// A simple queue run minute by minute: arrivals <paramref name="demand"/>, served up to <paramref name="capacity"/>
    /// a minute. Returns the longest queue, the minute it happened (0-based) and the queue left at the end.
    /// </summary>
    public static (double Peak, int PeakMinute, double End) Queue(double[] demand, double capacity)
    {
        double queue = 0, peak = 0;
        int at = 0;
        for (int m = 0; m < demand.Length; m++)
        {
            queue = Math.Max(0, queue + demand[m] - capacity);
            if (queue > peak)
            {
                peak = queue;
                at = m;
            }
        }

        return (peak, at, queue);
    }

    /// <summary>Mean people getting off per minute for each minute, through the service's curve, from its first train.</summary>
    private static double[] TrainSeries(SimTrain train, int minutes)
    {
        double mean = train.AlightPerTrain * 60 / train.HeadwayS;
        var series = new double[Math.Max(1, minutes)];
        for (int m = 0; m < series.Length; m++)
        {
            double midS = (m + 0.5) * 60;
            if (midS < train.OffsetS)
            {
                continue;
            }

            double multiplier = train.CurveMultipliers.Length > 0 ? RateCurves.MultiplierAt(train.CurveMinutes, train.CurveMultipliers, m + 0.5) : 1;
            series[m] = mean * Math.Max(0, multiplier);
        }

        return series;
    }

    /// <summary>Walks every mix line of an emitter, splitting the series by share.</summary>
    private static void WalkEmitter(SimModel model, Load load, SimEmitter emitter, double[] series, (int Train, double People)? burst)
    {
        double total = emitter.TotalShare;
        if (total <= 0 || series.All(v => v <= 0) || emitter.Cells.Length == 0)
        {
            return;
        }

        NavWorld world = model.World;
        Point2 start = world.Surfaces[emitter.Floor].CellCentre(emitter.Cells[emitter.Cells.Length / 2]);
        foreach (SimMixLine line in emitter.Lines)
        {
            if (line.Share <= 0)
            {
                continue;
            }

            var flow = new Flow(Scale(series, line.Share / total), burst is { } b ? (b.Train, b.People * line.Share / total) : null);
            SimProfile profile = model.Profiles[line.Profile];
            if (line.Journey < 0 || line.Journey >= model.Journeys.Count)
            {
                ToRuleSinks(model, load, emitter, profile, emitter.Floor, start, flow);
                continue;
            }

            // Chance each tag bit is held, from the spawn tags; the journey's checks and grants change it per path.
            var held = new Dictionary<ulong, double>();
            foreach ((ulong bit, double probability) in line.SpawnTags)
            {
                held[bit] = Math.Clamp(probability, 0, 1);
            }

            SimJourney journey = model.Journeys[line.Journey];
            int paths = 0;
            WalkNode(model, load, emitter, profile, journey, journey.Start, flow, emitter.Floor, start, held, ref paths, 0);
        }
    }

    /// <summary>Most paths followed through one journey per mix line; past it, the rest go straight to the rule's sinks.</summary>
    private const int MaxPaths = 512;

    /// <summary>Most nodes along one path (a guard only: validation refuses loops, D-306).</summary>
    private const int MaxDepth = 256;

    /// <summary>
    /// Follows a journey graph from <paramref name="node"/> (D-311): steps walk to their nearest target, like an
    /// arriving person with an empty building, and load devices; Chance splits the flow by its shares, Tag check by
    /// the chance the tag is held (then held, or not, on each side), Set tag and grants set it; merges simply carry
    /// on. Sinks walk to their targets; unwired outputs to the rule's sinks.
    /// </summary>
    private static void WalkNode(
        SimModel model, Load load, SimEmitter emitter, SimProfile profile, SimJourney journey, int node, Flow flow, int floor, Point2 at,
        Dictionary<ulong, double> held, ref int paths, int depth)
    {
        while (true)
        {
            if (node < 0 || node >= journey.Nodes.Length || depth++ > MaxDepth || paths > MaxPaths)
            {
                ToRuleSinks(model, load, emitter, profile, floor, at, flow);
                return;
            }

            SimNode n = journey.Nodes[node];
            switch (n.Kind)
            {
                case SimNodeKind.Chance:
                {
                    double previous = 0;
                    for (int port = 0; port < n.Cumulative.Length && port < n.Next.Length; port++)
                    {
                        double share = n.Cumulative[port] - previous;
                        previous = n.Cumulative[port];
                        if (share > 1e-9)
                        {
                            paths++;
                            WalkNode(model, load, emitter, profile, journey, n.Next[port], flow.Scaled(share), floor, at, new Dictionary<ulong, double>(held), ref paths, depth);
                        }
                    }

                    return;
                }

                case SimNodeKind.TagCheck:
                {
                    double p = n.Bit == 0 ? 0 : held.GetValueOrDefault(n.Bit);
                    if (n.Next.Length < 2)
                    {
                        node = -1;
                        continue;
                    }

                    if (p > 1e-9)
                    {
                        paths++;
                        var has = new Dictionary<ulong, double>(held) { [n.Bit] = 1 };
                        WalkNode(model, load, emitter, profile, journey, n.Next[0], flow.Scaled(p), floor, at, has, ref paths, depth);
                    }

                    if (1 - p > 1e-9)
                    {
                        paths++;
                        var hasNot = new Dictionary<ulong, double>(held);
                        if (n.Bit != 0)
                        {
                            hasNot[n.Bit] = 0;
                        }

                        WalkNode(model, load, emitter, profile, journey, n.Next[1], flow.Scaled(1 - p), floor, at, hasNot, ref paths, depth);
                    }

                    return;
                }

                case SimNodeKind.SetTag:
                    if (n.Bit != 0)
                    {
                        held[n.Bit] = n.Remove ? 0 : 1;
                    }

                    node = n.Next.Length > 0 ? n.Next[0] : -1;
                    continue;
            }

            SimStep step = journey.Steps[n.Step];
            int[] targets = step.Kind == JourneyStepKind.GoToDevice ? [.. step.Targets.Select(t => model.Devices[t].Target)] : step.Targets;
            if (step.ChooseBy == JourneyChooseBy.RandomWeighted && targets.Length > 1 && step.Kind != JourneyStepKind.BoardTrain)
            {
                // Random by weight (D-321): the flow splits over the reachable candidates by weight, each a path.
                SplitByWeight(model, load, emitter, profile, journey, step, targets, flow, floor, at, held, ref paths, depth);
                return;
            }

            int best = targets.Length > 0 ? Nearest(model, floor, at, targets, profile) : -1;
            if (step.Kind == JourneyStepKind.Exit)
            {
                if (best >= 0)
                {
                    Walk(model, load, ref floor, ref at, targets[best], profile, flow);
                }
                else
                {
                    ToRuleSinks(model, load, emitter, profile, floor, at, flow);
                }

                return;
            }

            if (best >= 0)
            {
                Walk(model, load, ref floor, ref at, targets[best], profile, flow);
                if (step.Kind == JourneyStepKind.GoToDevice)
                {
                    load.AddDeviceGroup(step.Targets, flow);
                }
                else if (step.Kind == JourneyStepKind.BoardTrain)
                {
                    // They leave on the train, not through a sink.
                    return;
                }

                for (int k = 0; k < 64; k++)
                {
                    ulong bit = 1UL << k;
                    if ((step.Grants & bit) != 0)
                    {
                        held[bit] = 1;
                    }
                }
            }

            // Steps no one can reach are passed by, as in the run.
            node = step.Next;
        }
    }

    /// <summary>
    /// Random by weight (D-321): each reachable candidate with a weight above zero takes its share of the flow, walks
    /// there, loads the device group, takes the step's grants and carries on along the step's output as its own path.
    /// </summary>
    private static void SplitByWeight(
        SimModel model, Load load, SimEmitter emitter, SimProfile profile, SimJourney journey, SimStep step, int[] targets, Flow flow, int floor, Point2 at,
        Dictionary<ulong, double> held, ref int paths, int depth)
    {
        var reachable = new List<(int Index, double Weight)>();
        for (int k = 0; k < targets.Length; k++)
        {
            double w = step.WeightAt(k);
            if (w > 0 && double.IsFinite(RoutePlanner.Choose(model.Routes, floor, at.X, at.Y, targets[k], profile.Mobility, profile.Template, null, out _, out _)))
            {
                reachable.Add((k, w));
            }
        }

        double total = reachable.Sum(r => r.Weight);
        if (total <= 0)
        {
            // No one can reach any of them: passed by, as in the run.
            WalkNode(model, load, emitter, profile, journey, step.IsSink ? -1 : step.Next, flow, floor, at, held, ref paths, depth);
            return;
        }

        foreach ((int k, double w) in reachable)
        {
            Flow part = flow.Scaled(w / total);
            int f = floor;
            Point2 p = at;
            Walk(model, load, ref f, ref p, targets[k], profile, part);
            if (step.IsSink)
            {
                continue;
            }

            if (step.Kind == JourneyStepKind.GoToDevice)
            {
                load.AddDeviceGroup(step.Targets, part);
            }

            var branch = new Dictionary<ulong, double>(held);
            for (int b = 0; b < 64; b++)
            {
                ulong bit = 1UL << b;
                if ((step.Grants & bit) != 0)
                {
                    branch[bit] = 1;
                }
            }

            paths++;
            WalkNode(model, load, emitter, profile, journey, step.Next, part, f, p, branch, ref paths, depth);
        }
    }

    /// <summary>On to the rule's sinks, split by weight (nearest sink: the nearest one).</summary>
    private static void ToRuleSinks(SimModel model, Load load, SimEmitter emitter, SimProfile profile, int floor, Point2 at, Flow flow)
    {
        IReadOnlyList<(int Sink, double Weight)> sinks = emitter.Nearest
            ? NearestSink(model, floor, at, emitter.Destinations, profile)
            : emitter.Destinations;
        double weights = sinks.Sum(s => Math.Max(0, s.Weight));
        foreach ((int sink, double weight) in sinks)
        {
            if (weight > 0 && weights > 0)
            {
                int f = floor;
                Point2 p = at;
                Walk(model, load, ref f, ref p, sink, profile, flow.Scaled(weight / weights));
            }
        }
    }

    /// <summary>
    /// The share of a mix line's people who reach each node of a journey (D-311), with <paramref name="held"/> the
    /// chance each tag bit is held at spawn (bits taken as independent): Chance splits by share, Tag check by the
    /// chance the tag is held, merges add up. Index = <see cref="SimJourney.Nodes"/>.
    /// </summary>
    public static double[] NodeShares(SimJourney journey, IReadOnlyDictionary<ulong, double> held) => FlowShares(journey, held).Nodes;

    /// <summary>
    /// Node shares as <see cref="NodeShares"/>, plus the share leaving each output port (round 019, D-334): Chance
    /// ports by their shares, Tag check by the chance the tag is held, everything else along its first output (a
    /// device's "gave up" is predicted at 0: the model has no patience).
    /// </summary>
    public static JourneyShares FlowShares(SimJourney journey, IReadOnlyDictionary<ulong, double> held)
    {
        var shares = new double[journey.Nodes.Length];
        var wires = new double[journey.Nodes.Length][];
        for (int n = 0; n < wires.Length; n++)
        {
            wires[n] = new double[journey.Nodes[n].Next.Length];
        }

        int paths = 0;
        Share(journey, journey.Start, 1, new Dictionary<ulong, double>(held), shares, wires, ref paths, 0);
        return new JourneyShares(shares, wires);
    }

    /// <summary>
    /// Predicted shares of a journey for everyone on it (<paramref name="profile"/> &lt; 0) or one profile (D-334):
    /// each mix line on the journey weighted by its share of its emitter's base rate; null when no line uses it.
    /// </summary>
    public static JourneyShares? PredictedShares(SimModel model, int journey, int profile)
    {
        if (journey < 0 || journey >= model.Journeys.Count)
        {
            return null;
        }

        SimJourney sim = model.Journeys[journey];
        var nodes = new double[sim.Nodes.Length];
        var wires = new double[sim.Nodes.Length][];
        for (int n = 0; n < wires.Length; n++)
        {
            wires[n] = new double[sim.Nodes[n].Next.Length];
        }

        double weights = 0;
        foreach (SimEmitter emitter in model.Emitters)
        {
            double total = emitter.TotalShare;
            foreach (SimMixLine line in emitter.Lines)
            {
                if (line.Journey != journey || (profile >= 0 && line.Profile != profile) || !(line.Share > 0) || !(total > 0))
                {
                    continue;
                }

                double weight = Math.Max(0, emitter.Rule.RatePerMinute) * line.Share / total;
                if (!(weight > 0))
                {
                    continue;
                }

                var held = new Dictionary<ulong, double>();
                foreach ((ulong bit, double probability) in line.SpawnTags)
                {
                    held[bit] = Math.Clamp(probability, 0, 1);
                }

                JourneyShares one = FlowShares(sim, held);
                for (int n = 0; n < nodes.Length; n++)
                {
                    nodes[n] += weight * one.Nodes[n];
                    for (int port = 0; port < wires[n].Length; port++)
                    {
                        wires[n][port] += weight * one.Wires[n][port];
                    }
                }

                weights += weight;
            }
        }

        if (!(weights > 0))
        {
            return null;
        }

        for (int n = 0; n < nodes.Length; n++)
        {
            nodes[n] /= weights;
            for (int port = 0; port < wires[n].Length; port++)
            {
                wires[n][port] /= weights;
            }
        }

        return new JourneyShares(nodes, wires);
    }

    private static void Share(SimJourney journey, int node, double share, Dictionary<ulong, double> held, double[] shares, double[][] wires, ref int paths, int depth)
    {
        while (node >= 0 && node < journey.Nodes.Length && depth++ <= MaxDepth && paths <= MaxPaths && share > 1e-12)
        {
            shares[node] += share;
            SimNode n = journey.Nodes[node];
            switch (n.Kind)
            {
                case SimNodeKind.Chance:
                {
                    double previous = 0;
                    for (int port = 0; port < n.Cumulative.Length && port < n.Next.Length; port++)
                    {
                        double part = n.Cumulative[port] - previous;
                        previous = n.Cumulative[port];
                        wires[node][port] += share * part;
                        paths++;
                        Share(journey, n.Next[port], share * part, new Dictionary<ulong, double>(held), shares, wires, ref paths, depth);
                    }

                    return;
                }

                case SimNodeKind.TagCheck when n.Next.Length >= 2:
                {
                    double p = n.Bit == 0 ? 0 : held.GetValueOrDefault(n.Bit);
                    paths += 2;
                    wires[node][0] += share * p;
                    wires[node][1] += share * (1 - p);
                    Share(journey, n.Next[0], share * p, new Dictionary<ulong, double>(held) { [n.Bit] = 1 }, shares, wires, ref paths, depth);
                    var hasNot = new Dictionary<ulong, double>(held);
                    if (n.Bit != 0)
                    {
                        hasNot[n.Bit] = 0;
                    }

                    Share(journey, n.Next[1], share * (1 - p), hasNot, shares, wires, ref paths, depth);
                    return;
                }

                case SimNodeKind.SetTag:
                    if (n.Bit != 0)
                    {
                        held[n.Bit] = n.Remove ? 0 : 1;
                    }

                    if (wires[node].Length > 0)
                    {
                        wires[node][0] += share;
                    }

                    node = n.Next.Length > 0 ? n.Next[0] : -1;
                    continue;

                case SimNodeKind.Step:
                {
                    SimStep step = journey.Steps[n.Step];
                    if (step.IsSink)
                    {
                        return;
                    }

                    for (int k = 0; k < 64; k++)
                    {
                        ulong bit = 1UL << k;
                        if ((step.Grants & bit) != 0)
                        {
                            held[bit] = 1;
                        }
                    }

                    if (wires[node].Length > 0)
                    {
                        wires[node][0] += share;
                    }

                    node = step.Next;
                    continue;
                }

                default:
                    return;
            }
        }
    }

    /// <summary>Index into <paramref name="targets"/> of the cheapest one to reach, or −1.</summary>
    private static int Nearest(SimModel model, int floor, Point2 at, int[] targets, SimProfile profile)
    {
        int best = -1;
        double bestCost = double.PositiveInfinity;
        for (int k = 0; k < targets.Length; k++)
        {
            double cost = RoutePlanner.Choose(model.Routes, floor, at.X, at.Y, targets[k], profile.Mobility, profile.Template, null, out _, out _);
            if (cost < bestCost)
            {
                bestCost = cost;
                best = k;
            }
        }

        return best;
    }

    /// <summary>The nearest of the emitter's sinks, as a single full-weight destination (or none).</summary>
    private static List<(int Sink, double Weight)> NearestSink(SimModel model, int floor, Point2 at, (int Sink, double Weight)[] sinks, SimProfile profile)
    {
        int[] ids = [.. sinks.Select(s => s.Sink)];
        int best = Nearest(model, floor, at, ids, profile);
        return best < 0 ? [] : [(ids[best], 1.0)];
    }

    /// <summary>
    /// Follows the planner from (<paramref name="floor"/>, <paramref name="at"/>) to <paramref name="target"/>, adding
    /// the flow to every connector it takes; leaves the position at the target.
    /// </summary>
    private static void Walk(SimModel model, Load load, ref int floor, ref Point2 at, int target, SimProfile profile, Flow flow)
    {
        NavWorld world = model.World;
        for (int leg = 0; leg < MaxLegs; leg++)
        {
            double cost = RoutePlanner.Choose(model.Routes, floor, at.X, at.Y, target, profile.Mobility, profile.Template, null, out bool direct, out RouteOption option);
            if (!double.IsFinite(cost) || direct || option.Connector < 0 || option.Entry < 0 || option.Entry >= world.Portals.Count
                || option.Exit < 0 || option.Exit >= world.Portals.Count)
            {
                break;
            }

            NavPortal entry = world.Portals[option.Entry];
            NavPortal exit = world.Portals[option.Exit];
            load.AddConnector(option.Connector, exit.ElevationM > entry.ElevationM, flow);
            floor = exit.Floor;
            at = world.Surfaces[floor].CellCentre(exit.FloorCell);
        }

        if (target >= 0 && target < world.Sinks.Count && world.Sinks[target].Cells.Length > 0)
        {
            NavSink sink = world.Sinks[target];
            floor = sink.Floor;
            at = world.Surfaces[floor].CellCentre(sink.Cells[sink.Cells.Length / 2]);
        }
    }

    private static void DeviceFindings(SimModel model, Load load, CapacityFindings findings)
    {
        foreach ((string key, double[] demand) in load.Groups)
        {
            int[] devices = load.GroupDevices[key];
            double capacity = devices.Sum(i => DeviceCapacityPerMin(model.Devices[i]));
            int lanes = devices.Sum(i => Math.Max(1, model.Devices[i].Lanes));
            string what = DeviceGroupName(model, devices);
            Guid id = model.Devices[devices[0]].DeviceId;
            string serves = string.Create(C, $"{lanes} lane{(lanes == 1 ? string.Empty : "s")} serve about {capacity:0.#} a minute");
            QueueFinding(findings, what, devices.Length == 1 ? "it" : "them", serves, demand, capacity, id);
        }
    }

    private static void ConnectorFindings(Study study, SimModel model, Load load, CapacityFindings findings)
    {
        foreach ((int index, double[] demand) in load.Connectors)
        {
            NavConnector connector = model.World.Connectors[index];
            Guid id = connector.IsLift ? connector.ShaftId : connector.ConnectorId;
            double capacity = connector.IsLift ? connector.CapacityPerSecond * 60 : connector.ThroughputPerMin;
            string kind = KindName(connector.Kind);
            if (capacity > 0 && QueueFinding(findings, $"{kind} '{connector.Name}'", "it", string.Create(C, $"it carries about {capacity:0.#} a minute"), demand, capacity, id))
            {
                continue;
            }

            (double peak, int peakMinute) = Peak(demand);
            if (peak > 0 && connector.Kind is ConnectorKind.Stair or ConnectorKind.Ramp or ConnectorKind.Travelator
                && study.Connectors.FirstOrDefault(c => c.Id == connector.ConnectorId) is { } drawn && drawn.WidthM > 0)
            {
                double perMetre = peak / drawn.WidthM;
                bool stair = connector.Kind == ConnectorKind.Stair;
                double density = perMetre / (stair ? StairSpeedMpm : WalkSpeedMpm);
                LevelOfService los = FruinLevelOfService.FromDensity(density, stair ? LosAreaType.Stair : LosAreaType.Walkway);
                findings.AddInfo(peak, new ValidationMessage(ValidationSeverity.Info, string.Create(C,
                    $"Capacity: {kind} '{connector.Name}': up to about {peak:0} people a minute (minute {peakMinute + 1}), {perMetre:0} per metre of width: expect Level of Service {los} (Fruin {(stair ? "stair" : "walkway")})."), id)
                { Rule = ValidationRules.Capacity });
            }
        }
    }

    /// <summary>
    /// Runs the queue and adds a Warning when it builds past a minute's service, or an Info when the peak is busy but
    /// the queue stays short. True when a finding was added.
    /// </summary>
    private static bool QueueFinding(CapacityFindings findings, string what, string pronoun, string serves, double[] demand, double capacity, Guid id)
    {
        if (!(capacity > 0) || double.IsPositiveInfinity(capacity))
        {
            return false;
        }

        (double peakDemand, int peakDemandMinute) = Peak(demand);
        (double queue, int queueMinute, double end) = Queue(demand, capacity);
        if (queue >= Math.Max(5, capacity))
        {
            string after = end < 1
                ? " It clears by the end."
                : string.Create(C, $" About {end:0} are still waiting at the end.");
            findings.Add(new ValidationMessage(ValidationSeverity.Warning, string.Create(C,
                $"Capacity: {what}: up to about {peakDemand:0} people a minute need {pronoun} (minute {peakDemandMinute + 1}), but {serves}. The queue peaks at ~{queue:0} people (~{queue / capacity:0} min wait) around minute {queueMinute + 1}.{after}"), id)
            { Rule = ValidationRules.Capacity });
            return true;
        }

        if (peakDemand >= capacity * BusyShare)
        {
            findings.AddInfo(peakDemand, new ValidationMessage(ValidationSeverity.Info, string.Create(C,
                $"Capacity: {what} run at up to about {100 * peakDemand / capacity:0} % of capacity (minute {peakDemandMinute + 1}, {peakDemand:0} of {capacity:0.#} a minute); expect short queues in surges."), id)
            { Rule = ValidationRules.Capacity });
            return true;
        }

        return false;
    }

    /// <summary>The highest value and its minute.</summary>
    private static (double Peak, int Minute) Peak(double[] series)
    {
        double peak = 0;
        int at = 0;
        for (int m = 0; m < series.Length; m++)
        {
            if (series[m] > peak)
            {
                peak = series[m];
                at = m;
            }
        }

        return (peak, at);
    }

    private static void TrainFindings(SimModel model, Load load, CapacityFindings findings)
    {
        foreach ((string key, Dictionary<int, double> bursts) in load.Bursts)
        {
            foreach ((int t, double people) in bursts)
            {
                SimTrain train = model.Trains[t];
                (string what, double capacity, Guid id) = load.Describe(key);
                if (!(capacity > 0) || double.IsPositiveInfinity(capacity) || people <= 0)
                {
                    continue;
                }

                double clearMin = people / capacity;
                double betweenMin = train.HeadwayS / 60;
                if (train.HeadwayS > 0 && clearMin > betweenMin)
                {
                    findings.Add(new ValidationMessage(ValidationSeverity.Warning, string.Create(C,
                        $"Capacity: each '{train.Name}' train's ~{people:0} people need ~{clearMin:0.#} min to get through {what} ({capacity:0.#} a minute), longer than the {betweenMin:0.#} min between trains: the queue keeps growing."), id)
                    { Rule = ValidationRules.Capacity });
                }
                else if (clearMin >= 1)
                {
                    findings.AddInfo(people / Math.Max(1, train.HeadwayS / 60), new ValidationMessage(ValidationSeverity.Info, string.Create(C,
                        $"Capacity: each '{train.Name}' train's ~{people:0} people take ~{clearMin:0.#} min to get through {what} ({capacity:0.#} a minute): expect a ~{clearMin:0} min queue after each train."), id)
                    { Rule = ValidationRules.Capacity });
                }
            }
        }
    }

    private static void EscalatorFindings(Study study, SimModel model, Load load, CapacityFindings findings)
    {
        foreach (Connector escalator in study.Connectors.Where(c => c.Kind == ConnectorKind.Escalator && c.Direction != ConnectorDirection.Bidirectional))
        {
            NavConnector? nav = model.World.Connectors.FirstOrDefault(n => n.ConnectorId == escalator.Id);
            if (nav is null || load.Connectors.ContainsKey(nav.Index))
            {
                continue;
            }

            bool up = escalator.Direction == ConnectorDirection.Up;
            double against = 0;
            foreach (((int index, bool goingUp), double[] series) in load.Directions)
            {
                NavConnector other = model.World.Connectors[index];
                if (goingUp != up && SameLevels(model, other, escalator))
                {
                    against = Math.Max(against, Peak(series).Peak);
                }
            }

            if (against > 0)
            {
                string levels = $"{study.FindLevel(escalator.BottomLevelId)?.Name ?? "?"} and {study.FindLevel(escalator.TopLevelId)?.Name ?? "?"}";
                findings.Add(new ValidationMessage(ValidationSeverity.Warning, string.Create(C,
                    $"Escalator '{escalator.Name}' runs {(up ? "up" : "down")}, but everyone between {levels} goes {(up ? "down" : "up")} (up to about {against:0} a minute), so it carries no one. Turn it round?"), escalator.Id)
                { Rule = ValidationRules.EscalatorDemand });
            }
        }
    }

    /// <summary>True when a nav connector links the same two levels as the escalator.</summary>
    private static bool SameLevels(SimModel model, NavConnector other, Connector escalator)
    {
        var levels = new HashSet<Guid>();
        foreach (int p in other.Portals)
        {
            levels.Add(model.World.Portals[p].LevelId);
        }

        return levels.Contains(escalator.BottomLevelId) && levels.Contains(escalator.TopLevelId);
    }

    /// <summary>
    /// Every placed device no walked journey step sends people to. The model only holds devices some journey asks for by
    /// type (r016 fix), so this goes over the study's devices.
    /// </summary>
    private static void UnusedDeviceFindings(Study study, SimModel model, Load load, CapacityFindings findings)
    {
        var used = new HashSet<Guid>(load.UsedDevices.Select(d => model.Devices[d].DeviceId));
        foreach (Device device in study.Devices)
        {
            if (!used.Contains(device.Id))
            {
                findings.Add(new ValidationMessage(ValidationSeverity.Warning,
                    $"Device '{device.Name}' isn't used: no emission rule's journey has a '{device.TypeTag}' step that people can reach it from.", device.Id)
                { Rule = ValidationRules.UnusedDevice });
            }
        }
    }

    /// <summary>A device's people per minute: lanes × 60 ÷ mean service seconds.</summary>
    private static double DeviceCapacityPerMin(SimDevice device) =>
        device.MeanServiceS > 0 ? Math.Max(1, device.Lanes) * 60 / device.MeanServiceS : double.PositiveInfinity;

    private static string DeviceGroupName(SimModel model, int[] devices) => devices.Length == 1
        ? $"'{model.Devices[devices[0]].Name}'"
        : $"the {devices.Length} '{model.Devices[devices[0]].TypeTag}' devices";

    private static string KindName(ConnectorKind kind) => kind switch
    {
        ConnectorKind.Stair => "stair",
        ConnectorKind.Escalator => "escalator",
        ConnectorKind.Ramp => "ramp",
        ConnectorKind.Travelator => "travelator",
        ConnectorKind.Lift => "lift",
        _ => "connector",
    };

    private static double[] Scale(double[] series, double share)
    {
        var scaled = new double[series.Length];
        for (int m = 0; m < series.Length; m++)
        {
            scaled[m] = series[m] * share;
        }

        return scaled;
    }

    /// <summary>People per minute for each minute, and per train for train bursts.</summary>
    private readonly record struct Flow(double[] Series, (int Train, double People)? Burst)
    {
        public Flow Scaled(double share) => new(Scale(Series, share), Burst is { } b ? (b.Train, b.People * share) : null);
    }

    /// <summary>Demand gathered from every walk, as per-minute series.</summary>
    private sealed class Load(SimModel model, int minutes)
    {
        public Dictionary<string, double[]> Groups { get; } = new();

        public Dictionary<string, int[]> GroupDevices { get; } = new();

        public Dictionary<int, double[]> Connectors { get; } = new();

        public Dictionary<(int Connector, bool Up), double[]> Directions { get; } = new();

        /// <summary>Per resource key ("d:…" device group, "c:…" connector): people per train.</summary>
        public Dictionary<string, Dictionary<int, double>> Bursts { get; } = new();

        public HashSet<int> UsedDevices { get; } = [];

        public void AddDeviceGroup(int[] devices, Flow flow)
        {
            string key = "d:" + string.Join(",", devices.Order());
            if (!Groups.TryGetValue(key, out double[]? demand))
            {
                Groups[key] = demand = new double[minutes];
                GroupDevices[key] = [.. devices.Order()];
            }

            Add(demand, flow.Series);
            UsedDevices.UnionWith(devices);
            AddBurst(key, flow);
        }

        public void AddConnector(int connector, bool up, Flow flow)
        {
            if (!Connectors.TryGetValue(connector, out double[]? demand))
            {
                Connectors[connector] = demand = new double[minutes];
            }

            Add(demand, flow.Series);
            if (!Directions.TryGetValue((connector, up), out double[]? way))
            {
                Directions[(connector, up)] = way = new double[minutes];
            }

            Add(way, flow.Series);
            AddBurst("c:" + connector.ToString(C), flow);
        }

        /// <summary>Name, capacity per minute and object of a resource key.</summary>
        public (string What, double Capacity, Guid Id) Describe(string key)
        {
            if (key.StartsWith("d:", StringComparison.Ordinal))
            {
                int[] devices = GroupDevices[key];
                return (DeviceGroupName(model, devices), devices.Sum(i => DeviceCapacityPerMin(model.Devices[i])), model.Devices[devices[0]].DeviceId);
            }

            NavConnector c = model.World.Connectors[int.Parse(key.AsSpan(2), C)];
            return ($"{KindName(c.Kind)} '{c.Name}'", c.IsLift ? c.CapacityPerSecond * 60 : c.ThroughputPerMin, c.IsLift ? c.ShaftId : c.ConnectorId);
        }

        private static void Add(double[] into, double[] series)
        {
            int n = Math.Min(into.Length, series.Length);
            for (int m = 0; m < n; m++)
            {
                into[m] += series[m];
            }
        }

        private void AddBurst(string key, Flow flow)
        {
            if (flow.Burst is not { } burst)
            {
                return;
            }

            if (!Bursts.TryGetValue(key, out Dictionary<int, double>? perTrain))
            {
                Bursts[key] = perTrain = new Dictionary<int, double>();
            }

            perTrain[burst.Train] = perTrain.GetValueOrDefault(burst.Train) + burst.People;
        }
    }
}
