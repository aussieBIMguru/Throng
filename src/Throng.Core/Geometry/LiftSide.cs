namespace Throng.Core.Geometry;

/// <summary>One door side of a lift bank (front or rear edge of the shaft) and its landing (D-241).</summary>
public sealed class LiftSide
{
    /// <summary>Edge start.</summary>
    public required Point2 A { get; init; }

    /// <summary>Edge end.</summary>
    public required Point2 B { get; init; }

    /// <summary>Unit direction out of the shaft onto the landing.</summary>
    public required Point2 Outward { get; init; }

    /// <summary>Landing rectangle in front of the edge (edge width × landing depth).</summary>
    public required IReadOnlyList<Point2> Landing { get; init; }

    /// <summary>Edge length, metres.</summary>
    public double Length => (B - A).Length;

    /// <summary>Midpoint of the edge.</summary>
    public Point2 Mid => (A + B) * 0.5;
}
