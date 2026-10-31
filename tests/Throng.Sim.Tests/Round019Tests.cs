using Throng.Analysis;
using Throng.Analysis.Checks;
using Throng.Core.Geometry;
using Throng.Core.Library;
using Throng.Core.Model;
using Throng.Sim.Engine;

namespace Throng.Sim.Tests;

/// <summary>Round 019: the run overlay's counters (D-331–D-338), predicted shares (D-334) and nodes.csv (D-337).</summary>
[TestClass]
public sealed class Round019Tests
{
    [TestMethod]
    public void ChanceWires_SplitAboutThirtySeventy_AndCountsAddUp()
    {
        Study study = Split(out JourneyTemplate journey, out Guid chance, out Guid north, out Guid south);
        RunResults results = Round009bTests.Run(study, 240).CopyResults();
        JourneyFlowResult flow = results.FlowFor(journey.Id, null) ?? throw new AssertFailedException("No overlay row for the journey.");

        long started = flow.Started;
        Assert.IsTrue(started > 40, $"{started} started.");
        Assert.AreEqual(started, flow.NodeOf(chance)!.Reached, "Everyone who left Spawn reached the chance node.");
        long toNorth = flow.CountOf(chance, 0), toSouth = flow.CountOf(chance, 1);
        Assert.AreEqual(started, toNorth + toSouth, "Every arrival at a chance node leaves by one output.");
        double share = (double)toNorth / started;
        Assert.IsTrue(share is > 0.15 and < 0.45, $"About 30 % north: {share:P0}.");
        Assert.AreEqual(toNorth, flow.NodeOf(north)!.Reached, "The wire's count is the sink's arrivals.");
        Assert.AreEqual(toSouth, flow.NodeOf(south)!.Reached);

        // Finished at a sink = the journey results' sink row (both count people spawned after the warm-up).
        JourneyResult northRow = results.Journeys.Single(j => j.Sink == "North exit");
        Assert.AreEqual(northRow.Finished, flow.NodeOf(north)!.Finished, "Sink node finished matches the per-sink row.");
    }

    [TestMethod]
    public void TagCheckAfterSetTag_AllOneWay()
    {
        Study study = TwoExits(out _, out _);
        var journey = new JourneyTemplate { Key = "journey.user.vip19", Name = "VIP" };
        var graph = new JourneyGraph.Builder(journey.Id);
        Guid set = graph.Add(JourneyGraph.SetTagNode("vip"), 1);
        Guid check = graph.Add(JourneyGraph.TagCheckNode("vip"), 2);
        Guid a = graph.Add(JourneyGraph.ZoneSinkNode("North", "North exit"), 3);
        Guid b = graph.Add(JourneyGraph.ZoneSinkNode("South", "South exit"), 3, 1);
        graph.Wire(graph.Start, set).Wire(set, check).Wire(check, 0, a).Wire(check, 1, b).Apply(journey);
        study.Library.Add(journey);
        UseJourney(study, journey.Id);

        JourneyFlowResult flow = Round009bTests.Run(study, 180).CopyResults().FlowFor(journey.Id, null)!;
        Assert.IsTrue(flow.Started > 20, $"{flow.Started} started.");
        Assert.AreEqual(flow.Started, flow.CountOf(set, 0), "Set tag has one output.");
        Assert.AreEqual(flow.Started, flow.CountOf(check, 0), "Everyone holds the tag just given.");
        Assert.AreEqual(0, flow.CountOf(check, 1));
    }

    [TestMethod]
    public void WarmUpPeople_AreNotCounted()
    {
        Study study = Split(out JourneyTemplate journey, out _, out _, out _);
        study.Settings.WarmUpS = 60;
        SimEngine engine = Round009bTests.Run(study, 180);
        RunResults results = engine.CopyResults();
        JourneyFlowResult flow = results.FlowFor(journey.Id, null)!;
        JourneyResult row = results.Journeys.Single(j => !j.IsSink && !j.IsAll);

        Assert.IsTrue(flow.Started > 0 && flow.Started < engine.Stats.Spawned, $"{flow.Started} of {engine.Stats.Spawned} counted.");
        Assert.AreEqual(row.Finished + row.Stuck + row.InProgress, flow.Started, "Same people as the journey results (D-338).");
    }

    [TestMethod]
    public void ProfileFilter_HasARowPerProfile_AndAllAddsThemUp()
    {
        Study study = Split(out JourneyTemplate journey, out Guid chance, out _, out _);
        RunResults results = Round009bTests.Run(study, 120).CopyResults();
        JourneyFlowResult all = results.FlowFor(journey.Id, null)!;
        JourneyFlowResult[] profiles = [.. results.Flows.Where(f => f.JourneyId == journey.Id && !f.IsAllProfiles)];

        Assert.AreEqual(1, profiles.Length, "One profile on the journey.");
        Assert.AreEqual(SimEngine.AllJourneysName, all.Profile);
        Assert.AreSame(profiles[0], results.FlowFor(journey.Id, profiles[0].Profile));
        Assert.AreEqual(all.Started, profiles.Sum(p => p.Started));
        Assert.AreEqual(all.CountOf(chance, 0), profiles.Sum(p => p.CountOf(chance, 0)));
        Assert.IsNull(results.FlowFor(journey.Id, "No such profile"));
    }

    [TestMethod]
    public void PredictedShares_MatchNodeShares_AndSplitTheChanceWires()
    {
        Study study = Split(out _, out _, out _, out _);
        SimModel model = SimTestStudies.Compile(study);
        SimJourney journey = model.Journeys.Single();
        JourneyShares shares = CapacityCheck.PredictedShares(model, 0, -1) ?? throw new AssertFailedException("No prediction.");
        double[] nodes = CapacityCheck.NodeShares(journey, new Dictionary<ulong, double>());

        Assert.AreEqual(nodes.Length, shares.Nodes.Length);
        for (int n = 0; n < nodes.Length; n++)
        {
            Assert.AreEqual(nodes[n], shares.Nodes[n], 1e-9, $"Node {n}.");
        }

        int chance = journey.Start;
        Assert.AreEqual(SimNodeKind.Chance, journey.Nodes[chance].Kind);
        Assert.AreEqual(0.3, shares.WireAt(chance, 0), 1e-9);
        Assert.AreEqual(0.7, shares.WireAt(chance, 1), 1e-9);
        Assert.AreEqual(journey.NodeIds.Length, journey.Nodes.Length, "Every compiled node maps back to the model.");
        Assert.IsNull(CapacityCheck.PredictedShares(model, 0, 5), "No line of that profile.");
    }

    [TestMethod]
    public void NodesCsv_HasTheHeader_AndARowPerNodeAndWire()
    {
        Study study = Split(out _, out _, out _, out _);
        SimModel model = SimTestStudies.Compile(study);
        RunResults results = Round009bTests.Run(study, 120).CopyResults();
        string[] lines = ResultsCsv.NodesCsv(results, model).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.AreEqual(ResultsCsv.NodesHeader, lines[0]);
        int columns = ResultsCsv.NodesHeader.Split(',').Length;
        Assert.IsTrue(lines.Skip(1).All(l => l.Split(',').Length == columns), "Every row has every column.");
        Assert.IsTrue(lines.Any(l => l.StartsWith("Split,ALL,Spawn,Spawn,0,Chance,", StringComparison.Ordinal)), string.Join("\n", lines.Take(4)));
        string chanceToNorth = lines.First(l => l.StartsWith("Split,ALL,Chance,Chance,0,North exit,", StringComparison.Ordinal));
        Assert.AreEqual("30.0", chanceToNorth.Split(',')[8], "PredictedPct from the capacity model.");

        // Spawn, chance (node + 2 wires), two sinks: 6 rows per block; blocks for ALL and the one profile.
        Assert.AreEqual(1 + (2 * 6), lines.Length, string.Join("\n", lines));
    }

    /// <summary>The two-exit hall with a Chance 30 / 70 between the north and south sink zones.</summary>
    private static Study Split(out JourneyTemplate journey, out Guid chance, out Guid north, out Guid south)
    {
        Study study = TwoExits(out _, out _);
        journey = new JourneyTemplate { Key = "journey.user.split19", Name = "Split" };
        var graph = new JourneyGraph.Builder(journey.Id);
        chance = graph.Add(JourneyGraph.ChanceNode(30, 70), 1);
        north = graph.Add(JourneyGraph.ZoneSinkNode("North", "North exit"), 2);
        south = graph.Add(JourneyGraph.ZoneSinkNode("South", "South exit"), 2, 1);
        graph.Wire(graph.Start, chance).Wire(chance, 0, north).Wire(chance, 1, south).Apply(journey);
        study.Library.Add(journey);
        UseJourney(study, journey.Id);
        return study;
    }

    /// <summary>A 30 × 10 m hall: spawn west; sink zones north-east (tag North) and south-east (tag South).</summary>
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
