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

        var findings = new List<ValidationMessage>();
        DeviceFindings(model, load, findings);
        ConnectorFindings(study, model, load, findings);
        TrainFindings(model, load, findings);
        EscalatorFindings(study, model, load, findings);
        UnusedDeviceFindings(study, model, load, findings);
        return findings;
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
            int floor = emitter.Floor;
            Point2 at = start;
            bool boarded = false;
            if (line.Journey >= 0 && line.Journey < model.Journeys.Count)
            {
                // Chance each tag bit is held, from the spawn tags; earlier steps' grants add to it.
                var held = new Dictionary<ulong, double>();
                foreach ((ulong bit, double probability) in line.SpawnTags)
                {
                    held[bit] = Math.Clamp(probability, 0, 1);
                }

                foreach (SimStep step in model.Journeys[line.Journey].Steps)
                {
                    if (step.Kind == JourneyStepKind.Exit)
                    {
                        break;
                    }

                    double applies = AppliesShare(step, held);
                    if (applies > 1e-9 && step.Targets.Length > 0)
                    {
                        // The nearest target by the planner's cost, like an arriving person with an empty building.
                        int[] targets = step.Kind == JourneyStepKind.GoToDevice
                            ? [.. step.Targets.Select(t => model.Devices[t].Target)]
                            : step.Targets;
                        int best = Nearest(model, floor, at, targets, profile);
                        if (best >= 0)
                        {
                            Flow stepFlow = flow.Scaled(applies);
                            Walk(model, load, ref floor, ref at, targets[best], profile, stepFlow);
                            if (step.Kind == JourneyStepKind.GoToDevice)
                            {
                                load.AddDeviceGroup(step.Targets, stepFlow);
                            }
                            else if (step.Kind == JourneyStepKind.BoardTrain && applies > 0.999)
                            {
                                // They leave on the train, not through a sink.
                                boarded = true;
                                break;
                            }
                        }
                    }

                    Grant(step.Grants, applies, held);
                }
            }

            if (boarded)
            {
                continue;
            }

            // On to the sinks, split by weight (nearest sink: the nearest one).
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
    }

    /// <summary>
    /// Share of people a step applies to: every needed bit held, no lacked bit held (bits taken as independent).
    /// </summary>
    public static double AppliesShare(SimStep step, IReadOnlyDictionary<ulong, double> held)
    {
        double share = 1;
        for (int k = 0; k < 64; k++)
        {
            ulong bit = 1UL << k;
            double p = held.TryGetValue(bit, out double v) ? v : 0;
            if ((step.Needs & bit) != 0)
            {
                share *= p;
            }

            if ((step.Lacks & bit) != 0)
            {
                share *= 1 - p;
            }
        }

        return Math.Clamp(share, 0, 1);
    }

    /// <summary>The step's grants reach the share it applied to.</summary>
    private static void Grant(ulong grants, double applies, Dictionary<ulong, double> held)
    {
        if (grants == 0 || applies <= 0)
        {
            return;
        }

        for (int k = 0; k < 64; k++)
        {
            ulong bit = 1UL << k;
            if ((grants & bit) != 0)
            {
                double p = held.TryGetValue(bit, out double v) ? v : 0;
                held[bit] = p + ((1 - p) * applies);
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

    private static void DeviceFindings(SimModel model, Load load, List<ValidationMessage> findings)
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

    private static void ConnectorFindings(Study study, SimModel model, Load load, List<ValidationMessage> findings)
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
                findings.Add(new ValidationMessage(ValidationSeverity.Info, string.Create(C,
                    $"Capacity: {kind} '{connector.Name}': up to about {peak:0} people a minute (minute {peakMinute + 1}), {perMetre:0} per metre of width: expect Level of Service {los} (Fruin {(stair ? "stair" : "walkway")})."), id)
                { Rule = ValidationRules.Capacity });
            }
        }
    }

    /// <summary>
    /// Runs the queue and adds a Warning when it builds past a minute's service, or an Info when the peak is busy but
    /// the queue stays short. True when a finding was added.
    /// </summary>
    private static bool QueueFinding(List<ValidationMessage> findings, string what, string pronoun, string serves, double[] demand, double capacity, Guid id)
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
            findings.Add(new ValidationMessage(ValidationSeverity.Info, string.Create(C,
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

    private static void TrainFindings(SimModel model, Load load, List<ValidationMessage> findings)
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
                    findings.Add(new ValidationMessage(ValidationSeverity.Info, string.Create(C,
                        $"Capacity: each '{train.Name}' train's ~{people:0} people take ~{clearMin:0.#} min to get through {what} ({capacity:0.#} a minute): expect a ~{clearMin:0} min queue after each train."), id)
                    { Rule = ValidationRules.Capacity });
                }
            }
        }
    }

    private static void EscalatorFindings(Study study, SimModel model, Load load, List<ValidationMessage> findings)
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
    private static void UnusedDeviceFindings(Study study, SimModel model, Load load, List<ValidationMessage> findings)
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
