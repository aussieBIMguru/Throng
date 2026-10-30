using System.Globalization;
using System.Text;
using Throng.Core.Geometry;
using Throng.Core.Library;
using Throng.Core.Model;
using Throng.IO;
using Throng.Sim.Engine;
using Throng.Sim.Nav;

namespace Throng.Sim.Tests;

/// <summary>Round 007: one-way stairs (D-115), per-agent belt cost (D-113) and escalator-pair ends (D-112).</summary>
[TestClass]
public sealed class Round007Tests
{
    /// <summary>Set by MSTest; diagnostics write to it (shown under the test's output).</summary>
    public TestContext TestContext { get; set; } = null!;
    [TestMethod]
    public void Stair_MarkedUp_IsOneWay()
    {
        Study study = SimTestStudies.TwoLevels("connector.stair-switchback-1500", 10);
        study.Connectors[0].Direction = ConnectorDirection.Up;
        NavWorld world = SimTestStudies.Compile(study).World;
        Assert.AreEqual(2, world.Portals.Count);
        NavPortal bottom = world.Portals.OrderBy(p => p.ElevationM).First();
        NavPortal top = world.Portals.OrderBy(p => p.ElevationM).Last();
        Assert.IsTrue(bottom.AllowsEntry && !bottom.AllowsExit, "Up stair: on at the bottom only.");
        Assert.IsTrue(top.AllowsExit && !top.AllowsEntry, "Up stair: off at the top only.");
    }

    [TestMethod]
    public void Stair_Bidirectional_StaysTwoWay()
    {
        NavWorld world = SimTestStudies.Compile(SimTestStudies.TwoLevels("connector.stair-switchback-1500", 10)).World;
        Assert.IsTrue(world.Portals.All(p => p.AllowsEntry && p.AllowsExit));
    }

    [TestMethod]
    public void BeltCorrection_StandersPayForTheSlowerRide()
    {
        var escalator = new NavConnector { Index = 0, ConnectorId = Guid.Empty, Name = "E", Kind = ConnectorKind.Escalator, BeltSpeedMps = 0.5 };
        var stair = new NavConnector { Index = 1, ConnectorId = Guid.Empty, Name = "S", Kind = ConnectorKind.Stair };

        // Nominal cost is belt + 0.5 m/s = 1.0 m/s; a stander rides at 0.5 m/s, so 20 s becomes 40 s.
        Assert.AreEqual(20, RoutePlanner.BeltCorrection(escalator, 20, 0), 1e-9);
        Assert.IsTrue(RoutePlanner.BeltCorrection(escalator, 20, 0.8) < 0, "A brisk walker beats the nominal time.");
        Assert.AreEqual(0, RoutePlanner.BeltCorrection(escalator, 20, -1));
        Assert.AreEqual(0, RoutePlanner.BeltCorrection(stair, 20, 0));
    }

    [TestMethod]
    public void EscalatorPair_BothWays_DoesNotGridlock()
    {
        SimEngine engine = Run(PairStudy(), SimTuning.Default, 240);
        SimStats stats = engine.Stats;
        Assert.IsTrue(stats.Arrived > 150, $"Only {stats.Arrived} arrived in four minutes.");
        Assert.IsTrue(stats.StuckRemoved <= 5, $"{stats.StuckRemoved} removed as stuck.");
    }

    /// <summary>
    /// Not a pass/fail check: runs the demo (5 min) and the escalator pair (4 min) with every round 007 rule on,
    /// all off, and each one off in turn, and writes arrivals, stuck removals and where people got stuck. Paste
    /// the output into the chat when tuning.
    /// </summary>
    [TestMethod]
    public void Diagnostics_Round007Rules()
    {
        (string Name, SimTuning Tuning)[] variants =
        [
            ("all on", SimTuning.Default),
            ("all off", new SimTuning { RunOffs = false, WalkClear = false, BoardingHold = false, BeltCost = false, MeasuredWait = false, Balk = false, KeepSideFade = false, CombSpacing = false, CounterflowLanes = false, LandingSqueeze = false, StallEscape = false, LandingCarry = false }),
            ("no run-offs", new SimTuning { RunOffs = false }),
            ("no walk clear", new SimTuning { WalkClear = false }),
            ("no boarding hold", new SimTuning { BoardingHold = false }),
            ("no belt cost", new SimTuning { BeltCost = false }),
            ("no measured wait", new SimTuning { MeasuredWait = false }),
            ("no balk", new SimTuning { Balk = false }),
            ("no keep-side fade", new SimTuning { KeepSideFade = false }),
            ("no comb spacing", new SimTuning { CombSpacing = false }),
            ("no counterflow gate", new SimTuning { CounterflowLanes = false }),
            ("no landing squeeze", new SimTuning { LandingSqueeze = false }),
            ("no stall escape", new SimTuning { StallEscape = false }),
            ("no landing carry or waypoints", new SimTuning { LandingCarry = false, LandingWaypoints = false }),
            ("with stalled stair wait (D-133)", new SimTuning { StalledStairWait = true }),
            ("no stair effort (D-174)", new SimTuning { StairEffort = false }),
            ("no standing rest (D-175)", new SimTuning { StandingRest = false }),
            ("no stair measured wait (D-181)", new SimTuning { StairMeasuredWait = false }),
            ("no sight limit (D-179)", new SimTuning { SightLimited = false }),
            ("no approach review (D-180)", new SimTuning { ApproachReview = false }),
            ("no exit crowd (D-182)", new SimTuning { ExitCrowd = false }),
            ("speed stress, not progress (D-191)", new SimTuning { ProgressStress = false }),
            ("no landing waypoints (D-192)", new SimTuning { LandingWaypoints = false }),
            ("stress from half patience (D-145)", new SimTuning { EarlyStress = false }),
            ("no crowd stress (D-194)", new SimTuning { CrowdStress = false }),
            ("no lane clear (D-209)", new SimTuning { LaneClear = false }),
            ("no accessible rule (D-213)", new SimTuning { AccessibleRule = false }),
            ("no lift priority (D-247, D-248)", new SimTuning { LiftPriority = false }),
            ("no lift patience (D-253)", new SimTuning { LiftPatience = false }),
            ("no destination dispatch (D-255)", new SimTuning { DestinationDispatch = false }),
            ("as r009 (no round 010 rules)", new SimTuning { StairMeasuredWait = false, SightLimited = false, ApproachReview = false, ExitCrowd = false, ProgressStress = false, LandingWaypoints = false, EarlyStress = false, CrowdStress = false }),
        ];

        var output = new StringBuilder();
        foreach ((string label, Func<Study> build, double seconds) in new (string, Func<Study>, double)[]
        {
            ("DEMO with journeys 300 s (D-155)", DemoStudyBuilder.Build, 300),
            ("DEMO without journeys 300 s (straight to sinks, as r008)", DemoWithoutJourneys, 300),
            ("PAIR 240 s", () => PairStudy(), 240),
            ("PAIR 120/min each way, 240 s (saturated: boarding vs cap)", () => PairStudy(120), 240),
            ("STAIR 3.6 m one way, 120/min, 240 s", () => SimTestStudies.TwoLevels("connector.stair-3600", 120), 240),
            ("STAIR 1.5 m + ESCALATOR up, 120/min up, 240 s (r010a)", () => Round010aTests.StairAndEscalator(120), 240),
            ("STAIR + ESCALATOR, counterflow: 120/min up, 60/min down the stair, 240 s (r010a-2)", () => Round010aTests.StairCounterflow(120, 60), 240),
            ("OFFICE TOWER 300 s (r013b, lifts only)", OfficeTowerBuilder.Build, 300),
        })
        {
            output.AppendLine(label);
            foreach ((string name, SimTuning tuning) in variants)
            {
                Study study = build();
                SimEngine engine = Run(study, tuning, seconds);
                SimStats stats = engine.Stats;
                output.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"  {name,-18} spawned {stats.Spawned,4}  arrived {stats.Arrived,4}  stuck {stats.StuckRemoved,3}  in system {stats.InSystem,4}  waiting {stats.Waiting,4}  {Boarding(engine)}  {Use(engine)}  {Trains(engine)}{Journeys(engine)}  {StuckPlaces(study, engine)}"));
            }
        }

        TestContext.WriteLine(output.ToString());
        Console.WriteLine(output.ToString());
    }

    /// <summary>
    /// D-132: with everyone going up a wide two-way stair there's no counterflow, so the lanes switch off and people
    /// use the middle of the stair as well as the sides.
    /// </summary>
    [TestMethod]
    public void WideStair_OneWay_UsesTheCentre()
    {
        SimEngine engine = Run(SimTestStudies.TwoLevels("connector.stair-3600", 120), SimTuning.Default, 180);
        AgentStore agents = engine.Agents;
        NavWorld world = engine.Model.World;
        int onStair = 0, inCentre = 0;
        for (int i = 0; i < agents.Count; i++)
        {
            NavSurface surface = world.Surfaces[agents.Surface[i]];
            if (surface.Kind != NavSurfaceKind.Piece || surface.Motion != NavMotion.Stair)
            {
                continue;
            }

            surface.ToLocal(agents.X[i], agents.Y[i], out _, out double v);
            onStair++;
            if (Math.Abs(v - surface.HalfWidth) < surface.HalfWidth / 3)
            {
                inCentre++;
            }
        }

        Assert.IsTrue(onStair >= 5, $"only {onStair} people on the stair");
        Assert.IsTrue(inCentre >= onStair * 0.15, $"{inCentre} of {onStair} on the stair are in its middle third");
    }

    /// <summary>
    /// Stress now (mean, peak, people at 0.5 or more; D-156), people queueing, and the mean queue wait and number served
    /// per device type (D-153).
    /// </summary>
    private static string Journeys(SimEngine engine)
    {
        (double mean, double peak) = engine.StressNow();
        SimStats stats = engine.Stats;
        var text = new StringBuilder(string.Create(CultureInfo.InvariantCulture,
            $"stress mean {mean:0.00} peak {peak:0.00} stressed {stats.Stressed,3}  queuing {stats.Queuing,3}  gave up {stats.GaveUp,3}"));
        foreach ((string type, double wait, long served) in engine.QueueWaitsByType())
        {
            text.Append(string.Create(CultureInfo.InvariantCulture, $"  {type} wait {wait,5:0.0} s (n {served})"));
        }

        return text.ToString();
    }

    /// <summary>The demo with every mix line's journey removed: people walk straight to their sinks (the r008 behaviour).</summary>
    private static Study DemoWithoutJourneys()
    {
        Study study = DemoStudyBuilder.Build();
        foreach (Region region in study.Regions)
        {
            for (int i = 0; i < region.EmissionRules.Count; i++)
            {
                EmissionRule rule = region.EmissionRules[i];
                region.EmissionRules[i] = rule with { Mix = [.. rule.Mix.Select(m => m with { JourneyId = null })] };
            }
        }

        return study;
    }

    /// <summary>The busiest metered connector's boardings per minute and as a share of its cap (D-128).</summary>
    private static string Boarding(SimEngine engine)
    {
        IReadOnlyList<NavConnector> connectors = engine.Model.World.Connectors;
        int best = -1;
        for (int k = 0; k < connectors.Count; k++)
        {
            if (connectors[k].ThroughputPerMin > 0 && (best < 0 || engine.BoardingsPerMinute(k) > engine.BoardingsPerMinute(best)))
            {
                best = k;
            }
        }

        if (best < 0)
        {
            return "boards -";
        }

        double rate = engine.BoardingsPerMinute(best);
        return string.Create(CultureInfo.InvariantCulture,
            $"boards {rate,5:0.0}/min ({100 * rate / connectors[best].ThroughputPerMin,3:0}% of cap, {connectors[best].Name})");
    }

    /// <summary>
    /// People stepping on per minute at every connector with any use (stairs counted since r010a) and its current
    /// crowd wait: "use Stair 1.5 m 44/min ~6 s, Escalator up 71/min ~2 s".
    /// </summary>
    private static string Use(SimEngine engine)
    {
        IReadOnlyList<NavConnector> connectors = engine.Model.World.Connectors;
        var parts = new List<string>();
        for (int k = 0; k < connectors.Count; k++)
        {
            double rate = engine.BoardingsPerMinute(k);
            if (rate > 0)
            {
                parts.Add(string.Create(CultureInfo.InvariantCulture, $"{connectors[k].Name} {rate:0}/min ~{engine.ConnectorWaitS(k):0} s"));
            }
        }

        return parts.Count == 0 ? "use -" : "use " + string.Join(", ", parts);
    }

    /// <summary>"trains boarded 120 left behind 4  " when the study has train services (round 010b), else empty.</summary>
    private static string Trains(SimEngine engine) =>
        engine.Model.Trains.Count == 0 ? string.Empty
            : string.Create(CultureInfo.InvariantCulture, $"trains boarded {engine.BoardedTrains} left behind {engine.LeftBehind}  ");

    private static SimEngine Run(Study study, SimTuning tuning, double seconds)
    {
        var engine = new SimEngine(SimTestStudies.Compile(study), tuning);
        int steps = (int)Math.Round(seconds / engine.Model.TimeStepS);
        for (int i = 0; i < steps; i++)
        {
            engine.Step();
        }

        return engine;
    }

    /// <summary>
    /// Up from the ground and down from the upper level at the same time, through one escalator pair, at 40/min each
    /// way: under what the model boards in a crowd (about half the 100/min cap), so queues shouldn't grow without end.
    /// </summary>
    private static Study PairStudy(double perMinute = 40)
    {
        Study study = SimTestStudies.TwoLevels("connector.escalator-pair", perMinute);
        Level ground = study.Levels[0];
        Level upper = study.Levels[1];
        Region downSink = SimTestStudies.Zone(study, ground, "Down exit", ZoneKind.Sink, new(4, 0), new(6, 20));
        Region downSpawn = SimTestStudies.Zone(study, upper, "Down entry", ZoneKind.Spawn, new(30, 0), new(33, 20));
        downSpawn.EmissionRules.Add(new EmissionRule
        {
            Mix = [MixLine.Of(SimTestStudies.Commuter)],
            RatePerMinute = perMinute,
            Pattern = EmissionPattern.Constant,
            DestinationMode = DestinationMode.ChosenSinks,
            Destinations = [new SinkWeight { SinkId = downSink.Id, Weight = 1 }],
        });
        return study;
    }

    /// <summary>Stuck removals grouped by place: a connector, or the floor near a connector end, or open floor.</summary>
    private static string StuckPlaces(Study study, SimEngine engine)
    {
        NavWorld world = engine.Model.World;
        var counts = new Dictionary<string, int>();
        foreach (StuckRecord entry in engine.StuckLog)
        {
            NavSurface surface = world.Surfaces[entry.Surface];
            string place;
            if (surface.Kind == NavSurfaceKind.Piece && surface.ConnectorIndex >= 0)
            {
                place = $"on {world.Connectors[surface.ConnectorIndex].Name}";
            }
            else
            {
                string level = world.FloorByLevel.FirstOrDefault(kv => kv.Value == entry.Surface).Key is { } id && study.FindLevel(id) is { } l ? l.Name : "?";
                NavPortal? nearest = world.Portals
                    .Where(p => p.Floor == entry.Surface)
                    .OrderBy(p => p.DistanceTo(new Point2(entry.X, entry.Y)))
                    .FirstOrDefault();
                place = nearest is not null && nearest.DistanceTo(new Point2(entry.X, entry.Y)) < 4
                    ? $"{level} at {world.Connectors[nearest.Connector].Name}"
                    : $"{level} floor";
            }

            place += entry.Mode switch { AgentMode.ToPortal => " (boarding)", AgentMode.OnConnector => " (riding)", _ => " (to sink)" };
            counts[place] = counts.TryGetValue(place, out int c) ? c + 1 : 1;
        }

        return counts.Count == 0 ? "" : "| " + string.Join(", ", counts.OrderByDescending(kv => kv.Value).Take(5).Select(kv => $"{kv.Key} ×{kv.Value}"));
    }
}
