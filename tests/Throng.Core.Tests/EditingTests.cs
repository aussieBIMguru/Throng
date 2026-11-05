using Throng.Core.Editing;
using Throng.Core.Geometry;
using Throng.Core.Model;

namespace Throng.Core.Tests;

[TestClass]
public sealed class EditingTests
{
    private static readonly SnapSettings Defaults = new();

    private static Region Rect(Guid levelId, string name, double x0, double y0, double x1, double y1, RegionKind kind = RegionKind.Walkable) => new()
    {
        LevelId = levelId,
        Name = name,
        Kind = kind,
        Outline = ShapeFactory.Rectangle(new Point2(x0, y0), new Point2(x1, y1)),
    };

    // ------------------------------------------------------------------ snapping

    [TestMethod]
    public void Snap_EndpointBeatsMidpointAndGrid()
    {
        var geometry = new SnapGeometry();
        geometry.AddOutline([new(0, 0), new(10.13, 0)], closed: false);

        SnapResult snap = Snapper.Snap(new Point2(10.2, 0.1), geometry, Defaults, tolerance: 0.3);
        Assert.AreEqual(SnapKind.Endpoint, snap.Kind);
        Assert.AreEqual(new Point2(10.13, 0), snap.Point);
    }

    [TestMethod]
    public void Snap_Midpoint()
    {
        var geometry = new SnapGeometry();
        geometry.AddOutline([new(0, 0), new(10, 0)], closed: false);

        SnapResult snap = Snapper.Snap(new Point2(5.1, 0.1), geometry, Defaults, tolerance: 0.3);
        Assert.AreEqual(SnapKind.Midpoint, snap.Kind);
        Assert.AreEqual(new Point2(5, 0), snap.Point);
    }

    [TestMethod]
    public void Snap_OrthoLocksAxisAndGridsTheFreeCoordinate()
    {
        SnapResult snap = Snapper.Snap(new Point2(7.3, 2.1), new SnapGeometry(), Defaults, tolerance: 0.3, reference: new Point2(1, 2));
        Assert.AreEqual(SnapKind.Ortho, snap.Kind);
        Assert.AreEqual(new Point2(7.5, 2), snap.Point);
        Assert.AreEqual(new Point2(1, 2), snap.OrthoFrom);
    }

    [TestMethod]
    public void Snap_ForcedOrthoIgnoresDistance()
    {
        SnapResult snap = Snapper.Snap(new Point2(3, 9), new SnapGeometry(), Defaults, tolerance: 0.3, reference: Point2.Zero, forceOrtho: true);
        Assert.AreEqual(SnapKind.Ortho, snap.Kind);
        Assert.AreEqual(new Point2(0, 9), snap.Point);
    }

    [TestMethod]
    public void Snap_GridThenNone()
    {
        SnapResult grid = Snapper.Snap(new Point2(1.26, -0.74), new SnapGeometry(), Defaults, tolerance: 0.1);
        Assert.AreEqual(SnapKind.Grid, grid.Kind);
        Assert.AreEqual(new Point2(1.5, -0.5), grid.Point);

        var off = new SnapSettings { Grid = false, Endpoint = false, Midpoint = false, Ortho = false };
        SnapResult raw = Snapper.Snap(new Point2(1.26, -0.74), new SnapGeometry(), off, tolerance: 0.1);
        Assert.AreEqual(SnapKind.None, raw.Kind);
        Assert.AreEqual(new Point2(1.26, -0.74), raw.Point);
    }

    // ------------------------------------------------------------------ picking

    [TestMethod]
    public void Pick_PrefersSmallerContainingRegion()
    {
        Guid level = Guid.NewGuid();
        Region floor = Rect(level, "Floor", 0, 0, 20, 20);
        Region column = Rect(level, "Column", 9, 9, 11, 11, RegionKind.Obstacle);

        Assert.AreSame(column, RegionPicking.Pick([floor, column], new Point2(10, 10), 0.1));
        Assert.AreSame(floor, RegionPicking.Pick([floor, column], new Point2(3, 3), 0.1));
        Assert.IsNull(RegionPicking.Pick([floor, column], new Point2(30, 30), 0.1));
    }

    [TestMethod]
    public void Pick_WallByThickness()
    {
        var wall = new Region { Kind = RegionKind.Wall, ThicknessM = 0.4, Outline = [new(0, 0), new(10, 0)] };
        Assert.AreSame(wall, RegionPicking.Pick([wall], new Point2(5, 0.15), 0.05));
        Assert.IsNull(RegionPicking.Pick([wall], new Point2(5, 0.5), 0.05));
    }

    [TestMethod]
    public void WindowAndCrossing_Differ()
    {
        Region region = Rect(Guid.NewGuid(), "R", 0, 0, 4, 4);
        Bounds2 partial = Bounds2.FromCorners(new Point2(2, -1), new Point2(6, 6));
        Bounds2 whole = Bounds2.FromCorners(new Point2(-1, -1), new Point2(5, 5));
        Bounds2 inside = Bounds2.FromCorners(new Point2(1, 1), new Point2(2, 2));

        Assert.IsFalse(RegionPicking.IsInside(region, partial));
        Assert.IsTrue(RegionPicking.Touches(region, partial));
        Assert.IsTrue(RegionPicking.IsInside(region, whole));
        Assert.IsTrue(RegionPicking.Touches(region, inside), "A box wholly inside the region touches it.");
    }

    // ------------------------------------------------------------------ actions

    [TestMethod]
    public void AddRemoveReplace_UndoRestoresExactly()
    {
        var study = Study.CreateDefault();
        Guid level = study.Levels[0].Id;
        Region a = Rect(level, "A", 0, 0, 1, 1);
        Region b = Rect(level, "B", 2, 0, 3, 1);
        Region c = Rect(level, "C", 4, 0, 5, 1);
        study.Regions.AddRange([a, b, c]);

        var stack = new UndoStack();
        stack.Execute(new RemoveRegionsAction("Delete", study, [c, a], null));
        CollectionAssert.AreEqual(new[] { b }, study.Regions);
        stack.Undo();
        CollectionAssert.AreEqual(new[] { a, b, c }, study.Regions, "Original order restored.");

        Region d = Rect(level, "D", 6, 0, 7, 1);
        stack.Execute(new AddRegionsAction("Draw", study, [d], null));
        Assert.AreEqual(4, study.Regions.Count);
        stack.Undo();
        Assert.AreEqual(3, study.Regions.Count);

        List<Point2> original = [.. a.Outline];
        var move = new ReplaceOutlinesAction("Move", [(a, ShapeFactory.Translate(a.Outline, new Point2(10, 0)))], null);
        Assert.IsFalse(move.IsNoOp);
        stack.Execute(move);
        Assert.AreEqual(10.0, a.Outline[0].X, 1e-9);
        stack.Undo();
        CollectionAssert.AreEqual(original, a.Outline);
    }

    [TestMethod]
    public void Factory_NamesAndCopies()
    {
        var study = Study.CreateDefault();
        Guid level = study.Levels[0].Id;
        study.Regions.Add(new Region { LevelId = level, Name = "Floor 3" });

        Region next = RegionFactory.Create(study, level, RegionKind.Walkable, ShapeFactory.Rectangle(Point2.Zero, new Point2(1, 1)));
        Assert.AreEqual("Floor 4", next.Name);

        Region wall = RegionFactory.Create(study, level, RegionKind.Wall, [new(0, 0), new(1, 0)]);
        Assert.AreEqual("Wall 1", wall.Name);
        Assert.AreEqual(RegionFactory.DefaultWallThicknessM, wall.ThicknessM);

        Guid other = Guid.NewGuid();
        Region copy = RegionFactory.Copy(next, other, new Point2(0.5, -0.5));
        Assert.AreNotEqual(next.Id, copy.Id);
        Assert.AreEqual(other, copy.LevelId);
        Assert.AreEqual(new Point2(0.5, -0.5), copy.Outline[0]);
    }

    // ------------------------------------------------------------------ typed input

    [TestMethod]
    public void TypedInput_ParsesOneOrTwoNumbers()
    {
        CollectionAssert.AreEqual(new[] { 12.5 }, TypedInput.Parse("12.5").Value);
        CollectionAssert.AreEqual(new[] { 12.5, 8.0 }, TypedInput.Parse(" 12.5, 8 ").Value);
        CollectionAssert.AreEqual(new[] { 3.0, -2.0 }, TypedInput.Parse("3x-2").Value);
        CollectionAssert.AreEqual(new[] { 4.0 }, TypedInput.Parse("4m").Value);
        Assert.IsFalse(TypedInput.Parse("").IsSuccess);
        Assert.IsFalse(TypedInput.Parse("1,2,3").IsSuccess);
        Assert.IsFalse(TypedInput.Parse("abc").IsSuccess);
    }
}
