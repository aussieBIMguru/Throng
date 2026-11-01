using System.IO.Compression;
using System.Text.Json;
using Throng.Core.Library;
using Throng.Core.Model;
using Throng.Core.Serialization;
using Throng.Core.Validation;

namespace Throng.IO.Tests;

/// <summary>Round 024a: format v12 (D-382, D-385, D-386): group templates and group mix lines; the old Family profile goes.</summary>
[TestClass]
public sealed class Round024FileTests
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
    public void V11File_FamilyProfileLines_BecomeFamilyGroupLines_AndTheProfileGoes()
    {
        Study demo = DemoStudyBuilder.Build();
        Guid oldFamily = TemplateIds.FromKey(BuiltInLibrary.RemovedFamilyProfileKey);
        demo.Library.Add(new AgentProfileTemplate { Id = oldFamily, Key = BuiltInLibrary.RemovedFamilyProfileKey, Name = "Family / group", Source = TemplateSource.Embedded, RadiusM = 0.35 });
        Region west = demo.Regions.Single(r => r.Name == "West entrance");
        west.EmissionRules[0] = west.EmissionRules[0] with
        {
            Mix = [.. west.EmissionRules[0].Mix.Where(m => !m.IsGroup), MixLine.Of(oldFamily, TemplateIds.FromKey("journey.ticket-gates"), 5, SpawnTag.Of("ticket", 50))],
        };

        string path = Path.Combine(_folder, "v11" + ThrongFile.Extension);
        Assert.IsTrue(ThrongFile.Save(demo, path).IsSuccess);
        using (ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            ThrongManifest manifest = JsonSerializer.Deserialize<ThrongManifest>(Read(zip, "manifest.json"), JsonDefaults.Options)!;
            manifest.FormatVersion = 11;
            Rewrite(zip, "manifest.json", JsonSerializer.Serialize(manifest, JsonDefaults.Options));
        }

        Study loaded = ThrongFile.Load(path).Value!;
        MixLine line = loaded.Regions.Single(r => r.Name == "West entrance").EmissionRules[0].Mix.Single(m => m.Share == 5);
        Assert.AreEqual(TemplateIds.FromKey(BuiltInLibrary.FamilyGroupKey), line.GroupId, "Family profile lines become Family group lines (D-385).");
        Assert.AreEqual(TemplateIds.FromKey("journey.ticket-gates"), line.JourneyId, "Journey kept.");
        Assert.AreEqual(50, line.SpawnTags.Single().Percent, "Tags kept.");
        Assert.IsNull(loaded.Library.Find(oldFamily), "The study's copy of the old profile is dropped once unused.");
    }

    [TestMethod]
    public void GroupTemplates_AndGroupLines_RoundTrip()
    {
        Study demo = DemoStudyBuilder.Build();
        var mine = new GroupTemplate
        {
            Key = "group.test", Name = "Test trio", Category = "Groups", Source = TemplateSource.User,
            Members = [GroupMember.Of(TemplateIds.FromKey("agent.commuter"), 1, 1), GroupMember.Of(TemplateIds.FromKey("agent.child"), 0, 2)],
            DefaultJourneyId = TemplateIds.FromKey("journey.through-gates"),
        };
        demo.Library.Add(mine);
        Region east = demo.Regions.Single(r => r.Name == "East entrance");
        east.EmissionRules[0] = east.EmissionRules[0] with { Mix = [.. east.EmissionRules[0].Mix, MixLine.OfGroup(mine.Id, share: 3) with { UseProfileJourney = true }] };

        string path = Path.Combine(_folder, "groups" + ThrongFile.Extension);
        Assert.IsTrue(ThrongFile.Save(demo, path).IsSuccess);
        Assert.AreEqual(12, ThrongFile.ReadManifest(path).Value!.FormatVersion);
        Study loaded = ThrongFile.Load(path).Value!;

        GroupTemplate group = loaded.Library.Groups.Single(g => g.Id == mine.Id);
        Assert.AreEqual(2, group.Members.Count);
        Assert.AreEqual(0, group.Members[1].Min);
        Assert.AreEqual(2, group.Members[1].Max);
        MixLine line = loaded.Regions.Single(r => r.Name == "East entrance").EmissionRules[0].Mix.Single(m => m.GroupId == mine.Id);
        Assert.AreEqual(mine.DefaultJourneyId, StudyTemplates.JourneyIdFor(loaded, line), "(profile default) is the group's default journey (D-389).");
        Assert.IsTrue(loaded.Library.Groups.Any(g => g.Key == BuiltInLibrary.FamilyGroupKey), "The demo's Family group is embedded.");
    }

    [TestMethod]
    public void Demos_UseFamilyGroups_AndValidateClean()
    {
        foreach (Study study in new[] { DemoStudyBuilder.Build(), OfficeTowerBuilder.Build() })
        {
            Assert.IsTrue(study.Regions.SelectMany(r => r.EmissionRules).SelectMany(r => r.Mix).Any(m => m.GroupId == TemplateIds.FromKey(BuiltInLibrary.FamilyGroupKey)), study.Name);
            Assert.AreEqual(0, StudyValidator.Validate(study).Count(f => f.Severity is ValidationSeverity.Error or ValidationSeverity.Warning), study.Name);
        }
    }

    private static string Read(ZipArchive zip, string name)
    {
        using var reader = new StreamReader(zip.GetEntry(name)!.Open());
        return reader.ReadToEnd();
    }

    private static void Rewrite(ZipArchive zip, string name, string text)
    {
        zip.GetEntry(name)?.Delete();
        using var writer = new StreamWriter(zip.CreateEntry(name).Open());
        writer.Write(text);
    }
}
