using Throng.Core.Editing;
using Throng.Core.Geometry;
using Throng.Core.Library;
using Throng.Core.Model;
using Throng.Core.Validation;
using Throng.Core.World;

namespace Throng.Core.Tests;

/// <summary>Three 20 × 20 m floors at 0, 4 and 9 m (storeys of 4 m and 5 m; the top level is 3.5 m high).</summary>
internal static class ThreeLevels
{
    public static (Study Study, Level L0, Level L1, Level L2) Build()
    {
        var study = new Study();
        var l0 = new Level { Name = "L0", ElevationM = 0, HeightM = 4 };
        var l1 = new Level { Name = "L1", ElevationM = 4, HeightM = 5 };
        var l2 = new Level { Name = "L2", ElevationM = 9, HeightM = 3.5 };
        study.Levels.AddRange([l0, l1, l2]);
        foreach (Level level in study.Levels)
        {
            study.Regions.Add(new Region { LevelId = level.Id, Name = "Floor " + level.Name, Outline = [new(0, 0), new(20, 0), new(20, 20), new(0, 20)] });
        }

        return (study, l0, l1, l2);
    }

    public static double Net(Study study, Level level) =>
        WalkableArea.NetAreaM2(WalkableArea.Compute(study, level.Id, ClipperPolygonOps.Instance).Value!);
}

[TestClass]
public sealed class SpanningRegionTests
{
    private static Region Column(Level baseLevel) => new()
    {
        LevelId = baseLevel.Id, Name = "Column", Kind = RegionKind.Obstacle, Outline = [new(5, 5), new(6, 5), new(6, 6), new(5, 6)],
    };

    [TestMethod]
    public void ThisLevelOnly_ByDefault()
    {
        (Study study, Level l0, Level l1, _) = ThreeLevels.Build();
        Region column = Column(l0);
        study.Regions.Add(column);
        Assert.IsTrue(study.RegionPresentOn(column, l0.Id));
        Assert.IsFalse(study.RegionPresentOn(column, l1.Id));
        Assert.AreEqual(400, ThreeLevels.Net(study, l1), 1e-6);
    }

    [TestMethod]
    public void SpanningObstacle_CutsEveryLevelUpToTheTop()
    {
        (Study study, Level l0, Level l1, Level l2) = ThreeLevels.Build();
        Region column = Column(l0);
        column.TopLevelId = l1.Id;
        study.Regions.Add(column);

        Assert.AreEqual(399, ThreeLevels.Net(study, l0), 1e-6);
        Assert.AreEqual(399, ThreeLevels.Net(study, l1), 1e-6);
        Assert.AreEqual(400, ThreeLevels.Net(study, l2), 1e-6);
    }

    [TestMethod]
    public void ExcludedLevel_IsSkipped()
    {
        (Study study, Level l0, Level l1, Level l2) = ThreeLevels.Build();
        Region column = Column(l0);
        column.TopLevelId = l2.Id;
        column.ExcludedLevelIds = [l1.Id];
        study.Regions.Add(column);

        Assert.AreEqual(3, study.SpannedLevels(column).Count);
        Assert.AreEqual(400, ThreeLevels.Net(study, l1), 1e-6);
        Assert.AreEqual(399, ThreeLevels.Net(study, l2), 1e-6);
    }

    [TestMethod]
    public void WalkableRegions_NeverSpan()
    {
        (Study study, Level l0, Level l1, _) = ThreeLevels.Build();
        Region floor = study.Regions[0];
        floor.TopLevelId = l1.Id;
        Assert.IsFalse(study.RegionPresentOn(floor, l1.Id));
    }

    [TestMethod]
    public void FixedHeight_RepeatsPerLevel_FullStorey_GoesFloorToFloor()
    {
        (Study study, Level l0, Level l1, Level l2) = ThreeLevels.Build();
        Region column = Column(l0);
        column.HeightM = 1.1;
        Assert.AreEqual(5.1, study.RegionExtentOn(column, l1).Top, 1e-9);

        column.HeightMode = RegionHeightMode.FullStorey;
        Assert.AreEqual((0.0, 4.0), study.RegionExtentOn(column, l0));
        Assert.AreEqual((4.0, 9.0), study.RegionExtentOn(column, l1));
        Assert.AreEqual((9.0, 12.5), study.RegionExtentOn(column, l2), "The topmost level uses its own height.");
    }

    [TestMethod]
    public void NewWalls_AreFullStorey_NewObstaclesFixed()
    {
        (Study study, Level l0, _, _) = ThreeLevels.Build();
        Assert.AreEqual(RegionHeightMode.FullStorey, RegionFactory.Create(study, l0.Id, RegionKind.Wall, [new(0, 0), new(1, 0)]).HeightMode);
        Assert.AreEqual(RegionHeightMode.Fixed, RegionFactory.Create(study, l0.Id, RegionKind.Obstacle, [new(0, 0), new(1, 0), new(1, 1)]).HeightMode);
    }

    [TestMethod]
    public void CopyRegion_ShiftsTheSpan()
    {
        (Study study, Level l0, Level l1, Level l2) = ThreeLevels.Build();
        Region column = Column(l0);
        column.TopLevelId = l1.Id;
        Region copy = ConnectorFactory.CopyRegion(study, column, l0.Id, l1.Id, Point2.Zero);
        Assert.AreEqual(l1.Id, copy.LevelId);
        Assert.AreEqual(l2.Id, copy.TopLevelId);

        Region clipped = ConnectorFactory.CopyRegion(study, copy, l1.Id, l2.Id, Point2.Zero);
        Assert.AreEqual(l2.Id, clipped.TopLevelId, "A span past the top is clipped to the highest level.");
    }

    [TestMethod]
    public void SpanToMissingLevel_IsAnError()
    {
        (Study study, Level l0, _, _) = ThreeLevels.Build();
        Region column = Column(l0);
        column.TopLevelId = Guid.NewGuid();
        study.Regions.Add(column);
        Assert.IsTrue(StudyValidator.Validate(study).Any(f => f.Severity == ValidationSeverity.Error && f.ObjectId == column.Id));
    }
}

[TestClass]
public sealed class PerLevelStairTests
{
    private static Connector Core(Study study, Level bottom, Level top)
    {
        var template = (ConnectorTemplate)BuiltInLibrary.Instance.FindByKey("connector.stair-switchback-1500")!;
        Connector stair = ConnectorFactory.FromTemplate(study, template, bottom.Id, top.Id, new Point2(4, 4), 0, TurnSide.Left)[0];
        study.Connectors.Add(stair);
        return stair;
    }

    [TestMethod]
    public void NewSwitchback_SpanningTwoStoreys_HasOneCircuitPerLevel()
    {
        (Study study, Level l0, Level l1, Level l2) = ThreeLevels.Build();
        Connector stair = Core(study, l0, l2);
        Assert.IsTrue(stair.PerLevelCircuits);

        ConnectorLayout layout = ConnectorLayout.Compute(stair, study).Value!;
        Assert.AreEqual(2, layout.Circuits.Count);
        Assert.AreEqual(4.0, layout.Circuits[0].RiseM, 1e-9);
        Assert.AreEqual(5.0, layout.Circuits[1].RiseM, 1e-9);
        Assert.AreEqual(4.0, layout.Circuits[1].BaseZ, 1e-9);
        Assert.AreEqual(l1.Id, layout.Circuits[0].TopLevelId);
        Assert.AreEqual(9.0, layout.Pieces[^1].Z1, 1e-9);
        Assert.AreEqual(layout.Circuits[0].Pieces[0].Start, layout.Circuits[1].Pieces[0].Start, "Circuits stack at the same landing.");
        Assert.IsTrue(layout.Circuits[1].Pieces.Sum(p => p.LengthM) > layout.Circuits[0].Pieces.Sum(p => p.LengthM), "The taller storey has longer runs.");
    }

    [TestMethod]
    public void OptionOff_IsOneLongRun()
    {
        (Study study, Level l0, _, Level l2) = ThreeLevels.Build();
        Connector stair = Core(study, l0, l2);
        stair.PerLevelCircuits = false;
        Assert.AreEqual(1, ConnectorLayout.Compute(stair, study).Value!.Circuits.Count);
    }

    [TestMethod]
    public void PerLevelCore_ValidatesClean_AndServesTheMiddleLevel()
    {
        (Study study, Level l0, Level l1, Level l2) = ThreeLevels.Build();
        Connector stair = Core(study, l0, l2);
        IReadOnlyList<ValidationMessage> findings = StudyValidator.Validate(study);
        Assert.AreEqual(0, findings.Count, string.Join("; ", findings.Select(f => f.Message)));
        Assert.IsTrue(study.ConnectorServes(stair, l1.Id));
        Assert.IsTrue(study.ConnectorsTouching(l1.Id).Contains(stair));
    }

    [TestMethod]
    public void ExcludedMiddleLevel_HasNoExit_ButIsStillCut()
    {
        (Study study, Level l0, Level l1, Level l2) = ThreeLevels.Build();
        double before = ThreeLevels.Net(study, l1);
        Connector stair = Core(study, l0, l2);
        stair.ExcludedLevelIds = [l1.Id];

        Assert.IsFalse(study.ConnectorServes(stair, l1.Id));
        Assert.IsTrue(ThreeLevels.Net(study, l1) < before - 1, "The stair still passes through.");
        Assert.IsTrue(StudyValidator.Validate(study).Any(f => f.Severity == ValidationSeverity.Warning && f.ObjectId == l1.Id), "L1 now has no vertical link.");
    }
}

[TestClass]
public sealed class TravelatorAndDeviceTests
{
    [TestMethod]
    public void Travelator_IsOneBeltOnOneLevel()
    {
        (Study study, Level l0, Level l1, _) = ThreeLevels.Build();
        var template = (ConnectorTemplate)BuiltInLibrary.Instance.FindByKey("connector.travelator")!;
        Connector belt = ConnectorFactory.FromTemplate(study, template, l0.Id, l0.Id, new Point2(2, 10), 0, TurnSide.Left, 12)[0];
        study.Connectors.Add(belt);

        ConnectorLayout layout = ConnectorLayout.Compute(belt, study).Value!;
        Assert.AreEqual(12, layout.LengthM, 1e-9);
        Assert.AreEqual(ConnectorPieceKind.Belt, layout.Pieces[0].Kind);
        Assert.AreEqual(ConnectorDirection.OneWay, belt.Direction);
        Assert.AreEqual(400 - 12, ThreeLevels.Net(study, l0), 1e-6);
        Assert.IsFalse(study.ConnectorServes(belt, l0.Id), "Travelators don't link levels.");

        belt.TopLevelId = l1.Id;
        Assert.IsFalse(ConnectorLayout.Compute(belt, study).IsSuccess, "A travelator stays on one level.");
    }

    [TestMethod]
    public void Device_BlocksItsFootprint()
    {
        (Study study, Level l0, _, _) = ThreeLevels.Build();
        var template = (DeviceTemplate)BuiltInLibrary.Instance.FindByKey("device.info-desk")!;
        study.Devices.Add(DeviceFactory.FromTemplate(study, template, l0.Id, new Point2(10, 10), 90));
        Assert.AreEqual(400 - (2.0 * 1.0), ThreeLevels.Net(study, l0), 1e-6);
    }

    [TestMethod]
    public void PassThroughDevice_BlocksOnlyItsDividers()
    {
        (Study study, Level l0, _, _) = ThreeLevels.Build();
        var template = (DeviceTemplate)BuiltInLibrary.Instance.FindByKey("device.gate-line")!;
        Device gates = DeviceFactory.FromTemplate(study, template, l0.Id, new Point2(10, 10), 0);
        study.Devices.Add(gates);

        Assert.AreEqual(4, gates.Lanes);
        Assert.AreEqual(5, DeviceGeometry.Blocking(gates).Count);
        Assert.AreEqual(400 - (5 * DeviceGeometry.DividerM * gates.DepthM), ThreeLevels.Net(study, l0), 1e-6);
    }

    [TestMethod]
    public void DeviceFacingAWall_IsAWarning()
    {
        (Study study, Level l0, _, _) = ThreeLevels.Build();
        var template = (DeviceTemplate)BuiltInLibrary.Instance.FindByKey("device.ticket-machine")!;
        Device machine = DeviceFactory.FromTemplate(study, template, l0.Id, new Point2(10, 19.7), 90);
        study.Devices.Add(machine);
        Assert.IsTrue(StudyValidator.Validate(study).Any(f => f.Severity == ValidationSeverity.Warning && f.ObjectId == machine.Id));
    }
}

[TestClass]
public sealed class UnderlayGeometryTests
{
    [TestMethod]
    public void Corners_FollowScaleAndCentre()
    {
        var underlay = new Underlay { PixelWidth = 1000, PixelHeight = 500, MetresPerPixel = 0.02, Centre = new Point2(10, 5) };
        Point2[] c = UnderlayGeometry.Corners(underlay);
        Assert.AreEqual(0, Point2.Distance(new Point2(0, 0), c[0]), 1e-9);
        Assert.AreEqual(0, Point2.Distance(new Point2(20, 10), c[2]), 1e-9);
    }

    [TestMethod]
    public void Calibration_ScalesAboutTheFirstPoint()
    {
        var underlay = new Underlay { PixelWidth = 1000, PixelHeight = 500, MetresPerPixel = 0.02, Centre = new Point2(10, 5) };
        Underlay next = UnderlayGeometry.Calibrated(underlay, new Point2(0, 0), new Point2(10, 0), 25)!;
        Assert.AreEqual(0.05, next.MetresPerPixel, 1e-12);
        Assert.AreEqual(0, Point2.Distance(new Point2(0, 0), UnderlayGeometry.Corners(next)[0]), 1e-9, "The first point stays put.");
        Assert.AreEqual(50, UnderlayGeometry.Size(next).Width, 1e-9);
        Assert.IsNull(UnderlayGeometry.Calibrated(underlay, new Point2(1, 1), new Point2(1, 1), 5));
    }
}
