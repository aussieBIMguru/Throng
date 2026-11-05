namespace Throng.Core.Geometry;

/// <summary>One car of a lift bank in plan (D-237): its footprint inside the shaft and its door on each side.</summary>
public sealed class LiftCarLayout
{
    /// <summary>Centre of the car.</summary>
    public required Point2 Centre { get; init; }

    /// <summary>Unit direction along the front edge.</summary>
    public required Point2 Along { get; init; }

    /// <summary>Unit direction from the front edge into the shaft.</summary>
    public required Point2 Inward { get; init; }

    /// <summary>Half the car's width along the front edge, metres.</summary>
    public required double HalfWidth { get; init; }

    /// <summary>Half the car's depth into the shaft, metres.</summary>
    public required double HalfDepth { get; init; }

    /// <summary>Front door opening on the front edge.</summary>
    public required (Point2 A, Point2 B) FrontDoor { get; init; }

    /// <summary>Rear door opening on the rear edge, or null when the shaft has no rear edge.</summary>
    public (Point2 A, Point2 B)? RearDoor { get; init; }

    /// <summary>Footprint rectangle (counter-clockwise or clockwise, as the front edge runs).</summary>
    public required IReadOnlyList<Point2> Footprint { get; init; }

    /// <summary>The door on a side (0 front, 1 rear), or null.</summary>
    public (Point2 A, Point2 B)? Door(int side) => side == 0 ? FrontDoor : RearDoor;
}
