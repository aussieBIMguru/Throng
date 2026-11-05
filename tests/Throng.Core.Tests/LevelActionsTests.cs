using Throng.Core.Common;
using Throng.Core.Editing;
using Throng.Core.Model;
using Throng.Core.Validation;

namespace Throng.Core.Tests;

/// <summary>Adding and deleting levels (D-142, D-143).</summary>
[TestClass]
public sealed class LevelActionsTests
{
    [TestMethod]
    public void AddAbove_TopLevel_TakesItsStoreyHeight()
    {
        (Study study, _, Level upper, _) = VerticalCutTests.TwoLevelsWithStair();
        Level added = LevelActions.Plan(study, upper, above: true).Value!;
        Assert.AreEqual(10.0, added.ElevationM, 1e-9);
        Assert.AreEqual(5.0, added.HeightM, 1e-9);
    }

    [TestMethod]
    public void AddBelow_BottomLevel_GoesOneStoreyDown()
    {
        (Study study, Level lower, _, _) = VerticalCutTests.TwoLevelsWithStair();
        Level added = LevelActions.Plan(study, lower, above: false).Value!;
        Assert.AreEqual(-5.0, added.ElevationM, 1e-9);
    }

    [TestMethod]
    public void AddBetween_SplitsTheStorey_MovesNothing_AndCheckFlagsTheStair()
    {
        (Study study, Level lower, Level upper, Connector stair) = VerticalCutTests.TwoLevelsWithStair();
        Level added = LevelActions.Plan(study, lower, above: true).Value!;
        Assert.AreEqual(2.5, added.ElevationM, 1e-9);
        Assert.AreEqual(2.5, added.HeightM, 1e-9);

        new UndoStack().Execute(LevelActions.Add(study, added, null));
        Assert.AreEqual(3, study.Levels.Count);
        Assert.AreEqual(0.0, lower.ElevationM, 1e-9);
        Assert.AreEqual(5.0, upper.ElevationM, 1e-9);
        Assert.IsTrue(StudyValidator.Validate(study).Any(m =>
            m.Severity == ValidationSeverity.Warning && m.ObjectId == stair.Id && m.Message.Contains("passes level", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void AddBetween_TooTight_Fails()
    {
        var study = new Study();
        var a = new Level { Name = "A", ElevationM = 0 };
        var b = new Level { Name = "B", ElevationM = 1.5 };
        study.Levels.AddRange([a, b]);
        Assert.IsFalse(LevelActions.Plan(study, a, above: true).IsSuccess);
    }

    [TestMethod]
    public void NewLevels_GetUniqueNames()
    {
        (Study study, _, Level upper, _) = VerticalCutTests.TwoLevelsWithStair();
        Level first = LevelActions.Plan(study, upper, above: true).Value!;
        LevelActions.Add(study, first, null).Do();
        Level second = LevelActions.Plan(study, first, above: true).Value!;
        Assert.AreNotEqual(first.Name, second.Name);
    }

    [TestMethod]
    public void Delete_RemovesItsContents_FlagsLinks_AndUndoRestoresThem()
    {
        (Study study, Level lower, Level upper, Connector stair) = VerticalCutTests.TwoLevelsWithStair();
        Region upperFloor = study.Regions.Single(r => r.LevelId == upper.Id);
        Assert.AreEqual(1, LevelActions.ContentsOf(study, upper).Regions);

        Result<IUndoableAction> delete = LevelActions.Delete(study, upper, null);
        Assert.IsTrue(delete.IsSuccess);
        var undo = new UndoStack();
        undo.Execute(delete.Value!);

        Assert.AreEqual(1, study.Levels.Count);
        Assert.IsFalse(study.Regions.Contains(upperFloor));
        Assert.IsTrue(study.Connectors.Contains(stair), "The stair starts on the lower level, so it stays.");
        Assert.IsTrue(StudyValidator.Validate(study).Any(m => m.Severity == ValidationSeverity.Error && m.ObjectId == stair.Id));

        undo.Undo();
        Assert.AreEqual(2, study.Levels.Count);
        Assert.IsTrue(study.Regions.Contains(upperFloor));
        Assert.AreSame(upper, study.FindLevel(upper.Id));
        Assert.AreEqual(1, LevelActions.ContentsOf(study, lower).Connectors);
    }

    [TestMethod]
    public void Delete_LowerLevel_TakesTheStairStartingThere()
    {
        (Study study, Level lower, _, Connector stair) = VerticalCutTests.TwoLevelsWithStair();
        LevelActions.Delete(study, lower, null).Value!.Do();
        Assert.IsFalse(study.Connectors.Contains(stair));
    }

    [TestMethod]
    public void Delete_OnlyLevel_Fails()
    {
        Study study = Study.CreateDefault();
        Assert.IsFalse(LevelActions.Delete(study, study.Levels[0], null).IsSuccess);
    }
}
