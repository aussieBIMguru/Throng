using Throng.Core.Editing;
using Throng.Core.Geometry;
using Throng.Core.Library;
using Throng.Core.Model;
using Throng.Core.Validation;
using Throng.Core.World;

namespace Throng.Core.Tests;

/// <summary>Round 006: emission-rule validation, connector portals and copying rules.</summary>
[TestClass]
public sealed class Round006Tests
{
    private static readonly Guid Commuter = TemplateIds.FromKey("agent.commuter");

    private static (Study Study, Region Spawn, Region Sink) OneLevel()
    {
        var study = new Study();
        var level = new Level { Name = "Ground" };
        study.Levels.Add(level);
        study.Regions.Add(new Region { LevelId = level.Id, Kind = RegionKind.Walkable, Outline = ShapeFactory.Rectangle(new(0, 0), new(20, 10)) });
        var spawn = new Region { LevelId = level.Id, Name = "In", Kind = RegionKind.Zone, ZoneKind = ZoneKind.Spawn, Outline = ShapeFactory.Rectangle(new(0, 0), new(2, 10)) };
        var sink = new Region { LevelId = level.Id, Name = "Out", Kind = RegionKind.Zone, ZoneKind = ZoneKind.Sink, Outline = ShapeFactory.Rectangle(new(18, 0), new(20, 10)) };
        study.Regions.AddRange([spawn, sink]);
        return (study, spawn, sink);
    }

    private static IEnumerable<ValidationMessage> Demand(Study study) =>
        StudyValidator.Validate(study).Where(m => m.Message.Contains("rule", StringComparison.Ordinal));

    [TestMethod]
    public void ValidRule_HasNoFindings()
    {
        var (study, spawn, sink) = OneLevel();
        spawn.EmissionRules.Add(new EmissionRule { ProfileId = Commuter, DestinationMode = DestinationMode.ChosenSinks, Destinations = [new SinkWeight { SinkId = sink.Id }] });
        Assert.AreEqual(0, Demand(study).Count());
    }

    [TestMethod]
    public void MissingSink_IsAnError()
    {
        var (study, spawn, _) = OneLevel();
        spawn.EmissionRules.Add(new EmissionRule { ProfileId = Commuter, DestinationMode = DestinationMode.ChosenSinks, Destinations = [new SinkWeight { SinkId = Guid.NewGuid() }] });
        Assert.IsTrue(Demand(study).Any(m => m.Severity == ValidationSeverity.Error && m.Message.Contains("no longer exists", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void NearestSink_WithNoOtherSink_IsAnError()
    {
        var (study, spawn, sink) = OneLevel();
        study.Regions.Remove(sink);
        spawn.ZoneKind = ZoneKind.SpawnSink;
        spawn.EmissionRules.Add(new EmissionRule { ProfileId = Commuter, DestinationMode = DestinationMode.NearestSink });
        Assert.IsTrue(Demand(study).Any(m => m.Severity == ValidationSeverity.Error));
    }

    [TestMethod]
    public void ZeroRate_RulesOnANonSpawn_AndUnknownProfiles_AreWarnings()
    {
        var (study, spawn, sink) = OneLevel();
        spawn.EmissionRules.Add(new EmissionRule { ProfileId = Guid.NewGuid(), RatePerMinute = 0, DestinationMode = DestinationMode.NearestSink });
        sink.EmissionRules.Add(new EmissionRule { ProfileId = Commuter });
        List<ValidationMessage> found = [.. Demand(study)];
        Assert.IsTrue(found.All(m => m.Severity == ValidationSeverity.Warning), string.Join("\n", found.Select(m => m.Message)));
        Assert.IsTrue(found.Any(m => m.Message.Contains("rate of zero", StringComparison.Ordinal)));
        Assert.IsTrue(found.Any(m => m.Message.Contains("Commuter is used instead", StringComparison.Ordinal)));
        Assert.IsTrue(found.Any(m => m.Message.Contains("isn't a spawn zone", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void CopiedRegion_GetsNewRuleIds()
    {
        var (_, spawn, sink) = OneLevel();
        spawn.EmissionRules.Add(new EmissionRule { ProfileId = Commuter, Destinations = [new SinkWeight { SinkId = sink.Id }] });
        Region copy = RegionFactory.Copy(spawn, spawn.LevelId, Point2.Zero);
        Assert.AreEqual(1, copy.EmissionRules.Count);
        Assert.AreNotEqual(spawn.EmissionRules[0].Id, copy.EmissionRules[0].Id);
        Assert.AreEqual(sink.Id, copy.EmissionRules[0].Destinations[0].SinkId);
    }

    [TestMethod]
    public void StraightStair_OpensOntoBothFloors_AndCompilesPortals()
    {
        var study = new Study();
        var ground = new Level { Name = "Ground", ElevationM = 0 };
        var upper = new Level { Name = "Upper", ElevationM = 4 };
        study.Levels.AddRange([ground, upper]);
        study.Regions.Add(new Region { LevelId = ground.Id, Kind = RegionKind.Walkable, Outline = ShapeFactory.Rectangle(new(0, 0), new(30, 10)) });
        study.Regions.Add(new Region { LevelId = upper.Id, Kind = RegionKind.Walkable, Outline = ShapeFactory.Rectangle(new(0, 0), new(30, 10)) });
        var template = (ConnectorTemplate)BuiltInLibrary.Instance.FindByKey("connector.stair-1500")!;
        Connector stair = ConnectorFactory.FromTemplate(study, template, ground.Id, upper.Id, new Point2(5, 5), 0, TurnSide.Left)[0];
        study.Connectors.Add(stair);

        ConnectorLayout layout = ConnectorLayout.Compute(stair, study).Value!;
        IReadOnlyList<FloorEnd> ends = ConnectorPortals.FloorEnds(study, stair, layout);
        Assert.AreEqual(2, ends.Count);
        Assert.IsTrue(ends.All(e => e.IsServed));
        Assert.AreEqual(1, ends.Count(e => e.IsLowerEnd));
        Assert.AreEqual(layout.Pieces.Count - 1, ConnectorPortals.Joints(layout, 0).Count, "Each piece joins the next.");

        WorldModel world = WorldCompiler.Compile(study, ClipperPolygonOps.Instance).Value!;
        Assert.AreEqual(layout.Pieces.Count - 1 + 2, world.Portals.Count);
        Assert.IsTrue(world.Portals.Count(p => p.SurfaceA == ground.Id || p.SurfaceA == upper.Id) == 2);
    }

    [TestMethod]
    public void Switchback_JoinsBothRunsToTheHalfLanding()
    {
        var study = new Study();
        var ground = new Level { Name = "Ground", ElevationM = 0 };
        var upper = new Level { Name = "Upper", ElevationM = 3 };
        study.Levels.AddRange([ground, upper]);
        var template = (ConnectorTemplate)BuiltInLibrary.Instance.FindByKey("connector.stair-switchback-1500")!;
        Connector stair = ConnectorFactory.FromTemplate(study, template, ground.Id, upper.Id, new Point2(0, 0), 0, TurnSide.Left)[0];
        ConnectorLayout layout = ConnectorLayout.Compute(stair, study).Value!;

        // Run 1 → half landing ← run 2: two joints, and the two run ends on the floors.
        Assert.AreEqual(2, ConnectorPortals.Joints(layout, 0).Count);
        Assert.AreEqual(2, ConnectorPortals.FloorEnds(study, stair, layout).Count);
    }

    [TestMethod]
    public void Escalator_EntryAndExitFollowTheBelt()
    {
        var study = new Study();
        var ground = new Level { Name = "Ground", ElevationM = 0 };
        var upper = new Level { Name = "Upper", ElevationM = 5 };
        study.Levels.AddRange([ground, upper]);
        var template = (ConnectorTemplate)BuiltInLibrary.Instance.FindByKey("connector.escalator-down")!;
        Connector down = ConnectorFactory.FromTemplate(study, template, ground.Id, upper.Id, new Point2(0, 0), 0, TurnSide.Left)[0];
        ConnectorLayout layout = ConnectorLayout.Compute(down, study).Value!;
        IReadOnlyList<FloorEnd> ends = ConnectorPortals.FloorEnds(study, down, layout);
        FloorEnd bottom = ends.Single(e => e.IsLowerEnd);
        FloorEnd top = ends.Single(e => !e.IsLowerEnd);
        Assert.IsTrue(ConnectorPortals.AllowsEntry(down, top) && !ConnectorPortals.AllowsExit(down, top));
        Assert.IsTrue(ConnectorPortals.AllowsExit(down, bottom) && !ConnectorPortals.AllowsEntry(down, bottom));
    }

    [TestMethod]
    public void StaffOnly_IsCopied()
    {
        var connector = new Connector { StaffOnly = true };
        Assert.IsTrue(connector.Clone().StaffOnly);
    }
}
