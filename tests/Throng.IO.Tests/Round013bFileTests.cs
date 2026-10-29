using Throng.Core.Geometry;
using Throng.Core.Model;
using Throng.Core.Validation;

namespace Throng.IO.Tests;

/// <summary>Round 013b: drawn train shapes (D-249, additive in format v9) and the office tower demo (D-250).</summary>
[TestClass]
public sealed class Round013bFileTests
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
    public void TrainShape_RoundTrips_AndTheDemoDrawsBothTrains()
    {
        Study demo = DemoStudyBuilder.Build();
        Assert.IsTrue(demo.TrainServices.All(t => t.Shape.Count == 4), "Both demo trains are drawn beside their platform edges (D-249).");
        TrainService north = demo.TrainServices.Single(t => t.Name == "Northbound");
        Assert.IsTrue(north.Shape.Min(p => p.Y) >= 23 - 1e-9, "The northbound train is north of the platform, not on it.");

        int index = demo.TrainServices.IndexOf(north);
        demo.TrainServices[index] = north with { Shape = [new Point2(1, 23), new Point2(50, 23), new Point2(50, 26), new Point2(20, 27), new Point2(1, 26)] };
        string path = Path.Combine(_folder, "trains.throng");
        Assert.IsTrue(ThrongFile.Save(demo, path).IsSuccess);
        Study loaded = ThrongFile.Load(path).Value!;
        CollectionAssert.AreEqual(demo.TrainServices[index].Shape, loaded.TrainServices.Single(t => t.Id == north.Id).Shape);
        Assert.AreEqual(0, new TrainService().Shape.Count, "Services saved before 013b load without a shape (automatic box).");
    }

    [TestMethod]
    public void OfficeTower_LoadsClean_WithAFourCarBank()
    {
        Study tower = OfficeTowerBuilder.Build();
        Assert.AreEqual(OfficeTowerBuilder.OfficeFloors + 1, tower.Levels.Count);
        Shaft core = tower.Shafts.Single();
        Assert.AreEqual(Lift.MaxCars, core.Lift!.Cars);
        Assert.AreEqual(tower.Levels.Count, tower.LiftStops(core).Count, "The bank stops at every floor.");
        Assert.AreEqual(0, tower.Connectors.Count, "Lifts only (D-250).");
        IReadOnlyList<ValidationMessage> findings = StudyValidator.Validate(tower);
        Assert.AreEqual(0, findings.Count(f => f.Severity is ValidationSeverity.Error or ValidationSeverity.Warning),
            string.Join("\n", findings.Select(f => $"[{f.Severity}] {f.Message}")));

        string path = Path.Combine(_folder, "tower.throng");
        Assert.IsTrue(ThrongFile.Save(tower, path).IsSuccess);
        Study loaded = ThrongFile.Load(path).Value!;
        Assert.AreEqual(2, loaded.Shafts.Single().Lift!.Schedule.Count, "Up-peak morning, collective later.");
    }
}
