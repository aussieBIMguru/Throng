using Throng.Core.Geometry;
using Throng.Core.Model;
using Throng.Core.Validation;

namespace Throng.Core.Tests;

[TestClass]
public sealed class StudyValidatorTests
{
    private static List<Point2> Rect() => [new(0, 0), new(10, 0), new(10, 10), new(0, 10)];

    [TestMethod]
    public void DefaultStudy_WarnsAboutNoWalkableArea_ButIsRunnable()
    {
        IReadOnlyList<ValidationMessage> findings = StudyValidator.Validate(Study.CreateDefault());
        Assert.IsTrue(findings.Any(f => f.Severity == ValidationSeverity.Warning));
        Assert.IsTrue(StudyValidator.IsRunnable(findings));
    }

    [TestMethod]
    public void NoLevels_IsAnError()
    {
        IReadOnlyList<ValidationMessage> findings = StudyValidator.Validate(new Study());
        Assert.IsFalse(StudyValidator.IsRunnable(findings));
    }

    [TestMethod]
    public void RegionOnMissingLevel_IsAnError()
    {
        Study study = Study.CreateDefault();
        var orphan = new Region { LevelId = Guid.NewGuid(), Name = "Orphan", Outline = Rect() };
        study.Regions.Add(orphan);

        IReadOnlyList<ValidationMessage> findings = StudyValidator.Validate(study);
        Assert.IsTrue(findings.Any(f => f.Severity == ValidationSeverity.Error && f.ObjectId == orphan.Id));
    }

    [TestMethod]
    public void ZeroAreaRegion_IsAnError()
    {
        Study study = Study.CreateDefault();
        study.Regions.Add(new Region { LevelId = study.Levels[0].Id, Name = "Flat", Outline = [new(0, 0), new(5, 0), new(10, 0)] });
        Assert.IsFalse(StudyValidator.IsRunnable(StudyValidator.Validate(study)));
    }

    [TestMethod]
    public void Wall_NeedsTwoPointsOnly()
    {
        Study study = Study.CreateDefault();
        Guid level = study.Levels[0].Id;
        study.Regions.Add(new Region { LevelId = level, Name = "Floor", Outline = Rect() });
        study.Regions.Add(new Region { LevelId = level, Name = "Wall", Kind = RegionKind.Wall, Outline = [new(0, 0), new(5, 0)] });
        Assert.AreEqual(0, StudyValidator.Validate(study).Count);
    }
}
