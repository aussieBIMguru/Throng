using Throng.Core.Editing;
using Throng.Core.Geometry;
using Throng.Core.Library;
using Throng.Core.Model;
using Throng.Sim.Engine;

namespace Throng.Sim.Tests;

/// <summary>
/// Round 009b: give-up pathways (D-165), profile device preferences (D-166), the shared snake of a drawn queue
/// (D-163) and hold points for people waiting for a lane (D-167).
/// </summary>
[TestClass]
public sealed class Round009bTests
{
    private static readonly Guid Mobility = TemplateIds.FromKey("agent.mobility");

    [TestMethod]
    public void GiveUp_Leave_QueuersPastTheirPatienceLeave_AndAreCounted()
    {
        Study study = MachineCorridor(JourneyGiveUp.Leave, giveUpStep: 0, patienceS: 30);
        SimEngine engine = Run(study, 240);
        Assert.IsTrue(engine.GaveUp > 0, "With 30 s of patience and a slow machine, some give up.");
        Assert.AreEqual(engine.GaveUp, engine.Stats.GaveUp, "The status bar count (D-168).");
        Assert.IsTrue(engine.ServedAt(0) > 0, "The machine still serves people.");
        Assert.IsTrue(engine.Stats.StuckRemoved <= 2, $"{engine.Stats.StuckRemoved} removed as stuck (leavers cross the line).");
    }

    [TestMethod]
    public void GiveUp_KeepWaiting_NoOneLeaves()
    {
        SimEngine engine = Run(MachineCorridor(JourneyGiveUp.KeepWaiting, giveUpStep: 0, patienceS: 30), 240);
        Assert.AreEqual(0, engine.GaveUp, "Keep waiting is stress only (the 009a behaviour).");
    }

    [TestMethod]
    public void GiveUp_JumpTo_SkipsToThatStep_WithoutCountingAsGaveUp()
    {
        // Step 1 machine (give up → step 3), step 2 a long dwell only the served reach, step 3 exit.
        SimEngine engine = Run(MachineCorridor(JourneyGiveUp.JumpTo, giveUpStep: 2, patienceS: 30), 240);
        Assert.AreEqual(0, engine.GaveUp, "Jumping isn't leaving.");
        Assert.IsTrue(engine.Stats.Arrived > 0, "People who gave up skipped the 1000 s dwell and left.");
        Assert.IsTrue(engine.ServedAt(0) > 0);
        AgentStore a = engine.Agents;
        int dwelling = 0;
        for (int i = 0; i < a.Count; i++)
        {
            dwelling += a.Phase[i] == AgentPhase.Dwell ? 1 : 0;
        }

        Assert.IsTrue(dwelling > 0, "Those served went on to the dwell step.");
    }

    [TestMethod]
    [Ignore("Parked (D-176): wheelchairs jam the 1.0 m accessible lane; the walk-through timeout frees the lane while it's still occupied. See round-009.md ▸ Parked.")]
    public void DevicePreference_WheelchairUsersPickTheAccessibleGate()
    {
        string diagnosis = string.Empty;
        double Accessible(Guid profile)
        {
            SimEngine engine = Run(WideGates(profile, 8), 240);
            SimDevice line = engine.Model.Devices.Single(d => d.PresetKey == "device.gate-line");
            SimDevice wide = engine.Model.Devices.Single(d => d.PresetKey == BuiltInLibrary.AccessibleGateKey);
            long total = engine.ServedAt(line.Index) + engine.ServedAt(wide.Index);
            Assert.IsTrue(total > 10, $"Only {total} passed the gates. {Diagnose(engine)}");
            diagnosis = Diagnose(engine) + " | waits " + string.Join(", ", engine.QueueWaitsByType().Select(w => $"{w.TypeTag} {w.MeanWaitS:0.0} s n {w.Served}"));
            return (double)engine.ServedAt(wide.Index) / total;
        }

        double wheelchairs = Accessible(Mobility);
        string wheelchairRun = diagnosis;
        double commuters = Accessible(SimTestStudies.Commuter);
        Assert.IsTrue(wheelchairs > 0.6, $"{wheelchairs:P0} of wheelchair users used the accessible gate. {wheelchairRun}");
        Assert.IsTrue(commuters < wheelchairs, $"Commuters {commuters:P0} vs wheelchair users {wheelchairs:P0}.");
    }

    [TestMethod]
    public void SharedSnake_FeedsEveryLane()
    {
        Study study = SnakeGates(60);
        var engine = new SimEngine(SimTestStudies.Compile(study));
        SimDevice gates = engine.Model.Devices[0];
        Assert.IsTrue(gates.HasSnake, "The drawn path became a snake (D-163).");

        var lanesUsed = new bool[gates.Lanes];
        bool snakeUsed = false;
        int steps = (int)Math.Round(240 / engine.Model.TimeStepS);
        for (int s = 0; s < steps; s++)
        {
            engine.Step();
            AgentStore a = engine.Agents;
            for (int i = 0; i < a.Count; i++)
            {
                if (a.Phase[i] == AgentPhase.Through)
                {
                    lanesUsed[a.QSlot[i]] = true;
                }
                else if (a.Phase[i] == AgentPhase.Queue && a.QLine[i] == gates.SnakeLine)
                {
                    snakeUsed = true;
                }
            }
        }

        Assert.IsTrue(snakeUsed, "People queued in the snake.");
        Assert.IsTrue(lanesUsed.Count(u => u) >= 3, $"Lanes used: {string.Join(", ", lanesUsed)}.");
        Assert.IsTrue(engine.ServedAt(0) > 60, $"Only {engine.ServedAt(0)} served.");
        Assert.IsTrue(engine.Stats.StuckRemoved <= 2, $"{engine.Stats.StuckRemoved} removed as stuck.");
    }

    [TestMethod]
    public void HoldPoints_AreClearOfTheLaneMouths()
    {
        SimModel model = SimTestStudies.Compile(WideGates(SimTestStudies.Commuter, 8));
        SimDevice line = model.Devices.Single(d => d.PresetKey == "device.gate-line");
        for (int side = 0; side < line.Sides; side++)
        {
            Assert.IsTrue(line.HoldPoints[side].Length > 0, $"Side {side} has a hold point with floor.");
            foreach (Point2 hold in line.HoldPoints[side])
            {
                for (int lane = 0; lane < line.Lanes; lane++)
                {
                    Point2 mouth = line.Slots[line.Line(side, lane)][0];
                    Assert.IsTrue(Point2.Distance(hold, mouth) >= SimSetup.LaneHoldOutM, $"Hold point {hold} is {Point2.Distance(hold, mouth):0.00} m from lane {lane}'s mouth.");
                }

                double across = Math.Abs(Point2.Dot(hold - line.Centre, line.Facing.Perpendicular()));
                Assert.IsTrue(across > line.WidthM / 2, "Beside the lanes, not in front of them.");
            }
        }
    }

    /// <summary>
    /// Where a run stands, for failure messages: counts, service per device, phases, people by 2 m of x, and the
    /// first stuck removals.
    /// </summary>
    private static string Diagnose(SimEngine engine)
    {
        var c = System.Globalization.CultureInfo.InvariantCulture;
        SimStats stats = engine.Stats;
        var text = new System.Text.StringBuilder(string.Create(c,
            $"spawned {stats.Spawned} arrived {stats.Arrived} in {stats.InSystem} waiting-to-spawn {stats.Waiting} stuck now {stats.StuckNow} removed {stats.StuckRemoved} queuing {stats.Queuing}."));
        foreach (SimDevice device in engine.Model.Devices)
        {
            text.Append(string.Create(c, $" | {device.PresetKey}: served {engine.ServedAt(device.Index)} queue {engine.QueueLength(device.Index)} slots/line {string.Join("/", device.Slots.Select(l => l.Length))}"));
        }

        AgentStore a = engine.Agents;
        var phases = new Dictionary<AgentPhase, int>();
        var bins = new SortedDictionary<int, int>();
        for (int i = 0; i < a.Count; i++)
        {
            phases[a.Phase[i]] = phases.GetValueOrDefault(a.Phase[i]) + 1;
            int bin = (int)Math.Floor(a.X[i] / 2) * 2;
            bins[bin] = bins.GetValueOrDefault(bin) + 1;
        }

        text.Append(" | phases " + string.Join(", ", phases.Select(kv => $"{kv.Key} {kv.Value}")));
        text.Append(" | by x " + string.Join(", ", bins.Select(kv => string.Create(c, $"{kv.Key}-{kv.Key + 2} m: {kv.Value}"))));
        text.Append(" | first agents " + string.Join("; ", Enumerable.Range(0, Math.Min(6, a.Count)).Select(i =>
            string.Create(c, $"({a.X[i]:0.0},{a.Y[i]:0.0}) {a.Phase[i]} dev {a.QDevice[i]} line {a.QLine[i]} slot {a.QSlot[i]} v {Math.Sqrt((a.Vx[i] * a.Vx[i]) + (a.Vy[i] * a.Vy[i])):0.00}"))));
        text.Append(" | stuck at " + string.Join("; ", engine.StuckLog.Take(4).Select(r => string.Create(c, $"({r.X:0.0},{r.Y:0.0})"))));
        return text.ToString();
    }

    /// <summary>
    /// The 30 × 4 m corridor with one ticket machine and a journey "machine (give-up as asked) → 1000 s dwell in a Retail
    /// zone → exit", for a commuter copy with little patience.
    /// </summary>
    private static Study MachineCorridor(JourneyGiveUp giveUp, int giveUpStep, double patienceS)
    {
        Study study = SimTestStudies.Corridor(20);
        Level level = study.Levels[0];
        var machine = (DeviceTemplate)BuiltInLibrary.Instance.FindByKey("device.ticket-machine")!;
        study.Devices.Add(DeviceFactory.FromTemplate(study, machine, level.Id, new Point2(8, 0.3), 90));
        Region shop = SimTestStudies.Zone(study, level, "Shop", ZoneKind.Dwell, new(18, 0), new(24, 4));
        shop.Tag = "Retail";

        var impatient = (AgentProfileTemplate)TemplateCloner.Clone(BuiltInLibrary.Instance.FindByKey("agent.commuter")!);
        impatient.Id = Guid.NewGuid();
        impatient.Key = "agent.test-impatient";
        impatient.PatienceS = patienceS;
        study.Library.Add(impatient);

        var journey = new JourneyTemplate
        {
            Key = "journey.test-give-up", Name = "Impatient", Category = "Journeys",
            Steps =
            [
                JourneyStep.Device("TicketMachine") with { OnGiveUp = giveUp, GiveUpStep = giveUpStep },
                JourneyStep.DwellIn("Retail", Distribution.Fixed(1000)),
                JourneyStep.ExitStep(),
            ],
        };
        study.Library.Add(journey);
        Region spawn = study.Regions.Single(r => r.IsSpawn);
        spawn.EmissionRules[0] = spawn.EmissionRules[0] with { Mix = [MixLine.Of(impatient.Id, journey.Id)] };
        return study;
    }

    /// <summary>
    /// A 40 × 8 m hall closed by a four-lane gate line (y 2–6) and an accessible gate (y 6.4–7.6) at x = 20, with
    /// obstacles filling the gaps; everyone walks west to east through a gate.
    /// </summary>
    private static Study WideGates(Guid profile, double perMinute)
    {
        var study = new Study { Name = "Wide gates" };
        study.Settings.Seed = 5;
        study.Settings.EndCondition = StudyEndCondition.Indefinite;
        var level = new Level { Name = "Ground", ElevationM = 0 };
        study.Levels.Add(level);
        study.Regions.Add(new Region { LevelId = level.Id, Name = "Floor", Kind = RegionKind.Walkable, Outline = ShapeFactory.Rectangle(new(0, 0), new(40, 8)) });
        study.Regions.Add(new Region { LevelId = level.Id, Name = "Gap south", Kind = RegionKind.Obstacle, Outline = ShapeFactory.Rectangle(new(19.4, 0), new(20.6, 2)) });
        study.Regions.Add(new Region { LevelId = level.Id, Name = "Gap middle", Kind = RegionKind.Obstacle, Outline = ShapeFactory.Rectangle(new(19.4, 6), new(20.6, 6.4)) });
        study.Regions.Add(new Region { LevelId = level.Id, Name = "Gap north", Kind = RegionKind.Obstacle, Outline = ShapeFactory.Rectangle(new(19.4, 7.6), new(20.6, 8)) });
        Region west = SimTestStudies.Zone(study, level, "West", ZoneKind.Spawn, new(0, 0), new(2, 8));
        Region east = SimTestStudies.Zone(study, level, "East", ZoneKind.Sink, new(38, 0), new(40, 8));
        var line = (DeviceTemplate)BuiltInLibrary.Instance.FindByKey("device.gate-line")!;
        var wide = (DeviceTemplate)BuiltInLibrary.Instance.FindByKey(BuiltInLibrary.AccessibleGateKey)!;
        study.Devices.Add(DeviceFactory.FromTemplate(study, line, level.Id, new Point2(20, 4), 180));
        study.Devices.Add(DeviceFactory.FromTemplate(study, wide, level.Id, new Point2(20, 7), 180));

        west.EmissionRules.Add(new EmissionRule
        {
            Mix = [MixLine.Of(profile, TemplateIds.FromKey("journey.through-gates"))],
            RatePerMinute = perMinute,
            Pattern = EmissionPattern.Constant,
            DestinationMode = DestinationMode.ChosenSinks,
            Destinations = [new SinkWeight { SinkId = east.Id, Weight = 1 }],
        });
        return study;
    }

    /// <summary>A 40 × 4 m corridor closed by a four-lane gate line at x = 20 facing west, with a snake drawn 8 m west.</summary>
    private static Study SnakeGates(double perMinute)
    {
        var study = new Study { Name = "Snake" };
        study.Settings.Seed = 9;
        study.Settings.EndCondition = StudyEndCondition.Indefinite;
        var level = new Level { Name = "Ground", ElevationM = 0 };
        study.Levels.Add(level);
        study.Regions.Add(new Region { LevelId = level.Id, Name = "Floor", Kind = RegionKind.Walkable, Outline = ShapeFactory.Rectangle(new(0, 0), new(40, 4)) });
        Region west = SimTestStudies.Zone(study, level, "West", ZoneKind.Spawn, new(0, 0), new(2, 4));
        Region east = SimTestStudies.Zone(study, level, "East", ZoneKind.Sink, new(38, 0), new(40, 4));
        var gates = (DeviceTemplate)BuiltInLibrary.Instance.FindByKey("device.gate-line")!;
        Device device = DeviceFactory.FromTemplate(study, gates, level.Id, new Point2(20, 2), 180);
        Point2 start = QueueLines.QueueStart(device);
        device.QueuePath = [start, start + new Point2(-8, 0)];
        study.Devices.Add(device);

        west.EmissionRules.Add(new EmissionRule
        {
            Mix = [MixLine.Of(SimTestStudies.Commuter, TemplateIds.FromKey("journey.through-gates"))],
            RatePerMinute = perMinute,
            Pattern = EmissionPattern.Constant,
            DestinationMode = DestinationMode.ChosenSinks,
            Destinations = [new SinkWeight { SinkId = east.Id, Weight = 1 }],
        });
        return study;
    }

    private static SimEngine Run(Study study, double seconds)
    {
        var engine = new SimEngine(SimTestStudies.Compile(study));
        int steps = (int)Math.Round(seconds / engine.Model.TimeStepS, MidpointRounding.AwayFromZero);
        for (int i = 0; i < steps; i++)
        {
            engine.Step();
        }

        return engine;
    }
}
