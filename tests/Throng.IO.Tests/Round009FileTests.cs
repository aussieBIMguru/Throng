using System.IO.Compression;
using System.Text;
using Throng.Core.Library;
using Throng.Core.Model;
using Throng.Core.Validation;

namespace Throng.IO.Tests;

/// <summary>Round 009: file format v6 carries mix lines, journeys and device queue settings (D-151, D-153, D-157).</summary>
[TestClass]
public sealed class Round009FileTests
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
    public void Load_FormatV5_MovesTheProfileIntoOneMixLine()
    {
        string path = Path.Combine(_folder, "v5.throng");
        Level a = new() { Name = "A", ElevationM = 0 };
        Guid commuter = TemplateIds.FromKey("agent.commuter");
        string study =
            $"{{\"levels\":[{{\"id\":\"{a.Id}\"}}]," +
            $"\"regions\":[{{\"levelId\":\"{a.Id}\",\"kind\":\"Zone\",\"zoneKind\":\"SpawnSink\",\"outline\":[{{\"x\":0,\"y\":0}},{{\"x\":1,\"y\":0}},{{\"x\":1,\"y\":1}}]," +
            $"\"emissionRules\":[{{\"profileId\":\"{commuter}\",\"ratePerMinute\":5}}]}}]," +
            $"\"devices\":[{{\"levelId\":\"{a.Id}\",\"typeTag\":\"Gate\"}}]}}";
        WriteZip(path, ("manifest.json", "{\"format\":\"throng-study\",\"formatVersion\":5}"), ("study.json", study));

        Study loaded = ThrongFile.Load(path).Value!;
        EmissionRule rule = loaded.Regions[0].EmissionRules.Single();
        Assert.AreEqual(1, rule.Mix.Count);
        Assert.AreEqual(commuter, rule.Mix[0].ProfileId);
        Assert.IsNull(rule.Mix[0].JourneyId, "Old rules walk straight to a sink.");
        Assert.AreEqual(1.0, rule.Mix[0].Share);
        Assert.IsNull(rule.LegacyProfileId, "The old field is cleared after the move.");
        Assert.AreEqual(5.0, rule.RatePerMinute);
        Assert.AreEqual(Device.DefaultMaxQueue, loaded.Devices[0].MaxQueue);
        Assert.AreEqual(0, loaded.Devices[0].QueuePath.Count);
        Assert.IsNotNull(loaded.Library.Journeys);
    }

    [TestMethod]
    public void V6_RoundTripsMixLinesJourneysAndQueues()
    {
        Study demo = DemoStudyBuilder.Build();
        Device machine = demo.Devices.First(d => d.TypeTag == "TicketMachine");
        machine.MaxQueue = 7;
        machine.QueuePath = [new(5, 1), new(5, 4), new(8, 4)];
        string path = Path.Combine(_folder, "v6" + ThrongFile.Extension);
        Assert.IsTrue(ThrongFile.Save(demo, path).IsSuccess);

        Study loaded = ThrongFile.Load(path).Value!;
        Region west = demo.Regions.Single(r => r.Name == "West entrance");
        EmissionRule before = west.EmissionRules[0];
        EmissionRule after = loaded.FindRegion(west.Id)!.EmissionRules[0];
        Assert.AreEqual(before.Mix.Count, after.Mix.Count);
        for (int i = 0; i < before.Mix.Count; i++)
        {
            Assert.AreEqual(before.Mix[i].ProfileId, after.Mix[i].ProfileId);
            Assert.AreEqual(before.Mix[i].JourneyId, after.Mix[i].JourneyId);
            Assert.AreEqual(before.Mix[i].Share, after.Mix[i].Share);
            CollectionAssert.AreEqual(before.Mix[i].SpawnTags, after.Mix[i].SpawnTags);
        }

        Device machineLoaded = loaded.Devices.Single(d => d.Id == machine.Id);
        Assert.AreEqual(7, machineLoaded.MaxQueue);
        CollectionAssert.AreEqual(machine.QueuePath, machineLoaded.QueuePath);

        JourneyTemplate journey = loaded.Library.Journeys.Single(j => j.Key == "journey.ticket-gates");
        Assert.AreEqual(3, journey.Steps.Count);
        Assert.AreEqual("ticket", journey.Steps[0].LacksTag);
        Assert.AreEqual("ticket", journey.Steps[0].GrantsTag);
        Assert.AreEqual(0, StudyValidator.Validate(loaded).Count, "Reloaded demo still validates clean.");
    }

    [TestMethod]
    public void Demo_UsesJourneysAndTagsPerD155()
    {
        Study demo = DemoStudyBuilder.Build();
        EmissionRule entrance = demo.Regions.Single(r => r.Name == "West entrance").EmissionRules.Single();
        Assert.AreEqual(100.0, entrance.Mix.Sum(m => m.Share), 1e-9);
        Assert.AreEqual(70.0, entrance.Mix.Where(m => m.ProfileId == TemplateIds.FromKey("agent.commuter")).Sum(m => m.Share), 1e-9);
        Assert.IsTrue(entrance.Mix.All(m => m.JourneyId is not null));
        Assert.IsTrue(demo.Regions.Any(r => r.ZoneKind == ZoneKind.Dwell && r.Tag == DemoStudyBuilder.RetailTag && demo.FindLevel(r.LevelId)!.Name == "Concourse"));
        Assert.AreEqual(8, demo.Devices.Count(d => d.TypeTag == "TicketMachine"));
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
