using Throng.Core.Library;
using Throng.Core.Model;

namespace Throng.IO.Tests;

/// <summary>Round 014: the office tower's stair (D-257) and destination dispatch saved as chosen (D-255, D-256).</summary>
[TestClass]
public sealed class Round014FileTests
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
    public void OfficeTower_StairServesEveryFloor_EastOfTheCore()
    {
        Study tower = OfficeTowerBuilder.Build();
        Connector stair = tower.Connectors.Single();
        Assert.AreEqual(ConnectorKind.Stair, stair.Kind);
        Assert.IsFalse(stair.StaffOnly, "Open to everyone (D-257).");
        Assert.AreEqual(tower.Levels[0].Id, stair.BottomLevelId, "From the ground lobby…");
        Assert.AreEqual(tower.Levels[^1].Id, stair.TopLevelId, "…to the top floor.");
        Assert.IsTrue(stair.BottomLanding.X > tower.Shafts.Single().Outline.Max(p => p.X), "East of the lift core.");
    }

    [TestMethod]
    public void DestinationDispatch_RoundTrips_InTheLiftAndItsSchedule()
    {
        Study tower = OfficeTowerBuilder.Build();
        Lift lift = tower.Shafts.Single().Lift!;
        lift.Dispatch = LiftDispatch.DestinationDispatch;
        lift.Schedule = [new LiftScheduleEntry { From = new TimeSpan(9, 0, 0), Dispatch = LiftDispatch.DestinationDispatch }];
        string path = Path.Combine(_folder, "destination.throng");
        Assert.IsTrue(ThrongFile.Save(tower, path).IsSuccess);
        Lift loaded = ThrongFile.Load(path).Value!.Shafts.Single().Lift!;
        Assert.AreEqual(LiftDispatch.DestinationDispatch, loaded.Dispatch);
        Assert.AreEqual(LiftDispatch.DestinationDispatch, loaded.Schedule.Single().Dispatch);
    }
}
