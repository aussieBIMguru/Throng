using System.IO.Compression;
using System.Text;
using Throng.Core.Geometry;
using Throng.Core.Library;
using Throng.Core.Model;
using Throng.Core.Validation;
using Throng.Core.World;

namespace Throng.IO.Tests;

[TestClass]
public sealed class ThrongFileTests
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
    public void Demo_RoundTripsThroughFile()
    {
        Study demo = DemoStudyBuilder.Build();
        string path = Path.Combine(_folder, "demo" + ThrongFile.Extension);

        Assert.IsTrue(ThrongFile.Save(demo, path).IsSuccess);
        Assert.IsTrue(File.Exists(path));
        Assert.IsFalse(File.Exists(path + ".tmp"), "Temp file is swapped into place.");

        var loaded = ThrongFile.Load(path);
        Assert.IsTrue(loaded.IsSuccess, loaded.Error);
        Study study = loaded.Value!;

        Assert.AreEqual(demo.Id, study.Id);
        Assert.AreEqual(demo.Name, study.Name);
        Assert.AreEqual(demo.Settings.Seed, study.Settings.Seed);
        Assert.AreEqual(demo.Settings.StartTimeOfDay, study.Settings.StartTimeOfDay);
        Assert.AreEqual(demo.Levels.Count, study.Levels.Count);
        Assert.AreEqual(demo.Regions.Count, study.Regions.Count);
        Assert.AreEqual(demo.Library.Count, study.Library.Count);

        Region original = demo.Regions[0];
        Region copy = study.Regions.Single(r => r.Id == original.Id);
        CollectionAssert.AreEqual(original.Outline, copy.Outline);
        Assert.AreEqual(original.Kind, copy.Kind);
        Assert.AreEqual(demo.Library.AgentProfiles[0].Speed.Mean, study.Library.AgentProfiles[0].Speed.Mean);

        // Format v2 content (D-064).
        Assert.AreEqual(demo.Connectors.Count, study.Connectors.Count);
        Assert.AreEqual(demo.Shafts.Count, study.Shafts.Count);
        Connector stair = demo.Connectors[0];
        Connector stairCopy = study.Connectors.Single(c => c.Id == stair.Id);
        Assert.AreEqual(stair.BottomLanding, stairCopy.BottomLanding);
        Assert.AreEqual(stair.Kind, stairCopy.Kind);
        Assert.AreEqual(stair.Shape, stairCopy.Shape);
        Assert.AreEqual(stair.PairId, stairCopy.PairId);
        CollectionAssert.AreEqual(demo.Shafts[0].Outline, study.Shafts[0].Outline);
    }

    [TestMethod]
    public void Load_FormatV1_MigratesWithEmptyConnectorsAndShafts()
    {
        string path = Path.Combine(_folder, "old.throng");
        Level level = new() { Name = "Ground" };
        string study = $"{{\"name\":\"Old\",\"levels\":[{{\"id\":\"{level.Id}\",\"name\":\"Ground\"}}],\"regions\":[]}}";
        WriteZip(path, ("manifest.json", "{\"format\":\"throng-study\",\"formatVersion\":1}"), ("study.json", study));

        var result = ThrongFile.Load(path);
        Assert.IsTrue(result.IsSuccess, result.Error);
        Assert.AreEqual("Old", result.Value!.Name);
        Assert.IsNotNull(result.Value.Connectors);
        Assert.IsNotNull(result.Value.Shafts);
        Assert.AreEqual(0, result.Value.Connectors.Count);
        Assert.AreEqual(0, result.Value.Shafts.Count);
    }

    [TestMethod]
    public void Save_WritesFormatV10()
    {
        string path = Path.Combine(_folder, "v10" + ThrongFile.Extension);
        Assert.IsTrue(ThrongFile.Save(DemoStudyBuilder.Build(), path).IsSuccess);
        Assert.AreEqual(10, ThrongFile.ReadManifest(path).Value!.FormatVersion);
    }

    [TestMethod]
    public void V3_RoundTripsDevicesSpansAndUnderlayImages()
    {
        Study demo = DemoStudyBuilder.Build();
        var underlay = new Underlay { Name = "Plan", LevelId = demo.Levels[0].Id, PixelWidth = 2, PixelHeight = 1, MetresPerPixel = 0.5 };
        underlay.ImageEntry = ThrongFile.UnderlayEntryName(underlay.Id, "plan.png");
        demo.Underlays.Add(underlay);
        demo.UnderlayImages[underlay.Id] = [1, 2, 3, 4, 5];
        string path = Path.Combine(_folder, "rt3" + ThrongFile.Extension);
        Assert.IsTrue(ThrongFile.Save(demo, path).IsSuccess);

        Study study = ThrongFile.Load(path).Value!;
        Assert.AreEqual(demo.Devices.Count, study.Devices.Count);
        Assert.AreEqual(demo.Devices[0].Position, study.Devices[0].Position);
        Region core = study.Regions.Single(r => r.Name == "Lift core walls");
        Assert.IsNotNull(core.TopLevelId);
        Assert.AreEqual(RegionHeightMode.FullStorey, core.HeightMode);
        Assert.IsTrue(study.Connectors.Any(c => c.PerLevelCircuits));
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4, 5 }, study.UnderlayImages[underlay.Id]);
        Assert.AreEqual(0.5, study.Underlays.Single().MetresPerPixel);
    }

    [TestMethod]
    public void Load_FormatV2_KeepsOldBehaviour()
    {
        string path = Path.Combine(_folder, "v2.throng");
        Level a = new() { Name = "A", ElevationM = 0 };
        string study = $"{{\"levels\":[{{\"id\":\"{a.Id}\"}}],\"regions\":[{{\"levelId\":\"{a.Id}\",\"kind\":\"Wall\",\"outline\":[{{\"x\":0,\"y\":0}},{{\"x\":1,\"y\":0}}]}}],\"connectors\":[{{\"kind\":\"Stair\",\"shape\":\"Switchback\"}}]}}";
        WriteZip(path, ("manifest.json", "{\"format\":\"throng-study\",\"formatVersion\":2}"), ("study.json", study));

        Study loaded = ThrongFile.Load(path).Value!;
        Assert.AreEqual(RegionHeightMode.Fixed, loaded.Regions[0].HeightMode, "Old walls keep their fixed height.");
        Assert.IsNull(loaded.Regions[0].TopLevelId);
        Assert.IsFalse(loaded.Connectors[0].PerLevelCircuits, "Old switchbacks stay one long run.");
        Assert.AreEqual(0, loaded.Devices.Count);
    }

    [TestMethod]
    public void Save_PreservesCreatedDate()
    {
        string path = Path.Combine(_folder, "dates" + ThrongFile.Extension);
        var created = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        Assert.IsTrue(ThrongFile.Save(Study.CreateDefault(), path, created).IsSuccess);

        var manifest = ThrongFile.ReadManifest(path);
        Assert.IsTrue(manifest.IsSuccess);
        Assert.AreEqual(created, manifest.Value!.CreatedUtc);
        Assert.AreEqual(ThrongFile.CurrentFormatVersion, manifest.Value.FormatVersion);
    }

    [TestMethod]
    public void Load_MissingFile_FailsWithoutThrowing()
    {
        var result = ThrongFile.Load(Path.Combine(_folder, "nope.throng"));
        Assert.IsFalse(result.IsSuccess);
        Assert.IsFalse(string.IsNullOrEmpty(result.Error));
    }

    [TestMethod]
    public void Load_NotAZip_FailsWithoutThrowing()
    {
        string path = Path.Combine(_folder, "junk.throng");
        File.WriteAllText(path, "this is not a zip");
        Assert.IsFalse(ThrongFile.Load(path).IsSuccess);
    }

    [TestMethod]
    public void Load_ZipWithoutManifest_IsRejected()
    {
        string path = Path.Combine(_folder, "other.throng");
        WriteZip(path, ("readme.txt", "hello"));
        Assert.IsFalse(ThrongFile.Load(path).IsSuccess);
    }

    [TestMethod]
    public void Load_NewerFormat_IsRejectedWithAHelpfulMessage()
    {
        string path = Path.Combine(_folder, "future.throng");
        WriteZip(path, ("manifest.json", "{\"format\":\"throng-study\",\"formatVersion\":999}"), ("study.json", "{}"));
        var result = ThrongFile.Load(path);
        Assert.IsFalse(result.IsSuccess);
        StringAssert.Contains(result.Error, "newer version");
    }

    private static void WriteZip(string path, params (string Name, string Content)[] entries)
    {
        using FileStream stream = File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach ((string name, string content) in entries)
        {
            using Stream entry = zip.CreateEntry(name).Open();
            byte[] bytes = Encoding.UTF8.GetBytes(content);
            entry.Write(bytes, 0, bytes.Length);
        }
    }
}

[TestClass]
public sealed class DemoStudyTests
{
    [TestMethod]
    public void Demo_HasThreeLevels_AndValidatesClean()
    {
        Study demo = DemoStudyBuilder.Build();
        Assert.AreEqual(3, demo.Levels.Count);
        Assert.AreEqual(DemoStudyBuilder.DemoSeed, demo.Settings.Seed);

        IReadOnlyList<ValidationMessage> findings = StudyValidator.Validate(demo);
        Assert.AreEqual(0, findings.Count, string.Join("; ", findings.Select(f => f.Message)));
    }

    [TestMethod]
    public void Demo_EmbedsTheTemplatesItUses()
    {
        Study demo = DemoStudyBuilder.Build();
        Assert.AreEqual(5, demo.Library.AgentProfiles.Count);
        Assert.AreEqual(5, demo.Library.Schedules.Count);
        Assert.AreEqual(3, demo.Library.Journeys.Count);
        Assert.IsTrue(demo.Library.All().All(t => t.Source == Core.Library.TemplateSource.Embedded));
    }

    [TestMethod]
    public void Demo_HasEveryRegionKind()
    {
        Study demo = DemoStudyBuilder.Build();
        foreach (RegionKind kind in Enum.GetValues<RegionKind>())
        {
            Assert.IsTrue(demo.Regions.Any(r => r.Kind == kind), kind.ToString());
        }
    }

    [TestMethod]
    public void Demo_CompilesToOneFloorSurfacePerLevelPlusConnectorPieces()
    {
        Study demo = DemoStudyBuilder.Build();
        var world = WorldCompiler.Compile(demo);
        Assert.IsTrue(world.IsSuccess, world.Error);
        List<Surface> floors = [.. world.Value!.Surfaces.Where(s => s.Kind == SurfaceKind.Floor)];
        Assert.AreEqual(3, floors.Count);
        Assert.IsTrue(floors.All(s => s.Walkable.Count > 0));

        Surface concourse = floors.Single(s => s.Name == "Concourse");
        Assert.AreEqual(-5.0, concourse.ToWorld(10, 10).Z, 1e-9);

        // Every connector piece is a surface whose far end meets the next piece's height (§7.1).
        List<Surface> pieces = [.. world.Value.Surfaces.Where(s => s.Kind == SurfaceKind.Connector)];
        Assert.IsTrue(pieces.Count >= demo.Connectors.Count * 3);
        Surface firstFlight = pieces.First(s => s.SourceId == demo.Connectors[0].Id);
        Assert.AreEqual(-5.0, firstFlight.ToWorld(0, 0).Z, 1e-9, "Street stair starts on the concourse.");
    }

    [TestMethod]
    public void Demo_HasItsVerticalSet()
    {
        Study demo = DemoStudyBuilder.Build();
        Assert.AreEqual(7, demo.Connectors.Count, "Three stairs and two escalator pairs.");
        Assert.AreEqual(4, demo.Connectors.Count(c => c.Kind == ConnectorKind.Escalator));
        Assert.AreEqual(2, demo.Connectors.Count(c => c.Shape == ConnectorShape.Switchback));
        Assert.AreEqual(2, demo.Shafts.Count);
        Assert.IsTrue(demo.Connectors.Any(c => ConnectorLayout.Compute(c, demo).Value!.Circuits.Count == 2), "The back-of-house core has one circuit per storey (three levels, two storeys).");
        Assert.IsTrue(demo.Shafts.All(s => s.Lift is not null && s.NoStopLevelIds.Count == 0), "Two accessible lifts, one per level change; the goods shaft is gone (D-246).");
        Assert.IsTrue(demo.Regions.Any(r => r.TopLevelId is not null && r.Kind == RegionKind.Wall), "Lift core walls span two levels.");
        Assert.AreEqual(13, demo.Devices.Count, "Eight ticket machines (D-155), two counters, two gates, the info desk.");
        Assert.IsFalse(demo.Regions.Any(r => string.Equals(r.Tag, "Void", StringComparison.OrdinalIgnoreCase)), "Auto openings replace the voids (D-061).");
        Assert.IsTrue(demo.Connectors.All(c => c.TemplateId is { } id && demo.Library.Find(id) is not null), "Presets are embedded.");
    }

    [TestMethod]
    public void Demo_IsDeterministicApartFromIds()
    {
        Study a = DemoStudyBuilder.Build();
        Study b = DemoStudyBuilder.Build();
        CollectionAssert.AreEqual(a.Regions.Select(r => r.Name).ToList(), b.Regions.Select(r => r.Name).ToList());
    }
}
