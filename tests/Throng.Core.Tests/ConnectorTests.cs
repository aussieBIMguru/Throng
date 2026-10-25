using System.Text.Json;
using Throng.Core.Editing;
using Throng.Core.Geometry;
using Throng.Core.Library;
using Throng.Core.Model;
using Throng.Core.Serialization;
using Throng.Core.Validation;
using Throng.Core.World;

namespace Throng.Core.Tests;

[TestClass]
public sealed class ConnectorLayoutTests
{
    private const double Tol = 1e-6;

    private static Connector Stair(double width = 1.5) => new() { Kind = ConnectorKind.Stair, WidthM = width, BottomLanding = Point2.Zero };

    [TestMethod]
    public void Stair_RisersFitTheRiseExactly()
    {
        ConnectorLayout layout = ConnectorLayout.Compute(Stair(), 5.0).Value!;
        Assert.AreEqual(29, layout.RiserCount);
        Assert.AreEqual(5.0 / 29, layout.ActualRiserM, Tol);
        Assert.AreEqual(5.0, layout.Pieces[^1].Z1, Tol);
    }

    [TestMethod]
    public void Stair_SplitsAtEighteenRisersWithALandingAsWideAsTheStair()
    {
        // 29 risers → two flights (15 + 14) and one landing (D-057, D-069).
        ConnectorLayout layout = ConnectorLayout.Compute(Stair(3.6), 5.0).Value!;
        Assert.AreEqual(2, layout.RunCount);
        Assert.AreEqual(3, layout.Pieces.Count);
        Assert.AreEqual(15, layout.Pieces[0].Risers);
        Assert.AreEqual(14, layout.Pieces[2].Risers);
        Assert.AreEqual(ConnectorPieceKind.Landing, layout.Pieces[1].Kind);
        Assert.AreEqual(3.6, layout.Pieces[1].LengthM, Tol);

        // n risers have n − 1 goings: 14 × 0.28 + 3.6 + 13 × 0.28.
        Assert.AreEqual((14 * 0.28) + 3.6 + (13 * 0.28), layout.LengthM, Tol);
    }

    [TestMethod]
    public void Stair_EighteenRisersIsOneFlight()
    {
        ConnectorLayout layout = ConnectorLayout.Compute(Stair(), 18 * 0.17).Value!;
        Assert.AreEqual(18, layout.RiserCount);
        Assert.AreEqual(1, layout.RunCount);
    }

    [TestMethod]
    public void NarrowStair_LandingIsAtLeastTheMinimum()
    {
        ConnectorLayout layout = ConnectorLayout.Compute(Stair(1.0), 5.0).Value!;
        Assert.AreEqual(1.2, layout.Pieces[1].LengthM, Tol);
    }

    [TestMethod]
    public void Switchback_ReturnsAlongsideAndExitsNearTheStart()
    {
        Connector stair = Stair();
        stair.Shape = ConnectorShape.Switchback;
        stair.ReturnSide = TurnSide.Left;
        ConnectorLayout layout = ConnectorLayout.Compute(stair, 6.0).Value!;

        Assert.AreEqual(35, layout.RiserCount);
        Assert.AreEqual(2, layout.RunCount);
        Assert.AreEqual((2 * 1.5) + 0.2, layout.FootprintWidthM, Tol);
        Assert.AreEqual((17 * 0.28) + 1.5, layout.LengthM, Tol);

        // Return run is on the left (+Y when running +X) and exits heading back (−X).
        Assert.AreEqual(1.7, layout.TopLanding.Y, Tol);
        Assert.AreEqual(17 * 0.28 - 16 * 0.28, layout.TopLanding.X, Tol);
        Assert.AreEqual(-1, layout.TopAxis.X, Tol);
    }

    [TestMethod]
    public void Switchback_FlipsToTheRight()
    {
        Connector stair = Stair();
        stair.Shape = ConnectorShape.Switchback;
        stair.ReturnSide = TurnSide.Right;
        ConnectorLayout layout = ConnectorLayout.Compute(stair, 6.0).Value!;
        Assert.AreEqual(-1.7, layout.TopLanding.Y, Tol);
    }

    [TestMethod]
    public void Escalator_IsThirtyDegreesWithFlatsEachEnd()
    {
        var escalator = new Connector { Kind = ConnectorKind.Escalator, WidthM = 1.0 };
        ConnectorLayout layout = ConnectorLayout.Compute(escalator, 5.0).Value!;
        Assert.AreEqual(3, layout.Pieces.Count);
        Assert.AreEqual(30, layout.PitchDeg, 1e-9);
        Assert.AreEqual(1.2 + (5.0 / Math.Tan(Math.PI / 6)) + 1.2, layout.LengthM, Tol);
    }

    [TestMethod]
    public void Escalator_IgnoresSwitchback()
    {
        var escalator = new Connector { Kind = ConnectorKind.Escalator, WidthM = 1.0, Shape = ConnectorShape.Switchback };
        Assert.AreEqual(1.0, ConnectorLayout.Compute(escalator, 5.0).Value!.FootprintWidthM, Tol);
    }

    [TestMethod]
    public void Ramp_GetsALandingEveryNineMetres()
    {
        // 1.5 m rise at 1:14 = 21 m of slope → three 7 m runs, two 1.2 m landings (D-072).
        var ramp = new Connector { Kind = ConnectorKind.Ramp, WidthM = 1.8, Gradient = 1.0 / 14.0 };
        ConnectorLayout layout = ConnectorLayout.Compute(ramp, 1.5).Value!;
        Assert.AreEqual(3, layout.RunCount);
        Assert.AreEqual(5, layout.Pieces.Count);
        Assert.AreEqual(21 + 2.4, layout.LengthM, 1e-9);
        Assert.AreEqual(1.0 / 14.0, layout.SteepestGradient, 1e-9);
    }

    [TestMethod]
    public void Layout_FollowsTheAngle()
    {
        Connector stair = Stair();
        stair.AngleDeg = 90;
        ConnectorLayout layout = ConnectorLayout.Compute(stair, 3.06).Value!;
        Assert.AreEqual(0, layout.TopLanding.X, Tol);
        Assert.AreEqual(layout.LengthM, layout.TopLanding.Y, Tol);
    }

    [TestMethod]
    public void Layout_RejectsANonPositiveRise()
    {
        Assert.IsFalse(ConnectorLayout.Compute(Stair(), 0).IsSuccess);
        Assert.IsFalse(ConnectorLayout.Compute(Stair(), -3).IsSuccess);
    }

    [TestMethod]
    public void Footprint_IsCounterClockwise()
    {
        Assert.IsTrue(PolygonMath.IsCounterClockwise(ConnectorLayout.Compute(Stair(), 5).Value!.Footprint));
    }
}

[TestClass]
public sealed class VerticalCutTests
{
    /// <summary>Two 40 × 20 m floors 5 m apart with a straight stair from (10, 10) running +X.</summary>
    internal static (Study Study, Level Lower, Level Upper, Connector Stair) TwoLevelsWithStair()
    {
        var study = new Study();
        var lower = new Level { Name = "Lower", ElevationM = 0, HeightM = 5 };
        var upper = new Level { Name = "Upper", ElevationM = 5, HeightM = 5 };
        study.Levels.AddRange([lower, upper]);
        foreach (Level level in study.Levels)
        {
            study.Regions.Add(new Region { LevelId = level.Id, Name = "Floor", Outline = [new(0, 0), new(40, 0), new(40, 20), new(0, 20)] });
        }

        var template = (ConnectorTemplate)BuiltInLibrary.Instance.FindByKey("connector.stair-2400")!;
        Connector stair = ConnectorFactory.FromTemplate(study, template, lower.Id, upper.Id, new Point2(10, 10), 0, TurnSide.Left)[0];
        study.Connectors.Add(stair);
        return (study, lower, upper, stair);
    }

    [TestMethod]
    public void Footprint_IsCutFromTheBottomLevel()
    {
        (Study study, Level lower, _, Connector stair) = TwoLevelsWithStair();
        ConnectorLayout layout = ConnectorLayout.Compute(stair, study).Value!;
        double net = WalkableArea.NetAreaM2(WalkableArea.Compute(study, lower.Id, ClipperPolygonOps.Instance).Value!);
        Assert.AreEqual(800 - PolygonMath.Area(layout.Footprint), net, 0.01);
    }

    [TestMethod]
    public void Opening_IsCutFromTheTopLevel_ButTheExitStaysWalkable()
    {
        (Study study, _, Level upper, Connector stair) = TwoLevelsWithStair();
        ConnectorLayout layout = ConnectorLayout.Compute(stair, study).Value!;
        var net = WalkableArea.Compute(study, upper.Id, ClipperPolygonOps.Instance).Value!;

        Assert.IsTrue(WalkableArea.NetAreaM2(net) < 800 - 1, "Something is cut.");
        Assert.IsFalse(WalkableArea.Contains(net, layout.TopLanding - (layout.TopAxis * 0.5)), "Just below the top is open.");
        Assert.IsTrue(WalkableArea.Contains(net, layout.TopLanding + (layout.TopAxis * 0.2)), "Just beyond the exit is floor.");
        Assert.IsTrue(WalkableArea.Contains(net, layout.BottomLanding + (layout.BottomAxis * 0.5)), "Floor stays over the low end (headroom).");
    }

    [TestMethod]
    public void Opening_HasClearanceAlongTheSides()
    {
        (Study study, _, Level upper, Connector stair) = TwoLevelsWithStair();
        ConnectorLayout layout = ConnectorLayout.Compute(stair, study).Value!;
        var net = WalkableArea.Compute(study, upper.Id, ClipperPolygonOps.Instance).Value!;
        Point2 nearTop = layout.TopLanding - (layout.TopAxis * 1.0);
        Point2 side = layout.TopAxis.Perpendicular();
        Assert.IsFalse(WalkableArea.Contains(net, nearTop + (side * (1.2 + 0.2))), "Inside the 0.3 m clearance.");
        Assert.IsTrue(WalkableArea.Contains(net, nearTop + (side * (1.2 + 0.4))), "Beyond the clearance.");
    }

    [TestMethod]
    public void Shaft_IsCutFromEverySpannedLevelOnly()
    {
        var study = new Study();
        Level[] levels = [new() { ElevationM = 0 }, new() { ElevationM = 4 }, new() { ElevationM = 8 }];
        study.Levels.AddRange(levels);
        foreach (Level level in levels)
        {
            study.Regions.Add(new Region { LevelId = level.Id, Outline = [new(0, 0), new(10, 0), new(10, 10), new(0, 10)] });
        }

        study.Shafts.Add(new Shaft { BaseLevelId = levels[0].Id, TopLevelId = levels[1].Id, Outline = [new(1, 1), new(3, 1), new(3, 3), new(1, 3)] });
        double Net(Level l) => WalkableArea.NetAreaM2(WalkableArea.Compute(study, l.Id, ClipperPolygonOps.Instance).Value!);

        Assert.AreEqual(96, Net(levels[0]), 1e-6);
        Assert.AreEqual(96, Net(levels[1]), 1e-6);
        Assert.AreEqual(100, Net(levels[2]), 1e-6);
    }
}

[TestClass]
public sealed class ConnectorValidationTests
{
    [TestMethod]
    public void WellPlacedStair_ValidatesClean()
    {
        (Study study, _, _, _) = VerticalCutTests.TwoLevelsWithStair();
        IReadOnlyList<ValidationMessage> findings = StudyValidator.Validate(study);
        Assert.AreEqual(0, findings.Count, string.Join("; ", findings.Select(f => f.Message)));
    }

    [TestMethod]
    public void LandingOffWalkable_IsAnError()
    {
        (Study study, _, _, Connector stair) = VerticalCutTests.TwoLevelsWithStair();
        stair.BottomLanding = new Point2(-5, 10);
        Assert.IsTrue(StudyValidator.Validate(study).Any(f => f.Severity == ValidationSeverity.Error && f.ObjectId == stair.Id));
    }

    [TestMethod]
    public void OverlappingConnectors_AreAnError()
    {
        (Study study, _, _, Connector stair) = VerticalCutTests.TwoLevelsWithStair();
        Connector copy = ConnectorFactory.Copy(stair, stair.BottomLevelId, stair.TopLevelId, new Point2(1, 0.5), null);
        study.Connectors.Add(copy);
        Assert.IsTrue(StudyValidator.Validate(study).Any(f => f.Severity == ValidationSeverity.Error && f.Message.Contains("overlap")));
    }

    [TestMethod]
    public void SteepRamp_IsAWarning()
    {
        (Study study, _, _, Connector stair) = VerticalCutTests.TwoLevelsWithStair();
        study.Connectors.Clear();
        var ramp = new Connector
        {
            Name = "Steep ramp", Kind = ConnectorKind.Ramp, WidthM = 1.8, Gradient = 1.0 / 8.0,
            BottomLevelId = stair.BottomLevelId, TopLevelId = stair.TopLevelId, BottomLanding = new Point2(2, 3),
        };
        study.Connectors.Add(ramp);
        IReadOnlyList<ValidationMessage> findings = StudyValidator.Validate(study);
        Assert.IsTrue(findings.Any(f => f.Severity == ValidationSeverity.Warning && f.ObjectId == ramp.Id), string.Join("; ", findings.Select(f => f.Message)));
    }

    [TestMethod]
    public void LevelWithoutVerticalLink_IsAWarning()
    {
        (Study study, Level lower, _, _) = VerticalCutTests.TwoLevelsWithStair();
        study.Connectors.Clear();
        Assert.IsTrue(StudyValidator.Validate(study).Any(f => f.Severity == ValidationSeverity.Warning && f.ObjectId == lower.Id));
    }

    [TestMethod]
    public void ShaftOnMissingLevel_IsAnError()
    {
        (Study study, Level lower, _, _) = VerticalCutTests.TwoLevelsWithStair();
        var shaft = new Shaft { Name = "Orphan", BaseLevelId = lower.Id, TopLevelId = Guid.NewGuid(), Outline = [new(30, 2), new(32, 2), new(32, 4), new(30, 4)] };
        study.Shafts.Add(shaft);
        Assert.IsTrue(StudyValidator.Validate(study).Any(f => f.Severity == ValidationSeverity.Error && f.ObjectId == shaft.Id));
    }
}

[TestClass]
public sealed class ConnectorEditingTests
{
    [TestMethod]
    public void PairPreset_PlacesTwoLinkedUnitsAtTheRightSpacing()
    {
        var study = new Study();
        var template = (ConnectorTemplate)BuiltInLibrary.Instance.FindByKey("connector.escalator-pair")!;
        List<Connector> pair = ConnectorFactory.FromTemplate(study, template, Guid.NewGuid(), Guid.NewGuid(), new Point2(10, 10), 0, TurnSide.Left);

        Assert.AreEqual(2, pair.Count);
        Assert.IsNotNull(pair[0].PairId);
        Assert.AreEqual(pair[0].PairId, pair[1].PairId);
        Assert.AreEqual(ConnectorDirection.Up, pair[0].Direction);
        Assert.AreEqual(ConnectorDirection.Down, pair[1].Direction);
        Assert.AreEqual(1.0 + 0.6, Point2.Distance(pair[0].BottomLanding, pair[1].BottomLanding), 1e-9);
        Assert.AreNotEqual(pair[0].Name, pair[1].Name);
    }

    [TestMethod]
    public void Clone_CopiesEveryValue()
    {
        var template = (ConnectorTemplate)BuiltInLibrary.Instance.FindByKey("connector.stair-switchback-1500")!;
        Connector original = ConnectorFactory.FromTemplate(new Study(), template, Guid.NewGuid(), Guid.NewGuid(), new Point2(3, 4), 30, TurnSide.Right)[0];
        original.PairId = Guid.NewGuid();
        string a = JsonSerializer.Serialize(original, JsonDefaults.Options);
        string b = JsonSerializer.Serialize(original.Clone(), JsonDefaults.Options);
        Assert.AreEqual(a, b);
    }

    [TestMethod]
    public void ReplaceState_UndoesMoveAndRotate()
    {
        var connector = new Connector { BottomLanding = new Point2(1, 1), AngleDeg = 0 };
        Connector moved = ConnectorFactory.Rotated(ConnectorFactory.Translated(connector, new Point2(2, 0)), new Point2(3, 1), 90);
        var action = StateActions.Connectors("Move", [(connector, moved)], null);

        action.Do();
        Assert.AreEqual(90, connector.AngleDeg, 1e-9);
        Assert.AreEqual(3, connector.BottomLanding.X, 1e-9);

        action.Undo();
        Assert.AreEqual(0, connector.AngleDeg, 1e-9);
        Assert.AreEqual(new Point2(1, 1), connector.BottomLanding);
    }

    [TestMethod]
    public void ShiftLevels_KeepsTheSpan()
    {
        var study = new Study();
        Level[] levels = [new() { ElevationM = 0 }, new() { ElevationM = 4 }, new() { ElevationM = 8 }, new() { ElevationM = 12 }];
        study.Levels.AddRange(levels);

        var shifted = ConnectorFactory.ShiftLevels(study, levels[0].Id, levels[1].Id, levels[0].Id, levels[2].Id);
        Assert.AreEqual((levels[2].Id, levels[3].Id), shifted);
        Assert.IsNull(ConnectorFactory.ShiftLevels(study, levels[0].Id, levels[1].Id, levels[0].Id, levels[3].Id), "No level above the top.");
    }

    [TestMethod]
    public void SnapAngle_RoundsToFifteenDegrees()
    {
        Assert.AreEqual(45, ConnectorFactory.SnapAngle(52, 15), 1e-9);
        Assert.AreEqual(0, ConnectorFactory.SnapAngle(359, 15), 1e-9);
        Assert.AreEqual(270, ConnectorFactory.SnapAngle(-88, 15), 1e-9);
    }

    [TestMethod]
    public void CompositeAction_UndoesInReverse()
    {
        var study = new Study();
        var connector = new Connector();
        var shaft = new Shaft();
        int calls = 0;
        var action = new CompositeAction("Add both", [
            new AddItemsAction<Connector>("c", study.Connectors, [connector], null),
            new AddItemsAction<Shaft>("s", study.Shafts, [shaft], null),
        ], () => calls++);

        action.Do();
        Assert.AreEqual(1, study.Connectors.Count);
        Assert.AreEqual(1, study.Shafts.Count);
        action.Undo();
        Assert.AreEqual(0, study.Connectors.Count + study.Shafts.Count);
        Assert.AreEqual(2, calls, "One change notification per do/undo.");
    }
}
