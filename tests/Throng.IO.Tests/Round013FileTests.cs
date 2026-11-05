using Throng.Core.Editing;
using Throng.Core.Library;
using Throng.Core.Model;

namespace Throng.IO.Tests;

/// <summary>Round 013a: lifts in shafts are saved in format v9 (D-236–D-244).</summary>
[TestClass]
public sealed class Round013FileTests
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
    public void Lift_RoundTrips_InFormatV9()
    {
        Study demo = DemoStudyBuilder.Build();
        Shaft shaft = demo.Shafts.First();
        var preset = (LiftTemplate)BuiltInLibrary.Instance.FindByKey("lift.accessible")!;
        Lift lift = LiftFactory.FromTemplate(shaft, preset, preset.Id);
        lift.Cars = 2;
        lift.FrontEdge = 2;
        lift.LandingDepthM = 3;
        lift.LobbyLevelId = shaft.TopLevelId;
        lift.Dispatch = LiftDispatch.UpPeak;
        lift.Schedule = [new LiftScheduleEntry { From = new TimeSpan(9, 30, 0), Dispatch = LiftDispatch.Collective }];
        lift.Doors = [new LiftLevelDoors { LevelId = shaft.BaseLevelId, Front = LiftDoorUse.Exit, Rear = LiftDoorUse.Entry }];
        shaft.Lift = lift;

        string path = Path.Combine(_folder, "lift.throng");
        Assert.IsTrue(ThrongFile.Save(demo, path).IsSuccess);
        Assert.AreEqual(ThrongFile.CurrentFormatVersion, ThrongFile.ReadManifest(path).Value!.FormatVersion);
        Lift loaded = ThrongFile.Load(path).Value!.Shafts.Single(s => s.Id == shaft.Id).Lift!;
        Assert.AreEqual("lift.accessible", loaded.PresetKey);
        Assert.AreEqual(2, loaded.Cars);
        Assert.AreEqual(2, loaded.FrontEdge);
        Assert.AreEqual(3, loaded.LandingDepthM, 1e-9);
        Assert.AreEqual(shaft.TopLevelId, loaded.LobbyLevelId);
        Assert.AreEqual(LiftDispatch.UpPeak, loaded.Dispatch);
        Assert.AreEqual(new TimeSpan(9, 30, 0), loaded.Schedule.Single().From);
        LiftLevelDoors doors = loaded.DoorsAt(shaft.BaseLevelId);
        Assert.AreEqual(LiftDoorUse.Exit, doors.Front);
        Assert.AreEqual(LiftDoorUse.Entry, doors.Rear);
        Assert.AreEqual(LiftDoorUse.Both, loaded.DoorsAt(shaft.TopLevelId).Front, "A level without a row: front both ways.");
    }

    [TestMethod]
    public void Shaft_CloneCopiesTheLift()
    {
        var shaft = new Shaft { Lift = new Lift { Name = "A", Cars = 3 } };
        Shaft copy = shaft.Clone();
        copy.Lift!.Cars = 1;
        Assert.AreEqual(3, shaft.Lift.Cars, "Undo snapshots don't share the lift.");
    }
}
