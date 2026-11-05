using Throng.Core.Geometry;

namespace Throng.Core.Tests;

[TestClass]
public sealed class EdgePushPullTests
{
    // Counter-clockwise 4 × 2 rectangle; edge 2 is the top edge (4,2) → (0,2).
    private static readonly Point2[] Rect = [new(0, 0), new(4, 0), new(4, 2), new(0, 2)];

    [TestMethod]
    public void Normal_IsOutwardForEitherWinding()
    {
        Assert.AreEqual(new Point2(0, 1), Round(EdgePushPull.Normal(Rect, closed: true, edgeIndex: 2)));

        Point2[] clockwise = [.. Enumerable.Reverse(Rect)];
        // Clockwise ring: edge 0 is (0,2) → (4,2), the top edge again.
        Assert.AreEqual(new Point2(0, 1), Round(EdgePushPull.Normal(clockwise, closed: true, edgeIndex: 0)));
    }

    [TestMethod]
    public void PushOut_RectangleStaysRectangle()
    {
        var result = EdgePushPull.Apply(Rect, closed: true, edgeIndex: 2, distance: 1.5);
        Assert.IsTrue(result.IsSuccess);
        CollectionAssert.AreEqual(new Point2[] { new(0, 0), new(4, 0), new(4, 3.5), new(0, 3.5) }, result.Value!.Select(Round).ToArray());
        Assert.AreEqual(14.0, PolygonMath.Area(result.Value!), 1e-9);
    }

    [TestMethod]
    public void PullIn_ShrinksAndTooFarFails()
    {
        var pulled = EdgePushPull.Apply(Rect, closed: true, edgeIndex: 2, distance: -1.0);
        Assert.AreEqual(4.0, PolygonMath.Area(pulled.Value!), 1e-9);

        var tooFar = EdgePushPull.Apply(Rect, closed: true, edgeIndex: 2, distance: -2.5);
        Assert.IsFalse(tooFar.IsSuccess);
        Assert.AreEqual(EdgePushPull.TooFar, tooFar.Error);
    }

    [TestMethod]
    public void AngledNeighbourKeepsItsDirection()
    {
        // Chamfered corner: (4,0) → (6,2) is a 45° edge leading into the right edge.
        Point2[] chamfer = [new(0, 0), new(4, 0), new(6, 2), new(6, 6), new(0, 6)];

        // Push the right edge (index 2: (6,2) → (6,6)) out by 1. The chamfer's end must slide along the 45° line.
        var result = EdgePushPull.Apply(chamfer, closed: true, edgeIndex: 2, distance: 1.0);
        Assert.IsTrue(result.IsSuccess);
        Point2 moved = Round(result.Value![2]);
        Assert.AreEqual(new Point2(7, 3), moved, "Slid one unit further along the 45° chamfer.");
        Assert.AreEqual(new Point2(7, 6), Round(result.Value![3]), "Top neighbour is horizontal, so the corner moves straight out.");
    }

    [TestMethod]
    public void OpenWallEndSegmentMovesItsFreeEndStraight()
    {
        Point2[] wall = [new(0, 0), new(5, 0), new(5, 5)];
        var result = EdgePushPull.Apply(wall, closed: false, edgeIndex: 0, distance: 1.0);
        Assert.IsTrue(result.IsSuccess);

        // Left normal of (0,0)→(5,0) is +Y. The free end moves straight; the joint slides along the vertical segment.
        Assert.AreEqual(new Point2(0, 1), Round(result.Value![0]));
        Assert.AreEqual(new Point2(5, 1), Round(result.Value![1]));
        Assert.AreEqual(new Point2(5, 5), Round(result.Value![2]));
    }

    private static Point2 Round(Point2 p) => new(Math.Round(p.X, 9), Math.Round(p.Y, 9));
}
