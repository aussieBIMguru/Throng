using Throng.Core.Editing;
using Throng.Core.Geometry;
using Throng.Core.Library;
using Throng.Core.Model;
using Throng.Sim.Engine;
using Throng.Sim.Nav;

namespace Throng.Sim.Tests;

/// <summary>
/// Round 010a: crowd-aware choice (D-179–D-182) and progress-based stress (D-191).
/// </summary>
[TestClass]
public sealed class Round010aTests
{
    /// <summary>Every round 010a rule off: route choice and stress as in round 009.</summary>
    private static readonly SimTuning AsRound009 = new()
    {
        StairMeasuredWait = false, SightLimited = false, ApproachReview = false, ExitCrowd = false, ProgressStress = false,
    };

    [TestMethod]
    public void BuiltIns_SightRadius_CommutersFurthest_InfrequentNearest()
    {
        static AgentProfileTemplate P(string key) => (AgentProfileTemplate)BuiltInLibrary.Instance.FindByKey(key)!;
        Assert.AreEqual(AgentProfileTemplate.DefaultSightRadiusM, new AgentProfileTemplate().SightRadiusM, "Profiles saved before 010a get the default.");
        Assert.IsTrue(P("agent.commuter").SightRadiusM > AgentProfileTemplate.DefaultSightRadiusM);
        Assert.IsTrue(P("agent.infrequent").SightRadiusM < AgentProfileTemplate.DefaultSightRadiusM);
        Assert.AreEqual(AgentProfileTemplate.DefaultSightRadiusM, P("agent.elderly").SightRadiusM);
    }

    [TestMethod]
    public void Sight_AWaitOutOfSightIsIgnored()
    {
        Study study = StairAndEscalator(rate: 1);
        SimModel model = SimTestStudies.Compile(study);
        NavRoutes routes = model.Routes;
        int floor = model.World.FloorByLevel[study.Levels[0].Id];
        int sink = 0;
        double x = 1.5, y = 3;

        RoutePlanner.Choose(routes, floor, x, y, sink, MobilityClass.Normal, null, null, out bool direct, out RouteOption plain);
        Assert.IsFalse(direct);
        double[] waits = new double[model.World.Connectors.Count];
        waits[plain.Connector] = 1000;

        RoutePlanner.Choose(routes, floor, x, y, sink, MobilityClass.Normal, null, null, out _, out RouteOption seen, waits);
        RoutePlanner.Choose(routes, floor, x, y, sink, MobilityClass.Normal, null, null, out _, out RouteOption unseen, waits, sightRadius: 0.01);
        Assert.AreNotEqual(plain.Connector, seen.Connector, "A huge wait in sight should send people the other way.");
        Assert.AreEqual(plain.Connector, unseen.Connector, "Out of sight, the connector is judged on its uncrowded time.");
    }

    /// <summary>
    /// r010a-2: in this layout the stair never saturates (about 47/min of its 75/min), so D-111's load ÷ capacity
    /// already balances it and the 010a rules give the same run (61 % both ways in Gavin's r010a-2 build). The check
    /// is now that they don't push people onto the stair and don't cause ping-pong; the counterflow case that should
    /// show the difference is a diagnostics block ("STAIR + ESCALATOR, counterflow") until its numbers are in.
    /// </summary>
    [TestMethod]
    public void StairAndEscalator_NewRulesDontPushPeopleOntoTheStair()
    {
        (double off, double offSwitches) = EscalatorShare(AsRound009);
        (double on, double onSwitches) = EscalatorShare(SimTuning.Default);
        Assert.IsTrue(on >= off - 0.02, $"Escalator share {on:P0} with the 010a rules vs {off:P0} without.");
        Assert.IsTrue(onSwitches < 2, $"People switched connector {onSwitches:0.0} times each on average (ping-pong). As r009: {offSwitches:0.0}.");
    }

    [TestMethod]
    public void ExitCrowd_PeopleSpreadToAQuieterExit()
    {
        (double off, double offPeak) = SecondExitShare(new SimTuning { ExitCrowd = false });
        (double on, double onPeak) = SecondExitShare(SimTuning.Default);
        Assert.AreEqual(0, offPeak, 1e-9, "No exit crowd wait with the rule off.");
        Assert.IsTrue(onPeak > 0, "The bottlenecked exit should read as crowded.");
        Assert.IsTrue(on > off, $"Share to the further, quieter exit: {on:P0} with exit crowding vs {off:P0} without (peak wait {onPeak:0} s).");
    }

    [TestMethod]
    public void ProgressStress_JigglingInAJamCountsAsHeldUp()
    {
        double off = MeanHeldInJam(new SimTuning { ProgressStress = false });
        double on = MeanHeldInJam(SimTuning.Default);
        Assert.IsTrue(on > off, $"Mean held-up time in a jam: {on:0.0} s by progress vs {off:0.0} s by speed.");
    }

    /// <summary>
    /// Two levels with an up escalator at y = 15 and a 1.5 m stair at y = 5, people appearing at the south-west,
    /// nearer the stair.
    /// </summary>
    internal static Study StairAndEscalator(double rate)
    {
        Study study = SimTestStudies.TwoLevels("connector.escalator-up", rate);
        study.Connectors[0].BottomLanding = new Point2(10, 15);
        Level ground = study.Levels[0];
        Level upper = study.Levels[1];
        var stair = (ConnectorTemplate)BuiltInLibrary.Instance.FindByKey("connector.stair-1500")!;
        study.Connectors.AddRange(ConnectorFactory.FromTemplate(study, stair, ground.Id, upper.Id, new Point2(10, 5), 0, TurnSide.Left));
        Region spawn = study.Regions.Single(r => r.IsSpawn);
        spawn.Outline = ShapeFactory.Rectangle(new(0, 0), new(3, 6));
        return study;
    }

    /// <summary>
    /// <see cref="StairAndEscalator"/> with commuters also coming down at <paramref name="downRate"/> from the upper
    /// floor's east end to the ground's west end. The escalator only goes up, so they all use the stair, and the
    /// people going up meet them head-on (diagnostics, r010a-2).
    /// </summary>
    internal static Study StairCounterflow(double upRate, double downRate)
    {
        Study study = StairAndEscalator(upRate);
        Level ground = study.Levels[0];
        Level upper = study.Levels[1];
        Region downSink = SimTestStudies.Zone(study, ground, "Down exit", ZoneKind.Sink, new(4, 0), new(6, 8));
        Region downSpawn = SimTestStudies.Zone(study, upper, "Down entry", ZoneKind.Spawn, new(30, 0), new(33, 8));
        downSpawn.EmissionRules.Add(new EmissionRule
        {
            Mix = [MixLine.Of(SimTestStudies.Commuter)],
            RatePerMinute = downRate,
            Pattern = EmissionPattern.Constant,
            DestinationMode = DestinationMode.ChosenSinks,
            Destinations = [new SinkWeight { SinkId = downSink.Id, Weight = 1 }],
        });
        return study;
    }

    /// <summary>
    /// Commuters (who like stairs) at 120/min towards a 1.5 m stair (75/min) with a free escalator 10 m further on.
    /// Returns the escalator share and the mean number of connector switches per person.
    /// </summary>
    private static (double Share, double Switches) EscalatorShare(SimTuning tuning)
    {
        var engine = new SimEngine(SimTestStudies.Compile(StairAndEscalator(120)), tuning);
        NavWorld world = engine.Model.World;
        var escalator = new HashSet<double>();
        var stairs = new HashSet<double>();
        var aim = new Dictionary<double, (int Connector, int Switches)>();
        int steps = (int)Math.Round(240 / engine.Model.TimeStepS);
        for (int s = 0; s < steps; s++)
        {
            engine.Step();
            AgentStore a = engine.Agents;
            for (int i = 0; i < a.Count; i++)
            {
                if (a.Mode[i] == AgentMode.ToPortal && a.Target[i] >= 0)
                {
                    int k = world.Portals[a.Target[i]].Connector;
                    aim[a.SpawnTime[i]] = aim.TryGetValue(a.SpawnTime[i], out var was)
                        ? (k, was.Switches + (was.Connector != k ? 1 : 0))
                        : (k, 0);
                }

                int connector = world.Surfaces[a.Surface[i]].ConnectorIndex;
                if (connector >= 0)
                {
                    (world.Connectors[connector].Kind == ConnectorKind.Escalator ? escalator : stairs).Add(a.SpawnTime[i]);
                }
            }
        }

        int total = escalator.Count + stairs.Count;
        Assert.IsTrue(total > 30, $"Only {total} people went up.");
        double switches = aim.Count == 0 ? 0 : aim.Values.Average(v => v.Switches);
        return ((double)escalator.Count / total, switches);
    }

    /// <summary>
    /// A 20 × 20 m hall: commuters at 150/min from the west (y 4–6) to the nearest of two exits. Exit A is nearer but
    /// behind a 1 m corridor (about 80/min); exit B is a couple of metres further, open, in the north-east corner.
    /// Returns the share of people last heading for B and the peak crowd wait read at A.
    /// </summary>
    private static (double ShareB, double PeakA) SecondExitShare(SimTuning tuning)
    {
        var study = new Study { Name = "Two exits" };
        study.Settings.Seed = 11;
        study.Settings.EndCondition = StudyEndCondition.Indefinite;
        var level = new Level { Name = "Ground", ElevationM = 0 };
        study.Levels.Add(level);
        study.Regions.Add(new Region { LevelId = level.Id, Name = "Hall", Kind = RegionKind.Walkable, Outline = ShapeFactory.Rectangle(new(0, 0), new(20, 20)) });
        study.Regions.Add(new Region { LevelId = level.Id, Name = "Corridor", Kind = RegionKind.Walkable, Outline = ShapeFactory.Rectangle(new(20, 4.5), new(24, 5.5)) });
        Region exitA = SimTestStudies.Zone(study, level, "Exit A", ZoneKind.Sink, new(21, 4.5), new(22, 5.5));
        Region exitB = SimTestStudies.Zone(study, level, "Exit B", ZoneKind.Sink, new(16, 19), new(20, 20));
        Region spawn = SimTestStudies.Zone(study, level, "West", ZoneKind.Spawn, new(0, 4), new(2, 6));
        spawn.EmissionRules.Add(new EmissionRule
        {
            Mix = [MixLine.Of(SimTestStudies.Commuter)],
            RatePerMinute = 150,
            Pattern = EmissionPattern.Constant,
            DestinationMode = DestinationMode.NearestSink,
            Destinations = [new SinkWeight { SinkId = exitA.Id, Weight = 1 }, new SinkWeight { SinkId = exitB.Id, Weight = 1 }],
        });

        var engine = new SimEngine(SimTestStudies.Compile(study), tuning);
        NavWorld world = engine.Model.World;
        int a = Enumerable.Range(0, world.Sinks.Count).Single(s => world.Sinks[s].RegionId == exitA.Id);
        int b = Enumerable.Range(0, world.Sinks.Count).Single(s => world.Sinks[s].RegionId == exitB.Id);
        var last = new Dictionary<double, int>();
        double peak = 0;
        int steps = (int)Math.Round(180 / engine.Model.TimeStepS);
        for (int s = 0; s < steps; s++)
        {
            engine.Step();
            peak = Math.Max(peak, engine.ExitCrowdS(a));
            AgentStore agents = engine.Agents;
            for (int i = 0; i < agents.Count; i++)
            {
                last[agents.SpawnTime[i]] = agents.Sink[i];
            }
        }

        Assert.IsTrue(last.Count > 100, $"Only {last.Count} people appeared.");
        return ((double)last.Values.Count(v => v == b) / last.Count, peak);
    }

    /// <summary>
    /// The 30 × 4 m corridor with an obstacle leaving a 0.6 m gap at x = 20, commuters at 60/min: a jam where people
    /// jostle on the spot. Returns the mean held-up time of everyone in the system after 4 minutes.
    /// </summary>
    private static double MeanHeldInJam(SimTuning tuning)
    {
        Study study = SimTestStudies.Corridor(ratePerMinute: 60);
        Level level = study.Levels[0];
        study.Regions.Add(new Region { LevelId = level.Id, Name = "Pinch", Kind = RegionKind.Obstacle, Outline = ShapeFactory.Rectangle(new(20, 0), new(21, 3.4)) });
        var engine = new SimEngine(SimTestStudies.Compile(study), tuning);
        int steps = (int)Math.Round(240 / engine.Model.TimeStepS);
        for (int s = 0; s < steps; s++)
        {
            engine.Step();
        }

        AgentStore agents = engine.Agents;
        Assert.IsTrue(agents.Count > 10, $"Only {agents.Count} people in the jam.");
        double sum = 0;
        for (int i = 0; i < agents.Count; i++)
        {
            sum += agents.HeldS[i];
        }

        return sum / agents.Count;
    }
}
