using Throng.Core.Library;
using Throng.Core.Model;
using Throng.Core.Validation;

namespace Throng.IO.Tests;

/// <summary>Round 010b: train services and Board train steps on format v7 (D-184–D-189).</summary>
[TestClass]
public sealed class Round010bFileTests
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
    public void Demo_HasTwoAlternatingServicesOverTheTwelveDoors()
    {
        Study demo = DemoStudyBuilder.Build();
        Assert.AreEqual(2, demo.TrainServices.Count);
        TrainService south = demo.TrainServices.Single(t => t.Tag == "Southbound");
        TrainService north = demo.TrainServices.Single(t => t.Tag == "Northbound");
        Assert.AreEqual(6, south.DoorZoneIds.Count);
        Assert.AreEqual(6, north.DoorZoneIds.Count);
        Assert.AreEqual(360, south.HeadwayS);
        Assert.AreEqual(180, north.OffsetS - south.OffsetS, "Three minutes apart (D-189).");
        Assert.AreEqual(0, StudyValidator.Validate(demo).Count, "Demo still validates clean.");
        Assert.AreEqual(100, south.AlightPerTrain, "Lighter trains (D-196).");
        List<Device> lines = [.. demo.Devices.Where(d => d.PresetKey == "device.gate-line").OrderByDescending(d => d.Position.Y)];
        Assert.AreEqual(1, lines.Count, "One gate line, moved south (D-198).");
        CollectionAssert.AreEqual(
            new[] { GateLaneDirection.FromBack, GateLaneDirection.FromBack, GateLaneDirection.FromFront, GateLaneDirection.FromFront },
            Enumerable.Range(0, lines[0].Lanes).Select(lines[0].DirectionOf).ToArray(),
            "Two lanes in, two out (D-196).");
        Device accessible = demo.Devices.Single(d => d.PresetKey == BuiltInLibrary.AccessibleGateKey);
        Assert.AreEqual(GateLaneDirection.Both, accessible.DirectionOf(0), "Lanes default to both ways.");
    }

    [TestMethod]
    public void TrainServicesAndBoardTrainSteps_RoundTripOnFormatV7()
    {
        Study demo = DemoStudyBuilder.Build();
        demo.TrainServices[0] = demo.TrainServices[0] with { Capacity = 123, RateCurve = [RateCurvePoint.At(0, 0.5), RateCurvePoint.At(30, 1.5)] };
        var journey = new JourneyTemplate { Key = "journey.user.test", Name = "Board south", Category = "Journeys", Steps = [JourneyStep.Device("Gate"), JourneyStep.BoardTrainStep("Southbound")] };
        demo.Library.Add(journey);

        string path = Path.Combine(_folder, "trains" + ThrongFile.Extension);
        Assert.IsTrue(ThrongFile.Save(demo, path).IsSuccess);
        Assert.AreEqual(ThrongFile.CurrentFormatVersion, ThrongFile.ReadManifest(path).Value!.FormatVersion);
        Study loaded = ThrongFile.Load(path).Value!;

        Assert.AreEqual(2, loaded.TrainServices.Count);
        foreach (Device line in demo.Devices.Where(d => d.PresetKey == "device.gate-line"))
        {
            CollectionAssert.AreEqual(line.LaneDirections, loaded.FindDevice(line.Id)!.LaneDirections, "Gate lane directions round-trip (D-196).");
        }
        TrainService before = demo.TrainServices[0], after = loaded.TrainServices[0];
        Assert.AreEqual(before.Id, after.Id);
        Assert.AreEqual(before.Name, after.Name);
        Assert.AreEqual(before.Tag, after.Tag);
        CollectionAssert.AreEqual(before.DoorZoneIds, after.DoorZoneIds);
        Assert.AreEqual(123, after.Capacity);
        Assert.AreEqual(before.DwellS, after.DwellS);
        CollectionAssert.AreEqual(before.RateCurve, after.RateCurve);

        var loadedJourney = (JourneyTemplate)loaded.Library.Find(journey.Id)!;
        Assert.AreEqual(JourneyStepKind.BoardTrain, loadedJourney.Steps[1].Kind);
        Assert.AreEqual("Southbound", loadedJourney.Steps[1].TargetTag);
    }

    [TestMethod]
    public void Validation_CatchesBadTimetablesAndMissingServices()
    {
        Study demo = DemoStudyBuilder.Build();
        demo.TrainServices[0] = demo.TrainServices[0] with { HeadwayS = 50, DwellS = 45 };
        demo.TrainServices.Add(demo.TrainServices[1] with { Id = Guid.NewGuid(), Name = "Copy" });
        IReadOnlyList<ValidationMessage> found = StudyValidator.Validate(demo);
        Assert.IsTrue(found.Any(m => m.Severity == ValidationSeverity.Error && m.Message.Contains("leave no gap", StringComparison.Ordinal)));
        Assert.IsTrue(found.Any(m => m.Severity == ValidationSeverity.Error && m.Message.Contains("belongs to two train services", StringComparison.Ordinal)));

        Study plain = DemoStudyBuilder.Build();
        plain.TrainServices.Clear();
        var journey = new JourneyTemplate { Key = "journey.user.test2", Name = "Board any", Category = "Journeys", Steps = [JourneyStep.BoardTrainStep()] };
        plain.Library.Add(journey);
        Region west = plain.Regions.Single(r => r.Name == "West entrance");
        west.EmissionRules[0] = west.EmissionRules[0] with { Mix = [MixLine.Of(TemplateIds.FromKey("agent.commuter"), journey.Id)] };
        Assert.IsTrue(StudyValidator.Validate(plain).Any(m => m.Severity == ValidationSeverity.Error && m.Message.Contains("needs a train service", StringComparison.Ordinal)));
    }
}
