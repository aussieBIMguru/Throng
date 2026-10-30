using Throng.Core.Editing;
using Throng.Core.Geometry;
using Throng.Core.Library;
using Throng.Core.Model;
using Throng.Core.World;

namespace Throng.Core.Tests;

/// <summary>Round 015: exploded levels (D-264) and lift shaft walls with landing door openings (D-261).</summary>
[TestClass]
public sealed class Round015Tests
{
    private static readonly double[] ThreeLevels = [0, 5, 10];

    [TestMethod]
    public void Explode_LevelsMoveUpByTheGap_AndSpansStretchToMeetThem()
    {
        const double gap = 3;
        Assert.AreEqual(0, ExplodeMap.LevelShift(0, ThreeLevels, gap), 1e-9);
        Assert.AreEqual(3, ExplodeMap.LevelShift(5, ThreeLevels, gap), 1e-9);
        Assert.AreEqual(6, ExplodeMap.LevelShift(10, ThreeLevels, gap), 1e-9);

        // Spanning geometry meets each floor exactly where the level's own meshes moved to.
        foreach (double z in ThreeLevels)
        {
            Assert.AreEqual(z + ExplodeMap.LevelShift(z, ThreeLevels, gap), ExplodeMap.Warp(z, ThreeLevels, gap), 1e-9, $"Floor at {z}.");
        }

        Assert.AreEqual(4, ExplodeMap.Warp(2.5, ThreeLevels, gap), 1e-9, "Half way up the first storey lands half way up the stretched one.");
        Assert.AreEqual(18, ExplodeMap.Warp(12, ThreeLevels, gap), 1e-9, "Above the top level: shifted like it.");
        Assert.AreEqual(-1, ExplodeMap.Warp(-1, ThreeLevels, gap), 1e-9, "Below the lowest level: unchanged.");
        Assert.AreEqual(7.3, ExplodeMap.Warp(7.3, ThreeLevels, 0), 1e-9, "No gap, no change.");
        Assert.AreEqual(7.3, ExplodeMap.Warp(7.3, [5.0], gap), 1e-9, "One level, nothing to separate.");
    }

    [TestMethod]
    public void Explode_Elevations_AreDistinctAndSorted()
    {
        double[] e = ExplodeMap.Elevations([5, 0, 5.0004, 10, double.NaN]);
        CollectionAssert.AreEqual(ThreeLevels, e);
        Assert.AreEqual(ExplodeMap.MaxLevels, ExplodeMap.Elevations(Enumerable.Range(0, 100).Select(i => i * 4.0)).Length);
    }

    [TestMethod]
    public void ShaftWalls_NoOpenings_OnePanelPerEdge()
    {
        List<Point2> square = ShapeFactory.Rectangle(new(0, 0), new(2.4, 2.4));
        List<WallPanel> panels = ShaftWalls.Panels(square, 0, 5, []);
        Assert.AreEqual(4, panels.Count);
        Assert.IsTrue(panels.All(p => p.Z0 == 0 && p.Z1 == 5));
    }

    [TestMethod]
    public void ShaftWalls_ADoorCutsItsColumn_AndTheAreaGoesDown()
    {
        List<Point2> square = ShapeFactory.Rectangle(new(0, 0), new(2.4, 2.4));
        var door = new WallOpening(new(0.7, 0), new(1.7, 0), 0, 2.1);
        List<WallPanel> panels = ShaftWalls.Panels(square, 0, 5, [door]);

        // The door's edge: wall either side, and a lintel over the door. The other three edges whole.
        Assert.AreEqual(6, panels.Count);
        double area = panels.Sum(p => (p.B - p.A).Length * (p.Z1 - p.Z0));
        Assert.AreEqual((4 * 2.4 * 5) - (1.0 * 2.1), area, 1e-6);
        Assert.IsTrue(panels.Any(p => Math.Abs(p.Z0 - 2.1) < 1e-9 && Math.Abs(p.Z1 - 5) < 1e-9 && Math.Abs((p.B - p.A).Length - 1.0) < 1e-9), "Lintel over the door.");

        // An opening off every edge changes nothing.
        Assert.AreEqual(4, ShaftWalls.Panels(square, 0, 5, [new WallOpening(new(1, 1), new(1.5, 1), 0, 2)]).Count);
    }

    [TestMethod]
    public void LiftOpenings_OnePerCarDoorAtEveryLanding()
    {
        var study = new Study();
        var ground = new Level { Name = "Ground", ElevationM = 0, HeightM = 4 };
        var upper = new Level { Name = "Upper", ElevationM = 4, HeightM = 4 };
        study.Levels.AddRange([ground, upper]);
        Shaft shaft = ConnectorFactory.CreateShaft(study, ground.Id, upper.Id, ShapeFactory.Rectangle(new(0, 0), new(4.8, 2.4)));
        var preset = (LiftTemplate)BuiltInLibrary.Instance.FindByKey("lift.passenger-13")!;
        shaft.Lift = LiftFactory.FromTemplate(shaft, preset, preset.Id);
        shaft.Lift.Cars = 2;
        study.Shafts.Add(shaft);

        List<WallOpening> openings = LiftOpenings.For(study, shaft);
        Assert.AreEqual(4, openings.Count, "Two cars × two landings, front doors only.");
        Assert.IsTrue(openings.All(o => o.Z1 - o.Z0 > 1.5 && o.Z1 - o.Z0 < 2.4), "Door height under the car height.");
        Assert.AreEqual(2, openings.Count(o => o.Z0 == 4), "Two at the upper landing.");

        List<WallPanel> panels = ShaftWalls.Panels(shaft.Outline, 0, 8, openings);
        double whole = PolygonPerimeter(shaft.Outline) * 8;
        double open = openings.Sum(o => (o.B - o.A).Length * (o.Z1 - o.Z0));
        Assert.AreEqual(whole - open, panels.Sum(p => (p.B - p.A).Length * (p.Z1 - p.Z0)), 1e-6, "Every opening lies on the shaft's walls.");

        shaft.Lift.Doors = [new LiftLevelDoors { LevelId = upper.Id, Front = LiftDoorUse.Closed }];
        Assert.AreEqual(2, LiftOpenings.For(study, shaft).Count, "A closed landing has no door opening.");
        shaft.Lift = null;
        Assert.AreEqual(0, LiftOpenings.For(study, shaft).Count, "No lift, no openings.");
    }

    [TestMethod]
    public void CarHeight_IsTheSameRuleAsBefore()
    {
        Assert.AreEqual(2.4, LiftLayout.CarHeightFor([0, 5, 10]), 1e-9);
        Assert.AreEqual(2.2, LiftLayout.CarHeightFor([0, 2.6]), 1e-9);
        Assert.AreEqual(1.8, LiftLayout.CarHeightFor([0, 1.0]), 1e-9);
        Assert.AreEqual(2.4, LiftLayout.CarHeightFor([0]), 1e-9);
    }

    private static double PolygonPerimeter(IReadOnlyList<Point2> outline)
    {
        double sum = 0;
        for (int i = 0; i < outline.Count; i++)
        {
            sum += (outline[(i + 1) % outline.Count] - outline[i]).Length;
        }

        return sum;
    }
}
