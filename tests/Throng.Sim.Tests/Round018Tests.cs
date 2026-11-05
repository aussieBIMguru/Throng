using Throng.Core.Geometry;
using Throng.Core.Library;
using Throng.Core.Model;
using Throng.IO;
using Throng.Sim.Engine;

namespace Throng.Sim.Tests;

/// <summary>Round 018a: journey graphs run (D-302–D-306, D-314), zone sinks (D-303) and per-sink results (D-312).</summary>
[TestClass]
public sealed class Round018Tests
{
    [TestMethod]
    public void Chance_SplitsPeopleBetweenTwoZoneSinks_AndResultsHaveARowPerSink()
    {
        Study study = TwoExits(out Region north, out _);
        var journey = new JourneyTemplate { Key = "journey.user.split", Name = "Split" };
        var graph = new JourneyGraph.Builder(journey.Id);
        Guid chance = graph.Add(JourneyGraph.ChanceNode(30, 70), 1);
        Guid a = graph.Add(JourneyGraph.ZoneSinkNode("North", "North exit"), 2);
        Guid b = graph.Add(JourneyGraph.ZoneSinkNode("South", "South exit"), 2, 1);
        graph.Wire(graph.Start, chance).Wire(chance, 0, a).Wire(chance, 1, b).Apply(journey);
        study.Library.Add(journey);
        UseJourney(study, journey.Id);

        SimEngine engine = Round009bTests.Run(study, 240);
        RunResults results = engine.CopyResults();
        JourneyResult[] sinks = [.. results.Journeys.Where(j => j.IsSink)];
        Assert.AreEqual(2, sinks.Length, string.Join(", ", results.Journeys.Select(j => j.Journey)));
        JourneyResult toNorth = sinks.Single(s => s.Sink == "North exit"), toSouth = sinks.Single(s => s.Sink == "South exit");
        long total = toNorth.Finished + toSouth.Finished;
        Assert.IsTrue(total > 40, $"{total} finished.");
        double share = (double)toNorth.Finished / total;
        Assert.IsTrue(share is > 0.15 and < 0.45, $"About 30 % go north: {share:P0} ({toNorth.Finished} of {total}).");
        Assert.AreEqual(total, results.Journeys.Single(j => !j.IsSink && !j.IsAll).Finished, "Sink rows add up to the journey row.");
        Assert.AreEqual("Split ▸ North exit", toNorth.Journey);
        Assert.AreEqual("North", north.Tag);
    }

    [TestMethod]
    public void TagCheck_AndSetTag_RouteEveryoneTheSameWay()
    {
        Study study = TwoExits(out _, out _);
        var journey = new JourneyTemplate { Key = "journey.user.vip", Name = "VIP" };
        var graph = new JourneyGraph.Builder(journey.Id);
        Guid set = graph.Add(JourneyGraph.SetTagNode("vip"), 1);
        Guid check = graph.Add(JourneyGraph.TagCheckNode("vip"), 2);
        Guid a = graph.Add(JourneyGraph.ZoneSinkNode("North", "North exit"), 3);
        Guid b = graph.Add(JourneyGraph.ZoneSinkNode("South", "South exit"), 3, 1);
        graph.Wire(graph.Start, set).Wire(set, check).Wire(check, 0, a).Wire(check, 1, b).Apply(journey);
        study.Library.Add(journey);
        UseJourney(study, journey.Id);

        RunResults results = Round009bTests.Run(study, 180).CopyResults();
        JourneyResult toNorth = results.Journeys.Single(j => j.Sink == "North exit");
        Assert.IsTrue(toNorth.Finished > 20, $"{toNorth.Finished} went north.");
        Assert.AreEqual(0, results.Journeys.Single(j => j.Sink == "South exit").Finished, "Everyone holds the tag they were just given.");
    }

    [TestMethod]
    public void ProfileDefaultJourney_IsCompiled_AndAJourneyWithOneSinkHasNoSinkRows()
    {
        Study study = SimTestStudies.Corridor(30);
        var own = (AgentProfileTemplate)TemplateCloner.Clone(BuiltInLibrary.Instance.FindByKey("agent.commuter")!);
        own.Id = Guid.NewGuid();
        own.Key = "agent.test-default";
        var journey = new JourneyTemplate { Key = "journey.user.one", Name = "One sink" };
        var graph = new JourneyGraph.Builder(journey.Id);
        Guid exit = graph.Add(JourneyGraph.ExitNode("Out"), 1);
        graph.Wire(graph.Start, exit).Apply(journey);
        own.DefaultJourneyId = journey.Id;
        study.Library.Add(own);
        study.Library.Add(journey);
        Region spawn = study.Regions.Single(r => r.IsSpawn);
        spawn.EmissionRules[0] = spawn.EmissionRules[0] with { Mix = [MixLine.Of(own.Id) with { UseProfileJourney = true }] };

        SimModel model = SimTestStudies.Compile(study);
        Assert.AreEqual("One sink", model.Journeys.Single().Name, "The profile's default journey (D-304).");
        Assert.AreEqual(0, model.Journeys[0].Start, "Spawn is wired to the exit.");
        RunResults results = Round009bTests.Run(study, 90).CopyResults();
        Assert.IsFalse(results.Journeys.Any(j => j.IsSink), "One sink: no sink rows (D-312).");
        Assert.IsTrue(results.Journeys.Single().Finished > 10);
    }

    [TestMethod]
    public void Demo_CompilesGraphs_LikeTheOldSteps()
    {
        SimModel model = SimTestStudies.Compile(DemoStudyBuilder.Build());
        SimJourney ticket = model.Journeys.Single(j => j.Name == "Ticket then gates");
        CollectionAssert.AreEqual(new[] { JourneyStepKind.GoToDevice, JourneyStepKind.GoToDevice, JourneyStepKind.Exit }, ticket.Steps.Select(s => s.Kind).ToArray(),
            "Gate, machine, exit (in the order they're reached from Spawn): " + string.Join(", ", ticket.Steps.Select(s => s.Label)));
        Assert.AreEqual(SimNodeKind.TagCheck, ticket.Nodes[ticket.Start].Kind, "Has ticket? comes first.");
        Assert.IsTrue(model.Journeys.All(j => j.SinkSteps.Length == 1), "The demo's journeys have one sink each.");
    }

    /// <summary>A 30 × 10 m hall: spawn west; sink zones north-east (tag North) and south-east (tag South), both rule destinations.</summary>
    private static Study TwoExits(out Region north, out Region south)
    {
        var study = new Study { Name = "Two exits" };
        study.Settings.Seed = 11;
        study.Settings.EndCondition = StudyEndCondition.Indefinite;
        var level = new Level { Name = "Ground", ElevationM = 0 };
        study.Levels.Add(level);
        study.Regions.Add(new Region { LevelId = level.Id, Name = "Floor", Kind = RegionKind.Walkable, Outline = ShapeFactory.Rectangle(new(0, 0), new(30, 10)) });
        Region spawn = SimTestStudies.Zone(study, level, "West", ZoneKind.Spawn, new(0, 0), new(2, 10));
        north = SimTestStudies.Zone(study, level, "North", ZoneKind.Sink, new(28, 6), new(30, 10));
        north.Tag = "North";
        south = SimTestStudies.Zone(study, level, "South", ZoneKind.Sink, new(28, 0), new(30, 4));
        south.Tag = "South";
        spawn.EmissionRules.Add(new EmissionRule
        {
            Mix = [MixLine.Of(SimTestStudies.Commuter)],
            RatePerMinute = 30,
            Pattern = EmissionPattern.Constant,
            DestinationMode = DestinationMode.ChosenSinks,
            Destinations = [new SinkWeight { SinkId = north.Id, Weight = 1 }, new SinkWeight { SinkId = south.Id, Weight = 1 }],
        });
        return study;
    }

    private static void UseJourney(Study study, Guid journeyId)
    {
        Region spawn = study.Regions.Single(r => r.IsSpawn);
        spawn.EmissionRules[0] = spawn.EmissionRules[0] with { Mix = [MixLine.Of(SimTestStudies.Commuter, journeyId)] };
    }
}
