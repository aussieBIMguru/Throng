using Throng.Core.Geometry;

namespace Throng.Core.Tests;

[TestClass]
public sealed class PolygonMathTests
{
    private static readonly Point2[] Square = [new(0, 0), new(10, 0), new(10, 10), new(0, 10)];

    // L-shape: 10 x 10 square missing its 5 x 5 north-east quarter (area 75).
    private static readonly Point2[] LShape = [new(0, 0), new(10, 0), new(10, 5), new(5, 5), new(5, 10), new(0, 10)];

    [TestMethod]
    public void Area_OfSquare_IsWidthTimesHeight()
    {
        Assert.AreEqual(100, PolygonMath.Area(Square), 1e-9);
    }

    [TestMethod]
    public void SignedArea_FlipsWithWinding()
    {
        Point2[] clockwise = [.. Enumerable.Reverse(Square)];
        Assert.IsTrue(PolygonMath.IsCounterClockwise(Square));
        Assert.IsFalse(PolygonMath.IsCounterClockwise(clockwise));
        Assert.AreEqual(-PolygonMath.SignedArea(Square), PolygonMath.SignedArea(clockwise), 1e-9);
    }

    [TestMethod]
    public void Centroid_OfSquare_IsCentre()
    {
        Point2 c = PolygonMath.Centroid(Square);
        Assert.AreEqual(5, c.X, 1e-9);
        Assert.AreEqual(5, c.Y, 1e-9);
    }

    [TestMethod]
    public void Contains_DistinguishesInsideAndOutside()
    {
        Assert.IsTrue(PolygonMath.Contains(LShape, new Point2(2, 2)));
        Assert.IsTrue(PolygonMath.Contains(LShape, new Point2(2, 8)));
        Assert.IsFalse(PolygonMath.Contains(LShape, new Point2(8, 8)), "The missing quarter is outside.");
        Assert.IsFalse(PolygonMath.Contains(LShape, new Point2(-1, 5)));
    }

    [TestMethod]
    public void Triangulate_Square_GivesTwoTriangles()
    {
        IReadOnlyList<int> indices = PolygonMath.Triangulate(Square);
        Assert.AreEqual(6, indices.Count);
        Assert.AreEqual(100, TriangulatedArea(Square, indices), 1e-9);
    }

    [TestMethod]
    public void Triangulate_ConcaveShape_CoversExactArea()
    {
        IReadOnlyList<int> indices = PolygonMath.Triangulate(LShape);
        Assert.AreEqual(12, indices.Count, "n - 2 = 4 triangles.");
        Assert.AreEqual(75, TriangulatedArea(LShape, indices), 1e-9);
        AssertAllCounterClockwise(LShape, indices);
    }

    [TestMethod]
    public void Triangulate_ClockwiseInput_StillWorks()
    {
        Point2[] clockwise = [.. Enumerable.Reverse(LShape)];
        IReadOnlyList<int> indices = PolygonMath.Triangulate(clockwise);
        Assert.AreEqual(75, TriangulatedArea(clockwise, indices), 1e-9);
        AssertAllCounterClockwise(clockwise, indices);
    }

    [TestMethod]
    public void Triangulate_CollinearPoints_AreSkipped()
    {
        Point2[] withMidpoints = [new(0, 0), new(5, 0), new(10, 0), new(10, 10), new(0, 10)];
        IReadOnlyList<int> indices = PolygonMath.Triangulate(withMidpoints);
        Assert.AreEqual(100, TriangulatedArea(withMidpoints, indices), 1e-9);
    }

    [TestMethod]
    public void Triangulate_TooFewPoints_ReturnsEmpty()
    {
        Assert.AreEqual(0, PolygonMath.Triangulate([new Point2(0, 0), new Point2(1, 1)]).Count);
    }

    [TestMethod]
    public void Bounds_IncludeAndUnion()
    {
        Bounds2 b = Bounds2.FromPoints(LShape);
        Assert.AreEqual(new Bounds2(0, 0, 10, 10), b);
        Assert.IsTrue(Bounds2.Empty.IsEmpty);
        Assert.AreEqual(b, Bounds2.Empty.Union(b));
        Assert.AreEqual(new Point2(5, 5), b.Center);
    }

    private static double TriangulatedArea(IReadOnlyList<Point2> polygon, IReadOnlyList<int> indices)
    {
        double total = 0;
        for (int i = 0; i < indices.Count; i += 3)
        {
            total += PolygonMath.Area([polygon[indices[i]], polygon[indices[i + 1]], polygon[indices[i + 2]]]);
        }

        return total;
    }

    private static void AssertAllCounterClockwise(IReadOnlyList<Point2> polygon, IReadOnlyList<int> indices)
    {
        for (int i = 0; i < indices.Count; i += 3)
        {
            Assert.IsTrue(PolygonMath.SignedArea([polygon[indices[i]], polygon[indices[i + 1]], polygon[indices[i + 2]]]) > 0);
        }
    }
}
