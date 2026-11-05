using Throng.Core.Geometry;
using Throng.Core.Model;
using Throng.Core.Validation;
using Throng.Core.World;

namespace Throng.IO.Tests;

/// <summary>Round 006: file format v4 and the demo's demand and connector portals.</summary>
[TestClass]
public sealed class Round006FileTests
{
    private string _folder = string.Empty;

    [TestInitialize]
    public void CreateFolder()
    {
        _folder = Path.Combine(Path.GetTempPath(), "throng-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    [TestCleanup]
    public void DeleteFolder()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // Temp cleanup is best effort.
        }
    }

    [TestMethod]
    public void V4_RoundTripsRulesKeepSideAndStaffOnly()
    {
        Study demo = DemoStudyBuilder.Build();
        demo.Settings.KeepSide = KeepSide.Right;
        demo.Settings.EndCondition = StudyEndCondition.Indefinite;
        string path = Path.Combine(_folder, "v4" + ThrongFile.Extension);
        Assert.IsTrue(ThrongFile.Save(demo, path).IsSuccess);

        Study loaded = ThrongFile.Load(path).Value!;
        Assert.AreEqual(KeepSide.Right, loaded.Settings.KeepSide);
        Assert.AreEqual(StudyEndCondition.Indefinite, loaded.Settings.EndCondition);
        Assert.AreEqual(demo.Connectors.Count(c => c.StaffOnly), loaded.Connectors.Count(c => c.StaffOnly));

        Region west = demo.Regions.Single(r => r.Name == "West entrance");
        Region westLoaded = loaded.FindRegion(west.Id)!;
        Assert.AreEqual(west.EmissionRules.Count, westLoaded.EmissionRules.Count);
        for (int i = 0; i < west.EmissionRules.Count; i++)
        {
            EmissionRule a = west.EmissionRules[i];
            EmissionRule b = westLoaded.EmissionRules[i];
            Assert.AreEqual(a.Id, b.Id);
            Assert.AreEqual(a.Mix.Count, b.Mix.Count);
            Assert.AreEqual(a.Mix[0].ProfileId, b.Mix[0].ProfileId);
            Assert.AreEqual(a.RatePerMinute, b.RatePerMinute);
            Assert.AreEqual(a.Pattern, b.Pattern);
            Assert.AreEqual(a.DestinationMode, b.DestinationMode);
            CollectionAssert.AreEqual(a.Destinations, b.Destinations);
        }
    }

    [TestMethod]
    public void Demo_HasFlowsBetweenStreetAndPlatform()
    {
        Study demo = DemoStudyBuilder.Build();
        List<Region> spawns = [.. demo.Regions.Where(r => r.EmissionRules.Count > 0)];
        Assert.AreEqual(3 + 12, spawns.Count, "Three street entrances and twelve platform doors.");
        Assert.IsTrue(spawns.All(r => r.IsSpawn));
        Assert.AreEqual(0, StudyValidator.Validate(demo).Count, "Demo still validates clean.");
    }

    [TestMethod]
    public void Demo_BackOfHouseStair_ServesEveryLevel_UnlessExcluded()
    {
        Study demo = DemoStudyBuilder.Build();
        Connector stair = demo.Connectors.Single(c => c.Name == "Back-of-house stair");
        Assert.IsTrue(stair.StaffOnly, "Staff only (D-110).");
        Level concourse = demo.Levels.Single(l => l.Name == "Concourse");

        ConnectorLayout layout = ConnectorLayout.Compute(stair, demo).Value!;
        IReadOnlyList<FloorEnd> ends = ConnectorPortals.FloorEnds(demo, stair, layout);
        Assert.AreEqual(4, ends.Count, "Platform start, concourse exit and start, street exit.");
        Assert.IsTrue(ends.All(e => e.IsServed));

        stair.ExcludedLevelIds = [concourse.Id];
        ends = ConnectorPortals.FloorEnds(demo, stair, layout);
        Assert.AreEqual(4, ends.Count);
        Assert.AreEqual(2, ends.Count(e => !e.IsServed && e.LevelId == concourse.Id), "Pass-over ends at the excluded level.");
    }

    [TestMethod]
    public void Demo_CompiledWorld_HasPortalsForEveryConnector()
    {
        Study demo = DemoStudyBuilder.Build();
        WorldModel world = WorldCompiler.Compile(demo, ClipperPolygonOps.Instance).Value!;
        foreach (Connector connector in demo.Connectors)
        {
            int floorPortals = world.Portals.Count(p => p.ConnectorId == connector.Id && demo.FindLevel(p.SurfaceA) is not null);
            Assert.IsTrue(floorPortals >= 2, $"'{connector.Name}' has {floorPortals} floor portals.");
        }
    }
}
