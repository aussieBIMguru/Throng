using Throng.Core.Editing;
using Throng.Core.Geometry;
using Throng.Core.Library;
using Throng.Core.Model;
using Throng.Sim.Engine;

namespace Throng.Sim.Tests;

/// <summary>Round 018b: pick lists (D-318), choose-by rules (D-319–D-321) and device node settings (D-322) in the run.</summary>
[TestClass]
public sealed class Round018bTests
{
    [TestMethod]
    public void PickList_OnlyThePickedDeviceIsUsed()
    {
        Study study = Hall(out Device near, out Device far);
        UseDeviceJourney(study, JourneyGraph.DeviceNode("Gate") with { Picks = [JourneyPick.Of(far.Id)] });
        SimEngine engine = Round009bTests.Run(study, 8);
        (int toNear, int toFar) = Heading(engine, near, far);
        Assert.IsTrue(toFar > 5, $"{toFar} head for the picked gate.");
        Assert.AreEqual(0, toNear, "Nobody uses the gate that isn't picked, though it's nearer.");
    }

    [TestMethod]
    public void AlongTheWay_PrefersTheDeviceOnTheRoute_QuickestTheNearer()
    {
        Study quick = Hall(out Device near, out Device far);
        UseDeviceJourney(quick, JourneyGraph.DeviceNode("Gate"));
        (int nearQuick, int farQuick) = Heading(Round009bTests.Run(quick, 8), near, far);
        Assert.IsTrue(nearQuick > farQuick, $"Quickest: the gate behind them is closer ({nearQuick} vs {farQuick}).");

        Study along = Hall(out near, out far);
        UseDeviceJourney(along, JourneyGraph.DeviceNode("Gate") with { ChooseBy = JourneyChooseBy.AlongTheWay });
        (int nearAlong, int farAlong) = Heading(Round009bTests.Run(along, 8), near, far);
        Assert.IsTrue(farAlong > nearAlong, $"Along the way: the gate on the way to the exit ({farAlong} vs {nearAlong}, D-320).");
    }

    [TestMethod]
    public void RandomByWeight_SplitsBetweenPickedSinkZones()
    {
        Study study = Hall(out _, out _);
        study.Devices.Clear();
        Level level = study.Levels[0];
        Region north = SimTestStudies.Zone(study, level, "North", ZoneKind.Sink, new(38, 6), new(40, 10));
        Region south = SimTestStudies.Zone(study, level, "South", ZoneKind.Sink, new(38, 0), new(40, 4));
        var journey = new JourneyTemplate { Key = "journey.user.weighted", Name = "Weighted" };
        var graph = new JourneyGraph.Builder(journey.Id);
        Guid exit = graph.Add(JourneyGraph.ZoneSinkNode(string.Empty) with { Picks = [JourneyPick.Of(north.Id, 3), JourneyPick.Of(south.Id, 1)], ChooseBy = JourneyChooseBy.RandomWeighted }, 1);
        graph.Wire(graph.Start, exit).Apply(journey);
        study.Library.Add(journey);
        Region spawn = study.Regions.Single(r => r.IsSpawn);
        spawn.EmissionRules[0] = spawn.EmissionRules[0] with { Mix = [MixLine.Of(SimTestStudies.Commuter, journey.Id)], RatePerMinute = 240 };

        SimEngine engine = Round009bTests.Run(study, 12);
        int northTarget = Target(engine, north), southTarget = Target(engine, south);
        int toNorth = 0, toSouth = 0;
        for (int i = 0; i < engine.Agents.Count; i++)
        {
            toNorth += engine.Agents.Sink[i] == northTarget ? 1 : 0;
            toSouth += engine.Agents.Sink[i] == southTarget ? 1 : 0;
        }

        double share = (double)toNorth / Math.Max(1, toNorth + toSouth);
        Assert.IsTrue(toNorth + toSouth > 25, $"{toNorth + toSouth} walking.");
        Assert.IsTrue(share is > 0.55 and < 0.93, $"About 75 % north (weights 3 : 1, D-321): {share:P0}.");
    }

    [TestMethod]
    public void DeviceNodeSettings_AreCompiled()
    {
        Study study = Hall(out _, out Device far);
        UseDeviceJourney(study, JourneyGraph.DeviceNode("Gate") with
        {
            Picks = [JourneyPick.Of(far.Id, 2)],
            ChooseBy = JourneyChooseBy.FirstFree,
            Balk = JourneyBalk.Off,
            PatienceS = 45,
            OnGiveUp = JourneyGiveUp.Leave,
        });
        SimStep step = SimTestStudies.Compile(study).Journeys.Single().Steps.First(s => s.Kind == JourneyStepKind.GoToDevice);
        Assert.AreEqual(JourneyChooseBy.FirstFree, step.ChooseBy);
        Assert.AreEqual(JourneyBalk.Off, step.Balk);
        Assert.AreEqual(45, step.PatienceS, 1e-9);
        Assert.AreEqual(1, step.Targets.Length);
        Assert.AreEqual(2, step.WeightAt(0), 1e-9);
    }

    /// <summary>
    /// A 40 × 10 m hall: spawn at x 4–6 (y 3–7), sink at the east end; a gate line behind the spawn at x 2 and one on the
    /// way at x 20.
    /// </summary>
    internal static Study Hall(out Device near, out Device far)
    {
        var study = new Study { Name = "Hall" };
        study.Settings.Seed = 13;
        study.Settings.EndCondition = StudyEndCondition.Indefinite;
        var level = new Level { Name = "Ground", ElevationM = 0 };
        study.Levels.Add(level);
        study.Regions.Add(new Region { LevelId = level.Id, Name = "Floor", Kind = RegionKind.Walkable, Outline = ShapeFactory.Rectangle(new(0, 0), new(40, 10)) });
        Region spawn = SimTestStudies.Zone(study, level, "West", ZoneKind.Spawn, new(4, 3), new(6, 7));
        Region sink = SimTestStudies.Zone(study, level, "East", ZoneKind.Sink, new(38, 3), new(40, 7));
        // Gate lines: quick service, so queues stay short and walking decides.
        var machine = (DeviceTemplate)BuiltInLibrary.Instance.FindByKey("device.gate-line")!;
        near = DeviceFactory.FromTemplate(study, machine, level.Id, new Point2(2, 5), 0);
        far = DeviceFactory.FromTemplate(study, machine, level.Id, new Point2(20, 5), 180);
        near.Name = "Behind";
        far.Name = "On the way";
        study.Devices.AddRange([near, far]);
        spawn.EmissionRules.Add(new EmissionRule
        {
            Mix = [MixLine.Of(SimTestStudies.Commuter)],
            RatePerMinute = 120,
            Pattern = EmissionPattern.Constant,
            DestinationMode = DestinationMode.ChosenSinks,
            Destinations = [new SinkWeight { SinkId = sink.Id, Weight = 1 }],
        });
        return study;
    }

    internal static void UseDeviceJourney(Study study, JourneyNode device)
    {
        var journey = new JourneyTemplate { Key = "journey.user.device", Name = "Device" };
        var graph = new JourneyGraph.Builder(journey.Id);
        Guid d = graph.Add(device, 1);
        Guid exit = graph.Add(JourneyGraph.ExitNode(), 2);
        graph.Wire(graph.Start, d).Wire(d, exit).Apply(journey);
        study.Library.Add(journey);
        Region spawn = study.Regions.Single(r => r.IsSpawn);
        spawn.EmissionRules[0] = spawn.EmissionRules[0] with { Mix = [MixLine.Of(SimTestStudies.Commuter, journey.Id)] };
    }

    /// <summary>People in the model heading for (or at) each machine.</summary>
    internal static (int Near, int Far) Heading(SimEngine engine, Device near, Device far)
    {
        int nearIndex = engine.Model.Devices.FirstOrDefault(d => d.DeviceId == near.Id)?.Index ?? -2;
        int farIndex = engine.Model.Devices.FirstOrDefault(d => d.DeviceId == far.Id)?.Index ?? -2;
        int toNear = 0, toFar = 0;
        for (int i = 0; i < engine.Agents.Count; i++)
        {
            toNear += engine.Agents.QDevice[i] == nearIndex ? 1 : 0;
            toFar += engine.Agents.QDevice[i] == farIndex ? 1 : 0;
        }

        return (toNear, toFar);
    }

    /// <summary>The route target of a sink zone (its exit).</summary>
    private static int Target(SimEngine engine, Region zone) =>
        engine.Model.World.Sinks.First(s => s.RegionId == zone.Id && s.Role == Throng.Sim.Nav.NavTargetRole.Exit).Index;
}
