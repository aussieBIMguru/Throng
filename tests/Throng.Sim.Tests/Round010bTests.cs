using Throng.Core.Geometry;
using Throng.Core.Library;
using Throng.Core.Model;
using Throng.Sim.Engine;
using Throng.Sim.Nav;

namespace Throng.Sim.Tests;

/// <summary>Round 010b: train services (D-184–D-187), early and crowd stress (D-193, D-194).</summary>
[TestClass]
public sealed class Round010bTests
{
    [TestMethod]
    public void Train_PeopleGetOffOnlyWhenATrainArrives()
    {
        Study study = Platform(entryRate: 0, alightPerTrain: 30, capacity: 100, offset: 10, headway: 120, dwell: 30, doorsAreSpawns: true);
        var engine = new SimEngine(SimTestStudies.Compile(study));
        Run(engine, 9);
        Assert.AreEqual(0L, engine.Stats.Spawned, "No one gets off before the first train.");
        Run(engine, 16);
        Assert.IsTrue(engine.Stats.Spawned >= 25 && engine.Stats.Spawned <= 30, $"{engine.Stats.Spawned} got off the first train (30 expected).");
        long first = engine.Stats.Spawned;
        Run(engine, 95);
        Assert.IsTrue(engine.Stats.Spawned <= 30, $"{engine.Stats.Spawned} got off by 120 s: the doors' rules mustn't emit between trains (first train: {first}).");
        Run(engine, 30);
        Assert.IsTrue(engine.Stats.Spawned > 30, "The second train lets people off too.");
    }

    [TestMethod]
    public void Train_BoardersWaitForTheDoors_AndTheTrainFillsUp()
    {
        Study study = Platform(entryRate: 120, alightPerTrain: 0, capacity: 20, offset: 60, headway: 120, dwell: 30, doorsAreSpawns: false);
        var engine = new SimEngine(SimTestStudies.Compile(study));
        Run(engine, 58);
        Assert.AreEqual(0L, engine.Stats.Arrived, "No one boards before the first train.");
        AgentStore a = engine.Agents;
        int waiting = 0;
        for (int i = 0; i < a.Count; i++)
        {
            waiting += a.Phase[i] == AgentPhase.WaitTrain ? 1 : 0;
        }

        Assert.IsTrue(waiting > 10, $"Only {waiting} people waiting beside the doors.");
        Run(engine, 40);
        Assert.AreEqual(20L, engine.BoardedTrains, "The train takes its 20 places and no more.");
        Assert.AreEqual(engine.BoardedTrains, engine.Stats.Arrived, "Boarding is how people leave here.");
        Assert.IsTrue(engine.Stats.LeftBehind > 0, "People who didn't fit are left behind.");
    }

    [TestMethod]
    public void BoardTrainStep_GoesToTheTaggedServiceOnly()
    {
        Study study = Platform(entryRate: 60, alightPerTrain: 0, capacity: 100, offset: 100, headway: 200, dwell: 30, doorsAreSpawns: false);
        Level level = study.Levels[0];
        var north = new List<Guid>();
        foreach (double x in new[] { 5.0, 15.0, 25.0 })
        {
            north.Add(SimTestStudies.Zone(study, level, $"North door {x}", ZoneKind.Sink, new(x, 9), new(x + 3, 10)).Id);
        }

        study.TrainServices[0] = study.TrainServices[0] with { Name = "South", Tag = "South" };
        study.TrainServices.Add(new TrainService { Name = "North", Tag = "North", DoorZoneIds = north, OffsetS = 100, HeadwayS = 200, DwellS = 30, AlightPerTrain = 0 });
        var journey = new JourneyTemplate { Key = "journey.test-north", Name = "North train", Category = "Journeys", Steps = [JourneyStep.BoardTrainStep("North")] };
        study.Library.Add(journey);
        Region entry = study.Regions.Single(r => r.Name == "Entry");
        entry.EmissionRules[0] = entry.EmissionRules[0] with { Mix = [MixLine.Of(SimTestStudies.Commuter, journey.Id)] };

        var engine = new SimEngine(SimTestStudies.Compile(study));
        Run(engine, 60);
        NavWorld world = engine.Model.World;
        AgentStore a = engine.Agents;
        Assert.IsTrue(a.Count > 20);
        for (int i = 0; i < a.Count; i++)
        {
            Assert.IsTrue(north.Contains(world.Sinks[a.Sink[i]].RegionId), $"Agent {i} is heading for '{world.Sinks[a.Sink[i]].Name}', not a North door.");
        }
    }

    [TestMethod]
    public void Stress_BuildsFromTheFirstSecond_AndCrowdThresholdFollowsTolerance()
    {
        Assert.AreEqual(0.0, SimEngine.StressFrom(0, 180), 1e-9);
        Assert.AreEqual(0.25, SimEngine.StressFrom(45, 180), 1e-9);
        Assert.AreEqual(0.5, SimEngine.StressFrom(90, 180), 1e-9, "STRESSED still at half the patience.");
        Assert.AreEqual(0.0, SimEngine.StressFromHalfPatience(45, 180), 1e-9, "The old curve, kept for the diagnostics.");
        Assert.AreEqual(2.5, SimEngine.CrowdStressDensity(0.5), 1e-9);
        Assert.IsTrue(SimEngine.CrowdStressDensity(0.9) > SimEngine.CrowdStressDensity(0.5));
    }

    /// <summary>
    /// A 40 × 10 m platform with three 3 m door zones on its south edge (y 0–1; the track is beyond y = 0), an exit at
    /// the east end and an entry at the west sending people to the doors. One train service owns the doors.
    /// </summary>
    internal static Study Platform(double entryRate, int alightPerTrain, int capacity, double offset, double headway, double dwell, bool doorsAreSpawns)
    {
        var study = new Study { Name = "Platform" };
        study.Settings.Seed = 5;
        study.Settings.EndCondition = StudyEndCondition.Indefinite;
        var level = new Level { Name = "Platform", ElevationM = 0 };
        study.Levels.Add(level);
        study.Regions.Add(new Region { LevelId = level.Id, Name = "Floor", Kind = RegionKind.Walkable, Outline = ShapeFactory.Rectangle(new(0, 0), new(40, 10)) });
        Region exit = SimTestStudies.Zone(study, level, "Stairs", ZoneKind.Sink, new(38, 3), new(40, 7));
        var doors = new List<Region>();
        foreach (double x in new[] { 5.0, 15.0, 25.0 })
        {
            Region door = SimTestStudies.Zone(study, level, $"South door {x}", doorsAreSpawns ? ZoneKind.SpawnSink : ZoneKind.Sink, new(x, 0), new(x + 3, 1));
            if (doorsAreSpawns)
            {
                door.EmissionRules.Add(new EmissionRule
                {
                    Mix = [MixLine.Of(SimTestStudies.Commuter)],
                    RatePerMinute = 1,
                    Pattern = EmissionPattern.Constant,
                    DestinationMode = DestinationMode.ChosenSinks,
                    Destinations = [new SinkWeight { SinkId = exit.Id, Weight = 1 }],
                });
            }

            doors.Add(door);
        }

        Region entry = SimTestStudies.Zone(study, level, "Entry", ZoneKind.Spawn, new(0, 4), new(2, 8));
        if (entryRate > 0)
        {
            entry.EmissionRules.Add(new EmissionRule
            {
                Mix = [MixLine.Of(SimTestStudies.Commuter)],
                RatePerMinute = entryRate,
                Pattern = EmissionPattern.Constant,
                DestinationMode = DestinationMode.ChosenSinks,
                Destinations = [.. doors.Select(d => new SinkWeight { SinkId = d.Id, Weight = 1 })],
            });
        }

        study.TrainServices.Add(new TrainService
        {
            Name = "Test service",
            DoorZoneIds = [.. doors.Select(d => d.Id)],
            OffsetS = offset,
            HeadwayS = headway,
            DwellS = dwell,
            AlightPerTrain = alightPerTrain,
            AlightOverS = 10,
            Capacity = capacity,
        });
        return study;
    }

    private static void Run(SimEngine engine, double seconds)
    {
        int steps = (int)Math.Round(seconds / engine.Model.TimeStepS);
        for (int s = 0; s < steps; s++)
        {
            engine.Step();
        }
    }
}
