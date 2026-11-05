namespace Throng.Core.Geometry;

/// <summary>
/// A rectangular hole in a vertical wall (round 015, D-261): a plan segment lying on one of the wall's edges and the
/// height range it's open over, e.g. a landing door in a lift shaft wall.
/// </summary>
/// <param name="A">One end of the opening in plan.</param>
/// <param name="B">The other end.</param>
/// <param name="Z0">Bottom of the opening, metres.</param>
/// <param name="Z1">Top of the opening, metres.</param>
public readonly record struct WallOpening(Point2 A, Point2 B, double Z0, double Z1);
