using Throng.Core.Geometry;
using Throng.Core.Model;
using Throng.Core.World;

namespace Throng.Core.Tests;

[TestClass]
public sealed class PolygonOpsTests
{
    private static readonly ClipperPolygonOps Ops = ClipperPolygonOps.Instance;

    private static double NetArea(IReadOnlyList<IReadOnlyList<Point2>> polygons) => WalkableArea.NetAreaM2(polygons);

    [TestMethod]
    public void Union_OverlappingSquares()
    {
        var a = ShapeFactory.Rectangle(new Point2(0, 0), new Point2(4, 4));
        var b = ShapeFactory.Rectangle(new Point2(2, 2), new Point2(6, 6));
        var union = Ops.Union([a, b]);
        Assert.AreEqual(1, union.Count);
        Assert.AreEqual(28.0, NetArea(union), 1e-3);
        Assert.IsTrue(PolygonMath.IsCounterClockwise(union[0]), "Outers wind counter-clockwise.");
    }

    [TestMethod]
    public void Union_MixedWindingStillUnions()
    {
        var ccw = ShapeFactory.Rectangle(new Point2(0, 0), new Point2(4, 4));
        List<Point2> cw = [.. Enumerable.Reverse(ShapeFactory.Rectangle(new Point2(2, 0), new Point2(6, 4)))];
        Assert.AreEqual(24.0, NetArea(Ops.Union([ccw, cw])), 1e-3, "A clockwise input must not cancel out the overlap.");
    }

    [TestMethod]
    public void Difference_LeavesAHole()
    {
        var floor = ShapeFactory.Rectangle(new Point2(0, 0), new Point2(10, 10));
        var column = ShapeFactory.Rectangle(new Point2(4, 4), new Point2(6, 6));
        var net = Ops.Difference([floor], [column]);
        Assert.AreEqual(96.0, NetArea(net), 1e-3);
        Assert.IsTrue(net.Any(p => !PolygonMath.IsCounterClockwise(p)), "The column becomes a clockwise hole.");
    }

    [TestMethod]
    public void ThickenPolyline_AreaIsLengthTimesThickness()
    {
        var wall = Ops.ThickenPolyline([new Point2(0, 0), new Point2(10, 0)], 0.2);
        Assert.AreEqual(2.0, NetArea(wall), 1e-3);
        Assert.AreEqual(0, Ops.ThickenPolyline([new Point2(0, 0)], 0.2).Count);
    }

    [TestMethod]
    public void WalkableArea_SubtractsObstaclesAndWalls()
    {
        var study = Study.CreateDefault();
        Guid level = study.Levels[0].Id;
        study.Regions.Add(new Region { LevelId = level, Kind = RegionKind.Walkable, Outline = ShapeFactory.Rectangle(new(0, 0), new(10, 10)) });
        study.Regions.Add(new Region { LevelId = level, Kind = RegionKind.Walkable, Outline = ShapeFactory.Rectangle(new(5, 0), new(15, 10)) });
        study.Regions.Add(new Region { LevelId = level, Kind = RegionKind.Obstacle, Outline = ShapeFactory.Rectangle(new(1, 1), new(3, 3)) });
        study.Regions.Add(new Region { LevelId = level, Kind = RegionKind.Wall, ThicknessM = 0.2, Outline = [new(12, 2), new(12, 8)] });
        study.Regions.Add(new Region { LevelId = level, Kind = RegionKind.Zone, Outline = ShapeFactory.Rectangle(new(0, 0), new(2, 2)) });

        var result = WalkableArea.Compute(study, level, Ops);
        Assert.IsTrue(result.IsSuccess);

        // 150 m² union − 4 m² obstacle − 1.2 m² wall; zones don't subtract.
        Assert.AreEqual(144.8, NetArea(result.Value!), 1e-3);
    }

    [TestMethod]
    public void WalkableArea_DemoComputesForEveryLevel()
    {
        Study demo = DemoFixture.BuildMinimal();
        foreach (Level level in demo.Levels)
        {
            var result = WalkableArea.Compute(demo, level.Id, Ops);
            Assert.IsTrue(result.IsSuccess, result.Error);
            Assert.IsTrue(NetArea(result.Value!) > 0);
        }
    }
}

/// <summary>Small two-level study for tests that need more than one level without referencing Throng.IO.</summary>
internal static class DemoFixture
{
    public static Study BuildMinimal()
    {
        var study = new Study { Name = "Fixture" };
        var ground = new Level { Name = "Ground", ElevationM = 0 };
        var upper = new Level { Name = "Upper", ElevationM = 5 };
        study.Levels.AddRange([ground, upper]);
        study.Regions.Add(new Region { LevelId = ground.Id, Kind = RegionKind.Walkable, Outline = ShapeFactory.Rectangle(new(0, 0), new(30, 20)) });
        study.Regions.Add(new Region { LevelId = upper.Id, Kind = RegionKind.Walkable, Outline = ShapeFactory.Circle(new(10, 10), 8) });
        study.Regions.Add(new Region { LevelId = upper.Id, Kind = RegionKind.Obstacle, Tag = "Void", Outline = ShapeFactory.Rectangle(new(8, 8), new(12, 12)) });
        return study;
    }
}
