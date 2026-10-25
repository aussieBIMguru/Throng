using Throng.Core.Editing;
using Throng.Core.Geometry;
using Throng.Core.Library;
using Throng.Core.Model;
using Throng.IO;
using Throng.Sim.Engine;

namespace Throng.Sim.Tests;

/// <summary>Round 009a: journeys, tags, device choice and queues (D-149–D-153), and the demo with journeys (D-155).</summary>
[TestClass]
public sealed class Round009Tests
{
    private const string Ticket = "ticket";

    [TestMethod]
    public void Tags_PeopleWithATicket_SkipTheMachine()
    {
        SimEngine engine = Run(CorridorWithMachines(20, 100, 15), 60);
        Assert.AreEqual(0, engine.ServedAt(0), "Everyone holds a ticket, so no one uses the machine.");
        Assert.IsTrue(engine.Stats.Arrived > 5, $"Only {engine.Stats.Arrived} arrived.");
    }

    [TestMethod]
    public void Tags_PeopleWithoutATicket_QueueAndAreServed()
    {
        SimEngine engine = Run(CorridorWithMachines(20, 0, 15), 120);
        Assert.IsTrue(engine.ServedAt(0) >= 2, $"Only {engine.ServedAt(0)} served at the machine.");
        Assert.IsTrue(engine.QueueLength(0) >= 3, $"Queue of {engine.QueueLength(0)}.");
        Assert.AreEqual(0, engine.Stats.StuckRemoved, "Queuers aren't stuck.");
    }

    [TestMethod]
    public void Queue_SlotsLineUp_AndAdvance()
    {
        SimEngine engine = Run(CorridorWithMachines(20, 0, 15), 120);
        AgentStore a = engine.Agents;
        SimDevice machine = engine.Model.Devices[0];
        List<int> slots = [];
        int near = 0, queued = 0;
        for (int i = 0; i < a.Count; i++)
        {
            if (a.Phase[i] is not (AgentPhase.Queue or AgentPhase.Service))
            {
                continue;
            }

            slots.Add(a.QSlot[i]);
            Point2[] line = machine.Slots[a.QLine[i]];
            Point2 slot = line[Math.Min(a.QSlot[i], line.Length - 1)];
            queued++;
            if (Point2.Distance(slot, new Point2(a.X[i], a.Y[i])) < 0.8)
            {
                near++;
            }
        }

        slots.Sort();
        CollectionAssert.AreEqual(Enumerable.Range(0, slots.Count).ToList(), slots, "Slots run 0, 1, 2… with no gaps.");
        Assert.IsTrue(near >= queued / 2, $"{near} of {queued} queuers are at their slot.");

        long served = engine.ServedAt(0);
        RunMore(engine, 60);
        Assert.IsTrue(engine.ServedAt(0) > served, "The line moves on as the head is served.");
    }

    [TestMethod]
    public void DeviceChoice_UsesTheFartherMachine_WhenTheNearOneHasAQueue()
    {
        SimEngine engine = Run(CorridorWithMachines(20, 0, 12, 20), 180);
        Assert.IsTrue(engine.ServedAt(0) > 0, "The near machine is used.");
        Assert.IsTrue(engine.ServedAt(1) > 0, "With a queue at the near machine, people walk on to the far one (walk + wait, D-152).");
    }

    [TestMethod]
    public void Gates_WorkBothWays()
    {
        SimEngine engine = Run(GateCorridor(20), 240);
        SimStats stats = engine.Stats;
        Assert.IsTrue(engine.ServedAt(0) > 60, $"Only {engine.ServedAt(0)} passed the gates.");
        Assert.IsTrue(stats.Arrived > 80, $"Only {stats.Arrived} arrived through the gates both ways.");
        Assert.IsTrue(stats.StuckRemoved <= 3, $"{stats.StuckRemoved} removed as stuck.");
    }

    [TestMethod]
    public void Dwell_LingersInTheZone()
    {
        Study study = SimTestStudies.Corridor(20);
        Level level = study.Levels[0];
        Region shop = SimTestStudies.Zone(study, level, "Shop", ZoneKind.Dwell, new(12, 0), new(18, 4));
        shop.Tag = "Retail";
        var journey = new JourneyTemplate
        {
            Key = "journey.test-dwell", Name = "Browse", Category = "Journeys",
            Steps = [JourneyStep.DwellIn("Retail", Distribution.Fixed(20)), JourneyStep.ExitStep()],
        };
        study.Library.Add(journey);
        Region spawn = study.Regions.Single(r => r.IsSpawn);
        spawn.EmissionRules[0] = spawn.EmissionRules[0] with { Mix = [MixLine.Of(SimTestStudies.Commuter, journey.Id)] };

        SimEngine engine = Run(study, 120);
        Assert.IsTrue(engine.Agents.Phase.Take(engine.Agents.Count).Any(p => p == AgentPhase.Dwell), "Someone is dwelling.");
        Assert.IsTrue(engine.Stats.Arrived > 5, $"Only {engine.Stats.Arrived} arrived.");
        Assert.IsTrue(engine.Stats.MeanJourneyS > 20 + (26 / 1.6), $"Mean journey {engine.Stats.MeanJourneyS:0.0} s doesn't include the 20 s dwell.");
    }

    [TestMethod]
    public void Mix_SharesSplitTheArrivals()
    {
        Study study = SimTestStudies.Corridor(60);
        Guid infrequent = TemplateIds.FromKey("agent.infrequent");
        Region spawn = study.Regions.Single(r => r.IsSpawn);
        spawn.EmissionRules[0] = spawn.EmissionRules[0] with { Mix = [MixLine.Of(SimTestStudies.Commuter, null, 3), MixLine.Of(infrequent, null, 1)] };
        SimEngine engine = Run(study, 60);
        int commuters = 0, total = engine.Agents.Count;
        for (int i = 0; i < total; i++)
        {
            if (engine.Model.Profiles[engine.Agents.Profile[i]].Template.Id == SimTestStudies.Commuter)
            {
                commuters++;
            }
        }

        Assert.IsTrue(total > 20);
        Assert.IsTrue(commuters > total * 0.55 && commuters < total * 0.95, $"{commuters} of {total} are commuters (expected about 75%).");
    }

    [TestMethod]
    public void Demo_WithJourneys_PeopleQueueAtMachinesAndGates()
    {
        SimEngine engine = Run(DemoStudyBuilder.Build(), 300);
        SimModel model = engine.Model;
        long machines = 0, gates = 0;
        foreach (SimDevice device in model.Devices)
        {
            if (device.TypeTag == "TicketMachine")
            {
                machines += engine.ServedAt(device.Index);
            }
            else if (device.TypeTag == "Gate")
            {
                gates += engine.ServedAt(device.Index);
            }
        }

        SimStats stats = engine.Stats;
        Assert.IsTrue(machines > 10, $"Only {machines} served at the ticket machines.");
        Assert.IsTrue(gates > 150, $"Only {gates} passed the gates.");
        Assert.IsTrue(stats.Arrived > 50, $"Only {stats.Arrived} arrived.");
        Assert.IsTrue(stats.StuckRemoved < (stats.Arrived / 10) + 5, $"{stats.StuckRemoved} removed as stuck vs {stats.Arrived} arrived.");
    }

    /// <summary>The corridor with ticket machines on its south wall and a "ticket, then exit" journey for everyone.</summary>
    private static Study CorridorWithMachines(double perMinute, double ticketPercent, params double[] machineXs)
    {
        Study study = SimTestStudies.Corridor(perMinute);
        Level level = study.Levels[0];
        var template = (DeviceTemplate)BuiltInLibrary.Instance.FindByKey("device.ticket-machine")!;
        foreach (double x in machineXs)
        {
            study.Devices.Add(DeviceFactory.FromTemplate(study, template, level.Id, new Point2(x, 0.3), 90));
        }

        var journey = new JourneyTemplate
        {
            Key = "journey.test-ticket", Name = "Ticket", Category = "Journeys",
            Steps = [JourneyStep.Device("TicketMachine", lacks: Ticket, grants: Ticket), JourneyStep.ExitStep()],
        };
        study.Library.Add(journey);
        Region spawn = study.Regions.Single(r => r.IsSpawn);
        spawn.EmissionRules[0] = spawn.EmissionRules[0] with { Mix = [MixLine.Of(SimTestStudies.Commuter, journey.Id, 1, SpawnTag.Of(Ticket, ticketPercent))] };
        return study;
    }

    /// <summary>A 40 × 4 m corridor closed by a four-lane gate line in the middle, with people walking both ways.</summary>
    private static Study GateCorridor(double perMinuteEachWay)
    {
        var study = new Study { Name = "Gates" };
        study.Settings.Seed = 11;
        study.Settings.EndCondition = StudyEndCondition.Indefinite;
        var level = new Level { Name = "Ground", ElevationM = 0 };
        study.Levels.Add(level);
        study.Regions.Add(new Region { LevelId = level.Id, Name = "Floor", Kind = RegionKind.Walkable, Outline = ShapeFactory.Rectangle(new(0, 0), new(40, 4)) });
        Region west = SimTestStudies.Zone(study, level, "West", ZoneKind.SpawnSink, new(0, 0), new(2, 4));
        Region east = SimTestStudies.Zone(study, level, "East", ZoneKind.SpawnSink, new(38, 0), new(40, 4));
        var gates = (DeviceTemplate)BuiltInLibrary.Instance.FindByKey("device.gate-line")!;
        study.Devices.Add(DeviceFactory.FromTemplate(study, gates, level.Id, new Point2(20, 2), 0));

        Guid throughGates = TemplateIds.FromKey("journey.through-gates");
        foreach ((Region from, Region to) in new[] { (west, east), (east, west) })
        {
            from.EmissionRules.Add(new EmissionRule
            {
                Mix = [MixLine.Of(SimTestStudies.Commuter, throughGates)],
                RatePerMinute = perMinuteEachWay,
                Pattern = EmissionPattern.Constant,
                DestinationMode = DestinationMode.ChosenSinks,
                Destinations = [new SinkWeight { SinkId = to.Id, Weight = 1 }],
            });
        }

        return study;
    }

    private static SimEngine Run(Study study, double seconds)
    {
        var engine = new SimEngine(SimTestStudies.Compile(study));
        RunMore(engine, seconds);
        return engine;
    }

    private static void RunMore(SimEngine engine, double seconds)
    {
        int steps = (int)Math.Round(seconds / engine.Model.TimeStepS, MidpointRounding.AwayFromZero);
        for (int i = 0; i < steps; i++)
        {
            engine.Step();
        }
    }
}
