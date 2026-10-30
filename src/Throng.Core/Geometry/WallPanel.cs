namespace Throng.Core.Geometry;

/// <summary>A vertical rectangle of wall (round 015, D-261): from plan point A to B, between heights Z0 and Z1.</summary>
/// <param name="A">Start of the panel in plan.</param>
/// <param name="B">End of the panel in plan.</param>
/// <param name="Z0">Bottom, metres.</param>
/// <param name="Z1">Top, metres.</param>
public readonly record struct WallPanel(Point2 A, Point2 B, double Z0, double Z1);
