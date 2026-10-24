using Throng.Core.Geometry;

namespace Throng.Core.Tests;

[TestClass]
public sealed class ShapeTests
{
    private static readonly Point2[] Square = [new(0, 0), new(4, 0), new(4, 4), new(0, 4)];

    [TestMethod]
    public void Rectangle_IsCounterClockwiseWithCorrectArea()
    {
        List<Point2> rect = ShapeFactory.Rectangle(new Point2(5, 3), new Point2(1, 1));
        Assert.AreEqual(4, rect.Count);
        Assert.IsTrue(PolygonMath.IsCounterClockwise(rect));
        Assert.AreEqual(8.0, PolygonMath.Area(rect), 1e-9);
    }

    [TestMethod]
    public void Circle_SmallRadiusUsesMinimumSegments()
    {
        Assert.AreEqual(ShapeFactory.MinCircleSegments, ShapeFactory.CircleSegmentCount(0.3));
        Assert.AreEqual(ShapeFactory.MinCircleSegments, ShapeFactory.Circle(Point2.Zero, 0.3).Count);
    }

    [TestMethod]
    public void Circle_ChordErrorWithinTolerance()
    {
        const double radius = 12.0;
        List<Point2> circle = ShapeFactory.Circle(new Point2(3, -2), radius);
        Assert.IsTrue(circle.Count > ShapeFactory.MinCircleSegments, "A 12 m circle needs more than the minimum.");

        // Sagitta of one segment is the worst-case distance between the polygon and the true circle.
        double sagitta = radius * (1 - Math.Cos(Math.PI / circle.Count));
        Assert.IsTrue(sagitta <= ShapeFactory.CircleChordToleranceM + 1e-9, $"Sagitta {sagitta} exceeds tolerance.");
        Assert.IsTrue(PolygonMath.IsCounterClockwise(circle));
    }

    [TestMethod]
    public void Circle_NonPositiveRadiusIsEmpty()
    {
        Assert.AreEqual(0, ShapeFactory.Circle(Point2.Zero, 0).Count);
        Assert.AreEqual(0, ShapeFactory.Circle(Point2.Zero, -1).Count);
    }

    [TestMethod]
    public void Clean_RemovesDuplicatesClosingPointAndCollinear()
    {
        Point2[] messy = [new(0, 0), new(0, 0), new(2, 0), new(4, 0), new(4, 4), new(0, 4), new(0, 0)];
        List<Point2> clean = ShapeValidator.Clean(messy, closed: true);
        CollectionAssert.AreEqual(Square, clean);
    }

    [TestMethod]
    public void Clean_RemovesSpike()
    {
        // (6,0) doubles back along the bottom edge.
        Point2[] spiky = [new(0, 0), new(6, 0), new(4, 0), new(4, 4), new(0, 4)];
        List<Point2> clean = ShapeValidator.Clean(spiky, closed: true);
        Assert.AreEqual(16.0, PolygonMath.Area(clean), 1e-9);
        Assert.IsFalse(clean.Contains(new Point2(6, 0)));
    }

    [TestMethod]
    public void Validate_AcceptsSquare()
    {
        var result = ShapeValidator.Validate(Square, closed: true);
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(4, result.Value!.Count);
    }

    [TestMethod]
    public void Validate_RejectsBowtie()
    {
        Point2[] bowtie = [new(0, 0), new(4, 4), new(4, 0), new(0, 4)];
        var result = ShapeValidator.Validate(bowtie, closed: true);
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ShapeValidator.EdgesCross, result.Error);
    }

    [TestMethod]
    public void Validate_RejectsCollinearAndTiny()
    {
        Point2[] line = [new(0, 0), new(1, 0), new(2, 0)];
        Assert.AreEqual(ShapeValidator.NeedThreePoints, ShapeValidator.Validate(line, closed: true).Error);

        Point2[] sliver = [new(0, 0), new(0.05, 0), new(0.05, 0.05)];
        Assert.AreEqual(ShapeValidator.NoArea, ShapeValidator.Validate(sliver, closed: true).Error);
    }

    [TestMethod]
    public void Validate_OpenWallNeedsTwoDistinctPoints()
    {
        Assert.IsFalse(ShapeValidator.Validate([new(1, 1), new(1, 1)], closed: false).IsSuccess);
        Assert.IsTrue(ShapeValidator.Validate([new(1, 1), new(5, 1)], closed: false).IsSuccess);
    }

    [TestMethod]
    public void SegmentsIntersect_HandlesCrossingTouchingAndApart()
    {
        Assert.IsTrue(SegmentMath.SegmentsIntersect(new(0, 0), new(4, 4), new(0, 4), new(4, 0)), "Crossing.");
        Assert.IsTrue(SegmentMath.SegmentsIntersect(new(0, 0), new(4, 0), new(4, 0), new(4, 4)), "Touching at an end.");
        Assert.IsTrue(SegmentMath.SegmentsIntersect(new(0, 0), new(4, 0), new(2, 0), new(6, 0)), "Collinear overlap.");
        Assert.IsFalse(SegmentMath.SegmentsIntersect(new(0, 0), new(4, 0), new(0, 1), new(4, 1)), "Parallel apart.");
    }
}
