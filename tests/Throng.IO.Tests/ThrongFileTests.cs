using System.IO.Compression;
using System.Text;
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
    public void Demo_CompilesToOneFloorSurfacePerLevel()
    {
        Study demo = DemoStudyBuilder.Build();
        var world = WorldCompiler.Compile(demo);
        Assert.IsTrue(world.IsSuccess, world.Error);
        Assert.AreEqual(3, world.Value!.Surfaces.Count);
        Assert.IsTrue(world.Value.Surfaces.All(s => s.Kind == SurfaceKind.Floor && s.Walkable.Count > 0));

        Surface concourse = world.Value.Surfaces.Single(s => s.Name == "Concourse");
        Assert.AreEqual(-5.0, concourse.ToWorld(10, 10).Z, 1e-9);
    }

    [TestMethod]
    public void Demo_IsDeterministicApartFromIds()
    {
        Study a = DemoStudyBuilder.Build();
        Study b = DemoStudyBuilder.Build();
        CollectionAssert.AreEqual(a.Regions.Select(r => r.Name).ToList(), b.Regions.Select(r => r.Name).ToList());
    }
}
