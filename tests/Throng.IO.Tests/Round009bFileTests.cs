using Throng.Core.Geometry;
using Throng.Core.Library;
using Throng.Core.Model;

namespace Throng.IO.Tests;

/// <summary>Round 009b: its new fields are additive, so files stay on format v6 (D-169) and round-trip.</summary>
[TestClass]
public sealed class Round009bFileTests
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
    public void Save_DuplicatedJourneyGiveUpPreferencesAndQueuePath_RoundTripOnFormatV6()
    {
        var study = new Study { Name = "Round trip" };
        var level = new Level { Name = "Ground" };
        study.Levels.Add(level);
        var builtIn = (JourneyTemplate)BuiltInLibrary.Instance.FindByKey("journey.ticket-gates")!;
        JourneyTemplate copy = StudyTemplates.CreateJourneyCopy(study, builtIn);
        copy.Steps = [copy.Steps[0] with { OnGiveUp = JourneyGiveUp.JumpTo, GiveUpStep = 2 }, copy.Steps[1] with { OnGiveUp = JourneyGiveUp.Leave }, copy.Steps[2]];
        study.Library.Add(copy);
        var profile = new AgentProfileTemplate
        {
            Key = "agent.test", Name = "Test", Category = "Agents",
            DevicePreferences = [new DevicePreference { Target = BuiltInLibrary.AccessibleGateKey, BonusS = 12.5 }],
        };
        study.Library.Add(profile);
        study.Devices.Add(new Device { LevelId = level.Id, TypeTag = "Gate", QueuePath = [new(1, 2), new(1, 6), new(4, 6)] });

        string path = Path.Combine(_folder, "rt.throng");
        Assert.IsTrue(ThrongFile.Save(study, path).IsSuccess);
        Assert.AreEqual(6, ThrongFile.ReadManifest(path).Value!.FormatVersion, "009b stays on format v6 (D-169).");

        Study loaded = ThrongFile.Load(path).Value!;
        var journey = (JourneyTemplate)loaded.Library.Find(copy.Id)!;
        Assert.AreEqual(copy.Name, journey.Name);
        Assert.AreEqual(copy.Key, journey.Key);
        Assert.AreEqual(JourneyGiveUp.JumpTo, journey.Steps[0].OnGiveUp);
        Assert.AreEqual(2, journey.Steps[0].GiveUpStep);
        Assert.AreEqual(JourneyGiveUp.Leave, journey.Steps[1].OnGiveUp);
        Assert.AreEqual(JourneyGiveUp.KeepWaiting, journey.Steps[2].OnGiveUp, "The default for steps saved without it.");
        Assert.IsTrue(StudyTemplates.IsEditableJourney(loaded, journey), "A duplicate stays editable after reopening.");

        var loadedProfile = (AgentProfileTemplate)loaded.Library.Find(profile.Id)!;
        Assert.AreEqual(12.5, loadedProfile.DeviceBonusS(BuiltInLibrary.AccessibleGateKey, "Gate"));
        CollectionAssert.AreEqual(new List<Point2> { new(1, 2), new(1, 6), new(4, 6) }, loaded.Devices[0].QueuePath);
    }
}
