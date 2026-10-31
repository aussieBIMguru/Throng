using System.IO.Compression;
using System.Text.Json;
using Throng.Core.Library;
using Throng.Core.Model;
using Throng.Core.Serialization;

namespace Throng.IO.Tests;

/// <summary>Round 018a: format v11 (D-302, D-304): step lists convert to graphs on load; graphs and defaults round-trip.</summary>
[TestClass]
public sealed class Round018FileTests
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
    public void V10File_JourneysBecomeGraphs_AndEmbeddedProfilesGetTheirDefault()
    {
        Study demo = DemoStudyBuilder.Build();
        string path = Path.Combine(_folder, "old" + ThrongFile.Extension);
        Assert.IsTrue(ThrongFile.Save(demo, path).IsSuccess);

        // Turn it back into a v10 file: step lists in the library, no profile defaults, manifest version 10.
        var library = new TemplateLibrary();
        foreach (TemplateBase template in demo.Library.All())
        {
            TemplateBase copy = TemplateCloner.Clone(template);
            if (copy is AgentProfileTemplate profile)
            {
                profile.DefaultJourneyId = null;
            }

            library.Add(copy);
        }

        JourneyTemplate ticket = library.Journeys.Single(j => j.Key == "journey.ticket-gates");
        ticket.Nodes = [];
        ticket.Wires = [];
        ticket.Steps = [JourneyStep.Device("TicketMachine", lacks: "ticket", grants: "ticket"), JourneyStep.Device("Gate") with { OnGiveUp = JourneyGiveUp.JumpTo, GiveUpStep = 0 }, JourneyStep.ExitStep()];
        using (ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            Rewrite(zip, "library.json", JsonSerializer.Serialize(library, JsonDefaults.Options));
            ThrongManifest manifest = JsonSerializer.Deserialize<ThrongManifest>(Read(zip, "manifest.json"), JsonDefaults.Options)!;
            manifest.FormatVersion = 10;
            Rewrite(zip, "manifest.json", JsonSerializer.Serialize(manifest, JsonDefaults.Options));
        }

        Study loaded = ThrongFile.Load(path).Value!;
        JourneyTemplate journey = loaded.Library.Journeys.Single(j => j.Key == "journey.ticket-gates");
        Assert.IsNull(journey.Steps, "Converted on load (D-302).");
        Assert.AreEqual("ticket", journey.Nodes.Single(n => n.Kind == JourneyNodeKind.TagCheck).TargetTag);
        Assert.AreEqual(JourneyGiveUp.Leave, journey.Nodes.Single(n => n.TargetTag == "Gate").OnGiveUp, "A backward jump leaves (D-306)…");
        StringAssert.Contains(journey.Description, "Converted to a graph", "…and says so in the description.");

        var commuter = (AgentProfileTemplate)loaded.Library.Find(TemplateIds.FromKey("agent.commuter"))!;
        Assert.AreEqual(TemplateIds.FromKey("journey.through-gates"), commuter.DefaultJourneyId, "Embedded built-ins take the built-in's default (D-304).");
        Assert.IsFalse(StudyDefinitions.IsModified(loaded, commuter), "So they don't show as MODIFIED.");
        Assert.IsTrue(loaded.Regions.SelectMany(r => r.EmissionRules).SelectMany(r => r.Mix).All(m => !m.UseProfileJourney), "Mix lines keep their explicit journeys.");
    }

    [TestMethod]
    public void Graphs_DefaultsAndProfileDefaultLines_RoundTripOnV11()
    {
        Study demo = DemoStudyBuilder.Build();
        var builtIn = (JourneyTemplate)BuiltInLibrary.Instance.FindByKey("journey.gates-north-south")!;
        JourneyTemplate copy = StudyTemplates.CreateJourneyCopy(demo, builtIn);
        demo.Library.Add(copy);
        Region west = demo.Regions.Single(r => r.Name == "West entrance");
        west.EmissionRules[0] = west.EmissionRules[0] with { Mix = [.. west.EmissionRules[0].Mix.Select(m => m with { UseProfileJourney = true })] };
        var infrequent = (AgentProfileTemplate)demo.Library.Find(TemplateIds.FromKey("agent.infrequent"))!;
        infrequent.DefaultJourneyId = copy.Id;

        string path = Path.Combine(_folder, "graph" + ThrongFile.Extension);
        Assert.IsTrue(ThrongFile.Save(demo, path).IsSuccess);
        Assert.AreEqual(11, ThrongFile.ReadManifest(path).Value!.FormatVersion);
        Study loaded = ThrongFile.Load(path).Value!;

        var journey = (JourneyTemplate)loaded.Library.Find(copy.Id)!;
        CollectionAssert.AreEqual(copy.Nodes.Select(n => n with { Weights = null }).ToList(), journey.Nodes.Select(n => n with { Weights = null }).ToList(), "Nodes round-trip (positions, kinds, tags).");
        CollectionAssert.AreEqual(copy.Wires, journey.Wires);
        CollectionAssert.AreEqual(new[] { 50.0, 50.0 }, journey.Nodes.Single(n => n.Kind == JourneyNodeKind.Chance).Weights!.ToArray());
        Assert.AreEqual(copy.Id, ((AgentProfileTemplate)loaded.Library.Find(infrequent.Id)!).DefaultJourneyId);
        Assert.IsTrue(loaded.FindRegion(west.Id)!.EmissionRules[0].Mix.All(m => m.UseProfileJourney));
    }

    [TestMethod]
    public void ProfileFile_FormatOneJourneys_ConvertOnImport()
    {
        string path = Path.Combine(_folder, "old" + ProfileFile.Extension);
        var bundle = new ProfileBundle
        {
            FormatVersion = 1,
            Profile = new AgentProfileTemplate { Key = "agent.user.old", Name = "Old" },
            Journeys = [new JourneyTemplate { Key = "journey.user.old", Name = "Old journey", Nodes = [], Steps = [JourneyStep.Device("Gate"), JourneyStep.ExitStep()] }],
        };
        File.WriteAllText(path, JsonSerializer.Serialize(bundle, JsonDefaults.Options));

        ProfileBundle loaded = ProfileFile.Load(path).Value!;
        JourneyTemplate journey = loaded.Journeys.Single();
        Assert.IsNull(journey.Steps);
        Assert.AreEqual("Gate", JourneyGraph.Reachable(journey).Single(n => n.Kind == JourneyNodeKind.Device).TargetTag);
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
