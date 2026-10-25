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
            ("no landing carry", new SimTuning { LandingCarry = false }),
        ];

        var output = new StringBuilder();
        foreach ((string label, Func<Study> build, double seconds) in new (string, Func<Study>, double)[]
        {
            ("DEMO 300 s", DemoStudyBuilder.Build, 300),
            ("PAIR 240 s", () => PairStudy(), 240),
            ("PAIR 120/min each way, 240 s (saturated: boarding vs cap)", () => PairStudy(120), 240),
            ("STAIR 3.6 m one way, 120/min, 240 s", () => SimTestStudies.TwoLevels("connector.stair-3600", 120), 240),
        })
        {
            output.AppendLine(label);
            foreach ((string name, SimTuning tuning) in variants)
            {
                Study study = build();
                SimEngine engine = Run(study, tuning, seconds);
                SimStats stats = engine.Stats;
                output.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"  {name,-18} spawned {stats.Spawned,4}  arrived {stats.Arrived,4}  stuck {stats.StuckRemoved,3}  in system {stats.InSystem,4}  waiting {stats.Waiting,4}  {Boarding(engine)}  {StuckPlaces(study, engine)}"));
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
            ProfileId = SimTestStudies.Commuter,
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
